from __future__ import annotations

import argparse
import json
import threading
import time

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import Image
from std_msgs.msg import String

from pc_side.globalcam_combined_detector_node import UdpLatestFrameInput, image_qos, parse_bool
from pc_side.ros_image_utils import cv_to_imgmsg


def apply_flip(frame, flip_mode: str):
    if flip_mode == "none":
        return frame
    if flip_mode == "horizontal":
        return cv2.flip(frame, 1)
    if flip_mode == "vertical":
        return cv2.flip(frame, 0)
    if flip_mode == "both":
        return cv2.flip(frame, -1)
    raise ValueError(f"Unsupported flip_mode: {flip_mode}")


class TurtleBotUdpDisplayNode(Node):
    def __init__(self, args):
        super().__init__("turtlebot_udp_display_node")
        self.args = args
        self.last_displayed_seq: int | None = None
        self.last_live_published_seq: int | None = None
        self.last_detections: list[dict] = []
        self.last_server_alert: dict | None = None
        self.last_server_alert_at = 0.0
        self._display_window_count = 0
        self._display_window_started = time.monotonic()
        self._live_window_count = 0
        self._live_window_started = time.monotonic()
        self._last_log_at = time.monotonic()
        self._stop_event = threading.Event()

        self.live_pub = self.create_publisher(Image, args.live_topic, image_qos(args.image_qos_depth))
        self.detections_sub = self.create_subscription(
            String,
            args.detections_topic,
            self.on_detections,
            10,
        )
        self.server_alert_sub = self.create_subscription(
            String,
            args.server_safety_event_topic,
            self.on_server_alert,
            10,
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

        self.create_timer(1.0 / max(args.live_fps, 0.1), self.publish_live_latest)
        self.create_timer(max(args.log_interval, 0.1), self.log_stats)
        self.create_timer(max(args.udp_timeout_sec / 2.0, 0.1), self.udp_input.cleanup_pending)

        self._display_thread = threading.Thread(
            target=self._display_loop,
            name="turtlebot-udp-display",
            daemon=True,
        )
        self._display_thread.start()

        self.get_logger().info(
            f"TurtleBot UDP display bind={args.udp_bind}:{args.udp_port} "
            f"allowed_host={args.udp_allowed_host or 'any'} "
            f"window={args.window_name} show_image={args.show_image} flip_mode={args.flip_mode}"
        )
        self.get_logger().info(
            f"Publishing live={args.live_topic}; subscribing detections={args.detections_topic} "
            f"server_alerts={args.server_safety_event_topic}"
        )

    def on_detections(self, msg: String):
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid detections JSON: {exc}")
            return
        detections = payload.get("detections", [])
        self.last_detections = detections if isinstance(detections, list) else []

    def on_server_alert(self, msg: String):
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid server alert JSON: {exc}")
            return
        self.last_server_alert = payload if isinstance(payload, dict) else {"raw": msg.data}
        self.last_server_alert_at = time.monotonic()

    def flipped_latest_frame(self):
        latest = self.udp_input.get_latest()
        if latest is None:
            return None, None
        return latest, apply_flip(latest.frame, self.args.flip_mode)

    def publish_live_latest(self):
        latest, frame = self.flipped_latest_frame()
        if latest is None or frame is None or self.last_live_published_seq == latest.frame_seq:
            return

        self.last_live_published_seq = latest.frame_seq
        stamp = self.get_clock().now().to_msg()
        self.live_pub.publish(cv_to_imgmsg(frame, latest.frame_id, stamp))
        self._live_window_count += 1

    def _display_loop(self):
        min_interval = 1.0 / max(self.args.display_fps, 1.0)
        while not self._stop_event.is_set():
            loop_start = time.monotonic()
            latest, frame = self.flipped_latest_frame()
            if latest is not None and frame is not None and self.last_displayed_seq != latest.frame_seq:
                self.last_displayed_seq = latest.frame_seq
                if self.args.show_image:
                    cv2.imshow(self.args.window_name, self.draw_overlay(frame))
                    cv2.waitKey(1)
                self._display_window_count += 1

            elapsed = time.monotonic() - loop_start
            sleep_for = min_interval - elapsed
            if sleep_for > 0:
                time.sleep(sleep_for)

    def draw_overlay(self, frame):
        annotated = frame.copy()
        self.draw_thirds(annotated)
        for detection in self.last_detections:
            bbox = detection.get("bbox_xyxy") or detection.get("bbox")
            if not bbox:
                continue
            x1, y1, x2, y2 = [int(round(value)) for value in bbox]
            class_name = str(detection.get("class", "object"))
            confidence = float(detection.get("confidence", 0.0))
            color = self.color_for_class(class_name)
            cv2.rectangle(annotated, (x1, y1), (x2, y2), color, 2)
            label = f"{class_name} {confidence:.2f}"
            face_label = self.face_label(detection.get("face_result"))
            if face_label:
                label = f"{label} {face_label}"
            self.draw_label(annotated, x1, y1, label, color)
        # Top server-alert banner disabled: it occludes detections/face labels.
        return annotated

    @staticmethod
    def draw_thirds(frame):
        height, width = frame.shape[:2]
        color = (255, 255, 0)
        for x in (width // 3, (width * 2) // 3):
            cv2.line(frame, (x, 0), (x, height - 1), color, 2)

    def draw_server_alert(self, frame):
        if self.last_server_alert is None:
            return
        age = time.monotonic() - self.last_server_alert_at
        if age > self.args.alert_display_sec:
            return

        event_type = str(self.last_server_alert.get("event_type", "server_alert"))
        confidence = self.last_server_alert.get("confidence")
        if isinstance(confidence, (int, float)):
            text = f"SERVER ALERT: {event_type} {confidence:.2f}"
        else:
            text = f"SERVER ALERT: {event_type}"

        height, width = frame.shape[:2]
        font = cv2.FONT_HERSHEY_SIMPLEX
        scale = 0.8
        thickness = 2
        (text_w, text_h), _ = cv2.getTextSize(text, font, scale, thickness)
        box_w = min(width, text_w + 24)
        cv2.rectangle(frame, (0, 0), (box_w, text_h + 24), (0, 0, 255), -1)
        cv2.putText(
            frame,
            text,
            (12, text_h + 12),
            font,
            scale,
            (255, 255, 255),
            thickness,
            cv2.LINE_AA,
        )

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
    def color_for_class(class_name: str):
        normalized = class_name.strip().lower().replace("-", "_")
        if normalized == "fire":
            return (0, 90, 255)
        if normalized in {"fall", "fallen_worker", "fall_detected"}:
            return (255, 0, 255)
        if normalized == "helmet":
            return (0, 180, 0)
        if normalized in {"head", "no_helmet"}:
            return (0, 0, 255)
        if normalized == "person":
            return (255, 180, 0)
        return (200, 200, 200)

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

    def display_fps(self) -> float:
        elapsed = max(time.monotonic() - self._display_window_started, 1e-6)
        fps = self._display_window_count / elapsed
        self._display_window_count = 0
        self._display_window_started = time.monotonic()
        return fps

    def live_publish_fps(self) -> float:
        elapsed = max(time.monotonic() - self._live_window_started, 1e-6)
        fps = self._live_window_count / elapsed
        self._live_window_count = 0
        self._live_window_started = time.monotonic()
        return fps

    def log_stats(self):
        now = time.monotonic()
        if now - self._last_log_at < self.args.log_interval:
            return

        self.get_logger().info(
            "turtlebot-udp-display stats "
            f"rx_packets={self.udp_input.rx_packets} "
            f"rx_frames={self.udp_input.rx_frames} "
            f"input_fps={self.udp_input.input_fps():.2f} "
            f"display_fps={self.display_fps():.2f} "
            f"live_publish_fps={self.live_publish_fps():.2f} "
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
    parser = argparse.ArgumentParser(description="TurtleBot UDP camera display and live ROS Image publisher.")
    parser.add_argument("--udp-bind", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5006)
    parser.add_argument("--udp-allowed-host", default="")
    parser.add_argument("--udp-timeout-sec", type=float, default=0.5)
    parser.add_argument("--udp-max-frames-buffer", type=int, default=32)
    parser.add_argument("--udp-socket-buffer", type=int, default=4194304)
    parser.add_argument("--window-name", default="TurtleBot-Camera")
    parser.add_argument("--show-image", type=parse_bool, nargs="?", const=True, default=True)
    parser.add_argument("--no-show-image", dest="show_image", action="store_false")
    parser.add_argument("--display-fps", type=float, default=25.0)
    parser.add_argument("--live-fps", type=float, default=20.0)
    parser.add_argument("--live-topic", default="/turtlebot_camera/live/image")
    parser.add_argument("--detections-topic", default="/turtlebot_camera/safety/detections")
    parser.add_argument("--server-safety-event-topic", default="/turtlebot_camera/server/safety_events")
    parser.add_argument("--alert-display-sec", type=float, default=5.0)
    parser.add_argument(
        "--flip-mode",
        choices=("none", "horizontal", "vertical", "both"),
        default="none",
    )
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--log-interval", type=float, default=1.0)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = TurtleBotUdpDisplayNode(args)
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
