from __future__ import annotations

import argparse
import json
import math
import sys
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from itertools import combinations
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


TURTLEBOT_MODEL_SEARCH_ROOTS = (
    Path("/home/gyul/yolo_test/runs"),
    Path("/home/gyul/yolo_test/models"),
)
TURTLEBOT_MODEL_EXCLUDE_KEYWORDS = (
    "hardhat",
    "safety",
    "fire",
    "fall",
    "helmet",
    "head",
)
TURTLEBOT_CLASS_NAMES = {"turtlebot", "turtlebot3-burger", "turtlebot3_burger"}

ALERT_SCHEMA_VERSION = "globalcam_turtlebot_proximity.v1"
STATE_NORMAL = "normal"
STATE_TOO_CLOSE = "too_close"
TURTLEBOT_COLOR = (255, 160, 30)


def find_latest_turtlebot_model() -> Path:
    candidates: list[Path] = []
    for root in TURTLEBOT_MODEL_SEARCH_ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*.pt"):
            path_text = str(path).lower()
            if any(keyword in path_text for keyword in TURTLEBOT_MODEL_EXCLUDE_KEYWORDS):
                continue
            if path.name == "best.pt" and (
                "turtlebot" in path_text or "tb3" in path_text or "burger" in path_text
            ):
                candidates.append(path)

    if not candidates:
        raise FileNotFoundError(
            "No turtlebot-only model found under "
            f"{', '.join(str(path) for path in TURTLEBOT_MODEL_SEARCH_ROOTS)}"
        )
    return max(candidates, key=lambda path: path.stat().st_mtime)


DEFAULT_TURTLEBOT_MODEL = str(find_latest_turtlebot_model())


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


def map_distance(a: dict, b: dict) -> float:
    ax, ay = a["map_xy"]
    bx, by = b["map_xy"]
    return math.hypot(ax - bx, ay - by)


def closest_turtlebot_pair(detections: list[dict]) -> tuple[float, dict, dict] | None:
    mapped = [
        detection
        for detection in detections
        if detection.get("map_position") is not None
        and detection["map_position"].get("map_xy") is not None
    ]
    if len(mapped) < 2:
        return None

    best_distance = None
    best_pair = None
    for left, right in combinations(mapped, 2):
        distance = map_distance(left["map_position"], right["map_position"])
        if best_distance is None or distance < best_distance:
            best_distance = distance
            best_pair = (left, right)
    if best_distance is None or best_pair is None:
        return None
    return best_distance, best_pair[0], best_pair[1]


def pair_to_alert_payload(left: dict, right: dict) -> list[dict]:
    return [
        {
            "index": 0,
            "map_x": left["map_position"]["x"],
            "map_y": left["map_position"]["y"],
            "confidence": left["confidence"],
        },
        {
            "index": 1,
            "map_x": right["map_position"]["x"],
            "map_y": right["map_position"]["y"],
            "confidence": right["confidence"],
        },
    ]


@dataclass
class ProximityAlertTracker:
    enter_distance: float
    exit_distance: float
    state: str = STATE_NORMAL

    def update(self, distance: float | None, pair: tuple[dict, dict] | None) -> dict | None:
        if distance is None or pair is None:
            return None

        if self.state == STATE_NORMAL:
            if distance <= self.enter_distance:
                self.state = STATE_TOO_CLOSE
                return {
                    "state": "too_close",
                    "distance": round(distance, 3),
                    "pair": pair,
                }
            return None

        if self.state == STATE_TOO_CLOSE and distance >= self.exit_distance:
            self.state = STATE_NORMAL
            return {
                "state": "cleared",
                "distance": round(distance, 3),
                "pair": pair,
            }
        return None


@dataclass
class TurtlebotDetection:
    confidence: float
    bbox_xyxy: list[int]
    map_position: dict | None = None
    map_reference_px: list[float] | None = None

    @property
    def center_px(self) -> list[float]:
        x1, y1, x2, y2 = self.bbox_xyxy
        return [round((x1 + x2) / 2.0, 1), round((y1 + y2) / 2.0, 1)]

    def to_internal_dict(self) -> dict:
        return {
            "class": "turtlebot",
            "confidence": round(self.confidence, 3),
            "bbox_xyxy": self.bbox_xyxy,
            "center_px": self.center_px,
            "map_reference_px": self.map_reference_px,
            "map_position": self.map_position,
        }


class TurtlebotProximityDetector:
    def __init__(self, model_path: str, confidence: float, imgsz: int, device: str):
        try:
            from ultralytics import YOLO
            import torch
        except ImportError as exc:
            raise RuntimeError(
                "Turtlebot proximity detector requires ultralytics and torch in face-env."
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

    def is_turtlebot_class(self, class_name: str) -> bool:
        normalized = class_name.strip().lower().replace("_", "-")
        if normalized in TURTLEBOT_CLASS_NAMES:
            return True
        return "turtlebot" in normalized

    def detect(self, frame: np.ndarray) -> list[TurtlebotDetection]:
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
            class_id = int(box.cls[0])
            class_name = str(self.model.names[class_id])
            if not self.is_turtlebot_class(class_name):
                continue
            confidence = float(box.conf[0])
            bbox = clamp_bbox(box.xyxy[0].tolist(), width, height)
            if bbox is None:
                continue
            detections.append(TurtlebotDetection(confidence, bbox))
        detections.sort(key=lambda item: item.confidence, reverse=True)
        return detections


class GlobalCamTurtlebotProximityNode(Node):
    def __init__(self, args):
        super().__init__("globalcam_turtlebot_proximity_node")
        self.args = args
        self.last_detector_at = 0.0
        self.last_map_line = None
        self.last_draw_detections: list[dict] = []
        self.last_closest_pair: tuple[dict, dict] | None = None
        self.last_distance: float | None = None
        self.last_proximity_state = STATE_NORMAL
        self.tracker = ProximityAlertTracker(
            args.proximity_enter_distance,
            args.proximity_exit_distance,
        )

        model_path = Path(args.turtlebot_model_path)
        if not model_path.exists():
            raise FileNotFoundError(f"Turtlebot model not found: {model_path}")
        self.detector = TurtlebotProximityDetector(
            str(model_path),
            args.turtlebot_confidence,
            args.turtlebot_imgsz,
            args.turtlebot_device,
        )
        self.get_logger().info(
            f"Loaded turtlebot proximity model={model_path} device={self.detector.device} "
            f"names={self.detector.model_names}"
        )

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        if self.map_line is None:
            self.get_logger().warn("Map line disabled; proximity distance cannot be computed.")

        msg_type = CompressedImage if args.image_compressed else Image
        qos = image_qos(args.image_qos_depth)
        self.image_subscription = self.create_subscription(
            msg_type,
            args.image_topic,
            self.on_image,
            qos,
        )
        self.alert_pub = self.create_publisher(String, args.alert_topic, 10)
        self.annotated_pub = None
        if args.publish_annotated:
            self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos)

        self.create_timer(8.0, self.log_waiting_for_image)
        self.get_logger().info(
            f"Listening {args.image_topic} ({msg_type.__name__}) camera_id={args.camera_id}"
        )
        self.get_logger().info(f"Publishing alerts: {args.alert_topic}")
        if args.publish_annotated:
            self.get_logger().info(f"Publishing annotated image: {args.annotated_topic}")
        self.get_logger().info(
            "Proximity hysteresis enter="
            f"{args.proximity_enter_distance} exit={args.proximity_exit_distance}"
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
        if now - self.last_detector_at < 1.0 / max(self.args.turtlebot_fps, 0.1):
            if self.args.publish_annotated:
                self.publish_annotated(frame, msg)
            return

        self.last_detector_at = now
        map_line = self.update_map_line(frame)
        try:
            detections = self.detector.detect(frame)
        except Exception as exc:
            self.get_logger().error(f"Turtlebot detector failed: {exc}")
            return

        enriched = self.enrich_detections(detections, map_line)
        self.last_draw_detections = enriched
        self.evaluate_proximity(enriched)

        if self.args.publish_annotated:
            self.publish_annotated(frame, msg)

    def update_map_line(self, frame: np.ndarray):
        if self.map_line is None:
            return None
        self.last_map_line = self.map_line.update(frame)
        return self.last_map_line

    def enrich_detections(self, detections: list[TurtlebotDetection], map_line) -> list[dict]:
        payloads = []
        for detection in detections:
            payload = detection.to_internal_dict()
            if self.map_line is not None and map_line is not None:
                self.map_line.enrich_detection(payload, map_line)
            payloads.append(payload)
        return payloads

    def evaluate_proximity(self, detections: list[dict]):
        closest = closest_turtlebot_pair(detections)
        if closest is None:
            self.last_distance = None
            self.last_closest_pair = None
            self.last_proximity_state = self.tracker.state
            return

        distance, left, right = closest
        self.last_distance = distance
        self.last_closest_pair = (left, right)
        alert = self.tracker.update(distance, (left, right))
        self.last_proximity_state = self.tracker.state
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

    def publish_annotated(self, frame, msg):
        if self.annotated_pub is None:
            return
        annotated = self.draw_overlay(frame)
        self.annotated_pub.publish(cv_to_imgmsg(annotated, msg.header.frame_id, msg.header.stamp))

    def draw_overlay(self, frame: np.ndarray):
        annotated = frame.copy()
        map_line_payload = self.last_map_line

        if self.map_line is not None and map_line_payload is not None:
            square_ready = map_line_payload.get("square_corners_px") is not None
            if square_ready:
                live_map_line = map_line_payload
                if live_map_line.get("_square_points_np") is None and square_ready:
                    live_map_line = dict(live_map_line)
                    live_map_line["_square_points_np"] = np.array(
                        map_line_payload["square_corners_px"], dtype=np.float32
                    )
                self.map_line.draw(annotated, live_map_line)
            else:
                self.draw_status_banner(annotated, "map line not initialized")
        elif self.map_line is not None:
            self.draw_status_banner(annotated, "map line not initialized")

        for detection in self.last_draw_detections:
            self.draw_detection(annotated, detection)

        if self.last_closest_pair is not None and self.last_distance is not None:
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

        state_text = self.last_proximity_state
        if self.last_distance is not None:
            state_text = f"{self.last_proximity_state} dist={self.last_distance:.3f}"
        self.draw_status_banner(annotated, f"proximity: {state_text}")
        return annotated

    @staticmethod
    def draw_status_banner(frame: np.ndarray, text: str):
        cv2.rectangle(frame, (12, 12), (520, 54), (30, 30, 30), -1)
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
        x1, y1, x2, y2 = detection["bbox_xyxy"]
        cv2.rectangle(frame, (x1, y1), (x2, y2), TURTLEBOT_COLOR, 2)

        label = f"turtlebot {detection['confidence']:.2f}"
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
        description="GlobalCam turtlebot proximity alerts using map-line coordinates."
    )
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--image-compressed", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-image-compressed", dest="image_compressed", action="store_false")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument(
        "--annotated-topic",
        default="/globalcam/turtlebot_proximity/annotated_image",
    )
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument("--turtlebot-model-path", default=DEFAULT_TURTLEBOT_MODEL)
    parser.add_argument("--turtlebot-device", default="auto")
    parser.add_argument("--turtlebot-confidence", type=float, default=0.6)
    parser.add_argument("--turtlebot-imgsz", type=int, default=1280)
    parser.add_argument("--turtlebot-fps", type=float, default=5.0)
    parser.add_argument("--proximity-enter-distance", type=float, default=0.2)
    parser.add_argument("--proximity-exit-distance", type=float, default=0.3)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--publish-annotated", action=argparse.BooleanOptionalAction, default=True)
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    if args.proximity_exit_distance <= args.proximity_enter_distance:
        raise SystemExit("proximity-exit-distance must be greater than proximity-enter-distance")
    rclpy.init()
    node = GlobalCamTurtlebotProximityNode(args)
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
