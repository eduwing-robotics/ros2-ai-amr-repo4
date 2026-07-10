from __future__ import annotations

import math
from dataclasses import asdict, dataclass

import cv2
import numpy as np


@dataclass
class LineSegment:
    x1: int
    y1: int
    x2: int
    y2: int
    length: float
    angle_deg: float
    width: float = 0.0

    @classmethod
    def from_points(cls, x1: int, y1: int, x2: int, y2: int, width: float = 0.0):
        length = float(math.hypot(x2 - x1, y2 - y1))
        angle_deg = float(math.degrees(math.atan2(y2 - y1, x2 - x1)))
        return cls(x1=x1, y1=y1, x2=x2, y2=y2, length=length, angle_deg=angle_deg, width=width)


@dataclass
class MapLineDetection:
    segments: list[LineSegment]
    center_x: float | None
    center_y: float | None
    lateral_offset: float | None
    heading_deg: float | None
    line_pixel_ratio: float
    roi: tuple[int, int, int, int]

    def to_payload(self, stamp, frame_id: str) -> dict:
        return {
            "stamp": {"sec": int(stamp.sec), "nanosec": int(stamp.nanosec)},
            "frame_id": frame_id,
            "segment_count": len(self.segments),
            "segments": [asdict(segment) for segment in self.segments],
            "center": None
            if self.center_x is None
            else {"x": self.center_x, "y": self.center_y},
            "lateral_offset": self.lateral_offset,
            "heading_deg": self.heading_deg,
            "line_pixel_ratio": self.line_pixel_ratio,
            "roi": {
                "x": self.roi[0],
                "y": self.roi[1],
                "width": self.roi[2],
                "height": self.roi[3],
            },
        }


def crop_roi(frame: np.ndarray, top_ratio: float):
    height, width = frame.shape[:2]
    top = int(round(height * max(0.0, min(top_ratio, 0.95))))
    roi = frame[top:height, 0:width]
    return roi, (0, top, width, height - top)


def extract_green_line_mask(
    frame: np.ndarray,
    blur_size: int,
    morph_kernel: int,
    green_h_min: int = 25,
    green_h_max: int = 100,
    green_s_min: int = 18,
    green_v_min: int = 8,
    green_v_max_dark: int = 165,
    green_v_min_light: int = 70,
    require_bgr_match: bool = True,
    bgr_g_min_dark: int = 18,
    bgr_g_min_light: int = 45,
    bgr_r_max_dark: int = 150,
    bgr_b_max_dark: int = 150,
    bgr_r_max_light: int = 220,
    bgr_b_max_light: int = 220,
    bgr_gap_min_dark: int = 2,
    bgr_gap_min_light: int = 2,
    bgr_dark_dominance: int = 3,
):
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    if blur_size > 0:
        blur_size = max(3, blur_size | 1)
        hsv = cv2.GaussianBlur(hsv, (blur_size, blur_size), 0)

    hue = hsv[:, :, 0]
    sat = hsv[:, :, 1]
    val = hsv[:, :, 2]
    in_hue = (hue >= green_h_min) & (hue <= green_h_max)
    dark_hsv = in_hue & (sat >= max(green_s_min, 15)) & (val >= green_v_min) & (val <= green_v_max_dark)
    light_hsv = in_hue & (sat >= max(green_s_min - 5, 12)) & (val >= green_v_min_light)
    hsv_mask = (dark_hsv | light_hsv).astype(np.uint8) * 255

    blue, green, red = cv2.split(frame)
    green_i = green.astype(np.int16)
    red_i = red.astype(np.int16)
    blue_i = blue.astype(np.int16)
    gr_gap = green_i - red_i
    gb_gap = green_i - blue_i

    dark_bgr = (
        (green >= bgr_g_min_dark)
        & (red <= bgr_r_max_dark)
        & (blue <= bgr_b_max_dark)
        & (green_i + bgr_dark_dominance >= red_i)
        & (green_i + bgr_dark_dominance >= blue_i)
        & (gr_gap >= bgr_gap_min_dark)
    )
    light_dominance = max(bgr_dark_dominance, 5)
    light_bgr = (
        (green >= bgr_g_min_light)
        & (red <= bgr_r_max_light)
        & (blue <= bgr_b_max_light)
        & (green_i + light_dominance >= red_i)
        & (green_i + light_dominance >= blue_i)
        & ((gr_gap >= bgr_gap_min_light) | (gb_gap >= bgr_gap_min_light))
    )
    bgr_mask = (dark_bgr | light_bgr).astype(np.uint8) * 255

    if require_bgr_match:
        mask = cv2.bitwise_and(hsv_mask, bgr_mask)
    else:
        mask = hsv_mask

    kernel_size = max(3, morph_kernel | 1)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel, iterations=1)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel, iterations=1)
    return mask


extract_red_orange_line_mask = extract_green_line_mask
extract_orange_line_mask = extract_green_line_mask
extract_red_line_mask = extract_green_line_mask


def remove_small_blobs(mask: np.ndarray, min_blob_area: int):
    if min_blob_area <= 0:
        return mask

    num_labels, labels, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    filtered = np.zeros_like(mask)
    for label in range(1, num_labels):
        if stats[label, cv2.CC_STAT_AREA] >= min_blob_area:
            filtered[labels == label] = 255
    return filtered


def contour_to_segment(
    contour,
    min_line_length: int,
    min_aspect_ratio: float,
    max_line_width: float,
):
    if len(contour) < 5:
        return None

    rect = cv2.minAreaRect(contour)
    (_, _), (rect_w, rect_h), _ = rect
    length = float(max(rect_w, rect_h))
    width = float(min(rect_w, rect_h))
    if length < min_line_length:
        return None
    if width > max_line_width:
        return None
    if length / max(width, 1.0) < min_aspect_ratio:
        return None

    box = cv2.boxPoints(rect)
    best_length = 0.0
    best_points = None
    for index in range(4):
        p1 = box[index]
        p2 = box[(index + 1) % 4]
        edge_length = float(np.linalg.norm(p2 - p1))
        if edge_length > best_length:
            best_length = edge_length
            best_points = p1, p2

    if best_points is None or best_length < min_line_length:
        return None

    p1, p2 = best_points
    return LineSegment.from_points(
        int(round(float(p1[0]))),
        int(round(float(p1[1]))),
        int(round(float(p2[0]))),
        int(round(float(p2[1]))),
        width=width,
    )


def normalize_angle(angle_deg: float) -> float:
    angle = angle_deg % 180.0
    if angle < 0:
        angle += 180.0
    return angle


def segment_midpoint(segment: LineSegment) -> tuple[float, float]:
    return (segment.x1 + segment.x2) / 2.0, (segment.y1 + segment.y2) / 2.0


def segments_are_similar(left: LineSegment, right: LineSegment, angle_tol: float, distance_tol: float) -> bool:
    angle_delta = abs(normalize_angle(left.angle_deg) - normalize_angle(right.angle_deg))
    angle_delta = min(angle_delta, 180.0 - angle_delta)
    if angle_delta > angle_tol:
        return False

    lx, ly = segment_midpoint(left)
    rx, ry = segment_midpoint(right)
    return math.hypot(lx - rx, ly - ry) <= distance_tol


def dedupe_segments(segments: list[LineSegment], angle_tol: float, distance_tol: float, max_segments: int):
    kept: list[LineSegment] = []
    for candidate in sorted(segments, key=lambda item: item.length, reverse=True):
        if any(segments_are_similar(candidate, existing, angle_tol, distance_tol) for existing in kept):
            continue
        kept.append(candidate)
        if len(kept) >= max_segments:
            break
    return kept


def detect_line_segments(
    mask: np.ndarray,
    min_line_length: int,
    min_contour_area: int,
    min_aspect_ratio: float,
    max_line_width: float,
    max_segments: int,
    max_contours: int,
    angle_tol: float,
    distance_tol: float,
) -> list[LineSegment]:
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    valid = [contour for contour in contours if cv2.contourArea(contour) >= min_contour_area]
    valid.sort(key=cv2.contourArea, reverse=True)

    segments: list[LineSegment] = []
    for contour in valid[: max(1, max_contours)]:
        segment = contour_to_segment(
            contour,
            min_line_length,
            min_aspect_ratio,
            max_line_width,
        )
        if segment is not None:
            segments.append(segment)

    return dedupe_segments(segments, angle_tol, distance_tol, max_segments)


def estimate_path_from_mask(mask: np.ndarray, roi_width: int, min_contour_area: int):
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    valid = [contour for contour in contours if cv2.contourArea(contour) >= min_contour_area]
    if not valid:
        return None, None, None, None

    largest = max(valid, key=cv2.contourArea)
    moments = cv2.moments(largest)
    if moments["m00"] <= 0:
        return None, None, None, None

    center_x = float(moments["m10"] / moments["m00"])
    center_y = float(moments["m01"] / moments["m00"])
    lateral_offset = center_x - (roi_width / 2.0)

    heading_deg = None
    if len(largest) >= 5:
        _, _, angle = cv2.fitEllipse(largest)
        heading_deg = float(angle)

    return center_x, center_y, lateral_offset, heading_deg


def effective_min_line_length(roi_width: int, min_line_length: int) -> int:
    return max(120, min(min_line_length, int(roi_width * 0.62)))


def detect_map_lines(
    frame: np.ndarray,
    roi_top_ratio: float = 0.0,
    blur_size: int = 5,
    morph_kernel: int = 7,
    green_h_min: int = 25,
    green_h_max: int = 100,
    green_s_min: int = 18,
    green_v_min: int = 8,
    green_v_max_dark: int = 165,
    green_v_min_light: int = 70,
    require_bgr_match: bool = True,
    bgr_g_min_dark: int = 18,
    bgr_g_min_light: int = 45,
    bgr_r_max_dark: int = 150,
    bgr_b_max_dark: int = 150,
    bgr_r_max_light: int = 220,
    bgr_b_max_light: int = 220,
    bgr_gap_min_dark: int = 2,
    bgr_gap_min_light: int = 2,
    bgr_dark_dominance: int = 3,
    min_blob_area: int = 280,
    min_contour_area: int = 520,
    min_line_length: int = 220,
    min_aspect_ratio: float = 7.0,
    max_line_width: float = 40.0,
    max_segments: int = 2,
    max_contours: int = 1,
    angle_tol: float = 8.0,
    distance_tol: float = 20.0,
) -> tuple[MapLineDetection, np.ndarray]:
    roi, roi_box = crop_roi(frame, roi_top_ratio)
    mask = extract_green_line_mask(
        roi,
        blur_size,
        morph_kernel,
        green_h_min=green_h_min,
        green_h_max=green_h_max,
        green_s_min=green_s_min,
        green_v_min=green_v_min,
        green_v_max_dark=green_v_max_dark,
        green_v_min_light=green_v_min_light,
        require_bgr_match=require_bgr_match,
        bgr_g_min_dark=bgr_g_min_dark,
        bgr_g_min_light=bgr_g_min_light,
        bgr_r_max_dark=bgr_r_max_dark,
        bgr_b_max_dark=bgr_b_max_dark,
        bgr_r_max_light=bgr_r_max_light,
        bgr_b_max_light=bgr_b_max_light,
        bgr_gap_min_dark=bgr_gap_min_dark,
        bgr_gap_min_light=bgr_gap_min_light,
        bgr_dark_dominance=bgr_dark_dominance,
    )
    mask = remove_small_blobs(mask, min_blob_area)
    line_length = effective_min_line_length(roi.shape[1], min_line_length)
    segments = detect_line_segments(
        mask,
        min_line_length=line_length,
        min_contour_area=min_contour_area,
        min_aspect_ratio=min_aspect_ratio,
        max_line_width=max_line_width,
        max_segments=max_segments,
        max_contours=max_contours,
        angle_tol=angle_tol,
        distance_tol=distance_tol,
    )
    center_x, center_y, lateral_offset, heading_deg = estimate_path_from_mask(
        mask,
        roi.shape[1],
        min_contour_area,
    )
    line_pixel_ratio = float(np.count_nonzero(mask)) / float(mask.size)

    detection = MapLineDetection(
        segments=segments,
        center_x=center_x,
        center_y=center_y,
        lateral_offset=lateral_offset,
        heading_deg=heading_deg,
        line_pixel_ratio=line_pixel_ratio,
        roi=roi_box,
    )
    return detection, mask


def annotate_map_lines(
    frame: np.ndarray,
    detection: MapLineDetection,
    mask: np.ndarray,
    fps: float,
    show_mask: bool = False,
):
    annotated = frame.copy()
    roi_x, roi_y, roi_w, roi_h = detection.roi

    if show_mask:
        roi_slice = annotated[roi_y : roi_y + roi_h, roi_x : roi_x + roi_w]
        highlighted = roi_slice.copy()
        highlighted[mask > 0] = (0, 255, 80)
        # Blend only tints detected pixels; untouched areas stay at original brightness.
        annotated[roi_y : roi_y + roi_h, roi_x : roi_x + roi_w] = cv2.addWeighted(
            roi_slice,
            0.72,
            highlighted,
            0.28,
            0,
        )

    cv2.rectangle(annotated, (roi_x, roi_y), (roi_x + roi_w - 1, roi_y + roi_h - 1), (0, 255, 80), 1)

    for index, segment in enumerate(detection.segments):
        color = (0, 255, 0) if index == 0 else (80, 255, 80)
        cv2.line(
            annotated,
            (roi_x + segment.x1, roi_y + segment.y1),
            (roi_x + segment.x2, roi_y + segment.y2),
            color,
            2,
            cv2.LINE_AA,
        )

    if detection.center_x is not None and detection.center_y is not None:
        cx = int(round(roi_x + detection.center_x))
        cy = int(round(roi_y + detection.center_y))
        cv2.circle(annotated, (cx, cy), 6, (0, 255, 0), -1, cv2.LINE_AA)
        mid_x = int(round(roi_x + roi_w / 2.0))
        cv2.line(annotated, (mid_x, roi_y + roi_h - 1), (cx, cy), (0, 255, 255), 2, cv2.LINE_AA)

    status = (
        f"Green lines={len(detection.segments)}  "
        f"ratio={detection.line_pixel_ratio * 100:.1f}%  "
        f"offset={detection.lateral_offset if detection.lateral_offset is not None else 'n/a'}  "
        f"fps={fps:.1f}"
    )
    if detection.segments:
        primary = detection.segments[0]
        status += f"  L={primary.length:.0f}px W={primary.width:.0f}px"
    cv2.putText(annotated, status, (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.58, (255, 255, 255), 2, cv2.LINE_AA)
    cv2.putText(annotated, status, (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.58, (0, 255, 80), 1, cv2.LINE_AA)
    return annotated
