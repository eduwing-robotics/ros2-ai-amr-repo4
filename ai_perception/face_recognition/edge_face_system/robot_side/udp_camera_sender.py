from __future__ import annotations

import argparse
import math
import socket
import struct
import time

import cv2

MAGIC = b"GCM1"
VERSION = 1
HEADER_FORMAT = "!4sBHIQHHIHHB"


def parse_args():
    parser = argparse.ArgumentParser(
        description="Capture camera frames and send JPEG chunks over UDP."
    )
    parser.add_argument("--camera", default="0", help="OpenCV camera index or device path, e.g. 0 or /dev/video6.")
    parser.add_argument("--host", default="192.168.40.11", help="UDP receiver host.")
    parser.add_argument("--port", type=int, default=5005, help="UDP receiver port.")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--fps", type=float, default=10.0, help="Maximum send FPS.")
    parser.add_argument("--camera-fps", type=float, default=15.0, help="Requested camera capture FPS.")
    parser.add_argument("--buffer-size", type=int, default=1, help="Requested OpenCV capture buffer size.")
    parser.add_argument("--jpeg-quality", type=int, default=65, help="JPEG compression quality.")
    parser.add_argument("--chunk-size", type=int, default=1200, help="JPEG payload bytes per UDP packet.")
    parser.add_argument("--frame-id", default="globalcam", help="Frame identifier sent in packet header.")
    parser.add_argument("--log-interval", type=float, default=1.0, help="Seconds between status log lines.")
    return parser.parse_args()


def camera_source(camera: str):
    return int(camera) if str(camera).isdigit() else camera


def build_packet_header(
    frame_seq: int,
    timestamp_ns: int,
    width: int,
    height: int,
    jpeg_size: int,
    total_chunks: int,
    chunk_index: int,
    frame_id_bytes: bytes,
) -> bytes:
    frame_id_len = len(frame_id_bytes)
    header_size = struct.calcsize(HEADER_FORMAT) + frame_id_len
    return struct.pack(
        HEADER_FORMAT,
        MAGIC,
        VERSION,
        header_size,
        frame_seq,
        timestamp_ns,
        width,
        height,
        jpeg_size,
        total_chunks,
        chunk_index,
        frame_id_len,
    ) + frame_id_bytes


def send_frame_udp(
    sock: socket.socket,
    host: str,
    port: int,
    frame_seq: int,
    frame,
    frame_id: str,
    jpeg_quality: int,
    chunk_size: int,
) -> tuple[int, int, int]:
    ok, jpeg_buf = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), jpeg_quality])
    if not ok:
        raise RuntimeError("JPEG encode failed")

    jpeg_bytes = jpeg_buf.tobytes()
    jpeg_size = len(jpeg_bytes)
    total_chunks = max(1, math.ceil(jpeg_size / chunk_size))
    timestamp_ns = time.time_ns()
    height, width = frame.shape[:2]
    frame_id_bytes = frame_id.encode("utf-8")[:255]

    sent_packets = 0
    for chunk_index in range(total_chunks):
        start = chunk_index * chunk_size
        end = min(start + chunk_size, jpeg_size)
        chunk = jpeg_bytes[start:end]
        header = build_packet_header(
            frame_seq=frame_seq,
            timestamp_ns=timestamp_ns,
            width=width,
            height=height,
            jpeg_size=jpeg_size,
            total_chunks=total_chunks,
            chunk_index=chunk_index,
            frame_id_bytes=frame_id_bytes,
        )
        sock.sendto(header + chunk, (host, port))
        sent_packets += 1

    return jpeg_size, total_chunks, sent_packets


def main():
    args = parse_args()
    source = camera_source(args.camera)
    cap = cv2.VideoCapture(source)
    if not cap.isOpened():
        raise RuntimeError(f"Cannot open camera {args.camera}")

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    cap.set(cv2.CAP_PROP_FPS, args.camera_fps)
    cap.set(cv2.CAP_PROP_BUFFERSIZE, args.buffer_size)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    send_every = 1.0 / max(args.fps, 1.0)

    print(
        f"UDP camera sender started: camera={args.camera}, target={args.host}:{args.port}, "
        f"resolution={args.width}x{args.height}, send_fps={args.fps:g}, "
        f"camera_fps={args.camera_fps:g}, jpeg_quality={args.jpeg_quality}, "
        f"chunk_size={args.chunk_size}"
    )

    frame_seq = 0
    last_send = 0.0
    sent_frames = 0
    sent_packets = 0
    last_jpeg_size = 0
    last_total_chunks = 0
    last_read_ms = 0.0
    last_encode_send_ms = 0.0
    log_sent_frames = 0
    log_sent_packets = 0
    last_log = time.monotonic()
    next_send = time.monotonic()

    try:
        while True:
            now = time.monotonic()
            if now < next_send:
                time.sleep(min(next_send - now, 0.001))
                continue

            read_started = time.monotonic()
            ok, frame = cap.read()
            read_finished = time.monotonic()
            if not ok:
                print("Warning: camera frame read failed")
                next_send = time.monotonic()
                continue

            send_started = time.monotonic()
            jpeg_size, total_chunks, packet_count = send_frame_udp(
                sock=sock,
                host=args.host,
                port=args.port,
                frame_seq=frame_seq,
                frame=frame,
                frame_id=args.frame_id,
                jpeg_quality=args.jpeg_quality,
                chunk_size=args.chunk_size,
            )
            send_finished = time.monotonic()

            frame_seq += 1
            sent_frames += 1
            sent_packets += packet_count
            last_jpeg_size = jpeg_size
            last_total_chunks = total_chunks
            last_read_ms = (read_finished - read_started) * 1000.0
            last_encode_send_ms = (send_finished - send_started) * 1000.0
            last_send = send_finished
            next_send += send_every
            if last_send > next_send:
                next_send = last_send

            if last_send - last_log >= args.log_interval:
                interval = last_send - last_log
                sent_fps = (sent_frames - log_sent_frames) / interval if interval > 0 else 0.0
                packets_in_interval = sent_packets - log_sent_packets
                print(
                    f"sent_fps={sent_fps:.2f} sent_frames={sent_frames} "
                    f"sent_packets={sent_packets} (+{packets_in_interval}) "
                    f"last_jpeg_size={last_jpeg_size} last_total_chunks={last_total_chunks} "
                    f"read_ms={last_read_ms:.1f} encode_send_ms={last_encode_send_ms:.1f}"
                )
                log_sent_frames = sent_frames
                log_sent_packets = sent_packets
                last_log = last_send
    except KeyboardInterrupt:
        print("Stopping UDP camera sender.")
    finally:
        cap.release()
        sock.close()


if __name__ == "__main__":
    main()
