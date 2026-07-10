from __future__ import annotations

import argparse
import time
from pathlib import Path

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import CompressedImage, Image

from pc_side.ros_image_utils import compressed_imgmsg_to_cv, imgmsg_to_cv


class GlobalCamReceiver(Node):
    def __init__(self, args):
        super().__init__("globalcam_receiver")
        self.args = args
        self.output = Path(args.output) if args.output else None
        if self.output:
            self.output.parent.mkdir(parents=True, exist_ok=True)
        self.last_write_at = 0.0
        self.frame_count = 0
        self.last_fps_at = time.monotonic()
        self.fps = 0.0
        self.compressed = args.compressed or args.topic.endswith("/compressed")
        msg_type = CompressedImage if self.compressed else Image
        self.subscription = self.create_subscription(msg_type, args.topic, self.on_image, qos_profile_sensor_data)
        mode = "compressed/jpeg" if self.compressed else "raw"
        self.get_logger().info(f"Receiving {args.topic} ({mode})")

    def on_image(self, msg):
        try:
            frame = compressed_imgmsg_to_cv(msg) if self.compressed else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(str(exc))
            return

        self.frame_count += 1
        now = time.monotonic()
        elapsed = now - self.last_fps_at
        if elapsed >= 1.0:
            self.fps = self.frame_count / elapsed
            self.frame_count = 0
            self.last_fps_at = now
            self.get_logger().info(f"GlobalCam receive rate: {self.fps:.2f} fps")

        if self.output and now - self.last_write_at >= 1.0 / max(self.args.write_fps, 1.0):
            ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), self.args.quality])
            if not ok:
                return
            tmp = self.output.with_suffix(".tmp")
            tmp.write_bytes(encoded.tobytes())
            tmp.replace(self.output)
            self.last_write_at = now


def parse_args():
    parser = argparse.ArgumentParser(description="Receive GlobalCam frames and optionally persist the latest JPEG.")
    parser.add_argument("--topic", default="/globalcam/image_raw")
    parser.add_argument("--output", default="/tmp/globalcam_received.jpg")
    parser.add_argument("--write-fps", type=float, default=5.0)
    parser.add_argument("--quality", type=int, default=82)
    parser.add_argument("--compressed", action="store_true")
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = GlobalCamReceiver(args)
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

