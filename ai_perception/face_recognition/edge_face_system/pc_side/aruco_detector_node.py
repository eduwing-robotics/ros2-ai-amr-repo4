from __future__ import annotations

import argparse
import json
import time

import cv2
import numpy as np
import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import CompressedImage, Image
from std_msgs.msg import String

from pc_side.ros_image_utils import compressed_imgmsg_to_cv, cv_to_imgmsg, imgmsg_to_cv


ARUCO_DICTIONARIES = {
    "4x4_50": cv2.aruco.DICT_4X4_50,
    "4x4_100": cv2.aruco.DICT_4X4_100,
    "4x4_250": cv2.aruco.DICT_4X4_250,
    "4x4_1000": cv2.aruco.DICT_4X4_1000,
    "5x5_50": cv2.aruco.DICT_5X5_50,
    "5x5_100": cv2.aruco.DICT_5X5_100,
    "5x5_250": cv2.aruco.DICT_5X5_250,
    "5x5_1000": cv2.aruco.DICT_5X5_1000,
    "6x6_50": cv2.aruco.DICT_6X6_50,
    "6x6_100": cv2.aruco.DICT_6X6_100,
    "6x6_250": cv2.aruco.DICT_6X6_250,
    "6x6_1000": cv2.aruco.DICT_6X6_1000,
    "7x7_50": cv2.aruco.DICT_7X7_50,
    "7x7_100": cv2.aruco.DICT_7X7_100,
    "7x7_250": cv2.aruco.DICT_7X7_250,
    "7x7_1000": cv2.aruco.DICT_7X7_1000,
    "original": cv2.aruco.DICT_ARUCO_ORIGINAL,
}


def image_qos(depth: int = 1):
    qos = QoSProfile(depth=depth)
    qos.history = HistoryPolicy.KEEP_LAST
    qos.reliability = ReliabilityPolicy.BEST_EFFORT
    return qos


def build_aruco_detector(dictionary_name: str, refine_corners: bool):
    if dictionary_name not in ARUCO_DICTIONARIES:
        supported = ", ".join(sorted(ARUCO_DICTIONARIES))
        raise ValueError(f"Unknown dictionary '{dictionary_name}'. Supported: {supported}")

    dictionary = cv2.aruco.getPredefinedDictionary(ARUCO_DICTIONARIES[dictionary_name])
    parameters = cv2.aruco.DetectorParameters()
    parameters.cornerRefinementMethod = (
        cv2.aruco.CORNER_REFINE_SUBPIX if refine_corners else cv2.aruco.CORNER_REFINE_NONE
    )
    return cv2.aruco.ArucoDetector(dictionary, parameters)


def marker_center(corners: np.ndarray) -> tuple[int, int]:
    points = corners.reshape(-1, 2)
    cx = int(round(float(points[:, 0].mean())))
    cy = int(round(float(points[:, 1].mean())))
    return cx, cy


def annotate_frame(frame, corners, ids, fps: float, dictionary_name: str):
    annotated = frame.copy()
    marker_count = 0 if ids is None else len(ids)

    if ids is not None and len(ids) > 0:
        cv2.aruco.drawDetectedMarkers(annotated, corners, ids)
        for corner_set, marker_id in zip(corners, ids.flatten()):
            cx, cy = marker_center(corner_set)
            cv2.putText(
                annotated,
                f"ID {int(marker_id)}",
                (cx - 24, cy - 12),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 255, 255),
                2,
                cv2.LINE_AA,
            )

    status = f"ArUco ({dictionary_name})  markers={marker_count}  fps={fps:.1f}"
    cv2.putText(
        annotated,
        status,
        (12, 28),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        (255, 255, 255),
        2,
        cv2.LINE_AA,
    )
    cv2.putText(
        annotated,
        status,
        (12, 28),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        (0, 180, 255),
        1,
        cv2.LINE_AA,
    )
    return annotated, marker_count


def detections_to_payload(ids, corners, stamp, frame_id: str, dictionary_name: str):
    markers = []
    if ids is not None:
        for marker_id, corner_set in zip(ids.flatten(), corners):
            markers.append(
                {
                    "id": int(marker_id),
                    "corners": corner_set.reshape(-1, 2).astype(float).tolist(),
                }
            )
    return {
        "stamp": {
            "sec": int(stamp.sec),
            "nanosec": int(stamp.nanosec),
        },
        "frame_id": frame_id,
        "dictionary": dictionary_name,
        "count": len(markers),
        "markers": markers,
    }


class ArucoDetectorNode(Node):
    def __init__(self, args):
        super().__init__("aruco_detector")
        self.args = args
        self.image_topic = args.image_topic
        self.compressed = args.compressed or self.image_topic.endswith("/compressed")
        self.detector = build_aruco_detector(args.dictionary, args.refine_corners)
        self.show_viewer = args.show_viewer
        self.max_width = args.max_width
        self.frame_count = 0
        self.last_fps_at = time.monotonic()
        self.fps = 0.0

        msg_type = CompressedImage if self.compressed else Image
        self.subscription = self.create_subscription(
            msg_type,
            self.image_topic,
            self.on_image,
            image_qos(args.qos_depth),
        )

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

        mode = "compressed/jpeg" if self.compressed else "raw"
        self.get_logger().info(
            f"ArUco detector listening on {self.image_topic} ({mode}), "
            f"dictionary={args.dictionary}, viewer={'on' if self.show_viewer else 'off'}"
        )

    def on_image(self, msg):
        try:
            frame = compressed_imgmsg_to_cv(msg) if self.compressed else imgmsg_to_cv(msg)
        except Exception as exc:
            self.get_logger().warning(str(exc))
            return

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        corners, ids, _rejected = self.detector.detectMarkers(gray)

        self.frame_count += 1
        now = time.monotonic()
        elapsed = now - self.last_fps_at
        if elapsed >= 1.0:
            self.fps = self.frame_count / elapsed
            self.frame_count = 0
            self.last_fps_at = now

        annotated, marker_count = annotate_frame(frame, corners, ids, self.fps, self.args.dictionary)

        if marker_count > 0:
            ids_text = ", ".join(str(int(marker_id)) for marker_id in ids.flatten())
            self.get_logger().info(f"Detected marker IDs: {ids_text}", throttle_duration_sec=1.0)

        if self.detection_pub is not None:
            payload = detections_to_payload(ids, corners, msg.header.stamp, msg.header.frame_id, self.args.dictionary)
            detection_msg = String()
            detection_msg.data = json.dumps(payload, ensure_ascii=False)
            self.detection_pub.publish(detection_msg)

        if self.annotated_pub is not None:
            out_msg = cv_to_imgmsg(annotated, msg.header.frame_id or self.args.frame_id, msg.header.stamp)
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

    def destroy_node(self):
        if self.show_viewer:
            cv2.destroyAllWindows()
        super().destroy_node()


def parse_args():
    parser = argparse.ArgumentParser(description="Detect ArUco markers from a ROS2 image topic.")
    parser.add_argument("--image-topic", default="/globalcam/image_raw/compressed")
    parser.add_argument("--compressed", action="store_true")
    parser.add_argument("--dictionary", default="4x4_50", choices=sorted(ARUCO_DICTIONARIES))
    parser.add_argument("--refine-corners", action="store_true", default=True)
    parser.add_argument("--no-refine-corners", dest="refine_corners", action="store_false")
    parser.add_argument("--show-viewer", action="store_true", default=True)
    parser.add_argument("--no-viewer", dest="show_viewer", action="store_false")
    parser.add_argument("--window-name", default="ArUco-GlobalCam")
    parser.add_argument("--max-width", type=int, default=960)
    parser.add_argument("--qos-depth", type=int, default=1)
    parser.add_argument("--publish-annotated", action="store_true", default=True)
    parser.add_argument("--no-publish-annotated", dest="publish_annotated", action="store_false")
    parser.add_argument("--annotated-topic", default="/aruco/annotated_image")
    parser.add_argument("--publish-detections", action="store_true", default=True)
    parser.add_argument("--no-publish-detections", dest="publish_detections", action="store_false")
    parser.add_argument("--detections-topic", default="/aruco/detections")
    parser.add_argument("--frame-id", default="globalcam")
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = ArucoDetectorNode(args)
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
