from __future__ import annotations

import argparse
import json
import sys
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
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
from pc_side.ros_image_utils import cv_to_imgmsg, imgmsg_to_cv


SAFETY_MODEL_SEARCH_ROOTS = (
    Path("/home/gyul/yolo_test/safety_fire_fall_model"),
    Path("/home/gyul/yolo_test/models"),
    Path("/home/gyul/yolo_test"),
    Path("/home/gyul/Downloads"),
)
SAFETY_MODEL_KEYWORDS = ("safety", "fire", "fall", "helmet", "hardhat", "fallen")

CLASS_COLORS = {
    "helmet": (0, 180, 0),
    "no_helmet": (0, 0, 255),
    "head": (0, 0, 255),
    "fire": (0, 90, 255),
    "fall": (255, 0, 255),
    "fallen_worker": (255, 0, 255),
    "fall_detected": (255, 0, 255),
    "person": (255, 180, 0),
}


def find_latest_safety_model() -> Path:
    candidates: list[Path] = []
    for root in SAFETY_MODEL_SEARCH_ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*.pt"):
            path_text = str(path).lower()
            if path.name == "best.pt" or any(keyword in path_text for keyword in SAFETY_MODEL_KEYWORDS):
                candidates.append(path)

    if not candidates:
        raise FileNotFoundError(
            "No safety/fire/fall model found under "
            f"{', '.join(str(path) for path in SAFETY_MODEL_SEARCH_ROOTS)}"
        )

    best_weights = [path for path in candidates if path.name == "best.pt"]
    pool = best_weights if best_weights else candidates
    return max(pool, key=lambda path: path.stat().st_mtime)


DEFAULT_SAFETY_MODEL = str(find_latest_safety_model())
EVENT_SCHEMA_VERSION = "globalcam_safety_event.v1"


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def compressed_imgmsg_to_cv(msg: CompressedImage):
    np_arr = np.frombuffer(msg.data, dtype=np.uint8)
    frame = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
    if frame is None:
        raise ValueError("Failed to decode compressed image")
    return frame


def clamp_bbox(bbox, width: int, height: int) -> list[int] | None:
    x1, y1, x2, y2 = [int(round(value)) for value in bbox]
    x1 = max(0, min(width - 1, x1))
    y1 = max(0, min(height - 1, y1))
    x2 = max(0, min(width - 1, x2))
    y2 = max(0, min(height - 1, y2))
    if x2 <= x1 or y2 <= y1:
        return None
    return [x1, y1, x2, y2]


@dataclass
class SafetyDetectionResult:
    class_name: str
    confidence: float
    bbox_xyxy: list[int]
    map_reference_px: list[float] | None = None
    map_position: dict | None = None

    @property
    def area(self) -> int:
        x1, y1, x2, y2 = self.bbox_xyxy
        return max(0, x2 - x1) * max(0, y2 - y1)

    @property
    def center_px(self) -> list[float]:
        x1, y1, x2, y2 = self.bbox_xyxy
        return [round((x1 + x2) / 2.0, 1), round((y1 + y2) / 2.0, 1)]

    def to_payload(self):
        return {
            "class": self.class_name,
            "confidence": round(self.confidence, 3),
            "bbox": self.bbox_xyxy,
            "center_px": self.center_px,
            "map_reference_px": self.map_reference_px,
            "map_position": self.map_position,
        }


class SafetyEventDetector:
    def __init__(self, model_path: str, confidence: float, imgsz: int, device: str):
        try:
            from ultralytics import YOLO
            import torch
        except ImportError as exc:
            raise RuntimeError(
                "Safety event detector requires ultralytics and torch in face-env."
            ) from exc

        if device == "auto":
            device = "cuda:0" if torch.cuda.is_available() else "cpu"
        self.device = device
        self.confidence = confidence
        self.imgsz = imgsz
        self.model = YOLO(str(model_path))

    @property
    def model_names(self) -> dict:
        return dict(getattr(self.model, "names", {}) or {})

    def detect(self, frame: np.ndarray) -> list[SafetyDetectionResult]:
        height, width = frame.shape[:2]
        results = self.model.predict(
            frame,
            conf=self.confidence,
            imgsz=self.imgsz,
            device=self.device,
            verbose=False,
        )
        if not results or results[0].boxes is None:
            return []

        detections = []
        for box in results[0].boxes:
            confidence = float(box.conf[0])
            class_id = int(box.cls[0])
            class_name = str(self.model.names[class_id])
            bbox = clamp_bbox(box.xyxy[0].tolist(), width, height)
            if bbox is None:
                continue
            detections.append(SafetyDetectionResult(class_name, confidence, bbox))
        detections.sort(key=lambda item: (item.confidence, item.area), reverse=True)
        return detections


def prepare_server_payload(event: dict) -> dict:
    """Shape a ROS event dict for future HTTP/MQTT upload."""
    return {
        "schema_version": event.get("schema_version"),
        "event_id": event.get("event_id"),
        "device_id": event.get("device_id"),
        "camera_id": event.get("camera_id"),
        "source_topic": event.get("source_topic"),
        "created_at": event.get("created_at"),
        "image_size": event.get("image_size"),
        "map_line": event.get("map_line"),
        "safety_detections": event.get("safety_detections", []),
        "timing": event.get("timing"),
    }


def publish_server_ready_event(event: dict) -> None:
    # TODO: Wire HTTP/MQTT upload here once the backend endpoint is available.
    _ = prepare_server_payload(event)


class GlobalCamObjectMapNode(Node):
    def __init__(self, args):
        super().__init__("globalcam_object_map_node")
        self.args = args
        self.last_detector_at = 0.0
        self.last_emit_at = 0.0
        self.last_event = None
        self.last_map_line = None
        self.last_draw_detections: list[dict] = []

        model_path = Path(args.safety_model_path)
        if not model_path.exists():
            raise FileNotFoundError(f"Safety model not found: {model_path}")
        self.detector = SafetyEventDetector(
            str(model_path),
            args.safety_confidence,
            args.safety_imgsz,
            args.safety_device,
        )
        self.get_logger().info(
            f"Loaded safety event model={model_path} device={self.detector.device} "
            f"names={self.detector.model_names}"
        )

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        if self.map_line is None:
            self.get_logger().warn("Map line disabled; map_position will not be available.")

        msg_type = CompressedImage if args.image_compressed else Image
        qos = image_qos(args.image_qos_depth)
        self.image_subscription = self.create_subscription(
            msg_type,
            args.image_topic,
            self.on_image,
            qos,
        )
        self.event_pub = self.create_publisher(String, args.event_topic, 10)
        self.annotated_pub = None
        if args.publish_annotated:
            self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos)

        self.create_timer(8.0, self.log_waiting_for_image)
        self.get_logger().info(
            f"Listening {args.image_topic} ({msg_type.__name__}) "
            f"camera_id={args.camera_id} compressed={args.image_compressed}"
        )
        self.get_logger().info(f"Publishing events: {args.event_topic}")
        if args.publish_annotated:
            self.get_logger().info(f"Publishing annotated image: {args.annotated_topic}")
        if self.map_line:
            self.get_logger().info(
                "Map line enabled with cached revalidation every "
                f"{args.map_line_recheck_interval}s."
            )

    def log_waiting_for_image(self):
        if self.last_detector_at <= 0:
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

        now = time.monotonic()
        if now - self.last_detector_at < 1.0 / max(self.args.safety_fps, 0.1):
            if self.args.publish_annotated and self.last_event is not None:
                self.publish_annotated(frame, self.last_event, msg)
            return

        self.last_detector_at = now
        started = time.perf_counter()
        map_line = self.update_map_line(frame)
        try:
            detections = self.detector.detect(frame)
        except Exception as exc:
            event = self.build_event(msg, frame, [], map_line, (time.perf_counter() - started) * 1000.0)
            event["state"] = "detector_error"
            event["message"] = "안전 이벤트 탐지 모델 오류"
            event["error"] = str(exc)
            self.publish_event(event, frame, msg)
            return

        event = self.build_event(
            msg,
            frame,
            detections,
            map_line,
            (time.perf_counter() - started) * 1000.0,
        )
        self.publish_event(event, frame, msg)

    def update_map_line(self, frame: np.ndarray):
        if self.map_line is None:
            return None
        self.last_map_line = self.map_line.update(frame)
        return self.last_map_line

    def enrich_all_detections(
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

    def filter_event_detections(self, payloads: list[dict]) -> list[dict]:
        return [payload for payload in payloads if self.should_include_detection(payload)]

    def should_include_detection(self, payload: dict) -> bool:
        map_position = payload.get("map_position")
        if map_position is None:
            return False
        if self.args.include_outside_objects:
            return True
        return bool(map_position.get("inside"))

    def build_event(
        self,
        msg: Image | CompressedImage,
        frame: np.ndarray,
        detections: list[SafetyDetectionResult],
        map_line,
        detector_ms: float,
    ):
        height, width = frame.shape[:2]
        all_detections = self.enrich_all_detections(detections, map_line)
        self.last_draw_detections = all_detections
        return {
            "schema_version": EVENT_SCHEMA_VERSION,
            "event_id": f"evt_{uuid.uuid4().hex}",
            "device_id": self.args.device_id,
            "camera_id": self.args.camera_id,
            "source_topic": self.args.image_topic,
            "created_at": utc_now_iso(),
            "captured_stamp": {
                "sec": int(msg.header.stamp.sec),
                "nanosec": int(msg.header.stamp.nanosec),
            },
            "image_size": {"width": width, "height": height},
            "map_line": MapLineReference.serializable(map_line),
            "safety_detections": self.filter_event_detections(all_detections),
            "timing": {"detector_ms": round(detector_ms, 1)},
        }

    def publish_event(self, event, frame, msg):
        self.last_event = event
        now = time.monotonic()
        if now - self.last_emit_at >= self.args.emit_interval:
            self.event_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
            publish_server_ready_event(event)
            self.last_emit_at = now
        if self.args.publish_annotated:
            self.publish_annotated(frame, event, msg)

    def publish_annotated(self, frame, event, msg):
        if self.annotated_pub is None:
            return
        annotated = self.draw_overlay(frame, event)
        self.annotated_pub.publish(cv_to_imgmsg(annotated, msg.header.frame_id, msg.header.stamp))

    def draw_overlay(self, frame: np.ndarray, event: dict):
        annotated = frame.copy()
        map_line_payload = event.get("map_line")

        if self.map_line is not None and map_line_payload is not None:
            square_ready = map_line_payload.get("square_corners_px") is not None
            if square_ready:
                live_map_line = self.last_map_line or map_line_payload
                if live_map_line.get("_square_points_np") is None and square_ready:
                    live_map_line = dict(live_map_line)
                    live_map_line["_square_points_np"] = np.array(
                        map_line_payload["square_corners_px"], dtype=np.float32
                    )
                self.map_line.draw(annotated, live_map_line)
            else:
                self.draw_map_line_status(annotated, "map line not initialized")
        else:
            self.draw_map_line_status(annotated, "map line not initialized")

        for detection in self.last_draw_detections:
            self.draw_detection(annotated, detection)

        return annotated

    @staticmethod
    def draw_map_line_status(frame: np.ndarray, text: str):
        cv2.rectangle(frame, (12, 12), (430, 54), (30, 30, 30), -1)
        cv2.putText(
            frame,
            text,
            (24, 41),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.75,
            (0, 180, 255),
            2,
            cv2.LINE_AA,
        )

    def draw_detection(self, frame: np.ndarray, detection: dict):
        bbox = detection.get("bbox") or detection.get("bbox_xyxy")
        if bbox is None:
            return
        x1, y1, x2, y2 = bbox
        class_name = detection["class"]
        color = CLASS_COLORS.get(class_name, (200, 200, 200))
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)

        label = f"{class_name} {detection['confidence']:.2f}"
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


def parse_bool(value):
    if isinstance(value, bool):
        return value
    normalized = str(value).strip().lower()
    if normalized in {"1", "true", "yes", "y", "on"}:
        return True
    if normalized in {"0", "false", "no", "n", "off"}:
        return False
    raise argparse.ArgumentTypeError(f"Invalid boolean value: {value}")


def parse_args():
    parser = argparse.ArgumentParser(
        description="GlobalCam safety event detection with ArUco map-line coordinate projection."
    )
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--image-compressed", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-image-compressed", dest="image_compressed", action="store_false")
    parser.add_argument("--event-topic", default="/globalcam/object_map/events")
    parser.add_argument("--annotated-topic", default="/globalcam/object_map/annotated_image")
    parser.add_argument("--device-id", default="edge-dev-001")
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument("--safety-model-path", default=DEFAULT_SAFETY_MODEL)
    parser.add_argument(
        "--detector-model-path",
        dest="safety_model_path",
        help=argparse.SUPPRESS,
    )
    parser.add_argument("--safety-device", default="auto")
    parser.add_argument("--detector-device", dest="safety_device", help=argparse.SUPPRESS)
    parser.add_argument("--safety-confidence", type=float, default=0.6)
    parser.add_argument("--detector-confidence", dest="safety_confidence", type=float, help=argparse.SUPPRESS)
    parser.add_argument("--safety-imgsz", type=int, default=1280)
    parser.add_argument("--detector-imgsz", dest="safety_imgsz", type=int, help=argparse.SUPPRESS)
    parser.add_argument("--safety-fps", type=float, default=5.0)
    parser.add_argument("--detector-fps", dest="safety_fps", type=float, help=argparse.SUPPRESS)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--emit-interval", type=float, default=0.2)
    parser.add_argument("--publish-annotated", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--include-outside-objects", action="store_true", default=False)
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamObjectMapNode(args)
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
