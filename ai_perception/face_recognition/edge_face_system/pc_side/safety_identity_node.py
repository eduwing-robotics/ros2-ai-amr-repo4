from __future__ import annotations

import argparse
import json
import queue
import sys
import threading
import time
import uuid
from collections import Counter, deque
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace

import cv2
import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from face_recognize_webcam import (
    MatchResult,
    load_registered_identities,
    recognize_frame,
)
from pc_side.perception_node import create_robot_face_app
from pc_side.ros_image_utils import compressed_imgmsg_to_cv, cv_to_imgmsg, imgmsg_to_cv


DEFAULT_DETECTOR_MODEL = "/home/gyul/yolo_test/models/hardhat_best.pt"


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def clamp_bbox(bbox, width: int, height: int) -> list[int] | None:
    x1, y1, x2, y2 = [int(round(value)) for value in bbox]
    x1 = max(0, min(width - 1, x1))
    y1 = max(0, min(height - 1, y1))
    x2 = max(0, min(width - 1, x2))
    y2 = max(0, min(height - 1, y2))
    if x2 <= x1 or y2 <= y1:
        return None
    return [x1, y1, x2, y2]


def expand_bbox(bbox, width: int, height: int, x_scale: float, y_up: float, y_down: float):
    x1, y1, x2, y2 = bbox
    box_w = x2 - x1
    box_h = y2 - y1
    cx = (x1 + x2) / 2.0
    nx1 = cx - box_w * x_scale / 2.0
    nx2 = cx + box_w * x_scale / 2.0
    ny1 = y1 - box_h * y_up
    ny2 = y2 + box_h * y_down
    return clamp_bbox([nx1, ny1, nx2, ny2], width, height)


@dataclass
class DetectionResult:
    class_name: str
    confidence: float
    bbox_xyxy: list[int]

    @property
    def area(self) -> int:
        x1, y1, x2, y2 = self.bbox_xyxy
        return max(0, x2 - x1) * max(0, y2 - y1)

    def to_payload(self):
        x1, y1, x2, y2 = self.bbox_xyxy
        return {
            "class": self.class_name,
            "confidence": round(self.confidence, 3),
            "bbox_xyxy": [x1, y1, x2, y2],
        }


@dataclass
class FaceJob:
    image_source: str
    frame_id: int
    frame: np.ndarray
    header_stamp: object
    header_frame_id: str
    roi_bbox: list[int] | None
    queued_at: float


class YoloMultiObjectDetector:
    def __init__(self, model_path: str, confidence: float, imgsz: int, device: str):
        try:
            from ultralytics import YOLO
            import torch
        except ImportError as exc:
            raise RuntimeError(
                "YOLO detector requires ultralytics and torch in face-env."
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

    def detect(self, frame: np.ndarray) -> list[DetectionResult]:
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
            detections.append(DetectionResult(class_name, confidence, bbox))
        detections.sort(key=lambda item: (item.confidence, item.area), reverse=True)
        return detections


class TemporalRecognizer:
    def __init__(self, frames: int, votes: int):
        self.history = deque(maxlen=frames)
        self.votes = votes

    def add(self, match: MatchResult | None):
        self.history.append(match)
        known = [item for item in self.history if item is not None and item.is_known]
        if not known:
            return None
        label, count = Counter(item.label for item in known).most_common(1)[0]
        if count < self.votes:
            return None
        return max(
            (item for item in known if item.label == label),
            key=lambda item: item.best_similarity or 0.0,
        )

    def reset(self):
        self.history.clear()


class HelmetStatusTemporal:
    def __init__(self, unsafe_frames: int, safe_frames: int):
        self.unsafe_history = deque(maxlen=max(1, unsafe_frames))
        self.safe_history = deque(maxlen=max(1, safe_frames))
        self.unsafe_frames = max(1, unsafe_frames)
        self.safe_frames = max(1, safe_frames)
        self.last_confirmed_status = "UNKNOWN"

    def update(self, instant_status: str):
        unsafe_seen = instant_status == "UNSAFE_CANDIDATE"
        safe_seen = instant_status == "SAFE_CANDIDATE"
        self.unsafe_history.append(unsafe_seen)
        self.safe_history.append(safe_seen)

        unsafe_streak = self._tail_streak(self.unsafe_history)
        safe_streak = self._tail_streak(self.safe_history)

        if unsafe_streak >= self.unsafe_frames:
            helmet_status = "UNSAFE"
            self.last_confirmed_status = helmet_status
            state = "no_helmet_confirmed"
            message = f"안전모 미착용 {unsafe_streak}프레임 연속 확인"
        elif safe_streak >= self.safe_frames:
            helmet_status = "SAFE"
            self.last_confirmed_status = helmet_status
            state = "helmet_confirmed"
            message = f"안전모 착용 {safe_streak}프레임 연속 확인"
        elif instant_status == "UNSAFE_CANDIDATE":
            helmet_status = "PENDING"
            state = "no_helmet_candidate"
            message = f"안전모 미착용 후보 {unsafe_streak}/{self.unsafe_frames}"
        elif instant_status == "SAFE_CANDIDATE":
            helmet_status = "PENDING"
            state = "helmet_candidate"
            message = f"안전모 착용 후보 {safe_streak}/{self.safe_frames}"
        else:
            helmet_status = "UNKNOWN"
            state = "helmet_unknown"
            message = "안전모 상태 확인 불가"

        return {
            "helmet_status": helmet_status,
            "last_confirmed_status": self.last_confirmed_status,
            "instant_status": instant_status,
            "state": state,
            "message": message,
            "unsafe_streak": unsafe_streak,
            "safe_streak": safe_streak,
            "unsafe_required_frames": self.unsafe_frames,
            "safe_required_frames": self.safe_frames,
        }

    @staticmethod
    def _tail_streak(history):
        streak = 0
        for value in reversed(history):
            if not value:
                break
            streak += 1
        return streak


class SafetyIdentityNode(Node):
    def __init__(self, args):
        super().__init__("safety_identity_node")
        self.args = args
        self.frame_seq = 0
        self.last_detector_at = 0.0
        self.last_detector_at_by_source = {}
        self.last_emit_at = 0.0
        self.last_event = None
        self.last_event_by_source = {}
        self.last_face_result = None
        self.last_face_result_by_source = {}
        self.last_face_completed_at_by_source = {}
        self.last_face_started_at = 0.0
        self.last_face_completed_at = 0.0
        self.face_busy = False
        self.face_lock = threading.Lock()
        self.stop_event = threading.Event()
        self.face_queue: queue.Queue[FaceJob] = queue.Queue(maxsize=1)

        model_path = Path(args.detector_model_path)
        if not model_path.exists():
            raise FileNotFoundError(f"Detector model not found: {model_path}")
        self.detector = YoloMultiObjectDetector(
            str(model_path),
            args.detector_confidence,
            args.detector_imgsz,
            args.detector_device,
        )
        self.get_logger().info(
            f"Loaded detector model={model_path} device={self.detector.device} names={self.detector.model_names}"
        )

        self.identities = load_registered_identities(Path(args.registered_dir))
        self.get_logger().info(f"Loaded {len(self.identities)} registered identities")
        self.get_logger().info("Face identity mode: embedding_similarity_only, depth/PAD disabled")
        self.face_app = create_robot_face_app(args.providers, args.det_size)
        self.face_args = SimpleNamespace(
            single_face=True,
            similarity_threshold=args.face_similarity,
            margin_threshold=args.margin_threshold,
            top_k=args.top_k,
        )
        self.temporal = TemporalRecognizer(args.face_temporal_frames, args.face_temporal_votes)
        self.temporal_by_source = {}
        self.helmet_temporal_by_source = {}

        self.global_image_topic = args.global_image_topic or args.image_topic
        self.image_subscriptions = [
            self.create_image_subscription(
                self.global_image_topic,
                lambda msg: self.on_image(msg, "globalcam", self.global_image_topic, args.camera_id),
            )
        ]
        if args.robot_image_topic:
            self.image_subscriptions.append(
                self.create_image_subscription(
                    args.robot_image_topic,
                    lambda msg: self.on_image(msg, "robot_picam", args.robot_image_topic, args.robot_camera_id),
                )
            )
        self.event_pub = self.create_publisher(String, args.event_topic, 10)
        self.annotated_pub = self.create_publisher(Image, args.annotated_topic, image_qos(args.image_qos_depth))
        self.face_thread = threading.Thread(target=self.face_worker, name="face_worker", daemon=True)
        self.face_thread.start()

        self.get_logger().info(f"Listening globalcam: {self.global_image_topic}")
        if args.robot_image_topic:
            self.get_logger().info(f"Listening robot picam optional: {args.robot_image_topic}")
        else:
            self.get_logger().info("Robot picam topic disabled")
        self.get_logger().info(f"Publishing events: {args.event_topic}")
        self.get_logger().info(f"Publishing annotated image: {args.annotated_topic}")
        self.get_logger().info(f"Image QoS: best_effort/depth{args.image_qos_depth}")

    def create_image_subscription(self, topic: str, callback):
        msg_type = CompressedImage if topic.endswith("/compressed") else Image
        mode = "compressed/jpeg" if msg_type is CompressedImage else "raw"
        self.get_logger().info(f"Subscribing image topic {topic} ({mode})")
        return self.create_subscription(msg_type, topic, callback, image_qos(self.args.image_qos_depth))

    def on_image(self, msg, image_source: str = "globalcam", source_topic: str | None = None, camera_id: str | None = None):
        try:
            frame = compressed_imgmsg_to_cv(msg) if isinstance(msg, CompressedImage) else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(f"Image conversion failed: {exc}")
            return

        if self.args.rotate_180:
            frame = cv2.rotate(frame, cv2.ROTATE_180)

        now = time.monotonic()
        source_topic = source_topic or self.args.image_topic
        camera_id = camera_id or self.args.camera_id
        last_detector_at = self.last_detector_at_by_source.get(image_source, 0.0)
        if now - last_detector_at < 1.0 / max(self.args.detector_fps, 0.1):
            last_event = self.last_event_by_source.get(image_source)
            if self.args.publish_annotated and last_event is not None:
                annotated = self.draw_overlay(frame, last_event)
                self.annotated_pub.publish(cv_to_imgmsg(annotated, msg.header.frame_id, msg.header.stamp))
            return

        self.frame_seq += 1
        self.last_detector_at = now
        self.last_detector_at_by_source[image_source] = now
        started = time.perf_counter()
        try:
            detections = self.detector.detect(frame)
        except Exception as exc:
            event = self.base_event(msg, frame, [], now, image_source, source_topic, camera_id)
            event.update(
                {
                    "state": "detector_error",
                    "message": "안전모/PPE 탐지 모델 오류",
                    "error": str(exc),
                }
            )
            self.publish_event(event, frame, msg, image_source)
            return

        detector_ms = (time.perf_counter() - started) * 1000.0
        event = self.build_detection_event(msg, frame, detections, now, detector_ms, image_source, source_topic, camera_id)
        face_roi = self.choose_face_roi(frame, detections)
        if face_roi is not None and self.should_submit_face(now):
            self.submit_face_job(
                FaceJob(
                    image_source=image_source,
                    frame_id=self.frame_seq,
                    frame=frame.copy(),
                    header_stamp=msg.header.stamp,
                    header_frame_id=msg.header.frame_id,
                    roi_bbox=face_roi,
                    queued_at=now,
                )
            )

        face_result = self.get_recent_face_result(image_source, now)
        if face_roi is None:
            event["face"] = {"visible": False, "reason": "no_face_roi_candidate"}
            event["identity_status"] = "face_not_visible"
            self.temporal_for(image_source).reset()
        elif face_result is not None:
            event["face"] = face_result
            event["identity_status"] = face_result.get("identity_status", "pending")
        else:
            event["face"] = {"visible": None, "reason": "face_worker_pending"}
            event["identity_status"] = "pending"

        event["decision"] = self.make_decision(event)
        self.publish_event(event, frame, msg, image_source)

    def base_event(self, msg: Image, frame: np.ndarray, detections, now: float, image_source: str, source_topic: str, camera_id: str):
        height, width = frame.shape[:2]
        return {
            "schema_version": "safety_identity.v1",
            "event_id": f"evt_{uuid.uuid4().hex}",
            "device_id": self.args.device_id,
            "camera_id": camera_id,
            "image_source": image_source,
            "source_topic": source_topic,
            "face_identity_mode": "embedding_similarity_only",
            "liveness_check": {"enabled": False, "reason": "not_used_for_safety_identity"},
            "created_at": utc_now_iso(),
            "captured_stamp": {
                "sec": int(msg.header.stamp.sec),
                "nanosec": int(msg.header.stamp.nanosec),
            },
            "image_size": {"width": width, "height": height},
            "detections": [item.to_payload() for item in detections],
        }

    def build_detection_event(self, msg, frame, detections, now, detector_ms, image_source, source_topic, camera_id):
        event = self.base_event(msg, frame, detections, now, image_source, source_topic, camera_id)
        helmet_detections = [
            item for item in detections
            if item.class_name in self.args.helmet_classes
            and item.confidence >= self.args.helmet_confidence
        ]
        no_helmet_detections = [
            item for item in detections
            if item.class_name in self.args.no_helmet_classes
            and item.confidence >= self.args.no_helmet_confidence
        ]
        raw_helmet_count = sum(1 for item in detections if item.class_name in self.args.helmet_classes)
        raw_no_helmet_count = sum(1 for item in detections if item.class_name in self.args.no_helmet_classes)
        helmet_count = len(helmet_detections)
        no_helmet_count = len(no_helmet_detections)
        tracked_objects = [
            item.to_payload()
            for item in detections
            if item.class_name in self.args.extra_object_classes
        ]

        if no_helmet_count > 0:
            instant_status = "UNSAFE_CANDIDATE"
        elif helmet_count > 0:
            instant_status = "SAFE_CANDIDATE"
        else:
            instant_status = "UNKNOWN"

        temporal = self.helmet_temporal_for(image_source)
        helmet_temporal = temporal.update(instant_status)

        event.update(
            {
                "state": helmet_temporal["state"],
                "message": helmet_temporal["message"],
                "helmet_status": helmet_temporal["helmet_status"],
                "helmet_instant_status": helmet_temporal["instant_status"],
                "helmet_count": helmet_count,
                "no_helmet_count": no_helmet_count,
                "raw_helmet_count": raw_helmet_count,
                "raw_no_helmet_count": raw_no_helmet_count,
                "helmet_detections": [item.to_payload() for item in helmet_detections],
                "no_helmet_detections": [item.to_payload() for item in no_helmet_detections],
                "helmet_thresholds": {
                    "helmet_confidence": self.args.helmet_confidence,
                    "no_helmet_confidence": self.args.no_helmet_confidence,
                    "safe_confirm_frames": self.args.safe_confirm_frames,
                    "unsafe_confirm_frames": self.args.unsafe_confirm_frames,
                },
                "helmet_temporal": {
                    "safe_streak": helmet_temporal["safe_streak"],
                    "unsafe_streak": helmet_temporal["unsafe_streak"],
                    "safe_required_frames": helmet_temporal["safe_required_frames"],
                    "unsafe_required_frames": helmet_temporal["unsafe_required_frames"],
                    "last_confirmed_status": helmet_temporal["last_confirmed_status"],
                },
                "tracked_objects": tracked_objects,
                "timing": {
                    "detector_ms": round(detector_ms, 1),
                    "face_busy": self.face_busy,
                    "last_face_age_sec": None
                    if self.last_face_completed_at_by_source.get(image_source, 0.0) <= 0
                    else round(now - self.last_face_completed_at_by_source.get(image_source, 0.0), 3),
                },
            }
        )
        return event

    def choose_face_roi(self, frame: np.ndarray, detections: list[DetectionResult]):
        height, width = frame.shape[:2]
        head_candidates = [
            item for item in detections if item.class_name in self.args.no_helmet_classes
        ]
        helmet_candidates = [
            item for item in detections if item.class_name in self.args.helmet_classes
        ]
        candidates = head_candidates or helmet_candidates
        if not candidates:
            return None
        best = max(candidates, key=lambda item: (item.confidence, item.area))
        if best.area < self.args.min_face_roi_area:
            return None
        if best.class_name in self.args.no_helmet_classes:
            return expand_bbox(best.bbox_xyxy, width, height, 2.4, 0.6, 2.8)
        return expand_bbox(best.bbox_xyxy, width, height, 2.6, 0.2, 3.2)

    def should_submit_face(self, now: float):
        if now - self.last_face_started_at < 1.0 / max(self.args.face_fps, 0.1):
            return False
        with self.face_lock:
            return not self.face_busy

    def submit_face_job(self, job: FaceJob):
        while True:
            try:
                self.face_queue.get_nowait()
            except queue.Empty:
                break
        try:
            self.face_queue.put_nowait(job)
            self.last_face_started_at = time.monotonic()
        except queue.Full:
            pass

    def face_worker(self):
        while not self.stop_event.is_set():
            try:
                job = self.face_queue.get(timeout=0.1)
            except queue.Empty:
                continue
            with self.face_lock:
                self.face_busy = True
            started = time.perf_counter()
            result = self.run_face_recognition(job)
            result["timing"] = {
                "face_ms": round((time.perf_counter() - started) * 1000.0, 1),
                "queue_age_sec": round(time.monotonic() - job.queued_at, 3),
                "frame_id": job.frame_id,
            }
            with self.face_lock:
                self.last_face_result = result
                self.last_face_result_by_source[job.image_source] = result
                completed_at = time.monotonic()
                self.last_face_completed_at = completed_at
                self.last_face_completed_at_by_source[job.image_source] = completed_at
                self.face_busy = False

    def run_face_recognition(self, job: FaceJob):
        frame = job.frame
        roi_offset = [0, 0]
        if job.roi_bbox is not None:
            x1, y1, x2, y2 = job.roi_bbox
            frame = frame[y1:y2, x1:x2].copy()
            roi_offset = [x1, y1]

        try:
            results = recognize_frame(frame, self.face_app, self.identities, self.face_args)
        except Exception as exc:
            self.temporal_for(job.image_source).reset()
            return {
                "image_source": job.image_source,
                "visible": None,
                "identity_status": "error",
                "match_method": "embedding_cosine_similarity",
                "liveness_checked": False,
                "reason": "face_model_error",
                "error": str(exc),
                "roi_bbox": job.roi_bbox,
            }

        if not results:
            self.temporal_for(job.image_source).reset()
            return {
                "image_source": job.image_source,
                "visible": False,
                "identity_status": "face_not_visible",
                "match_method": "embedding_cosine_similarity",
                "liveness_checked": False,
                "reason": "no_face_detected",
                "roi_bbox": job.roi_bbox,
            }

        face_bbox, match, _ = results[0]
        ox, oy = roi_offset
        absolute_face_bbox = [
            int(face_bbox[0] + ox),
            int(face_bbox[1] + oy),
            int(face_bbox[2] + ox),
            int(face_bbox[3] + oy),
        ]
        confirmed = self.temporal_for(job.image_source).add(match)
        selected = confirmed or match
        payload = {
            "image_source": job.image_source,
            "visible": True,
            "face_bbox": absolute_face_bbox,
            "roi_bbox": job.roi_bbox,
            "match_method": "embedding_cosine_similarity",
            "liveness_checked": False,
            "registered_source": "photo_embeddings",
            "identity_status": "employee" if confirmed is not None else "pending",
            "best_similarity": selected.best_similarity,
            "score": selected.score,
            "margin": selected.margin,
            "identity_key": selected.label,
        }
        if confirmed is not None:
            payload.update(
                {
                    "recognized": True,
                    "name": confirmed.name,
                    "number": confirmed.number,
                }
            )
        elif match is not None and not match.is_known:
            payload["identity_status"] = "unknown_candidate"
            payload["recognized"] = False
        return payload

    def temporal_for(self, image_source: str):
        temporal = self.temporal_by_source.get(image_source)
        if temporal is None:
            temporal = TemporalRecognizer(self.args.face_temporal_frames, self.args.face_temporal_votes)
            self.temporal_by_source[image_source] = temporal
        return temporal

    def helmet_temporal_for(self, image_source: str):
        temporal = self.helmet_temporal_by_source.get(image_source)
        if temporal is None:
            temporal = HelmetStatusTemporal(
                self.args.unsafe_confirm_frames,
                self.args.safe_confirm_frames,
            )
            self.helmet_temporal_by_source[image_source] = temporal
        return temporal

    def get_recent_face_result(self, image_source: str, now: float):
        with self.face_lock:
            result = self.last_face_result_by_source.get(image_source)
            completed_at = self.last_face_completed_at_by_source.get(image_source, 0.0)
            if result is None:
                return None
            if now - completed_at > self.args.face_result_ttl:
                return None
            return dict(result)

    def make_decision(self, event):
        helmet_status = event.get("helmet_status")
        identity_status = event.get("identity_status")
        if helmet_status == "SAFE" and identity_status == "employee":
            return {"access": "allow", "reason": "helmet_and_employee"}
        if helmet_status == "UNSAFE" and identity_status == "employee":
            return {"access": "deny", "reason": "employee_without_helmet"}
        if helmet_status == "UNSAFE":
            return {"access": "deny", "reason": "no_helmet"}
        if identity_status in {"unknown_candidate", "face_not_visible"}:
            return {"access": "pending", "reason": identity_status}
        return {"access": "pending", "reason": "processing"}

    def publish_event(self, event, frame, msg, image_source: str = "globalcam"):
        self.last_event = event
        self.last_event_by_source[image_source] = event
        now = time.monotonic()
        if now - self.last_emit_at >= self.args.emit_interval:
            self.event_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
            self.last_emit_at = now

        if self.args.publish_annotated:
            annotated = self.draw_overlay(frame, event)
            self.annotated_pub.publish(cv_to_imgmsg(annotated, msg.header.frame_id, msg.header.stamp))

    def draw_overlay(self, frame, event):
        annotated = frame.copy()
        for detection in event.get("detections", []):
            x1, y1, x2, y2 = detection["bbox_xyxy"]
            class_name = detection["class"]
            color = (0, 180, 0) if class_name in self.args.helmet_classes else (0, 0, 255)
            if class_name in self.args.extra_object_classes:
                color = (255, 160, 30)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), color, 2)
            label = f"{class_name} {detection['confidence']:.2f}"
            cv2.putText(
                annotated,
                label,
                (x1, max(18, y1 - 8)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                color,
                2,
                cv2.LINE_AA,
            )

        face = event.get("face") or {}
        if face.get("face_bbox"):
            x1, y1, x2, y2 = face["face_bbox"]
            color = (30, 200, 80) if face.get("identity_status") == "employee" else (40, 180, 255)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), color, 2)

        status = f"{event.get('helmet_status', 'UNKNOWN')} / {event.get('identity_status', 'pending')}"
        cv2.rectangle(annotated, (12, 12), (430, 54), (30, 30, 30), -1)
        cv2.putText(
            annotated,
            status,
            (24, 41),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.75,
            (255, 255, 255),
            2,
            cv2.LINE_AA,
        )
        return annotated

    def destroy_node(self):
        self.stop_event.set()
        if self.face_thread.is_alive():
            self.face_thread.join(timeout=1.0)
        super().destroy_node()


def split_classes(value: str):
    return [item.strip() for item in value.split(",") if item.strip()]


def parse_args():
    parser = argparse.ArgumentParser(
        description="Run hardhat/PPE detection first, then face recognition only when useful."
    )
    parser.add_argument("--image-topic", default="/globalcam/image_raw", help="Backward-compatible alias for --global-image-topic")
    parser.add_argument("--global-image-topic", default=None)
    parser.add_argument("--robot-image-topic", default="/robot/picam/image_raw", help="Optional robot Pi camera topic. Missing messages are allowed.")
    parser.add_argument("--event-topic", default="/safety_identity/events")
    parser.add_argument("--annotated-topic", default="/safety_identity/annotated_image")
    parser.add_argument("--device-id", default="edge-dev-001")
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument("--robot-camera-id", default="robot-picam-001")
    parser.add_argument("--detector-model-path", default=DEFAULT_DETECTOR_MODEL)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--detector-device", default="auto")
    parser.add_argument("--detector-imgsz", type=int, default=640)
    parser.add_argument("--detector-confidence", type=float, default=0.5)
    parser.add_argument("--detector-fps", type=float, default=5.0)
    parser.add_argument("--helmet-classes", type=split_classes, default=["helmet"])
    parser.add_argument("--no-helmet-classes", type=split_classes, default=["head"])
    parser.add_argument("--helmet-confidence", type=float, default=0.65)
    parser.add_argument("--no-helmet-confidence", type=float, default=0.6)
    parser.add_argument("--safe-confirm-frames", type=int, default=2)
    parser.add_argument("--unsafe-confirm-frames", type=int, default=5)
    parser.add_argument(
        "--extra-object-classes",
        type=split_classes,
        default=["turtlebot3", "turtlebot", "tb3", "burger"],
        help="Classes to pass through when the detector model is later replaced with a combined model.",
    )
    parser.add_argument("--registered-dir", default="$HOME/registered_faces")
    parser.add_argument("--providers", nargs="+", default=["CUDAExecutionProvider", "CPUExecutionProvider"])
    parser.add_argument("--det-size", type=int, default=320)
    parser.add_argument("--face-fps", type=float, default=2.0)
    parser.add_argument("--face-result-ttl", type=float, default=2.0)
    parser.add_argument("--face-similarity", type=float, default=0.34)
    parser.add_argument("--margin-threshold", type=float, default=0.03)
    parser.add_argument("--top-k", type=int, default=3)
    parser.add_argument("--face-temporal-frames", type=int, default=6)
    parser.add_argument("--face-temporal-votes", type=int, default=2)
    parser.add_argument("--min-face-roi-area", type=int, default=400)
    parser.add_argument("--emit-interval", type=float, default=0.2)
    parser.add_argument("--publish-annotated", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--rotate-180", action=argparse.BooleanOptionalAction, default=False)
    return parser.parse_args()


def main():
    args = parse_args()
    sys.path.insert(0, "/home/gyul")
    rclpy.init()
    node = SafetyIdentityNode(args)
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
