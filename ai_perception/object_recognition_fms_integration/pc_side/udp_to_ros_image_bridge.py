#!/usr/bin/env python3
"""
Server-side UDP to ROS Image bridge.

Receives GlobalCam UDP JPEG chunks from turtlebot_udp_camera_sender.py and publishes
sensor_msgs/Image without any display window. For display + live publish, use
turtlebot_udp_display_node instead.
"""

from __future__ import annotations

import argparse
import socket
import struct
import threading
import time
from collections import OrderedDict
from dataclasses import dataclass, field

import cv2
import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image


MAGIC = b"GCM1"
VERSION = 1
HEADER_FORMAT = "!4sBHIQHHIHHB"
HEADER_SIZE = struct.calcsize(HEADER_FORMAT)


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


def cv_to_imgmsg(frame, frame_id: str, stamp) -> Image:
    msg = Image()
    msg.header.stamp = stamp
    msg.header.frame_id = frame_id
    msg.height = int(frame.shape[0])
    msg.width = int(frame.shape[1])
    msg.encoding = "bgr8"
    msg.is_bigendian = False
    msg.step = int(frame.shape[1] * frame.shape[2])
    msg.data = frame.tobytes()
    return msg


@dataclass
class PendingUdpFrame:
    first_seen_monotonic: float
    last_seen_monotonic: float
    timestamp_ns: int
    width: int
    height: int
    jpeg_size: int
    total_chunks: int
    frame_id: str
    chunks: dict[int, bytes] = field(default_factory=dict)


@dataclass(frozen=True)
class ParsedUdpChunk:
    frame_seq: int
    timestamp_ns: int
    width: int
    height: int
    jpeg_size: int
    total_chunks: int
    chunk_index: int
    frame_id: str
    chunk_data: bytes


@dataclass(frozen=True)
class LatestUdpFrame:
    frame: np.ndarray
    frame_id: str
    timestamp_ns: int
    received_monotonic: float
    frame_seq: int


class UdpLatestFrameInput:
    def __init__(
        self,
        bind: str,
        port: int,
        allowed_host: str,
        timeout_sec: float,
        max_frames_buffer: int,
        socket_buffer: int,
        logger,
    ):
        self.bind = bind
        self.port = port
        self.allowed_host = allowed_host.strip()
        self.timeout_sec = timeout_sec
        self.max_frames_buffer = max_frames_buffer
        self.socket_buffer = socket_buffer
        self.logger = logger

        self._stop_event = threading.Event()
        self._state_lock = threading.Lock()
        self._pending: OrderedDict[int, PendingUdpFrame] = OrderedDict()
        self._latest: LatestUdpFrame | None = None

        self.rx_packets = 0
        self.rx_frames = 0
        self.dropped_packets = 0
        self.dropped_incomplete_frames = 0
        self.duplicate_chunks = 0
        self.last_sender_host = ""
        self.last_rejected_sender_host = ""
        self._input_window_count = 0
        self._input_window_started = time.monotonic()

        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._socket.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, socket_buffer)
        self._socket.bind((bind, port))

        self._recv_thread = threading.Thread(
            target=self._udp_receive_loop,
            name="udp-to-ros-image-recv",
            daemon=True,
        )
        self._recv_thread.start()

    def stop(self):
        self._stop_event.set()
        if self._recv_thread.is_alive():
            self._recv_thread.join(timeout=1.0)
        try:
            self._socket.close()
        except OSError:
            pass

    def get_latest(self) -> LatestUdpFrame | None:
        with self._state_lock:
            return self._latest

    def cleanup_pending(self):
        now = time.monotonic()
        expired: list[int] = []
        with self._state_lock:
            for frame_seq, pending in self._pending.items():
                if now - pending.last_seen_monotonic >= self.timeout_sec:
                    expired.append(frame_seq)
            for frame_seq in expired:
                del self._pending[frame_seq]
                self.dropped_incomplete_frames += 1

    def input_fps(self) -> float:
        elapsed = max(time.monotonic() - self._input_window_started, 1e-6)
        with self._state_lock:
            fps = self._input_window_count / elapsed
            self._input_window_count = 0
            self._input_window_started = time.monotonic()
        return fps

    @property
    def pending_count(self) -> int:
        with self._state_lock:
            return len(self._pending)

    def _udp_receive_loop(self):
        while not self._stop_event.is_set():
            try:
                self._socket.settimeout(0.2)
                packet, sender_addr = self._socket.recvfrom(65535)
            except socket.timeout:
                continue
            except OSError as exc:
                if not self._stop_event.is_set():
                    self.logger.warning(f"UDP recv failed: {exc}")
                break
            self._handle_packet(packet, sender_addr[0])

    def _handle_packet(self, packet: bytes, sender_host: str):
        with self._state_lock:
            self.rx_packets += 1
            self.last_sender_host = sender_host

        if self.allowed_host and sender_host != self.allowed_host:
            with self._state_lock:
                self.dropped_packets += 1
                self.last_rejected_sender_host = sender_host
            return

        parsed = self._parse_packet(packet)
        if parsed is None:
            with self._state_lock:
                self.dropped_packets += 1
            return

        now = time.monotonic()
        with self._state_lock:
            pending = self._pending.get(parsed.frame_seq)
            if pending is None:
                pending = PendingUdpFrame(
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
                    pending.timestamp_ns = parsed.timestamp_ns
                    pending.width = parsed.width
                    pending.height = parsed.height
                    pending.jpeg_size = parsed.jpeg_size
                    pending.total_chunks = parsed.total_chunks
                    pending.frame_id = parsed.frame_id
                    pending.chunks.clear()

            if parsed.chunk_index in pending.chunks:
                self.duplicate_chunks += 1
            pending.chunks[parsed.chunk_index] = parsed.chunk_data

            if len(pending.chunks) < pending.total_chunks:
                return

            jpeg_bytes = self._assemble_jpeg(pending)
            del self._pending[parsed.frame_seq]
            if jpeg_bytes is None:
                self.dropped_incomplete_frames += 1
                return

            frame = cv2.imdecode(np.frombuffer(jpeg_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
            if frame is None:
                self.dropped_incomplete_frames += 1
                return

            self.rx_frames += 1
            self._input_window_count += 1
            self._latest = LatestUdpFrame(
                frame=frame,
                frame_id=pending.frame_id,
                timestamp_ns=pending.timestamp_ns,
                received_monotonic=now,
                frame_seq=parsed.frame_seq,
            )

    def _parse_packet(self, packet: bytes) -> ParsedUdpChunk | None:
        if len(packet) < HEADER_SIZE:
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
        if header_size != expected_header_size or len(packet) < header_size:
            return None
        if total_chunks == 0 or chunk_index >= total_chunks or jpeg_size == 0:
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
        return ParsedUdpChunk(
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

    def _assemble_jpeg(self, pending: PendingUdpFrame) -> bytes | None:
        try:
            jpeg_bytes = b"".join(pending.chunks[index] for index in range(pending.total_chunks))
        except KeyError:
            return None
        if len(jpeg_bytes) != pending.jpeg_size:
            return None
        return jpeg_bytes

    def _enforce_pending_limit(self):
        while len(self._pending) > self.max_frames_buffer:
            self._pending.popitem(last=False)
            self.dropped_incomplete_frames += 1


CONFIG = {
    "udp_bind": "0.0.0.0",
    "udp_port": 5007,
    "udp_allowed_host": "",
    "topic": "/turtlebot_5007/image_raw",
    "frame_id": "turtlebot_5007_camera",
    "qos_depth": 1,
    "udp_timeout_sec": 0.5,
    "max_frames_buffer": 32,
    "socket_buffer": 4194304,
    "publish_fps": 0.0,
    "compressed": True,
    "jpeg_quality": 80,
    "resize_width": 1280,
    "resize_height": 960,
    "log_interval": 1.0,
}


class UdpToRosImageBridgeNode(Node):
    def __init__(self, config: dict):
        super().__init__("udp_to_ros_image_bridge")
        self.config = config
        self._last_published_seq: int | None = None
        self._publish_window_count = 0
        self._publish_window_started = time.monotonic()
        self._last_log_at = time.monotonic()
        self._last_publish_at = 0.0

        msg_type = CompressedImage if config["compressed"] else Image
        self._publisher = self.create_publisher(
            msg_type,
            config["topic"],
            image_qos(config["qos_depth"]),
        )
        self.udp_input = UdpLatestFrameInput(
            bind=config["udp_bind"],
            port=config["udp_port"],
            allowed_host=config["udp_allowed_host"],
            timeout_sec=config["udp_timeout_sec"],
            max_frames_buffer=config["max_frames_buffer"],
            socket_buffer=config["socket_buffer"],
            logger=self.get_logger(),
        )

        if config["publish_fps"] > 0:
            publish_interval = 1.0 / config["publish_fps"]
        else:
            publish_interval = 0.005

        self.create_timer(publish_interval, self.publish_latest)
        self.create_timer(max(config["udp_timeout_sec"] / 2.0, 0.1), self.udp_input.cleanup_pending)
        self.create_timer(max(config["log_interval"], 0.1), self.log_stats)

        allowed_host = config["udp_allowed_host"] or "any"
        self.get_logger().info(
            "UDP to ROS Image bridge started "
            f"bind={config['udp_bind']}:{config['udp_port']} "
            f"allowed_host={allowed_host} "
            f"topic={config['topic']} "
            f"frame_id={config['frame_id'] or '<from-udp>'} "
            f"publish_fps={config['publish_fps']} "
            f"compressed={config['compressed']} "
            f"resize={config['resize_width']}x{config['resize_height']} "
            f"jpeg_quality={config['jpeg_quality']}"
        )

    def resolve_frame_id(self, latest_frame_id: str) -> str:
        configured = self.config["frame_id"]
        if configured:
            return configured
        return latest_frame_id

    def publish_latest(self) -> None:
        if self.config["publish_fps"] > 0:
            now = time.monotonic()
            min_interval = 1.0 / self.config["publish_fps"]
            if now - self._last_publish_at < min_interval:
                return

        latest = self.udp_input.get_latest()
        if latest is None or self._last_published_seq == latest.frame_seq:
            return

        stamp = self.get_clock().now().to_msg()
        frame_id = self.resolve_frame_id(latest.frame_id)
        frame = self.resize_frame(latest.frame)
        if self.config["compressed"]:
            self._publisher.publish(
                self.cv_to_compressed_imgmsg(frame, frame_id, stamp, self.config["jpeg_quality"])
            )
        else:
            self._publisher.publish(cv_to_imgmsg(frame, frame_id, stamp))

        self._last_published_seq = latest.frame_seq
        self._publish_window_count += 1
        self._last_publish_at = time.monotonic()

    def resize_frame(self, frame):
        target_width = int(self.config["resize_width"] or 0)
        target_height = int(self.config["resize_height"] or 0)
        if target_width <= 0 and target_height <= 0:
            return frame
        height, width = frame.shape[:2]
        if target_width <= 0:
            target_width = max(1, int(round(width * target_height / height)))
        if target_height <= 0:
            target_height = max(1, int(round(height * target_width / width)))
        if width == target_width and height == target_height:
            return frame
        import cv2
        return cv2.resize(frame, (target_width, target_height), interpolation=cv2.INTER_AREA)

    @staticmethod
    def cv_to_compressed_imgmsg(frame, frame_id: str, stamp, jpeg_quality: int) -> CompressedImage:
        import cv2
        quality = max(1, min(100, int(jpeg_quality)))
        ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), quality])
        if not ok:
            raise RuntimeError("JPEG compression failed")
        msg = CompressedImage()
        msg.header.stamp = stamp
        msg.header.frame_id = frame_id
        msg.format = "jpeg"
        msg.data = encoded.tobytes()
        return msg

    def publish_fps(self) -> float:
        elapsed = max(time.monotonic() - self._publish_window_started, 1e-6)
        fps = self._publish_window_count / elapsed
        self._publish_window_count = 0
        self._publish_window_started = time.monotonic()
        return fps

    def log_stats(self) -> None:
        now = time.monotonic()
        if now - self._last_log_at < self.config["log_interval"]:
            return

        self.get_logger().info(
            "udp-to-ros-image-bridge stats "
            f"rx_packets={self.udp_input.rx_packets} "
            f"rx_frames={self.udp_input.rx_frames} "
            f"input_fps={self.udp_input.input_fps():.2f} "
            f"publish_fps={self.publish_fps():.2f} "
            f"dropped_packets={self.udp_input.dropped_packets} "
            f"dropped_incomplete_frames={self.udp_input.dropped_incomplete_frames} "
            f"duplicate_chunks={self.udp_input.duplicate_chunks} "
            f"pending_count={self.udp_input.pending_count} "
            f"last_sender_host={self.udp_input.last_sender_host or 'none'} "
            f"topic={self.config['topic']} "
            f"compressed={self.config['compressed']}"
        )
        self._last_log_at = now

    def destroy_node(self) -> None:
        self.udp_input.stop()
        super().destroy_node()


def build_config(args: argparse.Namespace) -> dict:
    config = dict(CONFIG)
    overrides = (
        ("udp_bind", "udp_bind"),
        ("udp_port", "udp_port"),
        ("udp_allowed_host", "udp_allowed_host"),
        ("topic", "topic"),
        ("frame_id", "frame_id"),
        ("qos_depth", "qos_depth"),
        ("udp_timeout_sec", "udp_timeout_sec"),
        ("max_frames_buffer", "max_frames_buffer"),
        ("socket_buffer", "socket_buffer"),
        ("publish_fps", "publish_fps"),
        ("compressed", "compressed"),
        ("jpeg_quality", "jpeg_quality"),
        ("resize_width", "resize_width"),
        ("resize_height", "resize_height"),
        ("log_interval", "log_interval"),
    )
    for key, arg_name in overrides:
        value = getattr(args, arg_name, None)
        if value is not None:
            config[key] = value
    return config


def parse_bool(value):
    if isinstance(value, bool):
        return value
    normalized = str(value).strip().lower()
    if normalized in {"1", "true", "yes", "y", "on"}:
        return True
    if normalized in {"0", "false", "no", "n", "off"}:
        return False
    raise argparse.ArgumentTypeError(f"Invalid boolean value: {value}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Receive TurtleBot UDP JPEG chunks and publish ROS2 sensor_msgs/Image."
    )
    parser.add_argument("--udp-bind", default=None)
    parser.add_argument("--udp-port", type=int, default=None)
    parser.add_argument("--udp-allowed-host", default=None)
    parser.add_argument("--topic", default=None)
    parser.add_argument("--frame-id", default=None)
    parser.add_argument("--qos-depth", type=int, default=None)
    parser.add_argument("--udp-timeout-sec", type=float, default=None)
    parser.add_argument("--max-frames-buffer", type=int, default=None)
    parser.add_argument("--socket-buffer", type=int, default=None)
    parser.add_argument("--publish-fps", type=float, default=None)
    parser.add_argument("--compressed", type=parse_bool, nargs="?", const=True, default=None)
    parser.add_argument("--no-compressed", dest="compressed", action="store_false")
    parser.add_argument("--jpeg-quality", type=int, default=None)
    parser.add_argument("--resize-width", type=int, default=None)
    parser.add_argument("--resize-height", type=int, default=None)
    parser.add_argument("--log-interval", type=float, default=None)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = build_config(args)

    if config["udp_port"] <= 0:
        raise ValueError("udp_port must be greater than 0")
    if config["qos_depth"] <= 0:
        raise ValueError("qos_depth must be greater than 0")
    if config["udp_timeout_sec"] <= 0:
        raise ValueError("udp_timeout_sec must be greater than 0")
    if config["max_frames_buffer"] <= 0:
        raise ValueError("max_frames_buffer must be greater than 0")
    if config["socket_buffer"] <= 0:
        raise ValueError("socket_buffer must be greater than 0")
    if config["publish_fps"] < 0:
        raise ValueError("publish_fps must be >= 0")
    if config["jpeg_quality"] < 1 or config["jpeg_quality"] > 100:
        raise ValueError("jpeg_quality must be between 1 and 100")
    if config["resize_width"] < 0 or config["resize_height"] < 0:
        raise ValueError("resize_width and resize_height must be >= 0")
    if config["log_interval"] <= 0:
        raise ValueError("log_interval must be greater than 0")

    rclpy.init()
    node = UdpToRosImageBridgeNode(config)
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


# Usage examples:
#
# Direct:
#   python3 ./udp_to_ros_image_bridge.py \
#     --udp-port 5007 \
#     --topic /turtlebot_5007/image_raw \
#     --compressed true \
#     --resize-width 1280 \
#     --resize-height 960
#
# Wrapper:
#   ./scripts/udp_to_ros_image_bridge \
#     --udp-port 5007 \
#     --topic /turtlebot_5007/image_raw
#
# Object detection node (later):
#   ... --live-topic /turtlebot_5007/image_raw
