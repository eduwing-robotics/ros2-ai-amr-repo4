from __future__ import annotations

import argparse
import json
import threading
import time

import cv2
import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from map_line_reference import MapLineReference, add_map_line_arguments
from pc_side.globalcam_combined_detector_node import UdpLatestFrameInput, image_qos, parse_bool
from pc_side.globalcam_object_map_node import CLASS_COLORS
from pc_side.globalcam_turtlebot_proximity_node import TURTLEBOT_COLOR
from pc_side.ros_image_utils import cv_to_imgmsg


class GlobalCamUdpDisplayNode(Node):
    def __init__(self, args):
        super().__init__("globalcam_udp_display_node")
        self.args = args
        self.last_detections = {"safety_detections": [], "turtlebot_detections": [], "proximity": {}}
        self.last_map_line = None
        self.last_turtlebot_goals: list[dict] = []
        self.last_proximity_alert: dict | None = None
        self.last_proximity_alert_at = 0.0
        self.last_displayed_seq: int | None = None
        self.last_live_published_seq: int | None = None
        self._display_window_count = 0
        self._display_window_started = time.monotonic()
        self._last_log_at = time.monotonic()
        self._stop_event = threading.Event()
        self._calibration_points: list[tuple[float, float]] = []
        self._last_display_shape: tuple[int, int] | None = None

        qos = image_qos(args.image_qos_depth)
        live_msg_type = CompressedImage if args.live_compressed else Image
        self.live_pub = self.create_publisher(live_msg_type, args.live_topic, qos)
        self.annotated_pub = None
        if args.publish_annotated:
            self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos)

        self.detection_sub = self.create_subscription(
            String,
            args.detections_topic,
            self.on_detections,
            10,
        )
        self.map_line_sub = self.create_subscription(
            String,
            args.map_line_topic,
            self.on_map_line,
            10,
        )
        self.turtlebot_goal_sub = self.create_subscription(
            String,
            args.turtlebot_goal_topic,
            self.on_turtlebot_goals,
            10,
        )
        self.proximity_alert_sub = self.create_subscription(
            String,
            args.alert_topic,
            self.on_proximity_alert,
            10,
        )

        self.map_line = MapLineReference(args) if args.enable_map_line else None
        if self.map_line is None:
            self.get_logger().warn("Map line drawing disabled.")

        self.udp_input = UdpLatestFrameInput(
            bind=args.udp_bind,
            port=args.udp_port,
            allowed_host=args.udp_allowed_host,
            timeout_sec=args.udp_timeout_sec,
            max_frames_buffer=args.udp_max_frames_buffer,
            socket_buffer=args.udp_socket_buffer,
            logger=self.get_logger(),
        )

        self.create_timer(1.0 / max(args.live_fps, 0.1), self.publish_live_latest)
        self.create_timer(max(args.log_interval, 0.1), self.log_stats)
        self.create_timer(max(args.udp_timeout_sec / 2.0, 0.1), self.udp_input.cleanup_pending)

        self._display_thread = threading.Thread(
            target=self._display_loop,
            name="globalcam-udp-display",
            daemon=True,
        )
        self._display_thread.start()

        self.get_logger().info(
            f"UDP display bind={args.udp_bind}:{args.udp_port} "
            f"allowed_host={args.udp_allowed_host or 'any'} "
            f"window={args.window_name} show_image={args.show_image}"
        )
        live_mode = "compressed" if args.live_compressed else "raw"
        self.get_logger().info(
            f"Publishing live={args.live_topic} mode={live_mode} "
            f"size={args.live_width}x{args.live_height} jpeg_quality={args.live_jpeg_quality}; "
            f"subscribing detections={args.detections_topic} map_line={args.map_line_topic} "
            f"turtlebot_goals={args.turtlebot_goal_topic} proximity_alerts={args.alert_topic}"
        )
        if self.annotated_pub:
            self.get_logger().info(f"Publishing annotated={args.annotated_topic}")
        if args.perspective_calibration_mode:
            self.get_logger().info(
                "Perspective calibration: click map corners in this order: TL, TR, BR, BL. "
                "Right-click removes the last point; press c to clear; press s to print the launch value."
            )

    def on_detections(self, msg: String):
        try:
            self.last_detections = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid detections JSON: {exc}")

    def on_map_line(self, msg: String):
        try:
            map_line = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid map_line JSON: {exc}")
            return
        if map_line.get("square_corners_px") is not None:
            map_line = dict(map_line)
            map_line["_square_points_np"] = np.array(
                map_line["square_corners_px"], dtype=np.float32
            )
        self.last_map_line = map_line

    def on_turtlebot_goals(self, msg: String):
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid turtlebot goal JSON: {exc}")
            return
        goals = payload.get("goals")
        if not isinstance(goals, list):
            self.get_logger().warning("Invalid turtlebot goal payload: missing goals list")
            return
        self.last_turtlebot_goals = [goal for goal in goals if isinstance(goal, dict)]

    def on_proximity_alert(self, msg: String):
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid proximity alert JSON: {exc}")
            return
        if not isinstance(payload, dict):
            self.get_logger().warning("Invalid proximity alert payload: expected object")
            return
        self.last_proximity_alert = payload
        self.last_proximity_alert_at = time.monotonic()
        self.get_logger().info(
            f"Proximity alert received state={payload.get('state')} "
            f"distance={payload.get('distance')} event_id={payload.get('event_id')}"
        )

    def publish_live_latest(self):
        latest = self.udp_input.get_latest()
        if latest is None or self.last_live_published_seq == latest.frame_seq:
            return

        self.last_live_published_seq = latest.frame_seq
        stamp = self.get_clock().now().to_msg()
        live_frame = self.resize_for_live(latest.frame)
        if self.args.live_compressed:
            self.live_pub.publish(
                self.cv_to_compressed_imgmsg(
                    live_frame, latest.frame_id, stamp, self.args.live_jpeg_quality
                )
            )
        else:
            self.live_pub.publish(cv_to_imgmsg(live_frame, latest.frame_id, stamp))

    def _display_loop(self):
        min_interval = 1.0 / max(self.args.display_fps, 1.0)
        while not self._stop_event.is_set():
            loop_start = time.monotonic()
            latest = self.udp_input.get_latest()
            if latest is not None:
                if self.last_displayed_seq != latest.frame_seq:
                    self.last_displayed_seq = latest.frame_seq
                    annotated = self.draw_overlay(latest.frame)
                    if self.args.show_image:
                        display_frame = self.resize_for_display(annotated)
                        self._last_display_shape = display_frame.shape[:2]
                        cv2.imshow(self.args.window_name, display_frame)
                        cv2.setMouseCallback(self.args.window_name, self.on_mouse)
                        key = cv2.waitKey(1) & 0xFF
                        self.handle_key(key)
                    if self.args.perspective_preview:
                        rectified = self.rectify_preview(latest.frame)
                        if rectified is not None:
                            cv2.imshow(f"{self.args.window_name}-Rectified", rectified)
                            cv2.waitKey(1)
                    if self.annotated_pub is not None:
                        stamp = self.get_clock().now().to_msg()
                        self.annotated_pub.publish(
                            cv_to_imgmsg(annotated, latest.frame_id, stamp)
                        )
                    self._display_window_count += 1

            elapsed = time.monotonic() - loop_start
            sleep_for = min_interval - elapsed
            if sleep_for > 0:
                time.sleep(sleep_for)

    def draw_overlay(self, frame: np.ndarray) -> np.ndarray:
        annotated = frame.copy()
        debug = self.args.show_debug_overlay

        if self.args.perspective_calibration_mode:
            labels = ("TL", "TR", "BR", "BL")
            points = [tuple(map(int, map(round, point))) for point in self._calibration_points]
            if len(points) > 1:
                cv2.polylines(
                    annotated, [np.asarray(points, dtype=np.int32)], False, (0, 255, 255), 2
                )
            for index, point in enumerate(points):
                cv2.circle(annotated, point, 6, (0, 255, 255), -1)
                cv2.putText(
                    annotated, labels[index], (point[0] + 8, point[1] - 8),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 255, 255), 2, cv2.LINE_AA,
                )
            help_text = "Click TL, TR, BR, BL | right-click undo | c clear | s print"
            cv2.putText(
                annotated, help_text, (12, annotated.shape[0] - 18),
                cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2, cv2.LINE_AA,
            )

        if self.map_line is not None and self.last_map_line is not None:
            map_line_payload = self.last_map_line
            if map_line_payload.get("square_corners_px") is not None:
                live_map_line = map_line_payload
                if live_map_line.get("_square_points_np") is None:
                    live_map_line = dict(live_map_line)
                    live_map_line["_square_points_np"] = np.array(
                        map_line_payload["square_corners_px"], dtype=np.float32
                    )
                self.map_line.draw(annotated, live_map_line)
                if self.args.show_map_point:
                    for map_x, map_y in self.display_map_points():
                        self.draw_map_point(annotated, live_map_line, map_x, map_y)
                if self.args.show_turtlebot_goals:
                    for goal in self.last_turtlebot_goals:
                        self.draw_turtlebot_goal(annotated, live_map_line, goal)

        for detection in self.last_detections.get("safety_detections", []):
            self.draw_detection(annotated, detection, is_turtlebot=False, debug=debug)

        for detection in self.last_detections.get("turtlebot_detections", []):
            self.draw_detection(annotated, detection, is_turtlebot=True, debug=debug)

        if debug:
            proximity = self.last_detections.get("proximity", {})
            text = f"proximity: {proximity.get('state', 'normal')}"
            if proximity.get("distance") is not None:
                text += f" dist={proximity['distance']:.3f}"
            cv2.rectangle(annotated, (12, 12), (520, 54), (30, 30, 30), -1)
            cv2.putText(
                annotated,
                text,
                (24, 41),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.75,
                (0, 180, 255),
                2,
                cv2.LINE_AA,
            )
        self.draw_proximity_alert(annotated)
        return annotated

    def draw_proximity_alert(self, frame: np.ndarray):
        alert = self.last_proximity_alert
        if alert is None:
            return
        if time.monotonic() - self.last_proximity_alert_at > self.args.alert_display_sec:
            return

        state = str(alert.get("state", "unknown"))
        distance = alert.get("distance")
        if state == "too_close":
            color = (0, 0, 210)
            label = "PROXIMITY TOPIC PUBLISHED: TURTLEBOTS TOO CLOSE"
        elif state == "cleared":
            color = (0, 150, 0)
            label = "PROXIMITY TOPIC PUBLISHED: DISTANCE CLEARED"
        else:
            color = (0, 140, 220)
            label = f"PROXIMITY TOPIC PUBLISHED: {state.upper()}"
        if isinstance(distance, (int, float)):
            label += f" | distance={float(distance):.3f}m"

        overlay = frame.copy()
        cv2.rectangle(overlay, (0, 0), (frame.shape[1], 64), color, -1)
        cv2.addWeighted(overlay, 0.78, frame, 0.22, 0, frame)
        cv2.putText(
            frame,
            label,
            (18, 42),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.72,
            (255, 255, 255),
            2,
            cv2.LINE_AA,
        )

    def display_map_points(self) -> list[tuple[float, float]]:
        text = self.args.map_points.strip()
        if not text:
            return [(self.args.map_point_x, self.args.map_point_y)]
        try:
            points = [tuple(float(value) for value in item.split(",")) for item in text.split(";")]
        except ValueError:
            self.get_logger().warning("Invalid map_points; expected x,y;x,y")
            return []
        if any(len(point) != 2 for point in points):
            self.get_logger().warning("Invalid map_points; expected x,y;x,y")
            return []
        return points

    def draw_map_point(self, frame: np.ndarray, map_line: dict, map_x: float, map_y: float):
        square_points = map_line.get("_square_points_np")
        corners = map_line.get("map_corner_calibration")
        if square_points is None or not isinstance(corners, dict):
            return
        try:
            map_corners = np.asarray(
                [corners["TL"], corners["TR"], corners["BR"], corners["BL"]],
                dtype=np.float32,
            )
            transform = cv2.getPerspectiveTransform(map_corners, square_points.astype(np.float32))
            point = cv2.perspectiveTransform(
                np.asarray([[[map_x, map_y]]], dtype=np.float32),
                transform,
            )[0][0]
        except (KeyError, TypeError, ValueError, cv2.error):
            return
        pixel = tuple(int(round(value)) for value in point)
        label = f"map ({map_x:.3f}, {map_y:.3f})"
        cv2.drawMarker(frame, pixel, (255, 0, 255), cv2.MARKER_CROSS, 26, 3)
        cv2.circle(frame, pixel, 8, (255, 0, 255), 2)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 3, cv2.LINE_AA)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1, cv2.LINE_AA)

    def draw_turtlebot_goal(self, frame: np.ndarray, map_line: dict, goal: dict):
        coordinate = goal.get("goal_coordinate")
        if not isinstance(coordinate, dict):
            return
        try:
            map_x = float(coordinate["x"])
            map_y = float(coordinate["y"])
            square_points = map_line["_square_points_np"].astype(np.float32)
            corners = map_line["map_corner_calibration"]
            map_corners = np.asarray(
                [corners["TL"], corners["TR"], corners["BR"], corners["BL"]], dtype=np.float32
            )
            transform = cv2.getPerspectiveTransform(map_corners, square_points)
            point = cv2.perspectiveTransform(
                np.asarray([[[map_x, map_y]]], dtype=np.float32), transform
            )[0][0]
        except (KeyError, TypeError, ValueError, cv2.error):
            return
        pixel = tuple(int(round(value)) for value in point)
        class_name = str(goal.get("class", "object"))
        label = f"TB goal {class_name}: ({map_x:.3f}, {map_y:.3f})"
        cv2.drawMarker(frame, pixel, (0, 0, 255), cv2.MARKER_TILTED_CROSS, 30, 3)
        cv2.circle(frame, pixel, 10, (0, 0, 255), 2)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] + 24), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] + 24), cv2.FONT_HERSHEY_SIMPLEX, 0.52, (0, 0, 255), 1, cv2.LINE_AA)

    def on_mouse(self, event, x, y, _flags, _userdata):
        if not self.args.perspective_calibration_mode:
            return
        if event == cv2.EVENT_RBUTTONDOWN:
            if self._calibration_points:
                self._calibration_points.pop()
                self.get_logger().info("Removed the last perspective calibration point.")
            return
        if event != cv2.EVENT_LBUTTONDOWN or len(self._calibration_points) >= 4:
            return
        latest = self.udp_input.get_latest()
        if latest is None or self._last_display_shape is None:
            return
        raw_height, raw_width = latest.frame.shape[:2]
        display_height, display_width = self._last_display_shape
        raw_x = float(x) * raw_width / display_width
        raw_y = float(y) * raw_height / display_height
        self._calibration_points.append((raw_x, raw_y))
        labels = ("TL", "TR", "BR", "BL")
        self.get_logger().info(
            f"Perspective point {labels[len(self._calibration_points) - 1]}="
            f"({raw_x:.1f}, {raw_y:.1f})"
        )
        if len(self._calibration_points) == 4:
            self.get_logger().info(
                "Four points selected. Inspect the rectified preview, then press s to print "
                "the map_image_corners launch value."
            )

    def handle_key(self, key: int):
        if not self.args.perspective_calibration_mode:
            return
        if key in (ord('c'), ord('C')):
            self._calibration_points.clear()
            self.get_logger().info("Cleared perspective calibration points.")
        elif key in (ord('s'), ord('S')) and len(self._calibration_points) == 4:
            value = ';'.join(f"{x:.1f},{y:.1f}" for x, y in self._calibration_points)
            self.get_logger().info(
                "Use this after restarting the launch: "
                f"map_image_corners:='{value}'"
            )

    def active_perspective_points(self) -> np.ndarray | None:
        if len(self._calibration_points) == 4:
            return np.asarray(self._calibration_points, dtype=np.float32)
        if self.map_line is not None and self.map_line.image_corners is not None:
            return self.map_line.image_corners
        return None

    def rectify_preview(self, frame: np.ndarray) -> np.ndarray | None:
        points = self.active_perspective_points()
        if points is None:
            return None
        width = max(1, self.args.perspective_preview_width)
        height = max(1, self.args.perspective_preview_height)
        destination = np.asarray(
            [[0, 0], [width - 1, 0], [width - 1, height - 1], [0, height - 1]],
            dtype=np.float32,
        )
        transform = cv2.getPerspectiveTransform(points, destination)
        rectified = cv2.warpPerspective(frame, transform, (width, height))
        if self.args.show_map_point:
            for map_x, map_y in self.display_map_points():
                self.draw_map_point_on_rectified(rectified, destination, map_x, map_y)
        return rectified

    def draw_map_point_on_rectified(self, frame: np.ndarray, destination: np.ndarray, map_x: float, map_y: float):
        if self.last_map_line is None:
            return
        corners = self.last_map_line.get("map_corner_calibration")
        if not isinstance(corners, dict):
            return
        try:
            map_corners = np.asarray(
                [corners["TL"], corners["TR"], corners["BR"], corners["BL"]],
                dtype=np.float32,
            )
            transform = cv2.getPerspectiveTransform(map_corners, destination)
            point = cv2.perspectiveTransform(
                np.asarray([[[map_x, map_y]]], dtype=np.float32),
                transform,
            )[0][0]
        except (KeyError, TypeError, ValueError, cv2.error):
            return
        pixel = tuple(int(round(value)) for value in point)
        label = f"map ({map_x:.3f}, {map_y:.3f})"
        cv2.drawMarker(frame, pixel, (255, 0, 255), cv2.MARKER_CROSS, 26, 3)
        cv2.circle(frame, pixel, 8, (255, 0, 255), 2)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 0, 255), 3, cv2.LINE_AA)
        cv2.putText(frame, label, (pixel[0] + 12, pixel[1] - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1, cv2.LINE_AA)

    def resize_for_live(self, frame: np.ndarray) -> np.ndarray:
        target_width = self.args.live_width
        target_height = self.args.live_height
        if target_width <= 0 and target_height <= 0:
            return frame
        height, width = frame.shape[:2]
        if target_width <= 0:
            target_width = max(1, int(round(width * target_height / height)))
        if target_height <= 0:
            target_height = max(1, int(round(height * target_width / width)))
        if width == target_width and height == target_height:
            return frame
        return cv2.resize(frame, (target_width, target_height), interpolation=cv2.INTER_AREA)

    @staticmethod
    def cv_to_compressed_imgmsg(frame: np.ndarray, frame_id: str, stamp, jpeg_quality: int) -> CompressedImage:
        quality = max(1, min(100, int(jpeg_quality)))
        ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), quality])
        if not ok:
            raise RuntimeError("JPEG compression failed for globalcam live frame")
        msg = CompressedImage()
        msg.header.stamp = stamp
        msg.header.frame_id = frame_id
        msg.format = "jpeg"
        msg.data = encoded.tobytes()
        return msg

    def resize_for_display(self, frame: np.ndarray) -> np.ndarray:
        target_width = self.args.display_width
        target_height = self.args.display_height
        if target_width > 0 or target_height > 0:
            height, width = frame.shape[:2]
            if target_width <= 0:
                target_width = max(1, int(round(width * target_height / height)))
            if target_height <= 0:
                target_height = max(1, int(round(height * target_width / width)))
            return cv2.resize(frame, (target_width, target_height), interpolation=cv2.INTER_AREA)

        scale = self.args.display_scale
        if scale > 0 and abs(scale - 1.0) > 1e-6:
            return cv2.resize(frame, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        return frame

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

    def draw_detection(self, frame: np.ndarray, detection: dict, is_turtlebot: bool, debug: bool = False):
        bbox = detection.get("bbox_xyxy") or detection.get("bbox")
        if not bbox:
            return
        x1, y1, x2, y2 = [int(round(value)) for value in bbox]
        class_name = "turtlebot" if is_turtlebot else detection.get("class", "object")
        color = TURTLEBOT_COLOR if is_turtlebot else CLASS_COLORS.get(class_name, (200, 200, 200))
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)
        label = f"{class_name} {float(detection.get('confidence', 0.0)):.2f}"
        if debug:
            map_position = detection.get("map_position")
            if map_position is not None:
                label += f" x={map_position['x']:.3f} y={map_position['y']:.3f}"
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
            if self.map_line is not None:
                self.map_line.draw_detection_reference(frame, detection)
        else:
            self.draw_compact_label(frame, x1, y1, label, color)

    def display_fps(self) -> float:
        elapsed = max(time.monotonic() - self._display_window_started, 1e-6)
        fps = self._display_window_count / elapsed
        self._display_window_count = 0
        self._display_window_started = time.monotonic()
        return fps

    def log_stats(self):
        now = time.monotonic()
        if now - self._last_log_at < self.args.log_interval:
            return

        input_fps = self.udp_input.input_fps()
        display_fps = self.display_fps()
        self.get_logger().info(
            "udp-display stats "
            f"rx_packets={self.udp_input.rx_packets} "
            f"rx_frames={self.udp_input.rx_frames} "
            f"input_fps={input_fps:.2f} "
            f"display_fps={display_fps:.2f} "
            f"dropped_packets={self.udp_input.dropped_packets} "
            f"dropped_incomplete_frames={self.udp_input.dropped_incomplete_frames} "
            f"pending={self.udp_input.pending_count}"
        )
        self._last_log_at = now

    def destroy_node(self):
        self._stop_event.set()
        if self._display_thread.is_alive():
            self._display_thread.join(timeout=1.0)
        self.udp_input.stop()
        if self.args.show_image:
            try:
                cv2.destroyWindow(self.args.window_name)
            except cv2.error:
                pass
            cv2.destroyAllWindows()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="GlobalCam UDP display node with live publish and map-line overlay.")
    parser.add_argument("--udp-bind", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5005)
    parser.add_argument("--udp-allowed-host", default="")
    parser.add_argument("--udp-timeout-sec", type=float, default=0.5)
    parser.add_argument("--udp-max-frames-buffer", type=int, default=32)
    parser.add_argument("--udp-socket-buffer", type=int, default=4194304)
    parser.add_argument("--window-name", default="GlobalCam-Combined")
    parser.add_argument("--show-image", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-show-image", dest="show_image", action="store_false")
    parser.add_argument("--display-fps", type=float, default=30.0)
    parser.add_argument("--display-width", type=int, default=0)
    parser.add_argument("--display-height", type=int, default=0)
    parser.add_argument("--display-scale", type=float, default=1.0)
    parser.add_argument("--live-fps", type=float, default=5.0)
    parser.add_argument("--map-line-publish-fps", type=float, default=5.0)
    parser.add_argument("--live-topic", default="/globalcam/live/image")
    parser.add_argument("--live-compressed", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-live-compressed", dest="live_compressed", action="store_false")
    parser.add_argument("--live-width", type=int, default=1280)
    parser.add_argument("--live-height", type=int, default=960)
    parser.add_argument("--live-jpeg-quality", type=int, default=80)
    parser.add_argument("--map-line-topic", default="/globalcam/map_line")
    parser.add_argument("--annotated-topic", default="/globalcam/combined/annotated_image")
    parser.add_argument("--detections-topic", default="/globalcam/combined/detections")
    parser.add_argument("--turtlebot-goal-topic", default="/globalcam/turtlebot_goal/coordinates")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument("--alert-display-sec", type=float, default=5.0)
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--show-debug-overlay", type=parse_bool, nargs="?", const=True, default=False)
    parser.add_argument("--no-show-debug-overlay", dest="show_debug_overlay", action="store_false")
    parser.add_argument("--publish-annotated", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-publish-annotated", dest="publish_annotated", action="store_false")
    parser.add_argument("--perspective-calibration-mode", type=parse_bool, nargs="?", const=True, default=False)
    parser.add_argument("--perspective-preview", type=parse_bool, nargs="?", const=True, default=False)
    parser.add_argument("--perspective-preview-width", type=int, default=900)
    parser.add_argument("--perspective-preview-height", type=int, default=900)
    parser.add_argument("--show-map-point", type=parse_bool, nargs="?", const=True, default=False)
    parser.add_argument("--show-turtlebot-goals", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--map-point-x", type=float, default=0.0)
    parser.add_argument("--map-point-y", type=float, default=0.0)
    parser.add_argument("--map-points", default="", help="Map points to display: x,y;x,y;...")
    add_map_line_arguments(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamUdpDisplayNode(args)
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
