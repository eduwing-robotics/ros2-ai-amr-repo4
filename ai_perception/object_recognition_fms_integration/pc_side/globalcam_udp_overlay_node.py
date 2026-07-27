from __future__ import annotations

import argparse
import json
import time

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from sensor_msgs.msg import Image
from std_msgs.msg import String

from pc_side.globalcam_combined_detector_node import UdpLatestFrameInput, image_qos, parse_bool
from pc_side.globalcam_object_map_node import CLASS_COLORS
from pc_side.globalcam_turtlebot_proximity_node import TURTLEBOT_COLOR
from pc_side.ros_image_utils import cv_to_imgmsg


class GlobalCamUdpOverlayNode(Node):
    def __init__(self, args):
        super().__init__("globalcam_udp_overlay_node")
        self.args = args
        self.last_detections = {"safety_detections": [], "turtlebot_detections": []}
        self.last_input_fps = 0.0
        self.last_log_at = time.monotonic()

        qos = image_qos(args.image_qos_depth)
        self.live_pub = self.create_publisher(Image, args.live_topic, qos)
        self.annotated_pub = self.create_publisher(Image, args.annotated_topic, qos)
        self.detection_sub = self.create_subscription(
            String,
            args.detections_topic,
            self.on_detections,
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

        self.create_timer(1.0 / max(args.publish_fps, 1.0), self.publish_annotated_latest)
        self.create_timer(1.0 / max(args.live_fps, 0.1), self.publish_live_latest)
        self.create_timer(max(args.log_interval, 0.1), self.log_stats)
        self.create_timer(max(args.udp_timeout_sec / 2.0, 0.1), self.udp_input.cleanup_pending)
        self.get_logger().info(
            f"UDP overlay bind={args.udp_bind}:{args.udp_port} "
            f"allowed_host={args.udp_allowed_host or 'any'} "
            f"publish_fps={args.publish_fps} live_fps={args.live_fps}"
        )
        self.get_logger().info(
            f"Publishing live={args.live_topic} annotated={args.annotated_topic}; "
            f"subscribing detections={args.detections_topic}"
        )

    def on_detections(self, msg: String):
        try:
            self.last_detections = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Invalid detections JSON: {exc}")

    def publish_live_latest(self):
        latest = self.udp_input.get_latest()
        if latest is None:
            return

        stamp = self.get_clock().now().to_msg()
        self.live_pub.publish(cv_to_imgmsg(latest.frame, latest.frame_id, stamp))

    def publish_annotated_latest(self):
        latest = self.udp_input.get_latest()
        if latest is None:
            return

        stamp = self.get_clock().now().to_msg()
        annotated = self.draw_overlay(latest.frame)
        self.annotated_pub.publish(cv_to_imgmsg(annotated, latest.frame_id, stamp))

    def draw_overlay(self, frame):
        annotated = frame.copy()
        debug = self.args.show_debug_overlay
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
        return annotated

    @staticmethod
    def draw_compact_label(frame, x1: int, y1: int, label: str, color):
        font = cv2.FONT_HERSHEY_SIMPLEX
        scale = 0.42
        thickness = 1
        (text_w, text_h), _ = cv2.getTextSize(label, font, scale, thickness)
        y_text = max(text_h + 4, y1 - 4)
        cv2.rectangle(frame, (x1, y_text - text_h - 2), (x1 + text_w + 4, y_text + 2), color, -1)
        cv2.putText(frame, label, (x1 + 2, y_text), font, scale, (255, 255, 255), thickness, cv2.LINE_AA)

    def draw_detection(self, frame, detection: dict, is_turtlebot: bool, debug: bool = False):
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
            cv2.putText(frame, label, (x1, max(18, y1 - 8)), cv2.FONT_HERSHEY_SIMPLEX, 0.55, color, 2, cv2.LINE_AA)
        else:
            self.draw_compact_label(frame, x1, y1, label, color)

    def log_stats(self):
        input_fps = self.udp_input.input_fps()
        self.get_logger().info(
            "udp-overlay stats "
            f"rx_packets={self.udp_input.rx_packets} "
            f"rx_frames={self.udp_input.rx_frames} "
            f"input_fps={input_fps:.2f} "
            f"dropped_packets={self.udp_input.dropped_packets} "
            f"dropped_incomplete_frames={self.udp_input.dropped_incomplete_frames} "
            f"pending={self.udp_input.pending_count}"
        )

    def destroy_node(self):
        self.udp_input.stop()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="GlobalCam UDP overlay publisher.")
    parser.add_argument("--udp-bind", default="0.0.0.0")
    parser.add_argument("--udp-port", type=int, default=5005)
    parser.add_argument("--udp-allowed-host", default="")
    parser.add_argument("--udp-timeout-sec", type=float, default=0.5)
    parser.add_argument("--udp-max-frames-buffer", type=int, default=32)
    parser.add_argument("--udp-socket-buffer", type=int, default=4194304)
    parser.add_argument("--publish-fps", type=float, default=30.0)
    parser.add_argument("--live-fps", type=float, default=5.0)
    parser.add_argument("--live-topic", default="/globalcam/live/image")
    parser.add_argument("--annotated-topic", default="/globalcam/combined/annotated_image")
    parser.add_argument("--detections-topic", default="/globalcam/combined/detections")
    parser.add_argument("--image-qos-depth", type=int, default=1)
    parser.add_argument("--log-interval", type=float, default=1.0)
    parser.add_argument("--show-debug-overlay", action="store_true", default=False)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamUdpOverlayNode(args)
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
