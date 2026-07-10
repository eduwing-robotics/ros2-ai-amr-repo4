from __future__ import annotations

import argparse
import json
import sys
import time
import uuid
from datetime import datetime, timezone
from collections import Counter, deque
from pathlib import Path
from types import SimpleNamespace

import cv2
import numpy as np
import onnxruntime as ort
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image
from std_msgs.msg import String
from insightface.app import FaceAnalysis

from face_recognize_webcam import (
    MatchResult,
    load_registered_identities,
    recognize_frame,
)
from pc_side.ros_image_utils import cv_to_imgmsg, depth_imgmsg_to_meters, imgmsg_to_cv


class YoloPersonDetector:
    def __init__(self, model_path: str, confidence_threshold: float, imgsz: int, device: str):
        try:
            from ultralytics import YOLO
            import torch
        except ImportError as exc:
            raise RuntimeError(
                "YOLO person detector requires ultralytics and torch in face-env. "
                "Install with: /home/gyul/face-env/bin/pip install ultralytics"
            ) from exc

        self.confidence_threshold = confidence_threshold
        self.imgsz = imgsz
        if device == "auto":
            device = "cuda:0" if torch.cuda.is_available() else "cpu"
        self.device = device
        self.model = YOLO(model_path)

    def detect(self, frame):
        height, width = frame.shape[:2]
        results = self.model.predict(
            frame,
            classes=[0],
            conf=self.confidence_threshold,
            imgsz=self.imgsz,
            device=self.device,
            verbose=False,
        )

        detections = []
        if not results:
            return detections

        boxes = results[0].boxes
        if boxes is None:
            return detections

        for box in boxes:
            confidence = float(box.conf[0])
            if confidence < self.confidence_threshold:
                continue
            x1, y1, x2, y2 = [int(value) for value in box.xyxy[0].tolist()]
            x1 = max(0, min(width - 1, x1))
            y1 = max(0, min(height - 1, y1))
            x2 = max(0, min(width - 1, x2))
            y2 = max(0, min(height - 1, y2))
            if x2 <= x1 or y2 <= y1:
                continue
            area_ratio = ((x2 - x1) * (y2 - y1)) / float(width * height)
            detections.append(
                {
                    "bbox": [x1, y1, x2, y2],
                    "confidence": confidence,
                    "area_ratio": area_ratio,
                }
            )

        detections.sort(key=lambda item: (item["confidence"], item["area_ratio"]), reverse=True)
        return detections


def limit_insightface_onnx_threads(threads: int):
    from insightface.model_zoo import model_zoo as mz

    threads = max(1, int(threads))

    def get_model_limited(name, **kwargs):
        root = mz.os.path.expanduser(kwargs.get("root", "~/.insightface"))
        model_root = mz.osp.join(root, "models")
        allow_download = kwargs.get("download", False)
        download_zip = kwargs.get("download_zip", False)
        if not name.endswith(".onnx"):
            model_dir = mz.os.path.join(model_root, name)
            model_file = mz.find_onnx_file(model_dir)
            if model_file is None:
                return None
        else:
            model_file = name
        if not mz.osp.exists(model_file) and allow_download:
            model_file = mz.download_onnx("models", model_file, root=root, download_zip=download_zip)
        assert mz.osp.exists(model_file), "model_file %s should exist" % model_file
        assert mz.osp.isfile(model_file), "model_file %s should be a file" % model_file
        router = mz.ModelRouter(model_file)
        providers = kwargs.get("providers", mz.get_default_providers())
        provider_options = kwargs.get("provider_options", mz.get_default_provider_options())
        session_options = ort.SessionOptions()
        session_options.intra_op_num_threads = threads
        session_options.inter_op_num_threads = 1
        session_options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        return router.get_model(
            providers=providers,
            provider_options=provider_options,
            sess_options=session_options,
        )

    mz.get_model = get_model_limited


def create_robot_face_app(providers: list[str], det_size: int, onnx_threads: int = 2):
    limit_insightface_onnx_threads(onnx_threads)
    app = FaceAnalysis(
        name="buffalo_l",
        allowed_modules=["detection", "recognition"],
        providers=providers,
    )
    app.prepare(ctx_id=0, det_size=(det_size, det_size))
    return app



def center_crop(frame, width_ratio: float = 1.0, height_ratio: float = 1.0):
    height, width = frame.shape[:2]
    width_ratio = max(0.1, min(1.0, float(width_ratio)))
    height_ratio = max(0.1, min(1.0, float(height_ratio)))
    crop_width = max(1, int(round(width * width_ratio)))
    crop_height = max(1, int(round(height * height_ratio)))
    x1 = max(0, (width - crop_width) // 2)
    y1 = max(0, (height - crop_height) // 2)
    return frame[y1:y1 + crop_height, x1:x1 + crop_width].copy()


class NoopPersonDetector:
    def detect(self, frame):
        height, width = frame.shape[:2]
        return [
            {
                "bbox": [0, 0, width - 1, height - 1],
                "confidence": 1.0,
                "area_ratio": 1.0,
            }
        ]


class HogPersonDetector:
    def __init__(self, confidence_threshold: float):
        self.confidence_threshold = confidence_threshold
        self.detector = cv2.HOGDescriptor()
        self.detector.setSVMDetector(cv2.HOGDescriptor_getDefaultPeopleDetector())

    def detect(self, frame):
        scale = 1.0
        height, width = frame.shape[:2]
        max_width = 640
        if width > max_width:
            scale = max_width / float(width)
            frame_for_detection = cv2.resize(frame, (max_width, int(height * scale)))
        else:
            frame_for_detection = frame

        rects, weights = self.detector.detectMultiScale(
            frame_for_detection,
            winStride=(8, 8),
            padding=(8, 8),
            scale=1.05,
        )

        detections = []
        inv_scale = 1.0 / scale
        for rect, weight in zip(rects, weights):
            score = float(weight)
            if score < self.confidence_threshold:
                continue
            x, y, w, h = [int(value * inv_scale) for value in rect]
            area_ratio = (w * h) / float(width * height)
            detections.append(
                {
                    "bbox": [x, y, x + w, y + h],
                    "confidence": score,
                    "area_ratio": area_ratio,
                }
            )

        detections.sort(key=lambda item: (item["confidence"], item["area_ratio"]), reverse=True)
        return detections



class DepthPadEstimator:
    def __init__(self, args):
        self.min_distance = args.pad_min_distance
        self.max_distance = args.pad_max_distance
        self.min_valid_ratio = args.pad_min_valid_ratio
        self.min_depth_std = args.pad_min_depth_std
        self.min_nose_delta = args.pad_min_nose_delta
        self.score_threshold = args.pad_score_threshold

    def estimate(self, depth_m, color_shape, bbox):
        if depth_m is None or bbox is None:
            return {
                "enabled": True,
                "live": False,
                "score": 0.0,
                "reason": "no_depth",
            }

        height, width = depth_m.shape[:2]
        color_height, color_width = color_shape[:2]
        x1, y1, x2, y2 = [int(value) for value in bbox]
        dx1 = int(np.clip(round(x1 * width / max(1, color_width)), 0, width - 1))
        dy1 = int(np.clip(round(y1 * height / max(1, color_height)), 0, height - 1))
        dx2 = int(np.clip(round(x2 * width / max(1, color_width)), 0, width - 1))
        dy2 = int(np.clip(round(y2 * height / max(1, color_height)), 0, height - 1))
        if dx2 <= dx1 or dy2 <= dy1:
            return {
                "enabled": True,
                "live": False,
                "score": 0.0,
                "reason": "invalid_roi",
            }

        roi = depth_m[dy1:dy2, dx1:dx2]
        valid = roi[np.isfinite(roi) & (roi > 0.05) & (roi < 5.0)]
        roi_pixels = max(1, roi.size)
        valid_ratio = float(valid.size / roi_pixels)
        if valid.size < 20:
            return {
                "enabled": True,
                "live": False,
                "score": 0.0,
                "reason": "insufficient_depth",
                "valid_depth_ratio": valid_ratio,
                "depth_bbox": [dx1, dy1, dx2, dy2],
            }

        distance = float(np.median(valid))
        depth_std = float(np.std(valid))
        depth_range = float(np.percentile(valid, 90) - np.percentile(valid, 10))
        nose_delta = self._nose_to_cheek_delta(roi)

        checks = {
            "distance": self.min_distance <= distance <= self.max_distance,
            "valid_ratio": valid_ratio >= self.min_valid_ratio,
            "depth_std": depth_std >= self.min_depth_std,
            "nose_delta": nose_delta >= self.min_nose_delta,
        }
        score = sum(1 for passed in checks.values() if passed) / float(len(checks))
        live = checks["distance"] and checks["valid_ratio"] and score >= self.score_threshold
        failed = [name for name, passed in checks.items() if not passed]
        reason = "depth_shape_pass" if live else "failed_" + "_".join(failed)

        return {
            "enabled": True,
            "live": live,
            "score": round(score, 3),
            "reason": reason,
            "distance_m": round(distance, 3),
            "valid_depth_ratio": round(valid_ratio, 3),
            "depth_std_m": round(depth_std, 4),
            "depth_range_m": round(depth_range, 4),
            "nose_cheek_delta_m": round(nose_delta, 4),
            "checks": checks,
            "depth_bbox": [dx1, dy1, dx2, dy2],
        }

    @staticmethod
    def _median_patch(roi, x1, y1, x2, y2):
        height, width = roi.shape[:2]
        x1 = int(np.clip(x1, 0, width - 1))
        x2 = int(np.clip(x2, x1 + 1, width))
        y1 = int(np.clip(y1, 0, height - 1))
        y2 = int(np.clip(y2, y1 + 1, height))
        patch = roi[y1:y2, x1:x2]
        valid = patch[np.isfinite(patch) & (patch > 0.05) & (patch < 5.0)]
        if valid.size < 5:
            return None
        return float(np.median(valid))

    def _nose_to_cheek_delta(self, roi):
        height, width = roi.shape[:2]
        nose = self._median_patch(roi, width * 0.42, height * 0.35, width * 0.58, height * 0.62)
        left = self._median_patch(roi, width * 0.18, height * 0.38, width * 0.34, height * 0.66)
        right = self._median_patch(roi, width * 0.66, height * 0.38, width * 0.82, height * 0.66)
        cheeks = [value for value in (left, right) if value is not None]
        if nose is None or not cheeks:
            return 0.0
        return max(0.0, float(np.median(cheeks) - nose))


class TemporalRecognizer:
    def __init__(self, quick_frames: int, detail_frames: int, quick_votes: int, detail_votes: int):
        self.quick_history = deque(maxlen=quick_frames)
        self.detail_history = deque(maxlen=detail_frames)
        self.quick_votes = quick_votes
        self.detail_votes = detail_votes

    def reset(self):
        self.quick_history.clear()
        self.detail_history.clear()

    def add_quick(self, match: MatchResult | None):
        self.quick_history.append(match)
        return self._known_consensus(self.quick_history, self.quick_votes)

    def add_detail(self, match: MatchResult | None):
        self.detail_history.append(match)
        return self._known_consensus(self.detail_history, self.detail_votes)

    @staticmethod
    def _known_consensus(history, required_votes: int):
        known = [match for match in history if match is not None and match.is_known]
        if not known:
            return None
        counts = Counter(match.label for match in known)
        label, votes = counts.most_common(1)[0]
        if votes < required_votes:
            return None
        candidates = [match for match in known if match.label == label]
        return max(candidates, key=lambda match: match.best_similarity or 0.0)


def prepare_recognition_event(event, args, source="edge_ros"):
    event = dict(event)
    now_iso = datetime.now(timezone.utc).isoformat()
    event.setdefault("schema_version", "robot_face_event.v1")
    event.setdefault("event_id", f"evt_{uuid.uuid4().hex}")
    event.setdefault("device_id", args.device_id)
    event.setdefault("source", source)
    event.setdefault("created_at", now_iso)
    event.setdefault("timestamp", event.get("created_at") or now_iso)

    user = event.get("user") if isinstance(event.get("user"), dict) else {}
    name = event.get("name") or user.get("name")
    number = event.get("number") or user.get("number")
    identity_key = event.get("identity_key") or user.get("identity_key")
    if name or number:
        event.setdefault("user", {"name": name, "number": number})

    if identity_key or number or name:
        event.setdefault(
            "subject",
            {
                "person_id": number,
                "display_name": name,
                "identity_key": identity_key,
            },
        )

    state = event.get("state")
    pad = event.get("pad") or {}
    if state == "confirmed_known" and event.get("recognized"):
        access = "allow" if not pad or pad.get("live", False) else "deny"
        reason = "face_and_pad_pass" if access == "allow" else "pad_failed"
    elif state == "spoof_rejected":
        access = "deny"
        reason = "pad_failed"
    elif state == "confirmed_unknown":
        access = "deny"
        reason = "unknown_person"
    elif state in {"model_error", "person_detector_error"}:
        access = "error"
        reason = state
    else:
        access = "pending"
        reason = state or "processing"
    event.setdefault("decision", {"access": access, "reason": reason})
    return event


class PerceptionFrameProcessor:
    def __init__(self, args, logger, *, enable_pad=True, event_source="edge_ros"):
        self.args = args
        self.logger = logger
        self.event_source = event_source
        if args.person_detector == "yolo":
            self.person_detector = YoloPersonDetector(
                args.person_model,
                args.person_confidence,
                args.person_imgsz,
                args.person_device,
            )
            self.logger.info(
                f"Person detector: YOLO model={args.person_model} device={self.person_detector.device}"
            )
        elif args.person_detector == "hog":
            self.person_detector = HogPersonDetector(args.person_confidence)
            self.logger.info("Person detector: OpenCV HOG")
        else:
            self.person_detector = NoopPersonDetector()
            self.logger.info("Person detector: disabled")
        self.identities = load_registered_identities(Path(args.registered_dir))
        if not self.identities:
            self.logger.warning(f"No registered identities found: {args.registered_dir}")
        else:
            self.logger.info(f"Loaded {len(self.identities)} registered identities")

        self.face_args = SimpleNamespace(
            single_face=True,
            similarity_threshold=args.quick_similarity,
            margin_threshold=args.margin_threshold,
            top_k=args.top_k,
        )
        self.detail_face_args = SimpleNamespace(
            single_face=True,
            similarity_threshold=args.detail_similarity,
            margin_threshold=args.margin_threshold,
            top_k=args.top_k,
        )
        self.face_app = create_robot_face_app(args.providers, args.det_size, args.onnx_threads)
        self.temporal = TemporalRecognizer(
            args.quick_frames,
            args.detail_frames,
            args.quick_votes,
            args.detail_votes,
        )
        self.state = "searching"
        self.detail_started_at = None
        self.last_model_error_at = 0.0
        self.pad_estimator = DepthPadEstimator(args) if enable_pad else None
        self.crop_width_ratio = max(0.1, min(1.0, args.crop_width_ratio))
        self.crop_height_ratio = max(0.1, min(1.0, args.crop_height_ratio))
        if self.crop_width_ratio < 1.0 or self.crop_height_ratio < 1.0:
            self.logger.info(
                f"Center crop enabled: width={self.crop_width_ratio:.2f}, height={self.crop_height_ratio:.2f}"
            )

    def crop_frame(self, frame):
        return center_crop(frame, self.crop_width_ratio, self.crop_height_ratio)

    def crop_depth(self, depth_m):
        if depth_m is None:
            return None
        return center_crop(depth_m, self.crop_width_ratio, self.crop_height_ratio)

    def recognize_safely(self, frame, face_args, emit_model_error):
        try:
            return recognize_frame(frame, self.face_app, self.identities, face_args)
        except Exception as exc:
            now = time.monotonic()
            if now - self.last_model_error_at >= self.args.emit_interval:
                event = prepare_recognition_event(
                    {
                        "state": "model_error",
                        "person_detected": True,
                        "message": "얼굴 인식 모델 오류",
                        "error": str(exc),
                        "providers": self.args.providers,
                    },
                    self.args,
                    self.event_source,
                )
                emit_model_error(event)
                self.logger.error(json.dumps(event, ensure_ascii=False))
                self.last_model_error_at = now
            self.temporal.reset()
            return []

    def process_frame(self, frame, depth_m=None, *, emit_model_error):
        now = time.monotonic()
        frame = self.crop_frame(frame)
        depth_m = self.crop_depth(depth_m)

        try:
            people = self.person_detector.detect(frame)
        except Exception as exc:
            event = {
                "state": "person_detector_error",
                "person_detected": False,
                "message": "사람 감지 모델 오류",
                "error": str(exc),
                "detector": self.args.person_detector,
            }
            return event, self.draw_overlay(frame, event, [])

        event = {
            "state": "searching",
            "person_detected": False,
            "message": "사람 탐색 중",
        }
        if not people:
            self.state = "searching"
            self.detail_started_at = None
            self.temporal.reset()
            return event, self.draw_overlay(frame, event, [])

        person = people[0]
        event = {
            "state": "person_detected",
            "person_detected": True,
            "person_confidence": person["confidence"],
            "person_bbox": person["bbox"],
            "message": "사람 후보 발견",
        }

        results = self.recognize_safely(frame, self.face_args, emit_model_error)
        match = results[0][1] if results else None
        confirmed = self.temporal.add_quick(match)
        if confirmed is not None:
            self.state = "confirmed_known"
            self.detail_started_at = None
            event = self.known_event("confirmed_known", confirmed)
        elif self.should_enter_detail(match, now):
            self.state = "detail_recognition"
            if self.detail_started_at is None:
                self.detail_started_at = now
            event = {
                "state": "detail_recognition",
                "person_detected": True,
                "person_confidence": person["confidence"],
                "person_bbox": person["bbox"],
                "message": "등록자 확인 실패, 정밀 인식 중",
            }
            detail_results = self.recognize_safely(frame, self.detail_face_args, emit_model_error)
            detail_match = detail_results[0][1] if detail_results else None
            detail_confirmed = self.temporal.add_detail(detail_match)
            results = detail_results or results
            if detail_confirmed is not None:
                self.state = "confirmed_known"
                self.detail_started_at = None
                event = self.known_event("confirmed_known", detail_confirmed)
            elif now - self.detail_started_at >= self.args.detail_seconds:
                self.state = "confirmed_unknown"
                event = {
                    "state": "confirmed_unknown",
                    "person_detected": True,
                    "person_confidence": person["confidence"],
                    "person_bbox": person["bbox"],
                    "message": "등록되지 않은 사람",
                }
                self.temporal.reset()
                self.detail_started_at = None
        else:
            self.state = "quick_recognition"
            event = {
                "state": "quick_recognition",
                "person_detected": True,
                "person_confidence": person["confidence"],
                "person_bbox": person["bbox"],
                "message": "얼굴 1차 인식 중",
            }
            if match is not None:
                event["best_similarity"] = match.best_similarity
                event["face_label"] = match.label

        self.attach_pad(event, frame, results, person, depth_m)
        return event, self.draw_overlay(frame, event, results)

    def attach_pad(self, event, frame, face_results, person, depth_m):
        if self.pad_estimator is None:
            return
        bbox = None
        if face_results:
            bbox = face_results[0][0]
        elif person is not None:
            bbox = person.get("bbox")

        pad = self.pad_estimator.estimate(depth_m, frame.shape, bbox)
        event["pad"] = pad
        if event.get("state") == "confirmed_known" and not pad.get("live", False):
            event["identity_candidate"] = {
                "name": event.pop("name", None),
                "number": event.pop("number", None),
                "identity_key": event.pop("identity_key", None),
                "score": event.get("score"),
                "best_similarity": event.get("best_similarity"),
            }
            event["recognized"] = False
            event["state"] = "spoof_rejected"
            event["message"] = "Depth PAD 실패: 실제 얼굴 형상 확인 필요"
            self.state = "spoof_rejected"
            self.temporal.reset()

    @staticmethod
    def should_enter_detail_state(processor_state, temporal, match: MatchResult | None, now: float, args, detail_started_at):
        if processor_state == "detail_recognition":
            return True
        if match is None:
            return len(temporal.quick_history) >= args.quick_frames
        if not match.is_known and len(temporal.quick_history) >= args.quick_frames:
            return True
        return False

    def should_enter_detail(self, match: MatchResult | None, now: float) -> bool:
        return PerceptionFrameProcessor.should_enter_detail_state(
            self.state,
            self.temporal,
            match,
            now,
            self.args,
            self.detail_started_at,
        )

    @staticmethod
    def known_event(state: str, match: MatchResult):
        return {
            "state": state,
            "recognized": True,
            "name": match.name,
            "number": match.number,
            "identity_key": match.label,
            "score": match.score,
            "best_similarity": match.best_similarity,
            "margin": match.margin,
            "message": f"등록자 인식: {match.name or match.label}",
        }

    @staticmethod
    def draw_overlay(frame, event, face_results):
        return frame


class RobotFacePerception(Node):
    def __init__(self, args):
        super().__init__("robot_face_perception")
        self.args = args
        if isinstance(args.depth_topic, str) and args.depth_topic.lower() in {"", "none", "false", "off"}:
            args.depth_topic = None
        self.processor = PerceptionFrameProcessor(
            args,
            self.get_logger(),
            enable_pad=bool(args.depth_topic),
            event_source="edge_ros",
        )
        self.last_emit_at = 0.0
        self.last_process_at = 0.0
        self.latest_depth_m = None
        self.latest_depth_stamp = None

        self.result_pub = self.create_publisher(String, args.result_topic, 10)
        self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos_profile_sensor_data)
        self.subscription = self.create_subscription(Image, args.image_topic, self.on_image, qos_profile_sensor_data)
        self.depth_subscription = None
        if args.depth_topic:
            self.depth_subscription = self.create_subscription(Image, args.depth_topic, self.on_depth, qos_profile_sensor_data)
            self.get_logger().info(f"Listening for depth images on {args.depth_topic}")
        self.get_logger().info(f"Listening for camera images on {args.image_topic}")

    def on_depth(self, msg: Image):
        try:
            depth_m = depth_imgmsg_to_meters(msg)
            self.latest_depth_m = self.processor.crop_depth(depth_m)
            self.latest_depth_stamp = msg.header.stamp
        except Exception as exc:
            self.get_logger().warning(f"Depth image ignored: {exc}")

    def on_image(self, msg: Image):
        now = time.monotonic()
        if now - self.last_process_at < 1.0 / max(self.args.process_fps, 0.1):
            return
        self.last_process_at = now
        try:
            frame = imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(str(exc))
            return

        def emit_model_error(event):
            self.result_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))

        event, annotated = self.processor.process_frame(
            frame,
            self.latest_depth_m,
            emit_model_error=emit_model_error,
        )
        self.publish(event, annotated, msg.header)

    def publish(self, event, annotated, header):
        event = prepare_recognition_event(event, self.args, "edge_ros")
        now = time.monotonic()
        if now - self.last_emit_at >= self.args.emit_interval:
            self.result_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
            self.get_logger().debug(json.dumps(event, ensure_ascii=False))
            self.last_emit_at = now

        if self.args.publish_annotated:
            self.annotated_pub.publish(cv_to_imgmsg(annotated, header.frame_id, header.stamp))


def add_common_perception_args(parser):
    parser.add_argument("--result-topic", default="/face/recognition_state")
    parser.add_argument("--annotated-topic", default="/face/annotated_image")
    parser.add_argument("--device-id", default="edge-dev-001", help="Stable device ID used by future server/app integrations")
    parser.add_argument("--registered-dir", default="$HOME/registered_faces")
    parser.add_argument("--providers", nargs="+", default=["CUDAExecutionProvider", "CPUExecutionProvider"])
    parser.add_argument("--det-size", type=int, default=640)
    parser.add_argument("--onnx-threads", type=int, default=2, help="ONNXRuntime CPU threads for face models.")
    parser.add_argument("--person-detector", choices=["yolo", "hog", "none"], default="yolo")
    parser.add_argument("--person-model", default="yolo11n.pt")
    parser.add_argument("--person-device", default="cpu", help="YOLO device: cpu, cuda:0, auto, etc.")
    parser.add_argument("--person-imgsz", type=int, default=640)
    parser.add_argument("--person-confidence", type=float, default=0.35)
    parser.add_argument("--crop-width-ratio", type=float, default=1.0, help="Center-crop input width before recognition, 0.1-1.0")
    parser.add_argument("--crop-height-ratio", type=float, default=1.0, help="Center-crop input height before recognition, 0.1-1.0")
    parser.add_argument("--quick-similarity", type=float, default=0.32)
    parser.add_argument("--detail-similarity", type=float, default=0.36)
    parser.add_argument("--margin-threshold", type=float, default=0.03)
    parser.add_argument("--top-k", type=int, default=3)
    parser.add_argument("--quick-frames", type=int, default=8)
    parser.add_argument("--detail-frames", type=int, default=20)
    parser.add_argument("--quick-votes", type=int, default=3)
    parser.add_argument("--detail-votes", type=int, default=6)
    parser.add_argument("--detail-seconds", type=float, default=3.0)
    parser.add_argument("--emit-interval", type=float, default=0.5)
    parser.add_argument("--process-fps", type=float, default=4.0, help="Maximum face recognition processing FPS.")
    parser.add_argument("--pad-min-distance", type=float, default=0.35)
    parser.add_argument("--pad-max-distance", type=float, default=1.20)
    parser.add_argument("--pad-min-valid-ratio", type=float, default=0.55)
    parser.add_argument("--pad-min-depth-std", type=float, default=0.008)
    parser.add_argument("--pad-min-nose-delta", type=float, default=0.015)
    parser.add_argument("--pad-score-threshold", type=float, default=0.75)
    parser.add_argument("--publish-annotated", action="store_true")


def parse_args():
    parser = argparse.ArgumentParser(description="Detect people, recognize faces, and confirm unknowns from ROS2 images.")
    parser.add_argument("--image-topic", default="/robot/camera/image_raw")
    parser.add_argument("--depth-topic", default=None, help="Optional depth image topic for RealSense anti-spoofing/PAD")
    add_common_perception_args(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    sys.path.insert(0, "/home/gyul")
    rclpy.init()
    node = RobotFacePerception(args)
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
