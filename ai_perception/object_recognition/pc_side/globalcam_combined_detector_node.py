from __future__ import annotations

import argparse
import json
import socket
import struct
import sys
import threading
import time
import uuid
from collections import OrderedDict
from dataclasses import dataclass, field
from pathlib import Path

YOLO_TEST_DIR = Path("/home/gyul/yolo_test")
if str(YOLO_TEST_DIR) not in sys.path:
    sys.path.insert(0, str(YOLO_TEST_DIR))

import cv2
import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from map_line_reference import MapLineReference, add_map_line_arguments
from pc_side.globalcam_object_map_node import (
    DEFAULT_SAFETY_MODEL,
    EVENT_SCHEMA_VERSION,
    CLASS_COLORS,
    SafetyEventDetector,
    SafetyDetectionResult,
    compressed_imgmsg_to_cv,
    publish_server_ready_event,
    utc_now_iso,
)
from pc_side.globalcam_turtlebot_proximity_node import (
    ALERT_SCHEMA_VERSION,
    DEFAULT_TURTLEBOT_MODEL,
    STATE_NORMAL,
    TURTLEBOT_COLOR,
    ProximityAlertTracker,
    TurtlebotProximityDetector,
    closest_turtlebot_pair,
    pair_to_alert_payload,
)
from pc_side.ros_image_utils import cv_to_imgmsg, imgmsg_to_cv


MAGIC = b"GCM1"
VERSION = 1
HEADER_FORMAT = "!4sBHIQHHIHHB"
HEADER_SIZE = struct.calcsize(HEADER_FORMAT)


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
            name="globalcam-udp-direct-recv",
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
            _, oldest = self._pending.popitem(last=False)
            self.dropped_incomplete_frames += 1
            _ = oldest


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


def parse_bool(value):
    if isinstance(value, bool):
        return value
    normalized = str(value).strip().lower()
    if normalized in {"1", "true", "yes", "y", "on"}:
        return True
    if normalized in {"0", "false", "no", "n", "off"}:
        return False
    raise argparse.ArgumentTypeError(f"Invalid boolean value: {value}")


class GlobalCamCombinedDetectorNode(Node):
    def __init__(self, args):
        super().__init__("globalcam_combined_detector_node")
        self.args = args
        self.input_mode = args.input_mode
        self.last_image_at = 0.0
        self.last_safety_at = 0.0
        self.last_turtlebot_at = 0.0
        self.last_safety_emit_at = 0.0
        self.last_map_line = None
        self.last_safety_event = None
        self.last_safety_detections: list[dict] = []
        self.last_turtlebot_detections: list[dict] = []
        self.last_closest_pair = None
        self.last_distance: float | None = None
        self.last_proximity_state = STATE_NORMAL
        self.last_udp_processed_seq: int | None = None
        self.last_udp_annotated_seq: int | None = None
        self.last_annotated_frame: np.ndarray | None = None
        self.last_annotated_frame_id = "globalcam"
        self.detector_processed_frames = 0
        self._detector_window_count = 0
        self._detector_window_started = time.monotonic()
        self._last_udp_log_at = time.monotonic()

        self.safety_detector = None
        self.turtlebot_detector = None
        self.proximity_tracker = None
        self.udp_input: UdpLatestFrameInput | None = None
        self.image_subscription = None

        if args.enable_safety_detector:
            safety_model = Path(args.safety_model_path)
            if not safety_model.exists():
                raise FileNotFoundError(f"Safety model not found: {safety_model}")
            self.safety_detector = SafetyEventDetector(
                str(safety_model),
                args.safety_confidence,
                args.safety_imgsz,
                args.safety_device,
            )
            self.get_logger().info(
                f"Loaded safety model={safety_model} device={self.safety_detector.device} "
                f"names={self.safety_detector.model_names}"
            )

        if args.enable_turtlebot_proximity:
            if args.proximity_exit_distance <= args.proximity_enter_distance:
                raise ValueError("proximity-exit-distance must be greater than proximity-enter-distance")
            turtlebot_model = Path(args.turtlebot_model_path)
            if not turtlebot_model.exists():
                raise FileNotFoundError(f"Turtlebot model not found: {turtlebot_model}")
            self.turtlebot_detector = TurtlebotProximityDetector(
                str(turtlebot_model),
                args.turtlebot_confidence,
                args.turtlebot_imgsz,
                args.turtlebot_device,
            )
            self.proximity_tracker = ProximityAlertTracker(
                args.proximity_enter_distance,
                args.proximity_exit_distance,
            )
            self.get_logger().info(
                f"Loaded turtlebot model={turtlebot_model} device={self.turtlebot_detector.device} "
                f"names={self.turtlebot_detector.model_names}"
            )
            self.get_logger().info(
                "Proximity hysteresis enter="
                f"{args.proximity_enter_distance} exit={args.proximity_exit_distance}"
            )

        if not args.enable_safety_detector and not args.enable_turtlebot_proximity:
            raise ValueError("At least one of safety detector or turtlebot proximity must be enabled")

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        if self.map_line is None:
            self.get_logger().warn("Map line disabled; map_position will not be available.")

        qos = image_qos(args.image_qos_depth)
        if self.input_mode == "ros-topic":
            msg_type = CompressedImage if args.image_compressed else Image
            self.image_subscription = self.create_subscription(
                msg_type,
                args.image_topic,
                self.on_image,
                qos,
            )
            self.create_timer(8.0, self.log_waiting_for_image)
        elif self.input_mode == "udp":
            self.udp_input = UdpLatestFrameInput(
                bind=args.udp_bind,
                port=args.udp_port,
                allowed_host=args.udp_allowed_host,
                timeout_sec=args.udp_timeout_sec,
                max_frames_buffer=args.udp_max_frames_buffer,
                socket_buffer=args.udp_socket_buffer,
                logger=self.get_logger(),
            )
            detector_fps = max(
                self.args.safety_fps if self.args.enable_safety_detector else 0.0,
                self.args.turtlebot_fps if self.args.enable_turtlebot_proximity else 0.0,
                1.0,
            )
            self.create_timer(1.0 / detector_fps, self._on_udp_detector_tick)
            if self.args.publish_annotated:
                annotated_fps = max(min(args.udp_annotated_fps, 60.0), 1.0)
                self.create_timer(1.0 / annotated_fps, self._on_udp_annotated_tick)
            self.create_timer(max(args.log_interval, 0.1), self._log_udp_stats)
            self.create_timer(max(args.udp_timeout_sec / 2.0, 0.1), self._cleanup_udp_pending)
            self.get_logger().info(
                f"input_mode=udp udp_bind={args.udp_bind} udp_port={args.udp_port} "
                f"allowed_host={args.udp_allowed_host} timeout_sec={args.udp_timeout_sec} "
                f"socket_buffer={args.udp_socket_buffer}"
            )
        else:
            raise ValueError(f"Unsupported input mode: {self.input_mode}")

        self.safety_event_pub = None
        if args.enable_safety_detector:
            self.safety_event_pub = self.create_publisher(String, args.event_topic, 10)
        self.alert_pub = None
        if args.enable_turtlebot_proximity:
            self.alert_pub = self.create_publisher(String, args.alert_topic, 10)
        self.annotated_pub = None
        if args.publish_annotated:
            self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos)

        if self.input_mode == "ros-topic":
            msg_type = CompressedImage if args.image_compressed else Image
            self.get_logger().info(
                f"input_mode=ros-topic listening {args.image_topic} ({msg_type.__name__}) "
                f"camera_id={args.camera_id} safety={args.enable_safety_detector} "
                f"turtlebot={args.enable_turtlebot_proximity}"
            )
        else:
            self.get_logger().info(
                f"camera_id={args.camera_id} safety={args.enable_safety_detector} "
                f"turtlebot={args.enable_turtlebot_proximity}"
            )
        if self.safety_event_pub:
            self.get_logger().info(f"Publishing safety events: {args.event_topic}")
        if self.alert_pub:
            self.get_logger().info(f"Publishing proximity alerts: {args.alert_topic}")
        if self.annotated_pub:
            self.get_logger().info(f"Publishing combined annotated image: {args.annotated_topic}")

    def log_waiting_for_image(self):
        if self.input_mode != "ros-topic":
            return
        if self.last_image_at <= 0:
            self.get_logger().info(
                f"Waiting for image topic: {self.args.image_topic}",
                throttle_duration_sec=8.0,
            )

    def on_image(self, msg: Image | CompressedImage):
        try:
            frame = compressed_imgmsg_to_cv(msg) if self.args.image_compressed else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(
                f"Image conversion failed topic={self.args.image_topic}: {exc}"
            )
            return

        self.last_image_at = time.monotonic()
        self.process_frame(
            frame,
            msg=msg,
            frame_id=msg.header.frame_id,
            timestamp_ns=None,
        )

    def _on_udp_detector_tick(self):
        if self.udp_input is None:
            return

        latest = self.udp_input.get_latest()
        if latest is None:
            return

        self.last_image_at = latest.received_monotonic
        self.last_annotated_frame = latest.frame
        self.last_annotated_frame_id = latest.frame_id

        if self.last_udp_processed_seq == latest.frame_seq:
            if self.args.publish_annotated:
                self.publish_annotated_frame(latest.frame, latest.frame_id)
            return

        self.last_udp_processed_seq = latest.frame_seq
        self.process_frame(
            latest.frame,
            msg=None,
            frame_id=latest.frame_id,
            timestamp_ns=latest.timestamp_ns,
        )
        self.detector_processed_frames += 1
        self._detector_window_count += 1

    def _on_udp_annotated_tick(self):
        if self.udp_input is None or not self.args.publish_annotated:
            return

        latest = self.udp_input.get_latest()
        if latest is None or self.last_udp_annotated_seq == latest.frame_seq:
            return

        self.last_udp_annotated_seq = latest.frame_seq
        self.publish_annotated_frame(latest.frame, latest.frame_id)

    def _cleanup_udp_pending(self):
        if self.udp_input is not None:
            self.udp_input.cleanup_pending()

    def _log_udp_stats(self):
        if self.udp_input is None:
            return
        now = time.monotonic()
        if now - self._last_udp_log_at < self.args.log_interval:
            return

        input_fps = self.udp_input.input_fps()
        elapsed = max(now - self._detector_window_started, 1e-6)
        detector_fps = self._detector_window_count / elapsed
        self._detector_window_count = 0
        self._detector_window_started = now

        pending_count = self.udp_input.pending_count
        self.get_logger().info(
            "udp stats "
            f"rx_packets={self.udp_input.rx_packets} "
            f"rx_frames={self.udp_input.rx_frames} "
            f"allowed_host={self.udp_input.allowed_host or 'any'} "
            f"last_sender={self.udp_input.last_sender_host or 'none'} "
            f"last_rejected_sender={self.udp_input.last_rejected_sender_host or 'none'} "
            f"dropped_packets={self.udp_input.dropped_packets} "
            f"dropped_incomplete_frames={self.udp_input.dropped_incomplete_frames} "
            f"duplicate_chunks={self.udp_input.duplicate_chunks} "
            f"detector_processed_frames={self.detector_processed_frames} "
            f"input_fps={input_fps:.2f} "
            f"detector_fps={detector_fps:.2f} "
            f"current_pending_frames={pending_count}"
        )
        self._last_udp_log_at = now

    def process_frame(
        self,
        frame: np.ndarray,
        msg: Image | CompressedImage | None = None,
        frame_id: str = "globalcam",
        timestamp_ns: int | None = None,
    ):
        now = time.monotonic()
        map_line = self.update_map_line(frame)

        if self.should_run(now, self.last_safety_at, self.args.safety_fps, self.args.enable_safety_detector):
            self.last_safety_at = now
            self.run_safety_pipeline(frame, map_line, msg=msg, timestamp_ns=timestamp_ns)

        if self.should_run(
            now,
            self.last_turtlebot_at,
            self.args.turtlebot_fps,
            self.args.enable_turtlebot_proximity,
        ):
            self.last_turtlebot_at = now
            self.run_turtlebot_pipeline(frame, map_line)

        if self.args.publish_annotated:
            self.publish_annotated_frame(frame, frame_id, msg=msg)

    @staticmethod
    def should_run(now: float, last_at: float, fps: float, enabled: bool) -> bool:
        if not enabled:
            return False
        return now - last_at >= 1.0 / max(fps, 0.1)

    def update_map_line(self, frame: np.ndarray):
        if self.map_line is None:
            return None
        self.last_map_line = self.map_line.update(frame)
        return self.last_map_line

    def enrich_safety_detections(
        self,
        detections: list[SafetyDetectionResult],
        map_line,
    ) -> list[dict]:
        payloads = []
        for detection in detections:
            payload = detection.to_payload()
            if self.map_line is not None and map_line is not None:
                self.map_line.enrich_detection(payload, map_line)
            payloads.append(payload)
        return payloads

    def enrich_turtlebot_detections(self, detections, map_line) -> list[dict]:
        payloads = []
        for detection in detections:
            payload = detection.to_internal_dict()
            if self.map_line is not None and map_line is not None:
                self.map_line.enrich_detection(payload, map_line)
            payloads.append(payload)
        return payloads

    def filter_event_detections(self, payloads: list[dict]) -> list[dict]:
        return [payload for payload in payloads if self.should_include_detection(payload)]

    def should_include_detection(self, payload: dict) -> bool:
        map_position = payload.get("map_position")
        if map_position is None:
            return False
        if self.args.include_outside_objects:
            return True
        return bool(map_position.get("inside"))

    def run_safety_pipeline(
        self,
        frame,
        map_line,
        msg: Image | CompressedImage | None = None,
        timestamp_ns: int | None = None,
    ):
        started = time.perf_counter()
        try:
            detections = self.safety_detector.detect(frame)
        except Exception as exc:
            event = self.build_safety_event(
                frame,
                [],
                map_line,
                (time.perf_counter() - started) * 1000.0,
                msg=msg,
                timestamp_ns=timestamp_ns,
            )
            event["state"] = "detector_error"
            event["message"] = "안전 이벤트 탐지 모델 오류"
            event["error"] = str(exc)
            self.publish_safety_event(event)
            return

        all_detections = self.enrich_safety_detections(detections, map_line)
        self.last_safety_detections = all_detections
        event = self.build_safety_event(
            frame,
            all_detections,
            map_line,
            (time.perf_counter() - started) * 1000.0,
            msg=msg,
            timestamp_ns=timestamp_ns,
        )
        self.publish_safety_event(event)

    def build_safety_event(
        self,
        frame: np.ndarray,
        all_detections: list[dict],
        map_line,
        detector_ms: float,
        msg: Image | CompressedImage | None = None,
        timestamp_ns: int | None = None,
    ):
        height, width = frame.shape[:2]
        if msg is not None:
            captured_stamp = {
                "sec": int(msg.header.stamp.sec),
                "nanosec": int(msg.header.stamp.nanosec),
            }
            source_topic = self.args.image_topic
        else:
            stamp_ns = int(timestamp_ns or 0)
            captured_stamp = {
                "sec": stamp_ns // 1_000_000_000,
                "nanosec": stamp_ns % 1_000_000_000,
            }
            source_topic = f"udp://{self.args.udp_allowed_host}:{self.args.udp_port}"

        return {
            "schema_version": EVENT_SCHEMA_VERSION,
            "event_id": f"evt_{uuid.uuid4().hex}",
            "device_id": self.args.device_id,
            "camera_id": self.args.camera_id,
            "source_topic": source_topic,
            "created_at": utc_now_iso(),
            "captured_stamp": captured_stamp,
            "image_size": {"width": width, "height": height},
            "map_line": MapLineReference.serializable(map_line),
            "safety_detections": self.filter_event_detections(all_detections),
            "timing": {"detector_ms": round(detector_ms, 1)},
        }

    def publish_safety_event(self, event: dict):
        self.last_safety_event = event
        now = time.monotonic()
        if now - self.last_safety_emit_at < self.args.emit_interval:
            return
        self.safety_event_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
        publish_server_ready_event(event)
        self.last_safety_emit_at = now

    def run_turtlebot_pipeline(self, frame, map_line):
        try:
            detections = self.turtlebot_detector.detect(frame)
        except Exception as exc:
            self.get_logger().error(f"Turtlebot detector failed: {exc}")
            return

        enriched = self.enrich_turtlebot_detections(detections, map_line)
        self.last_turtlebot_detections = enriched
        self.evaluate_proximity(enriched)

    def evaluate_proximity(self, detections: list[dict]):
        closest = closest_turtlebot_pair(detections)
        if closest is None:
            self.last_distance = None
            self.last_closest_pair = None
            if self.proximity_tracker is not None:
                self.last_proximity_state = self.proximity_tracker.state
            return

        distance, left, right = closest
        self.last_distance = distance
        self.last_closest_pair = (left, right)
        alert = self.proximity_tracker.update(distance, (left, right))
        self.last_proximity_state = self.proximity_tracker.state
        if alert is not None:
            self.publish_alert(alert)

    def build_alert_event(self, alert: dict) -> dict:
        left, right = alert["pair"]
        state = alert["state"]
        distance = alert["distance"]
        if state == "too_close":
            message = f"터틀봇 2대가 가까워졌습니다. 거리={distance:.3f}m"
        else:
            message = f"터틀봇 간 거리가 충분히 멀어졌습니다. 거리={distance:.3f}m"

        return {
            "schema_version": ALERT_SCHEMA_VERSION,
            "event_id": f"alert_{uuid.uuid4().hex}",
            "camera_id": self.args.camera_id,
            "created_at": utc_now_iso(),
            "state": state,
            "distance": distance,
            "threshold": {
                "enter": self.args.proximity_enter_distance,
                "exit": self.args.proximity_exit_distance,
            },
            "turtlebot_pair": pair_to_alert_payload(left, right),
            "message": message,
        }

    def publish_alert(self, alert: dict):
        event = self.build_alert_event(alert)
        self.alert_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
        self.get_logger().info(
            f"Proximity alert state={event['state']} distance={event['distance']:.3f} "
            f"pair={event['turtlebot_pair']}"
        )

    def publish_annotated_frame(
        self,
        frame: np.ndarray,
        frame_id: str,
        msg: Image | CompressedImage | None = None,
    ):
        if self.annotated_pub is None:
            return
        annotated = self.draw_overlay(frame)
        stamp = msg.header.stamp if msg is not None else self.get_clock().now().to_msg()
        self.annotated_pub.publish(cv_to_imgmsg(annotated, frame_id, stamp))

    def draw_overlay(self, frame: np.ndarray):
        annotated = frame.copy()
        debug = self.args.show_debug_overlay

        if self.map_line is not None and self.last_map_line is not None:
            map_line_payload = self.last_map_line
            square_ready = map_line_payload.get("square_corners_px") is not None
            if square_ready:
                live_map_line = map_line_payload
                if live_map_line.get("_square_points_np") is None:
                    live_map_line = dict(live_map_line)
                    live_map_line["_square_points_np"] = np.array(
                        map_line_payload["square_corners_px"], dtype=np.float32
                    )
                self.map_line.draw(annotated, live_map_line)
            elif debug:
                self.draw_status_banner(annotated, 12, "map line not initialized")
        elif debug and self.map_line is not None:
            self.draw_status_banner(annotated, 12, "map line not initialized")

        for detection in self.last_safety_detections:
            self.draw_safety_detection(annotated, detection, debug=debug)

        for detection in self.last_turtlebot_detections:
            self.draw_turtlebot_detection(annotated, detection, debug=debug)

        if debug and self.last_closest_pair is not None and self.last_distance is not None:
            left, right = self.last_closest_pair
            p1 = tuple(int(round(value)) for value in left["center_px"])
            p2 = tuple(int(round(value)) for value in right["center_px"])
            cv2.line(annotated, p1, p2, (0, 255, 255), 2)
            mid_x = int(round((p1[0] + p2[0]) / 2))
            mid_y = int(round((p1[1] + p2[1]) / 2))
            cv2.putText(
                annotated,
                f"dist={self.last_distance:.3f}",
                (mid_x + 6, mid_y - 6),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 255, 255),
                2,
                cv2.LINE_AA,
            )

        if debug:
            proximity_text = self.last_proximity_state
            if self.last_distance is not None:
                proximity_text = f"{self.last_proximity_state} dist={self.last_distance:.3f}"
            self.draw_status_banner(annotated, 58, f"proximity: {proximity_text}")
        return annotated

    @staticmethod
    def draw_status_banner(frame: np.ndarray, top: int, text: str):
        cv2.rectangle(frame, (12, top), (620, top + 42), (30, 30, 30), -1)
        cv2.putText(
            frame,
            text,
            (24, top + 29),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.75,
            (0, 180, 255),
            2,
            cv2.LINE_AA,
        )

    @staticmethod
    def draw_compact_label(frame: np.ndarray, x1: int, y1: int, label: str, color):
        font = cv2.FONT_HERSHEY_SIMPLEX
        scale = 0.42
        thickness = 1
        (text_w, text_h), _ = cv2.getTextSize(label, font, scale, thickness)
        y_text = max(text_h + 4, y1 - 4)
        cv2.rectangle(
            frame,
            (x1, y_text - text_h - 2),
            (x1 + text_w + 4, y_text + 2),
            color,
            -1,
        )
        cv2.putText(
            frame,
            label,
            (x1 + 2, y_text),
            font,
            scale,
            (255, 255, 255),
            thickness,
            cv2.LINE_AA,
        )

    def draw_safety_detection(self, frame: np.ndarray, detection: dict, debug: bool = False):
        bbox = detection.get("bbox") or detection.get("bbox_xyxy")
        if bbox is None:
            return
        x1, y1, x2, y2 = bbox
        class_name = detection["class"]
        color = CLASS_COLORS.get(class_name, (200, 200, 200))
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)

        label = f"{class_name} {detection['confidence']:.2f}"
        if debug:
            map_position = detection.get("map_position")
            if map_position is not None:
                label = f"{label} x={map_position['x']:.3f} y={map_position['y']:.3f}"
            cv2.putText(
                frame,
                label,
                (x1, max(18, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                color,
                2,
                cv2.LINE_AA,
            )
            center = detection.get("center_px")
            if center is not None:
                cx, cy = [int(round(value)) for value in center]
                cv2.circle(frame, (cx, cy), 4, color, -1)
            if self.map_line is not None:
                self.map_line.draw_detection_reference(frame, detection)
        else:
            self.draw_compact_label(frame, x1, y1, label, color)

    def draw_turtlebot_detection(self, frame: np.ndarray, detection: dict, debug: bool = False):
        x1, y1, x2, y2 = detection["bbox_xyxy"]
        cv2.rectangle(frame, (x1, y1), (x2, y2), TURTLEBOT_COLOR, 2)

        label = f"turtlebot {detection['confidence']:.2f}"
        if debug:
            map_position = detection.get("map_position")
            if map_position is not None:
                label = f"{label} x={map_position['x']:.3f} y={map_position['y']:.3f}"
            cv2.putText(
                frame,
                label,
                (x1, max(18, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                TURTLEBOT_COLOR,
                2,
                cv2.LINE_AA,
            )
            center = detection.get("center_px")
            if center is not None:
                cx, cy = [int(round(value)) for value in center]
                cv2.circle(frame, (cx, cy), 4, TURTLEBOT_COLOR, -1)
            if self.map_line is not None:
                self.map_line.draw_detection_reference(frame, detection)
        else:
            self.draw_compact_label(frame, x1, y1, label, TURTLEBOT_COLOR)

    def destroy_node(self):
        if self.udp_input is not None:
            self.udp_input.stop()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(
        description="GlobalCam combined safety + turtlebot proximity detector."
    )
    parser.add_argument("--input-mode", choices=("ros-topic", "udp"), default="ros-topic")
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--image-compressed", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-image-compressed", dest="image_compressed", action="store_false")
    parser.add_argument("--udp-bind", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5005)
    parser.add_argument("--udp-allowed-host", default="192.168.40.10")
    parser.add_argument("--udp-timeout-sec", type=float, default=0.5)
    parser.add_argument("--udp-max-frames-buffer", type=int, default=32)
    parser.add_argument("--udp-socket-buffer", type=int, default=4194304)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--event-topic", default="/globalcam/object_map/events")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument("--annotated-topic", default="/globalcam/combined/annotated_image")
    parser.add_argument("--device-id", default="edge-dev-001")
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument(
        "--enable-safety-detector",
        type=parse_bool,
        nargs="?",
        const=True,
        default=True,
    )
    parser.add_argument(
        "--no-enable-safety-detector",
        dest="enable_safety_detector",
        action="store_false",
    )
    parser.add_argument(
        "--enable-turtlebot-proximity",
        type=parse_bool,
        nargs="?",
        const=True,
        default=True,
    )
    parser.add_argument(
        "--no-enable-turtlebot-proximity",
        dest="enable_turtlebot_proximity",
        action="store_false",
    )
    parser.add_argument("--safety-model-path", default=DEFAULT_SAFETY_MODEL)
    parser.add_argument("--turtlebot-model-path", default=DEFAULT_TURTLEBOT_MODEL)
    parser.add_argument("--safety-device", default="auto")
    parser.add_argument("--turtlebot-device", default="auto")
    parser.add_argument("--safety-confidence", type=float, default=0.6)
    parser.add_argument("--turtlebot-confidence", type=float, default=0.6)
    parser.add_argument("--safety-imgsz", type=int, default=1280)
    parser.add_argument("--turtlebot-imgsz", type=int, default=1280)
    parser.add_argument("--safety-fps", type=float, default=5.0)
    parser.add_argument("--turtlebot-fps", type=float, default=5.0)
    parser.add_argument("--proximity-enter-distance", type=float, default=0.2)
    parser.add_argument("--proximity-exit-distance", type=float, default=0.3)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--emit-interval", type=float, default=0.2)
    parser.add_argument("--publish-annotated", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--udp-annotated-fps", type=float, default=30.0)
    parser.add_argument("--show-debug-overlay", action="store_true", default=False)
    parser.add_argument("--include-outside-objects", action="store_true", default=False)
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamCombinedDetectorNode(args)
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
