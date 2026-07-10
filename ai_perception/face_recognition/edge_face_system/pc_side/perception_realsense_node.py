from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import pyrealsense2 as rs
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from std_msgs.msg import String

from pc_side.perception_node import (
    PerceptionFrameProcessor,
    add_common_perception_args,
    prepare_recognition_event,
)
from robot_side.realsense_publisher import parse_profile


class RealSenseDirectPerception(Node):
    def __init__(self, args):
        super().__init__("robot_face_perception_realsense")
        self.args = args
        self.pipeline = rs.pipeline()
        self.align = rs.align(rs.stream.color)
        self.profile = self._start_pipeline(args.color_profile, args.depth_profile)
        depth_sensor = self.profile.get_device().first_depth_sensor()
        self.depth_scale = float(depth_sensor.get_depth_scale())
        self.processor = PerceptionFrameProcessor(
            args,
            self.get_logger(),
            enable_pad=True,
            event_source="edge_ros",
        )
        self.result_pub = self.create_publisher(String, args.result_topic, 10)
        self.last_emit_at = 0.0
        self.last_process_at = 0.0
        self.snapshot_output = Path(args.snapshot_output) if args.snapshot_output else None
        self.snapshot_fps = float(args.snapshot_fps or 0)
        self.snapshot_quality = int(args.snapshot_quality)
        self.last_snapshot_at = 0.0
        if self.snapshot_output and self.snapshot_fps > 0:
            self.snapshot_output.parent.mkdir(parents=True, exist_ok=True)
            self.get_logger().info(
                f"Writing UI snapshots to {self.snapshot_output} at {self.snapshot_fps} fps"
            )
        self.get_logger().info(
            f"Direct RealSense perception: color={args.color_profile} depth={args.depth_profile} "
            f"serial={args.serial or 'auto'}"
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
            depth_m = depth_raw.astype(np.float32) * self.depth_scale
            self._write_snapshot(color)

            now = time.monotonic()
            if now - self.last_process_at < 1.0 / max(self.args.process_fps, 0.1):
                return
            self.last_process_at = now

            def emit_model_error(event):
                self.result_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))

            event, _annotated = self.processor.process_frame(
                color,
                depth_m,
                emit_model_error=emit_model_error,
            )
            self.publish_event(event)
        except RuntimeError as exc:
            self.get_logger().warning(f"RealSense frame error: {exc}")

    def publish_event(self, event):
        event = prepare_recognition_event(event, self.args, "edge_ros")
        now = time.monotonic()
        if now - self.last_emit_at >= self.args.emit_interval:
            self.result_pub.publish(String(data=json.dumps(event, ensure_ascii=False)))
            self.get_logger().debug(json.dumps(event, ensure_ascii=False))
            self.last_emit_at = now

    def destroy_node(self):
        if hasattr(self, "pipeline"):
            self.pipeline.stop()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(
        description="Recognize faces directly from RealSense without raw image/depth ROS topics."
    )
    parser.add_argument("--serial", default="")
    parser.add_argument("--color-profile", default="424x240x15")
    parser.add_argument("--depth-profile", default="480x270x15")
    parser.add_argument("--snapshot-output", default="")
    parser.add_argument("--snapshot-fps", type=float, default=0)
    parser.add_argument("--snapshot-quality", type=int, default=82)
    add_common_perception_args(parser)
    return parser.parse_args()


def main():
    args = parse_args()
    sys.path.insert(0, "/home/gyul")
    rclpy.init()
    node = RealSenseDirectPerception(args)
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
