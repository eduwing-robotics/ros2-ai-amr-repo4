from __future__ import annotations

import argparse
import time
from pathlib import Path

import cv2
import numpy as np
import pyrealsense2 as rs
import rclpy
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import Image

from robot_side.ros_image_utils import cv_to_imgmsg


def parse_profile(value):
    try:
        width, height, fps = (int(item) for item in value.lower().split("x"))
    except (TypeError, ValueError) as exc:
        raise argparse.ArgumentTypeError("profile must be WIDTHxHEIGHTxFPS") from exc
    if min(width, height, fps) <= 0:
        raise argparse.ArgumentTypeError("profile values must be positive")
    return width, height, fps


def depth_to_imgmsg(depth_mm, frame_id, stamp):
    depth_mm = np.ascontiguousarray(depth_mm, dtype=np.uint16)
    msg = Image()
    msg.header.stamp = stamp
    msg.header.frame_id = frame_id
    msg.height = int(depth_mm.shape[0])
    msg.width = int(depth_mm.shape[1])
    msg.encoding = "16UC1"
    msg.is_bigendian = False
    msg.step = int(depth_mm.shape[1] * depth_mm.dtype.itemsize)
    msg.data = depth_mm.tobytes()
    return msg


class RealSensePublisher(Node):
    def __init__(self, args):
        super().__init__("robot_realsense_publisher")
        self.args = args
        self.pipeline = rs.pipeline()
        self.align = rs.align(rs.stream.color)
        self.profile = self._start_pipeline(args.color_profile, args.depth_profile)
        depth_sensor = self.profile.get_device().first_depth_sensor()
        self.depth_scale = float(depth_sensor.get_depth_scale())
        self.publish_image_topics = args.publish_image_topics.lower() == "true"
        self.color_pub = None
        self.depth_pub = None
        if self.publish_image_topics:
            self.color_pub = self.create_publisher(Image, args.image_topic, qos_profile_sensor_data)
            self.depth_pub = self.create_publisher(Image, args.depth_topic, qos_profile_sensor_data)
            self.get_logger().info(
                f"Publishing aligned RealSense color={args.color_profile} depth={args.depth_profile} "
                f"to {args.image_topic} and {args.depth_topic}"
            )
        else:
            self.get_logger().info("RealSense raw image/depth ROS topic publishing disabled")
        self.snapshot_output = Path(args.snapshot_output) if args.snapshot_output else None
        self.snapshot_fps = float(args.snapshot_fps or 0)
        self.snapshot_quality = int(args.snapshot_quality)
        self.last_snapshot_at = 0.0
        if self.snapshot_output and self.snapshot_fps > 0:
            self.snapshot_output.parent.mkdir(parents=True, exist_ok=True)
            self.get_logger().info(
                f"Writing UI snapshots to {self.snapshot_output} at {self.snapshot_fps} fps"
            )
        color_fps = parse_profile(args.color_profile)[2]
        depth_fps = parse_profile(args.depth_profile)[2]
        poll_hz = max(color_fps, depth_fps) * 2.0
        self.timer = self.create_timer(1.0 / max(poll_hz, 1.0), self.tick)

    def _start_pipeline(self, color_profile, depth_profile):
        color_width, color_height, color_fps = parse_profile(color_profile)
        depth_width, depth_height, depth_fps = parse_profile(depth_profile)
        config = rs.config()
        if self.args.serial:
            config.enable_device(self.args.serial)
        config.enable_stream(
            rs.stream.color,
            color_width,
            color_height,
            rs.format.bgr8,
            color_fps,
        )
        config.enable_stream(
            rs.stream.depth,
            depth_width,
            depth_height,
            rs.format.z16,
            depth_fps,
        )
        return self.pipeline.start(config)

    def _write_snapshot(self, color):
        if not self.snapshot_output or self.snapshot_fps <= 0:
            return
        now = time.monotonic()
        if now - self.last_snapshot_at < 1.0 / self.snapshot_fps:
            return
        try:
            ok, encoded = cv2.imencode(
                ".jpg",
                color,
                [int(cv2.IMWRITE_JPEG_QUALITY), self.snapshot_quality],
            )
            if not ok:
                return
            tmp = self.snapshot_output.with_suffix(".tmp")
            tmp.write_bytes(encoded.tobytes())
            tmp.replace(self.snapshot_output)
            self.last_snapshot_at = now
        except Exception as exc:
            self.get_logger().warning(f"UI snapshot write failed: {exc}")

    def tick(self):
        try:
            frames = self.pipeline.poll_for_frames()
            if not frames:
                return
            aligned = self.align.process(frames)
            color_frame = aligned.get_color_frame()
            depth_frame = aligned.get_depth_frame()
            if not color_frame or not depth_frame:
                return
            color = np.asanyarray(color_frame.get_data())
            depth_raw = np.asanyarray(depth_frame.get_data())
            depth_mm = np.clip(
                np.rint(depth_raw.astype(np.float32) * self.depth_scale * 1000.0),
                0,
                np.iinfo(np.uint16).max,
            ).astype(np.uint16)
            stamp = self.get_clock().now().to_msg()
            if self.publish_image_topics:
                self.color_pub.publish(cv_to_imgmsg(color, self.args.color_frame_id, stamp))
                self.depth_pub.publish(depth_to_imgmsg(depth_mm, self.args.depth_frame_id, stamp))
            self._write_snapshot(color)
        except RuntimeError as exc:
            self.get_logger().warning(f"RealSense frame error: {exc}")

    def destroy_node(self):
        if hasattr(self, "pipeline"):
            self.pipeline.stop()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="Publish aligned RealSense color and depth images.")
    parser.add_argument("--serial", default="")
    parser.add_argument("--color-profile", default="1920x1080x15")
    parser.add_argument("--depth-profile", default="1280x720x15")
    parser.add_argument("--image-topic", default="/camera/camera/color/image_raw")
    parser.add_argument("--depth-topic", default="/camera/camera/aligned_depth_to_color/image_raw")
    parser.add_argument("--color-frame-id", default="camera_color_optical_frame")
    parser.add_argument("--depth-frame-id", default="camera_color_optical_frame")
    parser.add_argument("--snapshot-output", default="")
    parser.add_argument("--snapshot-fps", type=float, default=0)
    parser.add_argument("--snapshot-quality", type=int, default=82)
    parser.add_argument("--publish-image-topics", default="true", choices=["true", "false"])
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = RealSensePublisher(args)
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()

