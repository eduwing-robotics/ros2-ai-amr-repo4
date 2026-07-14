from __future__ import annotations

import argparse
import json
import math
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from map_line_reference import MapLineReference, add_map_line_arguments
from pc_side.globalcam_combined_detector_node import GlobalCamCombinedDetectorNode, image_qos, parse_bool
from pc_side.globalcam_combined_threaded_detector_node import configure_cpu_runtime
from pc_side.globalcam_object_map_node import (
    DEFAULT_SAFETY_MODEL,
    SafetyDetectionResult,
    SafetyEventDetector,
    clamp_bbox,
    compressed_imgmsg_to_cv,
)
from pc_side.globalcam_turtlebot_proximity_node import (
    DEFAULT_TURTLEBOT_MODEL,
    STATE_NORMAL,
    ProximityAlertTracker,
    TurtlebotProximityDetector,
)
from pc_side.ros_image_utils import imgmsg_to_cv


SERVER_EVENT_CLASS_MAP = {
    "head": "no_helmet",
    "no_helmet": "no_helmet",
    "fire": "fire",
    "fall": "fall",
    "fallen_worker": "fall",
    "fall_detected": "fall",
}

DEFAULT_PERSON_MODEL = "/home/gyul/yolo11n.pt"
PERSON_CLASS_NAME = "person"
SAFETY_PERSON_CLASS_NAMES = {"person"}
ROI_SAFETY_CLASSES = {"head", "helmet", "no_helmet"}
# GlobalCam keeps only fire/fall to cut person + helmet compute.
GLOBALCAM_SAFETY_KEEP_CLASSES = {"fire", "fall", "fallen_worker", "fall_detected"}
GLOBALCAM_SAFETY_DROP_CLASSES = {
    "person",
    "head",
    "helmet",
    "no_helmet",
    "hardhat",
}


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def map_xy_from_detection(detection: dict) -> list[float] | None:
    map_position = detection.get("map_position")
    if not isinstance(map_position, dict):
        return None
    map_xy = map_position.get("map_xy")
    if isinstance(map_xy, list) and len(map_xy) >= 2:
        return [float(map_xy[0]), float(map_xy[1])]
    x = map_position.get("x")
    y = map_position.get("y")
    if x is None or y is None:
        return None
    return [float(x), float(y)]


def map_distance(left: list[float], right: list[float]) -> float:
    return math.hypot(float(left[0]) - float(right[0]), float(left[1]) - float(right[1]))


def detection_center_px(detection: dict) -> list[float] | None:
    center = detection.get("center_px")
    if isinstance(center, list) and len(center) >= 2:
        return [float(center[0]), float(center[1])]
    bbox = detection.get("bbox") or detection.get("bbox_xyxy")
    if isinstance(bbox, list) and len(bbox) >= 4:
        x1, y1, x2, y2 = [float(value) for value in bbox[:4]]
        return [(x1 + x2) / 2.0, (y1 + y2) / 2.0]
    return None


def pixel_distance(left: list[float], right: list[float]) -> float:
    return math.hypot(float(left[0]) - float(right[0]), float(left[1]) - float(right[1]))


def expand_bbox(bbox: list[int], width: int, height: int, scale: float) -> list[int] | None:
    x1, y1, x2, y2 = bbox
    box_w = max(x2 - x1, 1)
    box_h = max(y2 - y1, 1)
    pad_x = box_w * max(scale - 1.0, 0.0) / 2.0
    pad_y = box_h * max(scale - 1.0, 0.0) / 2.0
    return clamp_bbox([x1 - pad_x, y1 - pad_y, x2 + pad_x, y2 + pad_y], width, height)


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

    def detect(self, frame) -> list[SafetyDetectionResult]:
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

        detections = []
        for box in results[0].boxes:
            bbox = clamp_bbox(box.xyxy[0].tolist(), width, height)
            if bbox is None:
                continue
            detections.append(SafetyDetectionResult(PERSON_CLASS_NAME, float(box.conf[0]), bbox))
        detections.sort(key=lambda item: (item.confidence, item.area), reverse=True)
        return detections


class GlobalCamYoloResultNode(GlobalCamCombinedDetectorNode):
    def __init__(self, args):
        Node.__init__(self, "globalcam_yolo_result_node")
        self.args = args
        configure_cpu_runtime(args, self.get_logger())
        self.input_mode = "ros-topic"
        self.last_image_at = 0.0
        self.last_safety_at = 0.0
        self.last_turtlebot_at = 0.0
        self.last_map_line_at = 0.0
        self.last_safety_emit_at = 0.0
        self.last_map_line_published_at = 0.0
        self.last_map_line = None
        self.last_safety_event = None
        self.last_safety_detections: list[dict] = []
        self.last_turtlebot_detections: list[dict] = []
        self.last_closest_pair = None
        self.last_distance: float | None = None
        self.last_proximity_state = STATE_NORMAL
        self.detector_processed_frames = 0
        self.server_event_tracks: dict[str, dict] = {}
        self._stop_event = threading.Event()
        self._frame_lock = threading.Lock()
        self._latest_frame = None
        self._latest_msg = None
        self._latest_seq = 0
        self._processed_safety_seq = 0
        self._processed_turtlebot_seq = 0
        self._processed_map_line_seq = 0
        self._detector_window_count = 0
        self._detector_window_started = time.monotonic()

        self.safety_detector = None
        self.person_detector = None
        self.turtlebot_detector = None
        self.proximity_tracker = None

        if args.enable_safety_detector:
            safety_model = Path(args.safety_model_path)
            if not safety_model.exists():
                raise FileNotFoundError(f"Safety model not found: {safety_model}")
            self.safety_detector = SafetyEventDetector(
                str(safety_model), args.safety_confidence, args.safety_imgsz, args.safety_device
            )
            self.get_logger().info(
                f"Loaded safety model={safety_model} device={self.safety_detector.device} "
                f"names={self.safety_detector.model_names}"
            )
            self.get_logger().info(
                "GlobalCam safety keep classes="
                f"{sorted(GLOBALCAM_SAFETY_KEEP_CLASSES)}; "
                f"drop classes={sorted(GLOBALCAM_SAFETY_DROP_CLASSES)}"
            )
            if args.enable_person_roi_safety:
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
                    f"Loaded person model={person_model} device={self.person_detector.device} "
                    f"names={self.person_detector.model_names}"
                )
            else:
                self.get_logger().info(
                    "GlobalCam person ROI safety disabled; skipping person/helmet ROI path"
                )

        if args.enable_turtlebot_proximity:
            turtlebot_model = Path(args.turtlebot_model_path)
            if not turtlebot_model.exists():
                raise FileNotFoundError(f"Turtlebot model not found: {turtlebot_model}")
            self.turtlebot_detector = TurtlebotProximityDetector(
                str(turtlebot_model), args.turtlebot_confidence, args.turtlebot_imgsz, args.turtlebot_device
            )
            self.proximity_tracker = ProximityAlertTracker(
                args.proximity_enter_distance, args.proximity_exit_distance
            )
            self.get_logger().info(
                f"Loaded turtlebot model={turtlebot_model} device={self.turtlebot_detector.device} "
                f"names={self.turtlebot_detector.model_names}"
            )

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        qos = image_qos(args.image_qos_depth)
        live_msg_type = CompressedImage if args.live_compressed else Image
        self.subscription = self.create_subscription(live_msg_type, args.live_topic, self.on_image, qos)
        self.map_line_pub = self.create_publisher(String, args.map_line_topic, 10)
        self.detections_pub = self.create_publisher(String, args.detections_topic, 10)
        self.turtlebot_goal_pub = self.create_publisher(String, args.turtlebot_goal_topic, 10)
        self.safety_event_pub = self.create_publisher(String, args.event_topic, 10)
        self.alert_pub = self.create_publisher(String, args.alert_topic, 10)
        self.server_object_event_pub = self.create_publisher(String, args.server_object_event_topic, 10)
        self._inference_thread = threading.Thread(target=self._inference_loop, daemon=True)
        self._stats_thread = threading.Thread(target=self._stats_loop, daemon=True)
        self._inference_thread.start()
        self._stats_thread.start()
        live_mode = "compressed" if args.live_compressed else "raw"
        self.get_logger().info(
            f"Subscribing live={args.live_topic} mode={live_mode}; "
            f"publishing map_line={args.map_line_topic} detections={args.detections_topic} "
            f"turtlebot_goals={args.turtlebot_goal_topic}"
        )

    def on_image(self, msg: Image | CompressedImage):
        try:
            frame = compressed_imgmsg_to_cv(msg) if self.args.live_compressed else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(f"Image conversion failed: {exc}")
            return
        with self._frame_lock:
            self._latest_frame = frame
            self._latest_msg = msg
            self._latest_seq += 1
            self.last_image_at = time.monotonic()

    def update_map_line(self, frame):
        if self.map_line is None:
            return None
        self.last_map_line = self.map_line.update(frame)
        self.publish_map_line()
        return self.last_map_line

    def publish_map_line(self):
        if self.last_map_line is None:
            return
        now = time.monotonic()
        min_interval = 1.0 / max(self.args.map_line_publish_fps, 0.1)
        if now - self.last_map_line_published_at < min_interval:
            return
        payload = MapLineReference.serializable(self.last_map_line)
        if payload is None:
            return
        self.map_line_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))
        self.last_map_line_published_at = now

    def _get_latest(self):
        with self._frame_lock:
            if self._latest_frame is None:
                return None, None, 0
            return self._latest_frame.copy(), self._latest_msg, self._latest_seq

    def _inference_loop(self):
        while not self._stop_event.is_set():
            frame, msg, seq = self._get_latest()
            if frame is None:
                time.sleep(0.01)
                continue
            now = time.monotonic()
            map_line = None
            ran = False
            if (
                self.args.enable_map_line
                and seq != self._processed_map_line_seq
                and self.should_run(now, self.last_map_line_at, self.args.map_line_publish_fps, True)
            ):
                map_line = self.update_map_line(frame)
                self.last_map_line_at = now
                self._processed_map_line_seq = seq
            if (
                self.args.enable_safety_detector
                and seq != self._processed_safety_seq
                and self.should_run(now, self.last_safety_at, self.args.safety_fps, True)
            ):
                map_line = self.update_map_line(frame)
                self.last_safety_at = now
                self._processed_safety_seq = seq
                self.run_safety_pipeline(frame, map_line, msg=msg)
                self.update_server_object_events()
                ran = True
            if (
                self.args.enable_turtlebot_proximity
                and seq != self._processed_turtlebot_seq
                and self.should_run(now, self.last_turtlebot_at, self.args.turtlebot_fps, True)
            ):
                if map_line is None:
                    map_line = self.update_map_line(frame)
                self.last_turtlebot_at = now
                self._processed_turtlebot_seq = seq
                self.run_turtlebot_pipeline(frame, map_line)
                ran = True
            if ran:
                self.detector_processed_frames += 1
                self._detector_window_count += 1
                self.publish_detections()
            else:
                time.sleep(0.005)

    def run_safety_pipeline(self, frame, map_line, msg=None, timestamp_ns=None):
        started = time.perf_counter()
        try:
            detections = self.safety_detector.detect(frame)
            person_detections = self.person_detector.detect(frame) if self.person_detector else []
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

        detections = [
            detection
            for detection in detections
            if self.keep_globalcam_safety_class(detection.class_name)
        ]
        if self.person_detector is not None:
            detections.extend(self.detect_safety_in_person_rois(frame, person_detections))
            # Person boxes themselves are not published on GlobalCam.
            person_detections = []
        all_detections = self.enrich_safety_detections(detections + person_detections, map_line)
        for detection in all_detections:
            detection['turtlebot_goal_position'] = (
                self.map_line.turtlebot_goal_position(
                    detection.get('map_position'),
                    self.args.turtlebot_goal_offset_x,
                )
                if self.map_line is not None
                else None
            )
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

    @staticmethod
    def keep_globalcam_safety_class(class_name: str) -> bool:
        normalized = str(class_name).strip().lower().replace("-", "_")
        if normalized in GLOBALCAM_SAFETY_DROP_CLASSES:
            return False
        if normalized in GLOBALCAM_SAFETY_KEEP_CLASSES:
            return True
        # Unknown classes: keep only if not clearly person/helmet related.
        return normalized not in SAFETY_PERSON_CLASS_NAMES and normalized not in ROI_SAFETY_CLASSES

    def detect_safety_in_person_rois(
        self,
        frame,
        person_detections: list[SafetyDetectionResult],
    ) -> list[SafetyDetectionResult]:
        if self.person_detector is None or not person_detections:
            return []

        height, width = frame.shape[:2]
        roi_detections: list[SafetyDetectionResult] = []
        for person in person_detections[: self.args.person_roi_max_count]:
            roi_bbox = expand_bbox(person.bbox_xyxy, width, height, self.args.person_roi_expand)
            if roi_bbox is None:
                continue
            x1, y1, x2, y2 = roi_bbox
            roi = frame[y1:y2, x1:x2]
            if roi.size == 0:
                continue
            try:
                detections = self.safety_detector.detect(roi)
            except Exception as exc:
                self.get_logger().warning(f"Person ROI safety detection failed: {exc}")
                continue
            for detection in detections:
                class_name = str(detection.class_name).strip().lower()
                if class_name not in ROI_SAFETY_CLASSES:
                    continue
                bx1, by1, bx2, by2 = detection.bbox_xyxy
                bbox = clamp_bbox([bx1 + x1, by1 + y1, bx2 + x1, by2 + y1], width, height)
                if bbox is None:
                    continue
                roi_detections.append(
                    SafetyDetectionResult(detection.class_name, detection.confidence, bbox)
                )
        return roi_detections

    def publish_detections(self):
        payload = {
            "schema_version": "globalcam_combined_detections.v1",
            "created_at": time.time(),
            "safety_detections": self.last_safety_detections,
            "turtlebot_detections": self.last_turtlebot_detections,
            "proximity": {
                "state": self.last_proximity_state,
                "distance": round(self.last_distance, 3) if self.last_distance is not None else None,
            },
        }
        self.detections_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))
        goals = []
        for detection in self.last_safety_detections:
            goal = detection.get('turtlebot_goal_position')
            if goal is None:
                continue
            goals.append(
                {
                    'class': detection.get('class'),
                    'confidence': detection.get('confidence'),
                    'source_coordinate': detection.get('map_position'),
                    'goal_coordinate': goal,
                }
            )
        if goals:
            self.turtlebot_goal_pub.publish(
                String(
                    data=json.dumps(
                        {
                            'schema_version': 'globalcam_turtlebot_goal_coordinates.v1',
                            'created_at': utc_now_iso(),
                            'camera_id': self.args.camera_id,
                            'source_topic': self.args.live_topic,
                            'goals': goals,
                        },
                        ensure_ascii=False,
                    )
                )
            )

    def server_event_type(self, class_name: str) -> str | None:
        normalized = str(class_name).strip().lower().replace("-", "_")
        event_type = SERVER_EVENT_CLASS_MAP.get(normalized)
        if event_type == "no_helmet":
            return None
        return event_type

    def candidate_server_detections(self) -> dict[str, dict]:
        candidates: dict[str, dict] = {}
        for detection in self.last_safety_detections:
            event_type = self.server_event_type(detection.get("class", ""))
            if event_type is None:
                continue
            map_xy = map_xy_from_detection(detection)
            if map_xy is None:
                continue
            map_position = detection.get("map_position") or {}
            if not self.args.server_include_outside_objects and not map_position.get("inside"):
                continue
            current = candidates.get(event_type)
            if current is None or float(detection.get("confidence", 0.0)) > float(current.get("confidence", 0.0)):
                candidates[event_type] = detection
        return candidates

    def update_server_object_events(self) -> None:
        candidates = self.candidate_server_detections()
        now = time.monotonic()
        stale_after = max(self.args.server_track_stale_sec, self.args.server_required_duration_sec)

        for event_type in set(SERVER_EVENT_CLASS_MAP.values()):
            detection = candidates.get(event_type)
            track = self.server_event_tracks.get(event_type)

            if detection is None:
                if (
                    track is not None
                    and not track.get("emitted", False)
                    and now - track.get("last_seen_at", track["started_at"]) > stale_after
                ):
                    self.server_event_tracks.pop(event_type, None)
                continue

            map_xy = map_xy_from_detection(detection)
            center_px = detection_center_px(detection)
            if map_xy is None or center_px is None:
                continue

            same_region = (
                track is not None
                and pixel_distance(track["region_center_px"], center_px) <= self.args.server_pixel_tolerance
            )
            if not same_region:
                track = {
                    "count": 0,
                    "visible_duration_sec": 0.0,
                    "started_at": now,
                    "last_seen_at": now,
                    "region_center_px": center_px,
                    "last_center_px": center_px,
                    "last_map_xy": map_xy,
                    "last_detection": detection,
                    "emitted": False,
                }
                self.server_event_tracks[event_type] = track

            previous_seen_at = float(track.get("last_seen_at", now))
            if track["count"] > 0:
                gap = max(0.0, now - previous_seen_at)
                track["visible_duration_sec"] += min(gap, self.args.server_detection_gap_sec)

            track["count"] += 1
            track["last_center_px"] = center_px
            track["last_map_xy"] = map_xy
            track["last_detection"] = detection
            track["last_seen_at"] = now

            count_ready = track["count"] >= self.args.server_required_consecutive
            duration_ready = track["visible_duration_sec"] >= self.args.server_required_duration_sec
            if (count_ready or duration_ready) and not track["emitted"]:
                trigger = "count" if count_ready else "duration"
                self.publish_server_object_event(event_type, detection, track, trigger)
                track["emitted"] = True

    def publish_server_object_event(self, event_type: str, detection: dict, track: dict, trigger: str) -> None:
        map_xy = map_xy_from_detection(detection)
        if map_xy is None:
            return
        payload = {
            "schema_version": "globalcam_server_object_event.v1",
            "event_id": f"server_evt_{uuid.uuid4().hex}",
            "event_type": event_type,
            "created_at": utc_now_iso(),
            "camera_id": self.args.camera_id,
            "source_topic": self.args.live_topic,
            "coordinate": {
                "x": round(map_xy[0], 3),
                "y": round(map_xy[1], 3),
                "unit": "map",
            },
            "trigger": trigger,
            "detection_count": int(track["count"]),
            "consecutive_count": int(track["count"]),
            "visible_duration_sec": round(float(track.get("visible_duration_sec", 0.0)), 3),
            "pixel_tolerance": self.args.server_pixel_tolerance,
            "position_tolerance": self.args.server_position_tolerance,
            "region_center_px": [round(float(value), 1) for value in track.get("region_center_px", [])],
            "last_center_px": [round(float(value), 1) for value in track.get("last_center_px", [])],
            "last_detection": {
                "class": detection.get("class"),
                "confidence": detection.get("confidence"),
                "bbox": detection.get("bbox") or detection.get("bbox_xyxy"),
                "center_px": detection.get("center_px"),
                "map_position": detection.get("map_position"),
            },
        }
        self.server_object_event_pub.publish(String(data=json.dumps(payload, ensure_ascii=False)))
        self.get_logger().info(
            f"Server object event event_type={event_type} trigger={trigger} "
            f"count={track['count']} duration={payload['visible_duration_sec']:.3f}s "
            f"coordinate=({payload['coordinate']['x']},{payload['coordinate']['y']})"
        )

    def _stats_loop(self):
        while not self._stop_event.is_set():
            time.sleep(max(self.args.log_interval, 0.1))
            elapsed = max(time.monotonic() - self._detector_window_started, 1e-6)
            fps = self._detector_window_count / elapsed
            self._detector_window_count = 0
            self._detector_window_started = time.monotonic()
            self.get_logger().info(
                f"yolo-result stats detector_processed_frames={self.detector_processed_frames} detector_fps={fps:.2f}"
            )

    def destroy_node(self):
        self._stop_event.set()
        for thread in (self._inference_thread, self._stats_thread):
            if thread.is_alive():
                thread.join(timeout=1.0)
        Node.destroy_node(self)


def parse_args():
    parser = argparse.ArgumentParser(description="GlobalCam YOLO result node.")
    parser.add_argument("--live-topic", default="/globalcam/live/image")
    parser.add_argument("--live-compressed", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-live-compressed", dest="live_compressed", action="store_false")
    parser.add_argument("--map-line-topic", default="/globalcam/map_line")
    parser.add_argument("--detections-topic", default="/globalcam/combined/detections")
    parser.add_argument("--event-topic", default="/globalcam/object_map/events")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument("--server-object-event-topic", default="/globalcam/server/object_events")
    parser.add_argument("--turtlebot-goal-topic", default="/globalcam/turtlebot_goal/coordinates")
    parser.add_argument("--turtlebot-goal-offset-x", type=float, default=0.3)
    parser.add_argument("--image-topic", default="/globalcam/live/image")
    parser.add_argument("--device-id", default="edge-dev-001")
    parser.add_argument("--camera-id", default="globalcam-001")
    parser.add_argument("--enable-safety-detector", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-safety-detector", dest="enable_safety_detector", action="store_false")
    parser.add_argument("--enable-turtlebot-proximity", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-enable-turtlebot-proximity", dest="enable_turtlebot_proximity", action="store_false")
    parser.add_argument("--safety-model-path", default=DEFAULT_SAFETY_MODEL)
    parser.add_argument("--person-model-path", default=DEFAULT_PERSON_MODEL)
    parser.add_argument("--turtlebot-model-path", default=DEFAULT_TURTLEBOT_MODEL)
    parser.add_argument("--safety-device", default="auto")
    parser.add_argument("--turtlebot-device", default="auto")
    parser.add_argument("--safety-confidence", type=float, default=0.6)
    parser.add_argument("--person-confidence", type=float, default=0.35)
    parser.add_argument("--turtlebot-confidence", type=float, default=0.6)
    parser.add_argument("--safety-imgsz", type=int, default=1280)
    parser.add_argument("--person-imgsz", type=int, default=1280)
    parser.add_argument("--turtlebot-imgsz", type=int, default=1280)
    parser.add_argument("--safety-fps", type=float, default=2.0)
    parser.add_argument("--turtlebot-fps", type=float, default=2.0)
    parser.add_argument("--map-line-publish-fps", type=float, default=5.0)
    parser.add_argument("--torch-num-threads", type=int, default=2)
    parser.add_argument("--torch-num-interop-threads", type=int, default=1)
    parser.add_argument("--opencv-num-threads", type=int, default=1)
    parser.add_argument("--proximity-enter-distance", type=float, default=0.2)
    parser.add_argument("--proximity-exit-distance", type=float, default=0.3)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--emit-interval", type=float, default=0.2)
    parser.add_argument("--include-outside-objects", action="store_true", default=False)
    parser.add_argument("--enable-person-roi-safety", type=parse_bool, nargs="?", const=True, default=False)
    parser.add_argument("--no-enable-person-roi-safety", dest="enable_person_roi_safety", action="store_false")
    parser.add_argument("--person-roi-expand", type=float, default=1.6)
    parser.add_argument("--person-roi-max-count", type=int, default=8)
    parser.add_argument("--server-required-consecutive", type=int, default=10)
    parser.add_argument("--server-required-duration-sec", type=float, default=5.0)
    parser.add_argument("--server-pixel-tolerance", type=float, default=80.0)
    parser.add_argument("--server-detection-gap-sec", type=float, default=5.0)
    parser.add_argument("--server-track-stale-sec", type=float, default=12.0)
    parser.add_argument("--server-position-tolerance", type=float, default=0.15)
    parser.add_argument("--server-include-outside-objects", action="store_true", default=False)
    parser.add_argument("--log-interval", type=float, default=1.0)
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamYoloResultNode(args)
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
