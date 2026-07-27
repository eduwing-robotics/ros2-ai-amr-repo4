from __future__ import annotations

import argparse
import socket
import struct
import threading
import time
from collections import OrderedDict
from dataclasses import dataclass, field
from typing import Any

import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import CompressedImage
from std_msgs.msg import Header


MAGIC = b"GCM1"
VERSION = 1
HEADER_FORMAT = "!4sBHIQHHIHHB"
HEADER_SIZE = struct.calcsize(HEADER_FORMAT)


@dataclass
class PendingFrame:
    first_seen_monotonic: float
    last_seen_monotonic: float
    timestamp_ns: int
    width: int
    height: int
    jpeg_size: int
    total_chunks: int
    frame_id: str
    chunks: dict[int, bytes] = field(default_factory=dict)


@dataclass
class CompletedFrame:
    jpeg_bytes: bytes
    frame_id: str
    width: int
    height: int


class UdpGlobalcamReceiver(Node):
    def __init__(self, args: argparse.Namespace):
        super().__init__("udp_globalcam_receiver")
        self.args = args
        self._stop_event = threading.Event()
        self._state_lock = threading.Lock()
        self._pending: OrderedDict[int, PendingFrame] = OrderedDict()
        self._completed: list[CompletedFrame] = []

        self._rx_packets = 0
        self._rx_frames = 0
        self._published_frames = 0
        self._dropped_packets = 0
        self._dropped_incomplete_frames = 0
        self._duplicate_chunks = 0
        self._publish_window_count = 0
        self._publish_window_started = time.monotonic()
        self._last_log_at = time.monotonic()

        self._allowed_host = args.allowed_host.strip() or None
        self._display_window_created = False

        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._socket.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, args.socket_buffer)
        self._socket.bind((args.bind, args.port))

        self._publisher = self.create_publisher(
            CompressedImage,
            args.topic,
            qos_profile_sensor_data,
        )

        self._recv_thread = threading.Thread(
            target=self._udp_receive_loop,
            name="udp-globalcam-recv",
            daemon=True,
        )
        self._recv_thread.start()

        self.create_timer(max(args.publish_interval, 0.001), self._on_timer)

        allowed_text = self._allowed_host or "any"
        show_text = "enabled" if args.show_image else "disabled"
        self.get_logger().info(
            f"UDP bind={args.bind}:{args.port} allowed_host={allowed_text} "
            f"publish_topic={args.topic} timeout_sec={args.timeout_sec} "
            f"socket_buffer={args.socket_buffer} publish_interval={args.publish_interval} "
            f"show_image={show_text}"
        )

    def _on_timer(self):
        self._publish_completed_frames()
        self._cleanup_pending_frames()
        self._maybe_log_stats()

    def _udp_receive_loop(self):
        while not self._stop_event.is_set():
            try:
                self._socket.settimeout(0.2)
                packet, sender_addr = self._socket.recvfrom(65535)
            except socket.timeout:
                continue
            except OSError as exc:
                if not self._stop_event.is_set():
                    self.get_logger().warning(f"UDP recv failed: {exc}")
                break
            self._handle_packet(packet, sender_addr[0])

    def _handle_packet(self, packet: bytes, sender_host: str):
        with self._state_lock:
            self._rx_packets += 1

        parsed = self._parse_packet(packet, sender_host)
        if parsed is None:
            with self._state_lock:
                self._dropped_packets += 1
            return

        now = time.monotonic()
        with self._state_lock:
            pending = self._pending.get(parsed.frame_seq)
            if pending is None:
                pending = PendingFrame(
                    first_seen_monotonic=now,
                    last_seen_monotonic=now,
                    timestamp_ns=parsed.timestamp_ns,
                    width=parsed.width,
                    height=parsed.height,
                    jpeg_size=parsed.jpeg_size,
                    total_chunks=parsed.total_chunks,
                    frame_id=parsed.frame_id,
                )
                self._pending[parsed.frame_seq] = pending
                self._enforce_pending_limit()
            else:
                pending.last_seen_monotonic = now
                if (
                    pending.timestamp_ns != parsed.timestamp_ns
                    or pending.width != parsed.width
                    or pending.height != parsed.height
                    or pending.jpeg_size != parsed.jpeg_size
                    or pending.total_chunks != parsed.total_chunks
                    or pending.frame_id != parsed.frame_id
                ):
                    pending.first_seen_monotonic = now
                    pending.last_seen_monotonic = now
                    pending.timestamp_ns = parsed.timestamp_ns
                    pending.width = parsed.width
                    pending.height = parsed.height
                    pending.jpeg_size = parsed.jpeg_size
                    pending.total_chunks = parsed.total_chunks
                    pending.frame_id = parsed.frame_id
                    pending.chunks.clear()

            if parsed.chunk_index in pending.chunks:
                self._duplicate_chunks += 1
            pending.chunks[parsed.chunk_index] = parsed.chunk_data

            if len(pending.chunks) < pending.total_chunks:
                return

            jpeg_bytes = self._assemble_jpeg(pending)
            del self._pending[parsed.frame_seq]
            if jpeg_bytes is None:
                self._dropped_incomplete_frames += 1
                return

            if self.args.validate_jpeg and not self._validate_jpeg(jpeg_bytes):
                self._dropped_incomplete_frames += 1
                return

            self._rx_frames += 1
            self._completed.append(
                CompletedFrame(
                    jpeg_bytes=jpeg_bytes,
                    frame_id=pending.frame_id,
                    width=pending.width,
                    height=pending.height,
                )
            )

    def _parse_packet(self, packet: bytes, sender_host: str) -> Any | None:
        if len(packet) < HEADER_SIZE:
            return None

        if self._allowed_host is not None and sender_host != self._allowed_host:
            return None

        try:
            (
                magic,
                version,
                header_size,
                frame_seq,
                timestamp_ns,
                width,
                height,
                jpeg_size,
                total_chunks,
                chunk_index,
                frame_id_len,
            ) = struct.unpack(HEADER_FORMAT, packet[:HEADER_SIZE])
        except struct.error:
            return None

        if magic != MAGIC or version != VERSION:
            return None

        expected_header_size = HEADER_SIZE + frame_id_len
        if header_size != expected_header_size:
            return None
        if len(packet) < header_size:
            return None
        if frame_id_len > 255:
            return None
        if total_chunks == 0 or chunk_index >= total_chunks:
            return None
        if jpeg_size == 0:
            return None

        frame_id_bytes = packet[HEADER_SIZE:header_size]
        if len(frame_id_bytes) != frame_id_len:
            return None
        try:
            frame_id = frame_id_bytes.decode("utf-8")
        except UnicodeDecodeError:
            return None

        chunk_data = packet[header_size:]
        if not chunk_data:
            return None

        return _ParsedChunk(
            frame_seq=frame_seq,
            timestamp_ns=timestamp_ns,
            width=width,
            height=height,
            jpeg_size=jpeg_size,
            total_chunks=total_chunks,
            chunk_index=chunk_index,
            frame_id=frame_id,
            chunk_data=chunk_data,
        )

    def _assemble_jpeg(self, pending: PendingFrame) -> bytes | None:
        try:
            jpeg_bytes = b"".join(pending.chunks[index] for index in range(pending.total_chunks))
        except KeyError:
            return None
        if len(jpeg_bytes) != pending.jpeg_size:
            return None
        return jpeg_bytes

    def _validate_jpeg(self, jpeg_bytes: bytes) -> bool:
        try:
            import cv2
            import numpy as np
        except ImportError:
            self.get_logger().warning("validate-jpeg requested but cv2 is unavailable")
            return True

        frame = cv2.imdecode(np.frombuffer(jpeg_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
        return frame is not None

    def _enforce_pending_limit(self):
        while len(self._pending) > self.args.max_frames_buffer:
            _, oldest = self._pending.popitem(last=False)
            self._dropped_incomplete_frames += 1
            _ = oldest

    def _cleanup_pending_frames(self):
        now = time.monotonic()
        expired: list[int] = []
        with self._state_lock:
            for frame_seq, pending in self._pending.items():
                if now - pending.last_seen_monotonic >= self.args.timeout_sec:
                    expired.append(frame_seq)
            for frame_seq in expired:
                del self._pending[frame_seq]
                self._dropped_incomplete_frames += 1

    def _publish_completed_frames(self):
        with self._state_lock:
            completed = self._completed
            self._completed = []

        for item in completed:
            if self.args.show_image:
                self._show_completed_frame(item)

            msg = CompressedImage()
            msg.header = Header()
            msg.header.stamp = self.get_clock().now().to_msg()
            msg.header.frame_id = item.frame_id
            msg.format = "jpeg"
            msg.data = item.jpeg_bytes
            self._publisher.publish(msg)
            with self._state_lock:
                self._published_frames += 1
                self._publish_window_count += 1

    def _show_completed_frame(self, item: CompletedFrame):
        try:
            import cv2
            import numpy as np
        except ImportError:
            self.get_logger().warning("show-image requested but cv2 is unavailable")
            self.args.show_image = False
            return

        frame = cv2.imdecode(np.frombuffer(item.jpeg_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
        if frame is None:
            return

        max_width = self.args.display_max_width
        if max_width > 0 and frame.shape[1] > max_width:
            scale = max_width / frame.shape[1]
            frame = cv2.resize(
                frame,
                (max_width, max(1, int(frame.shape[0] * scale))),
                interpolation=cv2.INTER_AREA,
            )

        if not self._display_window_created:
            cv2.namedWindow(self.args.window_name, cv2.WINDOW_NORMAL)
            self._display_window_created = True
        cv2.imshow(self.args.window_name, frame)
        key = cv2.waitKey(1) & 0xFF
        if key in (27, ord("q")):
            self.get_logger().info("Display window requested shutdown")
            rclpy.shutdown()

    def _maybe_log_stats(self):
        now = time.monotonic()
        if now - self._last_log_at < self.args.log_interval:
            return

        elapsed = max(now - self._publish_window_started, 1e-6)
        with self._state_lock:
            publish_fps = self._publish_window_count / elapsed
            self.get_logger().info(
                "stats "
                f"rx_packets={self._rx_packets} "
                f"rx_frames={self._rx_frames} "
                f"published_frames={self._published_frames} "
                f"dropped_packets={self._dropped_packets} "
                f"dropped_incomplete_frames={self._dropped_incomplete_frames} "
                f"duplicate_chunks={self._duplicate_chunks} "
                f"current_pending_frames={len(self._pending)} "
                f"publish_fps={publish_fps:.2f}"
            )
            self._publish_window_count = 0
            self._publish_window_started = now
            self._last_log_at = now

    def destroy_node(self):
        self._stop_event.set()
        if self._recv_thread.is_alive():
            self._recv_thread.join(timeout=1.0)
        try:
            self._socket.close()
        except OSError:
            pass
        if self._display_window_created:
            try:
                import cv2

                cv2.destroyWindow(self.args.window_name)
            except Exception:
                pass
        super().destroy_node()


@dataclass(frozen=True)
class _ParsedChunk:
    frame_seq: int
    timestamp_ns: int
    width: int
    height: int
    jpeg_size: int
    total_chunks: int
    chunk_index: int
    frame_id: str
    chunk_data: bytes


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Receive GlobalCam UDP JPEG chunks and publish ROS2 CompressedImage."
    )
    parser.add_argument("--bind", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5005)
    parser.add_argument("--topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--allowed-host", default="192.168.40.10")
    parser.add_argument("--timeout-sec", type=float, default=0.5)
    parser.add_argument("--max-frames-buffer", type=int, default=32)
    parser.add_argument("--socket-buffer", type=int, default=4194304)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--publish-interval", type=float, default=0.005)
    parser.add_argument("--validate-jpeg", action="store_true")
    parser.add_argument("--show-image", action="store_true")
    parser.add_argument("--window-name", default="UDP GlobalCam Receiver")
    parser.add_argument("--display-max-width", type=int, default=960)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = UdpGlobalcamReceiver(args)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
