from __future__ import annotations

import numpy as np
from sensor_msgs.msg import CompressedImage, Image


def cv_to_imgmsg(frame, frame_id: str, stamp) -> Image:
    msg = Image()
    msg.header.stamp = stamp
    msg.header.frame_id = frame_id
    msg.height = int(frame.shape[0])
    msg.width = int(frame.shape[1])
    msg.encoding = "bgr8"
    msg.is_bigendian = False
    msg.step = int(frame.shape[1] * frame.shape[2])
    msg.data = frame.tobytes()
    return msg


def imgmsg_to_cv(msg: Image):
    if msg.encoding not in ("bgr8", "rgb8", "mono8"):
        raise ValueError(f"Unsupported image encoding: {msg.encoding}")

    if msg.encoding == "mono8":
        frame = np.frombuffer(msg.data, dtype=np.uint8).reshape(msg.height, msg.width)
        return frame

    channels = 3
    frame = np.frombuffer(msg.data, dtype=np.uint8).reshape(msg.height, msg.width, channels)
    if msg.encoding == "rgb8":
        frame = frame[:, :, ::-1]
    return frame.copy()


def compressed_imgmsg_to_cv(msg: CompressedImage):
    import cv2

    data = np.frombuffer(msg.data, dtype=np.uint8)
    frame = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if frame is None:
        raise ValueError("Compressed image decode failed")
    return frame


def depth_imgmsg_to_meters(msg: Image):
    if msg.encoding in ("16UC1", "mono16"):
        depth = np.frombuffer(msg.data, dtype=np.uint16).reshape(msg.height, msg.width)
        return depth.astype(np.float32) * 0.001
    if msg.encoding == "32FC1":
        return np.frombuffer(msg.data, dtype=np.float32).reshape(msg.height, msg.width).copy()
    if msg.encoding == "64FC1":
        return np.frombuffer(msg.data, dtype=np.float64).reshape(msg.height, msg.width).astype(np.float32)
    raise ValueError(f"Unsupported depth image encoding: {msg.encoding}")
