from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import cv2
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from pc_side.map_line_detection import annotate_map_lines, detect_map_lines
from pc_side.ros_image_utils import compressed_imgmsg_to_cv, cv_to_imgmsg, imgmsg_to_cv


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


class MapLineDetectorNode(Node):
    def __init__(self, args):
        super().__init__("map_line_detector")
        self.args = args
        self.image_topic = args.image_topic
        self.compressed = args.compressed or self.image_topic.endswith("/compressed")
        self.show_viewer = args.show_viewer
        self.max_width = args.max_width
        self.frame_count = 0
        self.last_fps_at = time.monotonic()
        self.fps = 0.0

        if args.image_file:
            self.image_file = Path(args.image_file)
            if not self.image_file.is_file():
                raise FileNotFoundError(f"Image file not found: {self.image_file}")
            self.subscription = None
            self.timer = self.create_timer(1.0 / max(args.file_fps, 1.0), self.on_file_timer)
        else:
            self.image_file = None
            msg_type = CompressedImage if self.compressed else Image
            self.subscription = self.create_subscription(
                msg_type,
                self.image_topic,
                self.on_image,
                image_qos(args.qos_depth),
            )
            self.timer = None

        if args.publish_annotated:
            self.annotated_pub = self.create_publisher(Image, args.annotated_topic, image_qos(args.qos_depth))
        else:
            self.annotated_pub = None

        if args.publish_detections:
            self.detection_pub = self.create_publisher(String, args.detections_topic, 10)
        else:
            self.detection_pub = None

        if self.show_viewer:
            cv2.namedWindow(args.window_name, cv2.WINDOW_NORMAL)

        if self.image_file:
            source = f"file:{self.image_file}"
        else:
            mode = "compressed/jpeg" if self.compressed else "raw"
            source = f"{self.image_topic} ({mode})"
        self.get_logger().info(
            f"Green line detector listening on {source}, viewer={'on' if self.show_viewer else 'off'}"
        )

    def process_frame(self, frame, stamp, frame_id: str):
        detection, mask = detect_map_lines(
            frame,
            roi_top_ratio=self.args.roi_top_ratio,
            blur_size=self.args.blur_size,
            morph_kernel=self.args.morph_kernel,
            green_h_min=self.args.green_h_min,
            green_h_max=self.args.green_h_max,
            green_s_min=self.args.green_s_min,
            green_v_min=self.args.green_v_min,
            green_v_max_dark=self.args.green_v_max_dark,
            green_v_min_light=self.args.green_v_min_light,
            require_bgr_match=self.args.require_bgr_match,
            bgr_g_min_dark=self.args.bgr_g_min_dark,
            bgr_g_min_light=self.args.bgr_g_min_light,
            bgr_r_max_dark=self.args.bgr_r_max_dark,
            bgr_b_max_dark=self.args.bgr_b_max_dark,
            bgr_r_max_light=self.args.bgr_r_max_light,
            bgr_b_max_light=self.args.bgr_b_max_light,
            bgr_gap_min_dark=self.args.bgr_gap_min_dark,
            bgr_gap_min_light=self.args.bgr_gap_min_light,
            bgr_dark_dominance=self.args.bgr_dark_dominance,
            min_blob_area=self.args.min_blob_area,
            min_contour_area=self.args.min_contour_area,
            min_line_length=self.args.min_line_length,
            min_aspect_ratio=self.args.min_aspect_ratio,
            max_line_width=self.args.max_line_width,
            max_segments=self.args.max_segments,
            max_contours=self.args.max_contours,
            angle_tol=self.args.angle_tol,
            distance_tol=self.args.distance_tol,
        )

        segment_count = len(detection.segments)
        if segment_count > 0:
            self.get_logger().info(
                f"Detected {segment_count} green line segment(s), "
                f"offset={detection.lateral_offset:.1f}px"
                if detection.lateral_offset is not None
                else f"Detected {segment_count} green line segment(s)",
                throttle_duration_sec=1.0,
            )

        annotated = annotate_map_lines(
            frame,
            detection,
            mask,
            self.fps,
            show_mask=self.args.show_mask,
        )

        if self.detection_pub is not None:
            payload = detection.to_payload(stamp, frame_id or self.args.frame_id)
            msg = String()
            msg.data = json.dumps(payload, ensure_ascii=False)
            self.detection_pub.publish(msg)

        if self.annotated_pub is not None:
            out_msg = cv_to_imgmsg(annotated, frame_id or self.args.frame_id, stamp)
            self.annotated_pub.publish(out_msg)

        if self.show_viewer:
            display = annotated
            if self.max_width and display.shape[1] > self.max_width:
                scale = self.max_width / float(display.shape[1])
                display = cv2.resize(display, (self.max_width, int(display.shape[0] * scale)))
            cv2.imshow(self.args.window_name, display)
            key = cv2.waitKey(1) & 0xFF
            if key in (27, ord("q")):
                rclpy.shutdown()

    def on_image(self, msg):
        try:
            frame = compressed_imgmsg_to_cv(msg) if self.compressed else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(str(exc))
            return

        self._update_fps()
        self.process_frame(frame, msg.header.stamp, msg.header.frame_id)

    def on_file_timer(self):
        frame = cv2.imread(str(self.image_file))
        if frame is None:
            self.get_logger().error(f"Failed to read image file: {self.image_file}")
            return

        stamp = self.get_clock().now().to_msg()
        self._update_fps()
        self.process_frame(frame, stamp, self.args.frame_id)

    def _update_fps(self):
        self.frame_count += 1
        now = time.monotonic()
        elapsed = now - self.last_fps_at
        if elapsed >= 1.0:
            self.fps = self.frame_count / elapsed
            self.frame_count = 0
            self.last_fps_at = now

    def destroy_node(self):
        if self.show_viewer:
            cv2.destroyAllWindows()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="Detect green map lines from a ROS2 image topic.")
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--image-file", default="")
    parser.add_argument("--file-fps", type=float, default=5.0)
    parser.add_argument("--compressed", action="store_true")
    parser.add_argument("--roi-top-ratio", type=float, default=0.0)
    parser.add_argument("--blur-size", type=int, default=5)
    parser.add_argument("--green-h-min", type=int, default=25)
    parser.add_argument("--green-h-max", type=int, default=100)
    parser.add_argument("--green-s-min", type=int, default=18)
    parser.add_argument("--green-v-min", type=int, default=8)
    parser.add_argument("--green-v-max-dark", type=int, default=165)
    parser.add_argument("--green-v-min-light", type=int, default=70)
    parser.add_argument("--require-bgr-match", action="store_true", default=True)
    parser.add_argument("--no-require-bgr-match", dest="require_bgr_match", action="store_false")
    parser.add_argument("--bgr-g-min-dark", type=int, default=18)
    parser.add_argument("--bgr-g-min-light", type=int, default=45)
    parser.add_argument("--bgr-r-max-dark", type=int, default=150)
    parser.add_argument("--bgr-b-max-dark", type=int, default=150)
    parser.add_argument("--bgr-r-max-light", type=int, default=220)
    parser.add_argument("--bgr-b-max-light", type=int, default=220)
    parser.add_argument("--bgr-gap-min-dark", type=int, default=2)
    parser.add_argument("--bgr-gap-min-light", type=int, default=2)
    parser.add_argument("--bgr-dark-dominance", type=int, default=3)
    parser.add_argument("--morph-kernel", type=int, default=7)
    parser.add_argument("--min-blob-area", type=int, default=280)
    parser.add_argument("--min-contour-area", type=int, default=520)
    parser.add_argument("--min-line-length", type=int, default=220, help="Minimum span along the line axis in pixels.")
    parser.add_argument("--min-aspect-ratio", type=float, default=7.0, help="Minimum length/width ratio.")
    parser.add_argument("--max-line-width", type=float, default=40.0, help="Reject blobs wider than this many pixels.")
    parser.add_argument("--max-segments", type=int, default=2)
    parser.add_argument("--max-contours", type=int, default=1)
    parser.add_argument("--angle-tol", type=float, default=8.0)
    parser.add_argument("--distance-tol", type=float, default=20.0)
    parser.add_argument("--show-mask", action="store_true", default=True)
    parser.add_argument("--no-show-mask", dest="show_mask", action="store_false")
    parser.add_argument("--show-viewer", action="store_true", default=True)
    parser.add_argument("--no-viewer", dest="show_viewer", action="store_false")
    parser.add_argument("--window-name", default="Green-Line-GlobalCam")
    parser.add_argument("--max-width", type=int, default=960)
    parser.add_argument("--qos-depth", type=int, default=1)
    parser.add_argument("--publish-annotated", action="store_true", default=True)
    parser.add_argument("--no-publish-annotated", dest="publish_annotated", action="store_false")
    parser.add_argument("--annotated-topic", default="/map_line/annotated_image")
    parser.add_argument("--publish-detections", action="store_true", default=True)
    parser.add_argument("--no-publish-detections", dest="publish_detections", action="store_false")
    parser.add_argument("--detections-topic", default="/map_line/detections")
    parser.add_argument("--frame-id", default="globalcam")
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = MapLineDetectorNode(args)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        if rclpy.ok():
            node.destroy_node()
            rclpy.shutdown()
        else:
            node.destroy_node()


if __name__ == "__main__":
    main()
