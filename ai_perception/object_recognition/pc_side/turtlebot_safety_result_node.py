from __future__ import annotations

import argparse
import json
import math
import queue
import threading
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from types import SimpleNamespace

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import Image
from std_msgs.msg import String

from pc_side.globalcam_combined_detector_node import image_qos, parse_bool
from pc_side.globalcam_combined_threaded_detector_node import configure_cpu_runtime
from pc_side.globalcam_object_map_node import (
    CLASS_COLORS,
    SafetyDetectionResult,
    SafetyEventDetector,
    utc_now_iso,
)
from pc_side.ros_image_utils import cv_to_imgmsg, imgmsg_to_cv


DETECTIONS_SCHEMA_VERSION = "turtlebot_safety_detections.v1"
EVENT_SCHEMA_VERSION = "turtlebot_safety_event.v1"
SERVER_EVENT_SCHEMA_VERSION = "turtlebot_server_safety_event.v1"
DEFAULT_SAFETY_MODEL = (
    "/home/gyul/yolo_test/runs/safety_continue45_plus_nohelmet_e20/weights/best.pt"
)
DEFAULT_PERSON_MODEL = "/home/gyul/yolo11n.pt"
EVENT_CLASS_MAP = {
    "fire": "fire",
    "fall": "fall",
    "fallen_worker": "fall",
    "fall_detected": "fall",
    "head": "no_helmet",
    "no_helmet": "no_helmet",
}
FACE_ROI_CLASSES = {"head", "no_helmet"}
# person is kept for overlay/debug; it must not become a safety/server event.
IGNORED_SAFETY_CLASSES: set[str] = set()
DISPLAY_ONLY_CLASSES = {"person"}
ROI_SAFETY_CLASSES = {"head", "no_helmet", "helmet"}
HELMET_CLASS_NAMES = {"helmet", "hardhat"}


@dataclass
class FaceJob:
    frame: object
    detection: dict
    roi_bbox_xyxy: list[int]
    queued_at: float
    generation: int


def is_similar_face_roi(current_bbox: list[int] | None, previous_bbox: list[int] | None) -> bool:
    if current_bbox is None or previous_bbox is None:
        return False

    cx1, cy1, cx2, cy2 = current_bbox
    px1, py1, px2, py2 = previous_bbox
    current_w = max(cx2 - cx1, 1)
    current_h = max(cy2 - cy1, 1)
    prev_w = max(px2 - px1, 1)
    prev_h = max(py2 - py1, 1)

    current_cx = (cx1 + cx2) / 2.0
    current_cy = (cy1 + cy2) / 2.0
    prev_cx = (px1 + px2) / 2.0
    prev_cy = (py1 + py2) / 2.0
    center_distance = math.hypot(current_cx - prev_cx, current_cy - prev_cy)
    diagonal = math.hypot(current_w, current_h)

    if center_distance > diagonal * 0.5:
        return False
    if abs(current_w - prev_w) > current_w * 0.4:
        return False
    if abs(current_h - prev_h) > current_h * 0.4:
        return False
    return True


def pick_representative_face_roi(frame, raw_detections, args) -> list[int] | None:
    height, width = frame.shape[:2]
    candidates: list[tuple[float, int, list[int]]] = []
    for detection in raw_detections:
        if str(detection.class_name) not in FACE_ROI_CLASSES:
            continue
        bbox = clamp_bbox_xyxy(detection.bbox_xyxy, width, height)
        if bbox is None:
            continue
        meets_threshold, _ = roi_meets_face_threshold(bbox, width, height, args)
        if not meets_threshold:
            continue
        x1, y1, x2, y2 = bbox
        area = (x2 - x1) * (y2 - y1)
        candidates.append((float(detection.confidence), area, bbox))

    if not candidates:
        return None
    candidates.sort(key=lambda item: (item[0], item[1]), reverse=True)
    return candidates[0][2]


def event_type_for_class(class_name: str) -> str | None:
    normalized = str(class_name).strip().lower().replace("-", "_")
    return EVENT_CLASS_MAP.get(normalized)


def server_event_type_for_class(class_name: str) -> str | None:
    normalized = str(class_name).strip().lower().replace("-", "_")
    if normalized == "helmet":
        return None
    return EVENT_CLASS_MAP.get(normalized)


def server_event_candidate(
    detection: dict,
    frame_width: int,
    frame_height: int,
    args,
) -> dict | None:
    event_type = server_event_type_for_class(str(detection.get("class", "")))
    if event_type is None:
        return None

    bbox = detection.get("bbox_xyxy")
    if not bbox or len(bbox) != 4:
        return None

    x1, y1, x2, y2 = [float(value) for value in bbox]
    bbox_width = max(x2 - x1, 1.0)
    bbox_height = y2 - y1

    middle_x1 = frame_width / 3.0
    middle_x2 = frame_width * 2.0 / 3.0
    overlap_width = max(0.0, min(x2, middle_x2) - max(x1, middle_x1))
    center_overlap_ratio = overlap_width / bbox_width
    bbox_center_x = (x1 + x2) / 2.0

    if event_type == "no_helmet":
        if not (middle_x1 <= bbox_center_x <= middle_x2):
            return None
    elif center_overlap_ratio < args.server_center_overlap_ratio:
        return None

    if event_type == "fire":
        required_height_ratio = args.server_fire_min_height_ratio
    elif event_type == "no_helmet":
        required_height_ratio = args.server_no_helmet_min_height_ratio
    else:
        required_height_ratio = args.server_fall_min_height_ratio

    required_height = frame_height * required_height_ratio
    if bbox_height < required_height:
        return None

    return {
        "event_type": event_type,
        "detection": detection,
        "condition": {
            "center_region_x1": middle_x1,
            "center_region_x2": middle_x2,
            "bbox_center_x": round(bbox_center_x, 1),
            "center_method": "bbox_center" if event_type == "no_helmet" else "bbox_overlap",
            "center_overlap_ratio": round(center_overlap_ratio, 4),
            "required_center_overlap_ratio": None
            if event_type == "no_helmet"
            else args.server_center_overlap_ratio,
            "bbox_height": round(bbox_height, 1),
            "required_bbox_height": round(required_height, 1),
            "height_ratio": round(bbox_height / max(frame_height, 1), 4),
            "required_height_ratio": required_height_ratio,
        },
    }


def roi_meets_face_threshold(bbox: list[int], width: int, height: int, args) -> tuple[bool, dict]:
    x1, y1, x2, y2 = bbox
    roi_width = x2 - x1
    roi_height = y2 - y1
    frame_area = max(width * height, 1)
    roi_area_ratio = (roi_width * roi_height) / frame_area
    metrics = {
        "roi_width": roi_width,
        "roi_height": roi_height,
        "roi_area_ratio": round(roi_area_ratio, 4),
    }
    meets = (
        roi_width >= args.face_min_roi_width
        and roi_height >= args.face_min_roi_height
        and roi_area_ratio >= args.face_min_roi_area_ratio
    )
    return meets, metrics


def skipped_face_result(bbox: list[int], metrics: dict) -> dict:
    return {
        "enabled": True,
        "status": "skipped",
        "identity_status": "skipped",
        "reason": "face_roi_too_small",
        "roi_bbox_xyxy": bbox,
        "roi_width": metrics["roi_width"],
        "roi_height": metrics["roi_height"],
        "roi_area_ratio": metrics["roi_area_ratio"],
        "updated_at": utc_now_iso(),
    }


def clamp_bbox_xyxy(bbox, width: int, height: int) -> list[int] | None:
    x1, y1, x2, y2 = [int(round(value)) for value in bbox]
    x1 = max(0, min(width - 1, x1))
    y1 = max(0, min(height - 1, y1))
    x2 = max(0, min(width - 1, x2))
    y2 = max(0, min(height - 1, y2))
    if x2 <= x1 or y2 <= y1:
        return None
    return [x1, y1, x2, y2]


def expand_bbox_xyxy(bbox: list[int], width: int, height: int, scale: float) -> list[int] | None:
    x1, y1, x2, y2 = bbox
    box_w = max(x2 - x1, 1)
    box_h = max(y2 - y1, 1)
    pad_x = box_w * max(scale - 1.0, 0.0) / 2.0
    pad_y = box_h * max(scale - 1.0, 0.0) / 2.0
    return clamp_bbox_xyxy([x1 - pad_x, y1 - pad_y, x2 + pad_x, y2 + pad_y], width, height)


def bbox_iou(left: list[int], right: list[int]) -> float:
    lx1, ly1, lx2, ly2 = left
    rx1, ry1, rx2, ry2 = right
    ix1 = max(lx1, rx1)
    iy1 = max(ly1, ry1)
    ix2 = min(lx2, rx2)
    iy2 = min(ly2, ry2)
    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    if inter <= 0:
        return 0.0
    left_area = max(0, lx2 - lx1) * max(0, ly2 - ly1)
    right_area = max(0, rx2 - rx1) * max(0, ry2 - ry1)
    return inter / max(left_area + right_area - inter, 1)


def dedupe_safety_detections(
    detections: list[SafetyDetectionResult],
    iou_threshold: float,
) -> list[SafetyDetectionResult]:
    ordered = sorted(detections, key=lambda item: float(item.confidence), reverse=True)
    kept: list[SafetyDetectionResult] = []
    for detection in ordered:
        class_name = str(detection.class_name).strip().lower().replace("-", "_")
        duplicate = False
        for existing in kept:
            existing_class = str(existing.class_name).strip().lower().replace("-", "_")
            if class_name == existing_class and bbox_iou(detection.bbox_xyxy, existing.bbox_xyxy) >= iou_threshold:
                duplicate = True
                break
        if not duplicate:
            kept.append(detection)
    return kept


def bbox_center(bbox: list[int]) -> tuple[float, float]:
    x1, y1, x2, y2 = bbox
    return ((x1 + x2) / 2.0, (y1 + y2) / 2.0)


def center_inside_bbox(inner_bbox: list[int], outer_bbox: list[int]) -> bool:
    cx, cy = bbox_center(inner_bbox)
    x1, y1, x2, y2 = outer_bbox
    return x1 <= cx <= x2 and y1 <= cy <= y2


def person_has_helmet(person_bbox: list[int], detections: list[SafetyDetectionResult]) -> bool:
    """Return True only when a helmet clearly belongs to THIS person.

    Matching is per-person (not global): another person's helmet must not skip
    ROI for a different person. Use helmet-center inside the upper body region.
    """
    px1, py1, px2, py2 = person_bbox
    person_h = max(py2 - py1, 1)
    # Helmet should sit on the head/upper torso, not anywhere that overlaps the body.
    head_region = [px1, py1, px2, py1 + int(person_h * 0.55)]
    for detection in detections:
        class_name = str(detection.class_name).strip().lower().replace("-", "_")
        if class_name not in HELMET_CLASS_NAMES:
            continue
        if center_inside_bbox(detection.bbox_xyxy, head_region):
            return True
    return False


@dataclass
class PersonDetection:
    confidence: float
    bbox_xyxy: list[int]

    @property
    def area(self) -> int:
        x1, y1, x2, y2 = self.bbox_xyxy
        return max(0, x2 - x1) * max(0, y2 - y1)


class CocoPersonDetector:
    def __init__(self, model_path: str, confidence: float, imgsz: int, device: str):
        try:
            from ultralytics import YOLO
            import torch
        except ImportError as exc:
            raise RuntimeError("Person detector requires ultralytics and torch in face-env.") from exc

        if device == "auto":
            device = "cuda:0" if torch.cuda.is_available() else "cpu"
        self.device = device
        self.confidence = confidence
        self.imgsz = imgsz
        self.model = YOLO(str(model_path))

    @property
    def model_names(self) -> dict:
        return dict(getattr(self.model, "names", {}) or {})

    def detect(self, frame) -> list[PersonDetection]:
        height, width = frame.shape[:2]
        results = self.model.predict(
            frame,
            conf=self.confidence,
            imgsz=self.imgsz,
            device=self.device,
            classes=[0],
            verbose=False,
        )
        if not results or results[0].boxes is None:
            return []

        detections: list[PersonDetection] = []
        for box in results[0].boxes:
            bbox = clamp_bbox_xyxy(box.xyxy[0].tolist(), width, height)
            if bbox is None:
                continue
            detections.append(PersonDetection(float(box.conf[0]), bbox))
        detections.sort(key=lambda item: (item.confidence, item.area), reverse=True)
        return detections


class FaceRecognitionWorker:
    def __init__(self, args, logger):
        self.args = args
        self.logger = logger
        self.enabled = bool(args.enable_face_recognition)
        self.error_reason: str | None = None
        self.face_busy = False
        self.last_face_started_at = 0.0
        self.last_face_completed_at = 0.0
        self.last_face_result: dict | None = None
        self.generation = 0
        self._last_reset_log_at = 0.0
        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.queue: queue.Queue[FaceJob] = queue.Queue(maxsize=1)
        self.temporal = None
        self.face_app = None
        self.identities = []
        self.face_args = None
        self.thread: threading.Thread | None = None

        if not self.enabled:
            return

        try:
            from face_recognize_webcam import load_registered_identities, recognize_frame
            from pc_side.perception_node import create_robot_face_app
            from pc_side.safety_identity_node import TemporalRecognizer

            self._recognize_frame = recognize_frame
            self.identities = load_registered_identities(Path(args.registered_dir))
            self.face_app = create_robot_face_app(args.providers, args.det_size)
            self.face_args = SimpleNamespace(
                single_face=True,
                similarity_threshold=args.face_similarity,
                margin_threshold=args.margin_threshold,
                top_k=args.top_k,
            )
            self.temporal = TemporalRecognizer(args.face_temporal_frames, args.face_temporal_votes)
            self.logger.info(
                f"Face recognition enabled identities={len(self.identities)} "
                f"providers={args.providers} det_size={args.det_size}"
            )
        except Exception as exc:
            self.error_reason = str(exc)
            self.logger.warning(f"Face recognition initialization failed; safety continues: {exc}")
            return

        self.thread = threading.Thread(
            target=self._worker_loop,
            name="turtlebot-face-worker",
            daemon=True,
        )
        self.thread.start()

    def shutdown(self):
        self.stop_event.set()
        if self.thread is not None and self.thread.is_alive():
            self.thread.join(timeout=1.0)

    def reset_cache(self, reason: str = "reset"):
        with self.lock:
            self.generation += 1
            self.last_face_result = None
            self.last_face_completed_at = 0.0
            if self.temporal is not None:
                self.temporal.reset()

        while True:
            try:
                self.queue.get_nowait()
            except queue.Empty:
                break

        now = time.monotonic()
        if now - self._last_reset_log_at >= 1.0:
            self.logger.info(f"Face cache reset reason={reason} generation={self.generation}")
            self._last_reset_log_at = now

    def status_result(self, status: str, reason: str | None = None, roi_bbox_xyxy=None) -> dict:
        payload = {
            "enabled": self.enabled,
            "status": status,
            "identity_status": status,
            "name": None,
            "employee_id": None,
            "similarity": None,
            "roi_bbox_xyxy": roi_bbox_xyxy,
            "updated_at": utc_now_iso(),
        }
        if reason:
            payload["reason"] = reason
        return payload

    def process_face_roi(self, frame, detection: dict) -> dict:
        roi_bbox = detection.get("bbox_xyxy")
        if not self.enabled:
            return self.status_result("disabled", "face_recognition_disabled", roi_bbox)
        if self.error_reason is not None:
            return self.status_result("error", self.error_reason, roi_bbox)

        now = time.monotonic()
        self._submit_if_ready(frame, detection, roi_bbox, now)

        with self.lock:
            if (
                self.last_face_result is not None
                and now - self.last_face_completed_at <= self.args.face_result_ttl
                and is_similar_face_roi(roi_bbox, self.last_face_result.get("roi_bbox_xyxy"))
            ):
                return dict(self.last_face_result)
            if self.face_busy:
                return self.status_result("pending", "face_worker_busy", roi_bbox)
            return self.status_result("pending", "waiting_for_face_worker", roi_bbox)

    def _submit_if_ready(self, frame, detection: dict, roi_bbox: list[int] | None, now: float):
        if roi_bbox is None:
            return
        if now - self.last_face_started_at < 1.0 / max(self.args.face_fps, 0.1):
            return
        with self.lock:
            job_generation = self.generation
            if self.face_busy:
                return

        while True:
            try:
                self.queue.get_nowait()
            except queue.Empty:
                break
        try:
            self.queue.put_nowait(
                FaceJob(
                    frame=frame.copy(),
                    detection=dict(detection),
                    roi_bbox_xyxy=list(roi_bbox),
                    queued_at=now,
                    generation=job_generation,
                )
            )
            self.last_face_started_at = now
        except queue.Full:
            pass

    def _worker_loop(self):
        while not self.stop_event.is_set():
            try:
                job = self.queue.get(timeout=0.1)
            except queue.Empty:
                continue
            with self.lock:
                self.face_busy = True
            started = time.perf_counter()
            result = self._run_face_recognition(job)
            result["timing"] = {
                "face_ms": round((time.perf_counter() - started) * 1000.0, 1),
                "queue_age_sec": round(time.monotonic() - job.queued_at, 3),
            }
            with self.lock:
                self.face_busy = False
                if job.generation != self.generation:
                    continue
                self.last_face_result = result
                self.last_face_completed_at = time.monotonic()

    def _run_face_recognition(self, job: FaceJob) -> dict:
        x1, y1, x2, y2 = job.roi_bbox_xyxy
        roi = job.frame[y1:y2, x1:x2].copy()
        if roi.size == 0:
            if self.temporal is not None:
                self.temporal.reset()
            return self.status_result("error", "empty_face_roi", job.roi_bbox_xyxy)

        try:
            results = self._recognize_frame(roi, self.face_app, self.identities, self.face_args)
        except Exception as exc:
            if self.temporal is not None:
                self.temporal.reset()
            return self.status_result("error", str(exc), job.roi_bbox_xyxy)

        if not results:
            if self.temporal is not None:
                self.temporal.reset()
            return self.status_result("no_face", "no_face_detected", job.roi_bbox_xyxy)

        face_bbox, match, _ = results[0]
        absolute_face_bbox = [
            int(face_bbox[0] + x1),
            int(face_bbox[1] + y1),
            int(face_bbox[2] + x1),
            int(face_bbox[3] + y1),
        ]
        confirmed = self.temporal.add(match) if self.temporal is not None else match
        selected = confirmed or match
        if confirmed is not None:
            status = "recognized"
            identity_status = "employee"
        elif match is not None and not match.is_known:
            status = "unknown"
            identity_status = "unknown_candidate"
        else:
            status = "pending"
            identity_status = "pending"

        return {
            "enabled": True,
            "status": status,
            "identity_status": identity_status,
            "name": getattr(selected, "name", None) if selected is not None else None,
            "employee_id": getattr(selected, "number", None) if selected is not None else None,
            "similarity": round(float(selected.best_similarity), 3)
            if selected is not None and selected.best_similarity is not None
            else None,
            "roi_bbox_xyxy": job.roi_bbox_xyxy,
            "face_bbox_xyxy": absolute_face_bbox,
            "updated_at": utc_now_iso(),
            "match_method": "embedding_cosine_similarity",
            "liveness_checked": False,
        }

    def stats(self) -> dict:
        with self.lock:
            last_age = (
                None
                if self.last_face_completed_at <= 0
                else round(time.monotonic() - self.last_face_completed_at, 3)
            )
            return {
                "face_enabled": self.enabled,
                "face_busy": self.face_busy,
                "last_face_age_sec": last_age,
                "face_queue_size": self.queue.qsize(),
                "face_error": self.error_reason,
            }


def process_face_roi(frame, detection, face_recognizer: FaceRecognitionWorker | None = None):
    if face_recognizer is None:
        return {
            "enabled": False,
            "status": "disabled",
            "identity_status": "disabled",
            "reason": "face_recognizer_not_configured",
            "roi_bbox_xyxy": detection.get("bbox_xyxy"),
            "updated_at": utc_now_iso(),
        }
    return face_recognizer.process_face_roi(frame, detection)


def normalize_providers(values: list[str]) -> list[str]:
    providers: list[str] = []
    for value in values:
        for item in str(value).replace(",", " ").split():
            if item:
                providers.append(item)
    return providers or ["CPUExecutionProvider"]


def detection_to_payload(
    frame,
    detection,
    face_recognizer: FaceRecognitionWorker | None = None,
    args=None,
) -> dict | None:
    height, width = frame.shape[:2]
    bbox = clamp_bbox_xyxy(detection.bbox_xyxy, width, height)
    if bbox is None:
        return None

    class_name = str(detection.class_name)
    if class_name.strip().lower() in IGNORED_SAFETY_CLASSES:
        return None
    x1, y1, x2, y2 = bbox
    payload = {
        "class": class_name,
        "confidence": round(float(detection.confidence), 3),
        "bbox_xyxy": bbox,
        "center_px": [round((x1 + x2) / 2.0, 1), round((y1 + y2) / 2.0, 1)],
        "face_result": None,
    }

    if class_name in FACE_ROI_CLASSES and args is not None:
        meets_threshold, metrics = roi_meets_face_threshold(bbox, width, height, args)
        if not meets_threshold:
            payload["face_result"] = skipped_face_result(bbox, metrics)
        else:
            payload["face_result"] = process_face_roi(frame, payload, face_recognizer)
    return payload


class TurtleBotSafetyResultNode(Node):
    def __init__(self, args):
        super().__init__("turtlebot_safety_result_node")
        self.args = args
        configure_cpu_runtime(args, self.get_logger())

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
            f"Loaded TurtleBot safety model={model_path} device={self.detector.device} "
            f"names={self.detector.model_names}"
        )
        self.roi_detector = None
        self.person_detector = None
        if args.enable_person_roi_safety:
            self.roi_detector = SafetyEventDetector(
                str(model_path),
                args.safety_roi_confidence,
                args.safety_roi_imgsz,
                args.safety_device,
            )
            self.get_logger().info(
                f"Loaded TurtleBot safety ROI model={model_path} device={self.roi_detector.device} "
                f"imgsz={args.safety_roi_imgsz} conf={args.safety_roi_confidence}"
            )
            person_model = Path(args.person_model_path)
            if not person_model.exists():
                raise FileNotFoundError(f"Person model not found: {person_model}")
            self.person_detector = CocoPersonDetector(
                str(person_model),
                args.person_confidence,
                args.person_imgsz,
                args.safety_device,
            )
            self.get_logger().info(
                f"Loaded TurtleBot person model={person_model} device={self.person_detector.device} "
                f"names={self.person_detector.model_names}"
            )
            self.get_logger().info(
                "TurtleBot person ROI mode=helmet-missing-only "
                f"(full-frame imgsz={args.safety_imgsz}, ROI imgsz={args.safety_roi_imgsz})"
            )
        else:
            self.get_logger().info(
                "TurtleBot person ROI safety disabled; using single-pass safety detection only"
            )
        self.face_recognizer = FaceRecognitionWorker(args, self.get_logger())
        self.get_logger().info(
            "Face ROI thresholds "
            f"width>={args.face_min_roi_width} "
            f"height>={args.face_min_roi_height} "
            f"area_ratio>={args.face_min_roi_area_ratio} "
            f"cache_reset_gap_sec={args.face_cache_reset_gap_sec}"
        )

        qos = image_qos(args.image_qos_depth)
        self.subscription = self.create_subscription(Image, args.live_topic, self.on_image, qos)
        self.detections_pub = self.create_publisher(String, args.detections_topic, 10)
        self.event_pub = self.create_publisher(String, args.event_topic, 10)
        self.server_safety_event_pub = self.create_publisher(String, args.server_safety_event_topic, 10)
        self.annotated_pub = (
            self.create_publisher(Image, args.annotated_topic, qos)
            if args.publish_annotated
            else None
        )

        self._stop_event = threading.Event()
        self._frame_lock = threading.Lock()
        self._latest_frame = None
        self._latest_msg = None
        self._latest_seq = 0
        self._processed_seq = 0
        self.last_safety_at = 0.0
        self.detector_processed_frames = 0
        self._detector_window_count = 0
        self._detector_window_started = time.monotonic()
        self._last_event_at_by_type: dict[str, float] = {}
        self._last_server_event_at_by_type: dict[str, float] = {}
        self.last_face_roi_seen_at = 0.0
        self.last_face_roi_bbox: list[int] | None = None

        self._inference_thread = threading.Thread(
            target=self._inference_loop,
            name="turtlebot-safety-inference",
            daemon=True,
        )
        self._stats_thread = threading.Thread(
            target=self._stats_loop,
            name="turtlebot-safety-stats",
            daemon=True,
        )
        self._inference_thread.start()
        self._stats_thread.start()

        self.get_logger().info(
            f"Subscribing live={args.live_topic}; publishing detections={args.detections_topic} "
            f"events={args.event_topic} server_events={args.server_safety_event_topic}"
        )
        self.get_logger().info(
            "Server safety event thresholds "
            f"center_overlap_ratio>={args.server_center_overlap_ratio} "
            f"fire_min_height_ratio>={args.server_fire_min_height_ratio} "
            f"fall_min_height_ratio>={args.server_fall_min_height_ratio} "
            f"cooldown_sec={args.server_event_cooldown_sec}"
        )
        if self.annotated_pub:
            self.get_logger().info(f"Publishing annotated={args.annotated_topic}")

    def on_image(self, msg: Image):
        try:
            frame = imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(f"Image conversion failed: {exc}")
            return
        with self._frame_lock:
            self._latest_frame = frame
            self._latest_msg = msg
            self._latest_seq += 1

    def _get_latest(self):
        with self._frame_lock:
            if self._latest_frame is None:
                return None, None, 0
            return self._latest_frame.copy(), self._latest_msg, self._latest_seq

    def _inference_loop(self):
        while not self._stop_event.is_set():
            frame, msg, seq = self._get_latest()
            if frame is None or msg is None:
                time.sleep(0.01)
                continue

            now = time.monotonic()
            min_interval = 1.0 / max(self.args.safety_fps, 0.1)
            if seq == self._processed_seq or now - self.last_safety_at < min_interval:
                time.sleep(0.005)
                continue

            self._processed_seq = seq
            self.last_safety_at = now
            self.run_safety_pipeline(frame, msg)
            self.detector_processed_frames += 1
            self._detector_window_count += 1

    def update_face_roi_tracking(self, frame, raw_detections, now: float):
        current_roi = pick_representative_face_roi(frame, raw_detections, self.args)
        if current_roi is None:
            if (
                self.last_face_roi_seen_at > 0.0
                and now - self.last_face_roi_seen_at >= self.args.face_cache_reset_gap_sec
            ):
                self.face_recognizer.reset_cache("face_candidate_lost")
                self.last_face_roi_bbox = None
            return

        if self.last_face_roi_bbox is None or not is_similar_face_roi(
            current_roi, self.last_face_roi_bbox
        ):
            self.face_recognizer.reset_cache("new_face_roi")

        self.last_face_roi_seen_at = now
        self.last_face_roi_bbox = list(current_roi)

    def run_safety_pipeline(self, frame, msg: Image):
        try:
            raw_detections = self.detector.detect(frame)
        except Exception as exc:
            self.get_logger().warning(f"Safety detection failed: {exc}")
            return

        raw_detections.extend(self.detect_safety_in_person_rois(frame, raw_detections))
        raw_detections = dedupe_safety_detections(
            raw_detections,
            self.args.safety_dedupe_iou,
        )

        self.update_face_roi_tracking(frame, raw_detections, time.monotonic())

        detections = []
        for detection in raw_detections:
            payload = detection_to_payload(frame, detection, self.face_recognizer, self.args)
            if payload is not None:
                detections.append(payload)

        self.publish_detections(detections)
        self.publish_events(detections)
        self.publish_server_safety_events(frame, detections)

        if self.annotated_pub is not None:
            annotated = self.draw_annotated(frame, detections)
            self.annotated_pub.publish(cv_to_imgmsg(annotated, msg.header.frame_id, msg.header.stamp))

    def detect_safety_in_person_rois(
        self,
        frame,
        existing_detections: list[SafetyDetectionResult],
    ) -> list[SafetyDetectionResult]:
        if (
            not self.args.enable_person_roi_safety
            or self.person_detector is None
            or self.roi_detector is None
        ):
            return []

        try:
            person_detections = self.person_detector.detect(frame)
        except Exception as exc:
            self.get_logger().warning(f"Person detection failed: {exc}")
            return []

        height, width = frame.shape[:2]
        roi_detections: list[SafetyDetectionResult] = []
        checked = 0
        skipped_with_helmet = 0
        for person in person_detections[: self.args.person_roi_max_count]:
            # Always publish person boxes so ROI coverage is visible on the overlay.
            roi_detections.append(
                SafetyDetectionResult("person", float(person.confidence), list(person.bbox_xyxy))
            )
            # Helmet already found on full-frame 640 pass for this person -> skip expensive 1280 ROI.
            if person_has_helmet(person.bbox_xyxy, existing_detections):
                skipped_with_helmet += 1
                continue

            roi_bbox = expand_bbox_xyxy(person.bbox_xyxy, width, height, self.args.person_roi_expand)
            if roi_bbox is None:
                continue
            x1, y1, x2, y2 = roi_bbox
            roi = frame[y1:y2, x1:x2]
            if roi.size == 0:
                continue
            checked += 1
            try:
                detections = self.roi_detector.detect(roi)
            except Exception as exc:
                self.get_logger().warning(f"Person ROI safety detection failed: {exc}")
                continue
            for detection in detections:
                class_name = str(detection.class_name).strip().lower().replace("-", "_")
                if class_name not in ROI_SAFETY_CLASSES:
                    continue
                bx1, by1, bx2, by2 = detection.bbox_xyxy
                bbox = clamp_bbox_xyxy([bx1 + x1, by1 + y1, bx2 + x1, by2 + y1], width, height)
                if bbox is None:
                    continue
                roi_detections.append(
                    SafetyDetectionResult(detection.class_name, detection.confidence, bbox)
                )

        if checked or skipped_with_helmet or person_detections:
            self.get_logger().info(
                f"Person ROI per-person check persons={min(len(person_detections), self.args.person_roi_max_count)} "
                f"checked_1280={checked} skipped_with_helmet={skipped_with_helmet} "
                f"roi_hits={len(roi_detections)}"
            )
        return roi_detections

    def publish_detections(self, detections: list[dict]):
        payload = {
            "schema_version": DETECTIONS_SCHEMA_VERSION,
            "created_at": utc_now_iso(),
            "camera_id": self.args.camera_id,
            "source_topic": self.args.live_topic,
            "detections": detections,
        }
        self.detections_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))

    def publish_events(self, detections: list[dict]):
        now = time.monotonic()
        for detection in detections:
            event_type = event_type_for_class(str(detection.get("class", "")))
            if event_type is None:
                continue
            last_event_at = self._last_event_at_by_type.get(event_type, 0.0)
            if now - last_event_at < self.args.event_cooldown_sec:
                continue
            self._last_event_at_by_type[event_type] = now
            payload = {
                "schema_version": EVENT_SCHEMA_VERSION,
                "event_id": f"turtlebot_safety_{uuid.uuid4().hex}",
                "event_type": event_type,
                "created_at": utc_now_iso(),
                "camera_id": self.args.camera_id,
                "confidence": detection["confidence"],
                "bbox_xyxy": detection["bbox_xyxy"],
                "center_px": detection["center_px"],
                "source_topic": self.args.live_topic,
            }
            self.event_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))
            self.get_logger().info(
                f"TurtleBot safety event event_type={event_type} confidence={detection['confidence']:.3f}"
            )

    def publish_server_safety_events(self, frame, detections: list[dict]):
        frame_height, frame_width = frame.shape[:2]
        now = time.monotonic()
        best_by_type: dict[str, dict] = {}

        for detection in detections:
            candidate = server_event_candidate(detection, frame_width, frame_height, self.args)
            if candidate is None:
                continue
            event_type = candidate["event_type"]
            confidence = float(detection.get("confidence", 0.0))
            existing = best_by_type.get(event_type)
            if existing is None or confidence > float(existing["detection"].get("confidence", 0.0)):
                best_by_type[event_type] = candidate

        for event_type, candidate in best_by_type.items():
            last_event_at = self._last_server_event_at_by_type.get(event_type, 0.0)
            if now - last_event_at < self.args.server_event_cooldown_sec:
                continue
            self._last_server_event_at_by_type[event_type] = now

            detection = candidate["detection"]
            condition = candidate["condition"]
            payload = {
                "schema_version": SERVER_EVENT_SCHEMA_VERSION,
                "event_id": f"turtlebot_server_evt_{uuid.uuid4().hex}",
                "event_type": event_type,
                "created_at": utc_now_iso(),
                "camera_id": self.args.camera_id,
                "source_topic": self.args.live_topic,
                "confidence": detection["confidence"],
                "bbox_xyxy": detection["bbox_xyxy"],
                "center_px": detection["center_px"],
                "image_size": {
                    "width": frame_width,
                    "height": frame_height,
                },
                "condition": condition,
            }
            self.server_safety_event_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))
            self.get_logger().info(
                "TurtleBot server safety event "
                f"event_type={event_type} "
                f"confidence={detection['confidence']:.3f} "
                f"center_overlap_ratio={condition['center_overlap_ratio']:.3f} "
                f"bbox_height={condition['bbox_height']:.1f} "
                f"required_bbox_height={condition['required_bbox_height']:.1f}"
            )

    def draw_annotated(self, frame, detections: list[dict]):
        annotated = frame.copy()
        for detection in detections:
            bbox = detection.get("bbox_xyxy")
            if not bbox:
                continue
            x1, y1, x2, y2 = [int(round(value)) for value in bbox]
            class_name = str(detection.get("class", "object"))
            color = CLASS_COLORS.get(class_name, (200, 200, 200))
            cv2.rectangle(annotated, (x1, y1), (x2, y2), color, 2)
            label = f"{class_name} {float(detection.get('confidence', 0.0)):.2f}"
            face_label = self.face_label(detection.get("face_result"))
            if face_label:
                label = f"{label} {face_label}"
            self.draw_label(annotated, x1, y1, label, color)
        return annotated

    @staticmethod
    def face_label(face_result):
        if not isinstance(face_result, dict):
            return ""
        status = str(face_result.get("status") or face_result.get("identity_status") or "").strip()
        if not status:
            return ""
        employee_id = face_result.get("employee_id")
        if status == "recognized" and employee_id:
            return f"face:id-{employee_id}"
        if status == "recognized":
            return "face:recognized"
        if status == "unknown":
            return "face:unknown"
        if status == "no_face":
            return "face:no-face"
        if status == "pending":
            return "face:pending"
        if status == "skipped":
            return "face:too-small"
        if status == "disabled":
            return "face:disabled"
        if status == "error":
            return "face:error"
        return f"face:{status}"

    @staticmethod
    def draw_label(frame, x1: int, y1: int, label: str, color):
        font = cv2.FONT_HERSHEY_SIMPLEX
        scale = 0.5
        thickness = 1
        (text_w, text_h), _ = cv2.getTextSize(label, font, scale, thickness)
        y_text = max(text_h + 4, y1 - 5)
        cv2.rectangle(
            frame,
            (x1, y_text - text_h - 4),
            (x1 + text_w + 6, y_text + 3),
            color,
            -1,
        )
        cv2.putText(
            frame,
            label,
            (x1 + 3, y_text),
            font,
            scale,
            (255, 255, 255),
            thickness,
            cv2.LINE_AA,
        )

    def _stats_loop(self):
        while not self._stop_event.is_set():
            time.sleep(max(self.args.log_interval, 0.1))
            elapsed = max(time.monotonic() - self._detector_window_started, 1e-6)
            fps = self._detector_window_count / elapsed
            self._detector_window_count = 0
            self._detector_window_started = time.monotonic()
            face_stats = self.face_recognizer.stats()
            self.get_logger().info(
                "turtlebot-safety stats "
                f"detector_processed_frames={self.detector_processed_frames} "
                f"detector_fps={fps:.2f} "
                f"face_enabled={face_stats['face_enabled']} "
                f"face_busy={face_stats['face_busy']} "
                f"last_face_age_sec={face_stats['last_face_age_sec']} "
                f"face_queue_size={face_stats['face_queue_size']}"
            )

    def destroy_node(self):
        self._stop_event.set()
        self.face_recognizer.shutdown()
        for thread in (self._inference_thread, self._stats_thread):
            if thread.is_alive():
                thread.join(timeout=1.0)
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="TurtleBot safety detector for UDP camera live images.")
    parser.add_argument("--live-topic", default="/turtlebot_camera/live/image")
    parser.add_argument("--detections-topic", default="/turtlebot_camera/safety/detections")
    parser.add_argument("--event-topic", default="/turtlebot_camera/safety/events")
    parser.add_argument("--annotated-topic", default="/turtlebot_camera/safety/annotated_image")
    parser.add_argument("--publish-annotated", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-publish-annotated", dest="publish_annotated", action="store_false")
    parser.add_argument("--safety-model-path", default=DEFAULT_SAFETY_MODEL)
    parser.add_argument("--person-model-path", default=DEFAULT_PERSON_MODEL)
    parser.add_argument("--safety-device", default="auto")
    parser.add_argument("--safety-confidence", type=float, default=0.25)
    parser.add_argument("--safety-imgsz", type=int, default=640)
    parser.add_argument("--safety-roi-confidence", type=float, default=0.25)
    parser.add_argument("--safety-roi-imgsz", type=int, default=1280)
    parser.add_argument("--person-confidence", type=float, default=0.35)
    parser.add_argument("--person-imgsz", type=int, default=640)
    parser.add_argument("--enable-person-roi-safety", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-person-roi-safety", dest="enable_person_roi_safety", action="store_false")
    parser.add_argument("--person-roi-expand", type=float, default=1.1)
    parser.add_argument("--person-roi-max-count", type=int, default=4)
    parser.add_argument("--safety-dedupe-iou", type=float, default=0.6)
    parser.add_argument("--safety-fps", type=float, default=2.0)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--camera-id", default="turtlebot-camera-001")
    parser.add_argument("--event-cooldown-sec", type=float, default=2.0)
    parser.add_argument("--server-safety-event-topic", default="/turtlebot_camera/server/safety_events")
    parser.add_argument("--server-center-overlap-ratio", type=float, default=0.6)
    parser.add_argument("--server-fire-min-height-ratio", type=float, default=0.5)
    parser.add_argument("--server-fall-min-height-ratio", type=float, default=0.5)
    parser.add_argument("--server-no-helmet-min-height-ratio", type=float, default=0.03)
    parser.add_argument("--server-event-cooldown-sec", type=float, default=3.0)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--torch-num-threads", type=int, default=1)
    parser.add_argument("--torch-num-interop-threads", type=int, default=1)
    parser.add_argument("--opencv-num-threads", type=int, default=1)
    parser.add_argument("--enable-face-recognition", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-face-recognition", dest="enable_face_recognition", action="store_false")
    parser.add_argument("--face-fps", type=float, default=1.0)
    parser.add_argument("--face-result-ttl", type=float, default=4.0)
    parser.add_argument("--face-similarity", type=float, default=0.34)
    parser.add_argument("--face-temporal-frames", type=int, default=6)
    parser.add_argument("--face-temporal-votes", type=int, default=2)
    parser.add_argument("--providers", nargs="+", default=["CUDAExecutionProvider", "CPUExecutionProvider"])
    parser.add_argument("--det-size", type=int, default=320)
    parser.add_argument("--registered-dir", default="/home/gyul/registered_faces")
    parser.add_argument("--margin-threshold", type=float, default=0.03)
    parser.add_argument("--top-k", type=int, default=3)
    parser.add_argument("--face-min-roi-width", type=int, default=70)
    parser.add_argument("--face-min-roi-height", type=int, default=55)
    parser.add_argument("--face-min-roi-area-ratio", type=float, default=0.015)
    parser.add_argument("--face-cache-reset-gap-sec", type=float, default=2.5)
    args = parser.parse_args()
    args.providers = normalize_providers(args.providers)
    return args


def main():
    args = parse_args()
    rclpy.init()
    node = TurtleBotSafetyResultNode(args)
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
