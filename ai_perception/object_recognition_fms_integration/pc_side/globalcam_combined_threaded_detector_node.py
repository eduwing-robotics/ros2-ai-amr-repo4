from __future__ import annotations

import argparse
import json
import os
import threading
import time
from pathlib import Path

import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import Image
from std_msgs.msg import String

from map_line_reference import MapLineReference, add_map_line_arguments
from pc_side.globalcam_combined_detector_node import (
    GlobalCamCombinedDetectorNode,
    UdpLatestFrameInput,
    image_qos,
    parse_bool,
)
from pc_side.globalcam_object_map_node import (
    DEFAULT_SAFETY_MODEL,
    SafetyEventDetector,
)
from pc_side.globalcam_turtlebot_proximity_node import (
    DEFAULT_TURTLEBOT_MODEL,
    STATE_NORMAL,
    ProximityAlertTracker,
    TurtlebotProximityDetector,
)


def configure_cpu_runtime(args: argparse.Namespace, logger) -> None:
    thread_count = max(int(args.torch_num_threads), 1)
    os.environ.setdefault("OMP_NUM_THREADS", str(thread_count))
    os.environ.setdefault("MKL_NUM_THREADS", str(thread_count))
    os.environ.setdefault("OPENBLAS_NUM_THREADS", str(thread_count))
    os.environ.setdefault("NUMEXPR_NUM_THREADS", str(thread_count))

    try:
        import torch

        torch.set_num_threads(thread_count)
        if args.torch_num_interop_threads > 0:
            torch.set_num_interop_threads(int(args.torch_num_interop_threads))
    except Exception as exc:
        logger.warning(f"Failed to configure torch threads: {exc}")

    try:
        import cv2

        cv2.setNumThreads(max(int(args.opencv_num_threads), 0))
    except Exception as exc:
        logger.warning(f"Failed to configure OpenCV threads: {exc}")

    logger.info(
        "CPU runtime threads "
        f"torch={thread_count} "
        f"interop={args.torch_num_interop_threads} "
        f"opencv={args.opencv_num_threads}"
    )


class GlobalCamCombinedThreadedDetectorNode(GlobalCamCombinedDetectorNode):
    """UDP input, YOLO inference, and annotated publishing run independently."""

    def __init__(self, args: argparse.Namespace):
        Node.__init__(self, "globalcam_combined_threaded_detector_node")
        self.args = args
        configure_cpu_runtime(args, self.get_logger())
        self.input_mode = "udp"
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
        self.last_safety_seq: int | None = None
        self.last_turtlebot_seq: int | None = None
        self.detector_processed_frames = 0
        self._detector_window_count = 0
        self._detector_window_started = time.monotonic()
        self._last_stats_at = time.monotonic()
        self._stop_event = threading.Event()

        self.safety_detector = None
        self.turtlebot_detector = None
        self.proximity_tracker = None

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

        if not args.enable_safety_detector and not args.enable_turtlebot_proximity:
            raise ValueError("At least one detector must be enabled")

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        if self.map_line is None:
            self.get_logger().warn("Map line disabled; map_position will not be available.")

        qos = image_qos(args.image_qos_depth)
        self.safety_event_pub = (
            self.create_publisher(String, args.event_topic, 10)
            if args.enable_safety_detector
            else None
        )
        self.alert_pub = (
            self.create_publisher(String, args.alert_topic, 10)
            if args.enable_turtlebot_proximity
            else None
        )
        self.annotated_pub = (
            self.create_publisher(Image, args.annotated_topic, qos)
            if args.publish_annotated
            else None
        )

        self.udp_input = UdpLatestFrameInput(
            bind=args.udp_bind,
            port=args.udp_port,
            allowed_host=args.udp_allowed_host,
            timeout_sec=args.udp_timeout_sec,
            max_frames_buffer=args.udp_max_frames_buffer,
            socket_buffer=args.udp_socket_buffer,
            logger=self.get_logger(),
        )

        self._inference_thread = threading.Thread(
            target=self._inference_loop,
            name="globalcam-threaded-yolo",
            daemon=True,
        )
        self._annotated_thread = threading.Thread(
            target=self._annotated_loop,
            name="globalcam-threaded-annotated",
            daemon=True,
        )
        self._stats_thread = threading.Thread(
            target=self._stats_loop,
            name="globalcam-threaded-stats",
            daemon=True,
        )
        self._inference_thread.start()
        if args.publish_annotated:
            self._annotated_thread.start()
        self._stats_thread.start()

        self.get_logger().info(
            f"input_mode=udp-threaded udp_bind={args.udp_bind} udp_port={args.udp_port} "
            f"allowed_host={args.udp_allowed_host or 'any'} annotated_fps={args.udp_annotated_fps}"
        )
        self.get_logger().info(
            f"camera_id={args.camera_id} safety={args.enable_safety_detector} "
            f"turtlebot={args.enable_turtlebot_proximity}"
        )
        if self.safety_event_pub:
            self.get_logger().info(f"Publishing safety events: {args.event_topic}")
        if self.alert_pub:
            self.get_logger().info(f"Publishing proximity alerts: {args.alert_topic}")
        if self.annotated_pub:
            self.get_logger().info(f"Publishing threaded annotated image: {args.annotated_topic}")

    def _inference_loop(self):
        while not self._stop_event.is_set():
            latest = self.udp_input.get_latest()
            if latest is None:
                time.sleep(0.005)
                continue

            now = time.monotonic()
            ran_detector = False
            map_line = None

            if (
                self.args.enable_safety_detector
                and latest.frame_seq != self.last_safety_seq
                and self.should_run(now, self.last_safety_at, self.args.safety_fps, True)
            ):
                map_line = self.update_map_line(latest.frame)
                self.last_safety_at = now
                self.last_safety_seq = latest.frame_seq
                self.run_safety_pipeline(
                    latest.frame,
                    map_line,
                    msg=None,
                    timestamp_ns=latest.timestamp_ns,
                )
                ran_detector = True

            if (
                self.args.enable_turtlebot_proximity
                and latest.frame_seq != self.last_turtlebot_seq
                and self.should_run(now, self.last_turtlebot_at, self.args.turtlebot_fps, True)
            ):
                if map_line is None:
                    map_line = self.update_map_line(latest.frame)
                self.last_turtlebot_at = now
                self.last_turtlebot_seq = latest.frame_seq
                self.run_turtlebot_pipeline(latest.frame, map_line)
                ran_detector = True

            if ran_detector:
                self.detector_processed_frames += 1
                self._detector_window_count += 1
            else:
                time.sleep(0.003)

    def _annotated_loop(self):
        period = 1.0 / max(min(self.args.udp_annotated_fps, 60.0), 1.0)
        next_at = time.monotonic()
        while not self._stop_event.is_set():
            latest = self.udp_input.get_latest()
            if latest is not None:
                self.last_image_at = latest.received_monotonic
                self.publish_annotated_frame(latest.frame, latest.frame_id)
            next_at += period
            sleep_for = next_at - time.monotonic()
            if sleep_for > 0:
                time.sleep(sleep_for)
            else:
                next_at = time.monotonic()

    def _stats_loop(self):
        while not self._stop_event.is_set():
            time.sleep(max(self.args.log_interval, 0.1))
            self.udp_input.cleanup_pending()
            input_fps = self.udp_input.input_fps()
            now = time.monotonic()
            elapsed = max(now - self._detector_window_started, 1e-6)
            detector_fps = self._detector_window_count / elapsed
            self._detector_window_count = 0
            self._detector_window_started = now
            self.get_logger().info(
                "udp-threaded stats "
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
                f"current_pending_frames={self.udp_input.pending_count}"
            )

    def publish_safety_event(self, event: dict):
        if self.safety_event_pub is None:
            return
        self.last_safety_event = event
        now = time.monotonic()
        if now - self.last_safety_emit_at < self.args.emit_interval:
            return
        self.safety_event_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
        self.last_safety_emit_at = now

    def destroy_node(self):
        self._stop_event.set()
        for thread in (self._inference_thread, self._annotated_thread, self._stats_thread):
            if thread.is_alive():
                thread.join(timeout=1.0)
        if self.udp_input is not None:
            self.udp_input.stop()
        Node.destroy_node(self)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Threaded GlobalCam UDP combined safety + turtlebot proximity detector."
    )
    parser.add_argument("--udp-bind", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5005)
    parser.add_argument("--udp-allowed-host", default="")
    parser.add_argument("--udp-timeout-sec", type=float, default=0.5)
    parser.add_argument("--udp-max-frames-buffer", type=int, default=32)
    parser.add_argument("--udp-socket-buffer", type=int, default=4194304)
    parser.add_argument("--udp-annotated-fps", type=float, default=30.0)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--event-topic", default="/globalcam/object_map/events")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument("--annotated-topic", default="/globalcam/combined/annotated_image")
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--device-id", default="edge-dev-001")
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument("--enable-safety-detector", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-safety-detector", dest="enable_safety_detector", action="store_false")
    parser.add_argument("--enable-turtlebot-proximity", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-turtlebot-proximity", dest="enable_turtlebot_proximity", action="store_false")
    parser.add_argument("--safety-model-path", default=DEFAULT_SAFETY_MODEL)
    parser.add_argument("--turtlebot-model-path", default=DEFAULT_TURTLEBOT_MODEL)
    parser.add_argument("--safety-device", default="auto")
    parser.add_argument("--turtlebot-device", default="auto")
    parser.add_argument("--safety-confidence", type=float, default=0.6)
    parser.add_argument("--turtlebot-confidence", type=float, default=0.6)
    parser.add_argument("--safety-imgsz", type=int, default=1280)
    parser.add_argument("--turtlebot-imgsz", type=int, default=1280)
    parser.add_argument("--safety-fps", type=float, default=2.0)
    parser.add_argument("--turtlebot-fps", type=float, default=2.0)
    parser.add_argument("--torch-num-threads", type=int, default=2)
    parser.add_argument("--torch-num-interop-threads", type=int, default=1)
    parser.add_argument("--opencv-num-threads", type=int, default=1)
    parser.add_argument("--proximity-enter-distance", type=float, default=0.2)
    parser.add_argument("--proximity-exit-distance", type=float, default=0.3)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--emit-interval", type=float, default=0.2)
    parser.add_argument("--publish-annotated", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--show-debug-overlay", action="store_true", default=False)
    parser.add_argument("--include-outside-objects", action="store_true", default=False)
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamCombinedThreadedDetectorNode(args)
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
