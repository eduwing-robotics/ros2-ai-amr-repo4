from __future__ import annotations

import argparse
import time

import cv2
import rclpy
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image

from robot_side.ros_image_utils import cv_to_compressed_imgmsg, cv_to_imgmsg


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


class CameraPublisher(Node):
    def __init__(self, args):
        super().__init__("robot_camera_publisher")
        self.camera = args.camera
        self.frame_id = args.frame_id
        self.args = args
        self.publish_every = 1.0 / max(args.fps, 1.0)
        self.compressed = args.compressed or args.topic.endswith("/compressed")
        msg_type = CompressedImage if self.compressed else Image
        self.publisher = self.create_publisher(msg_type, args.topic, image_qos(args.qos_depth))
        camera_source = int(self.camera) if str(self.camera).isdigit() else self.camera
        self.capture = cv2.VideoCapture(camera_source)
        if not self.capture.isOpened():
            raise RuntimeError(f"Cannot open camera {self.camera}")

        self.capture.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
        self.capture.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
        self.capture.set(cv2.CAP_PROP_FPS, args.camera_fps)
        self.capture.set(cv2.CAP_PROP_BUFFERSIZE, args.buffer_size)
        self.last_publish = 0.0
        self.timer = self.create_timer(0.005, self.tick)
        mode = f"compressed/jpeg q={args.jpeg_quality}" if self.compressed else "raw bgr8"
        self.get_logger().info(
            f"Publishing camera {self.camera} to {args.topic} "
            f"at {args.width}x{args.height}, max {args.fps:g} fps, "
            f"camera_fps={args.camera_fps:g}, qos=best_effort/depth{args.qos_depth}, mode={mode}"
        )

    def tick(self):
        now = time.monotonic()
        if now - self.last_publish < self.publish_every:
            return

        ok, frame = self.capture.read()
        if not ok:
            self.get_logger().warning("Camera frame read failed")
            return

        stamp = self.get_clock().now().to_msg()
        if self.compressed:
            msg = cv_to_compressed_imgmsg(frame, self.frame_id, stamp, self.args.jpeg_quality)
        else:
            msg = cv_to_imgmsg(frame, self.frame_id, stamp)
        self.publisher.publish(msg)
        self.last_publish = now

    def destroy_node(self):
        if hasattr(self, "capture"):
            self.capture.release()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="Publish USB/Pi camera frames as a ROS2 Image topic.")
    parser.add_argument("--camera", default="0", help="OpenCV camera index or device path, e.g. 0 or /dev/video6.")
    parser.add_argument("--topic", default="/robot/camera/image_raw")
    parser.add_argument("--frame-id", default="robot_camera")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--fps", type=float, default=10.0, help="Maximum ROS publish FPS.")
    parser.add_argument("--camera-fps", type=float, default=15.0, help="Requested camera capture FPS.")
    parser.add_argument("--buffer-size", type=int, default=1, help="Requested OpenCV capture buffer size.")
    parser.add_argument("--qos-depth", type=int, default=1, help="ROS image QoS queue depth.")
    parser.add_argument("--compressed", action="store_true", help="Publish sensor_msgs/CompressedImage JPEG frames.")
    parser.add_argument("--jpeg-quality", type=int, default=65, help="JPEG quality for compressed publishing.")
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = CameraPublisher(args)
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()

