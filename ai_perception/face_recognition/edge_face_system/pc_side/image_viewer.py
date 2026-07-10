from __future__ import annotations

import argparse
import time

import cv2
import rclpy
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image

from pc_side.ros_image_utils import compressed_imgmsg_to_cv, imgmsg_to_cv


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


class ImageViewer(Node):
    def __init__(self, args):
        super().__init__('robot_face_image_viewer')
        self.topic = args.topic
        self.window_name = args.window_name
        self.max_width = args.max_width
        self.last_frame_at = None
        self.frame_count = 0
        self.last_fps_at = time.monotonic()
        self.fps = 0.0
        self.compressed = args.compressed or self.topic.endswith('/compressed')
        msg_type = CompressedImage if self.compressed else Image
        self.subscription = self.create_subscription(msg_type, self.topic, self.on_image, image_qos(args.qos_depth))
        cv2.namedWindow(self.window_name, cv2.WINDOW_NORMAL)
        mode = 'compressed/jpeg' if self.compressed else 'raw'
        self.get_logger().info(f'Viewing image topic {self.topic} ({mode}) with best_effort/depth{args.qos_depth}')

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

        if self.max_width and frame.shape[1] > self.max_width:
            scale = self.max_width / float(frame.shape[1])
            frame = cv2.resize(frame, (self.max_width, int(frame.shape[0] * scale)))

        cv2.imshow(self.window_name, frame)
        key = cv2.waitKey(1) & 0xFF
        if key in (27, ord('q')):
            rclpy.shutdown()

    def destroy_node(self):
        cv2.destroyAllWindows()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description='View a ROS2 Image topic on the local PC.')
    parser.add_argument('--topic', default='/face/annotated_image')
    parser.add_argument('--window-name', default='Robot Face View')
    parser.add_argument('--max-width', type=int, default=960)
    parser.add_argument('--qos-depth', type=int, default=1)
    parser.add_argument('--compressed', action='store_true')
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = ImageViewer(args)
    try:
        rclpy.spin(node)
    finally:
        if rclpy.ok():
            node.destroy_node()
            rclpy.shutdown()
        else:
            node.destroy_node()


if __name__ == '__main__':
    main()
