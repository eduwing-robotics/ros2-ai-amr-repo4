from __future__ import annotations

import argparse
import time
from pathlib import Path

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image

from pc_side.ros_image_utils import imgmsg_to_cv


class AnnotatedSnapshot(Node):
    def __init__(self, args):
        super().__init__("robot_face_annotated_snapshot")
        self.args = args
        self.output = Path(args.output)
        self.output.parent.mkdir(parents=True, exist_ok=True)
        self.last_write_at = 0.0
        self.subscription = self.create_subscription(Image, args.topic, self.on_image, qos_profile_sensor_data)
        self.get_logger().info(f"Writing {args.topic} snapshots to {self.output}")

    def on_image(self, msg: Image):
        now = time.monotonic()
        if now - self.last_write_at < 1.0 / max(self.args.fps, 1.0):
            return
        try:
            frame = imgmsg_to_cv(msg)
            ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), self.args.quality])
            if not ok:
                return
            tmp = self.output.with_suffix(".tmp")
            tmp.write_bytes(encoded.tobytes())
            tmp.replace(self.output)
            self.last_write_at = now
        except Exception as exc:
            self.get_logger().warning(str(exc))


def parse_args():
    parser = argparse.ArgumentParser(description="Write ROS annotated face images to a JPEG file for the HTML UI.")
    parser.add_argument("--topic", default="/face/annotated_image")
    parser.add_argument("--output", default="/tmp/robot_face_annotated.jpg")
    parser.add_argument("--fps", type=float, default=12.0)
    parser.add_argument("--quality", type=int, default=82)
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = AnnotatedSnapshot(args)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        if rclpy.ok():
            node.destroy_node()
            rclpy.shutdown()


if __name__ == "__main__":
    main()
