from __future__ import annotations

from dataclasses import dataclass
import argparse
import time
from typing import Any

import cv2
import numpy as np


ARUCO_DICTIONARIES = {
    'DICT_4X4_50': cv2.aruco.DICT_4X4_50,
    'DICT_4X4_100': cv2.aruco.DICT_4X4_100,
    'DICT_4X4_250': cv2.aruco.DICT_4X4_250,
    'DICT_4X4_1000': cv2.aruco.DICT_4X4_1000,
    'DICT_5X5_50': cv2.aruco.DICT_5X5_50,
    'DICT_5X5_100': cv2.aruco.DICT_5X5_100,
    'DICT_5X5_250': cv2.aruco.DICT_5X5_250,
    'DICT_5X5_1000': cv2.aruco.DICT_5X5_1000,
    'DICT_6X6_50': cv2.aruco.DICT_6X6_50,
    'DICT_6X6_100': cv2.aruco.DICT_6X6_100,
    'DICT_6X6_250': cv2.aruco.DICT_6X6_250,
    'DICT_6X6_1000': cv2.aruco.DICT_6X6_1000,
    'DICT_7X7_50': cv2.aruco.DICT_7X7_50,
    'DICT_7X7_100': cv2.aruco.DICT_7X7_100,
    'DICT_7X7_250': cv2.aruco.DICT_7X7_250,
    'DICT_7X7_1000': cv2.aruco.DICT_7X7_1000,
}


@dataclass(frozen=True)
class MapLineArgs:
    aruco_dictionary: str = 'DICT_4X4_50'
    target_marker_id: int = 0
    left_offset: float = 185.0
    right_offset: float = 185.0
    down_offset: float = 390.0
    bottom_line_length: float = 570.0
    square_thickness: int = 3
    show_square_ticks: bool = False
    tick_interval: int = 10
    major_tick_interval: int = 50
    map_line_recheck_interval: float = 60.0
    map_br_x: float = -0.151
    map_br_y: float = -0.093
    map_tr_x: float = 1.599
    map_tr_y: float = -0.223
    map_tl_x: float = 1.721
    map_tl_y: float = 1.527
    map_bl_x: float = -0.041
    map_bl_y: float = 1.527


class MapLineReference:
    def __init__(self, args: Any):
        self.args = MapLineArgs(
            aruco_dictionary=args.aruco_dictionary,
            target_marker_id=args.target_marker_id,
            left_offset=args.left_offset,
            right_offset=args.right_offset,
            down_offset=args.down_offset,
            bottom_line_length=args.bottom_line_length,
            square_thickness=args.square_thickness,
            show_square_ticks=args.show_square_ticks,
            tick_interval=args.tick_interval,
            major_tick_interval=args.major_tick_interval,
            map_line_recheck_interval=args.map_line_recheck_interval,
            map_br_x=args.map_br_x,
            map_br_y=args.map_br_y,
            map_tr_x=args.map_tr_x,
            map_tr_y=args.map_tr_y,
            map_tl_x=args.map_tl_x,
            map_tl_y=args.map_tl_y,
            map_bl_x=args.map_bl_x,
            map_bl_y=args.map_bl_y,
        )
        dictionary_id = ARUCO_DICTIONARIES.get(self.args.aruco_dictionary)
        if dictionary_id is None:
            raise ValueError(f'Unsupported ArUco dictionary: {self.args.aruco_dictionary}')

        self.aruco_dict = cv2.aruco.getPredefinedDictionary(dictionary_id)
        self.aruco_params = cv2.aruco.DetectorParameters()
        self.detector = None
        if hasattr(cv2.aruco, 'ArucoDetector'):
            self.detector = cv2.aruco.ArucoDetector(self.aruco_dict, self.aruco_params)
        self.cached_map_line: dict[str, Any] | None = None
        self.last_attempt_at: float | None = None
        self.last_detected_at: float | None = None

    def update(self, frame: np.ndarray) -> dict[str, Any]:
        now = time.monotonic()
        should_recheck = (
            self.cached_map_line is None
            or self.last_attempt_at is None
            or now - self.last_attempt_at >= max(float(self.args.map_line_recheck_interval), 0.0)
        )
        if not should_recheck and self.cached_map_line is not None:
            return self.with_cache_status(self.cached_map_line, now, 'cached')

        self.last_attempt_at = now
        detected = self.detect(frame)
        if detected.get('target_found'):
            self.cached_map_line = detected
            self.last_detected_at = now
            return self.with_cache_status(detected, now, 'detected')

        if self.cached_map_line is not None:
            return self.with_cache_status(self.cached_map_line, now, 'target_missing')
        return self.with_cache_status(detected, now, 'not_initialized')

    def with_cache_status(self, map_line: dict[str, Any], now: float, validation_status: str) -> dict[str, Any]:
        result = dict(map_line)
        result['cached'] = self.cached_map_line is not None and map_line is self.cached_map_line
        result['validation_status'] = validation_status
        result['recheck_interval_sec'] = self.args.map_line_recheck_interval
        result['last_detected_age_sec'] = (
            round(now - self.last_detected_at, 3) if self.last_detected_at is not None else None
        )
        result['last_validation_age_sec'] = (
            round(now - self.last_attempt_at, 3) if self.last_attempt_at is not None else None
        )
        return result

    def detect(self, frame: np.ndarray) -> dict[str, Any]:
        if self.detector is not None:
            corners, ids, _ = self.detector.detectMarkers(frame)
        else:
            corners, ids, _ = cv2.aruco.detectMarkers(frame, self.aruco_dict, parameters=self.aruco_params)
        markers = []
        square_points = None
        reference = None

        if ids is not None:
            for index, marker_id in enumerate(ids.flatten()):
                corner = corners[index][0]
                marker_reference = self.marker_bottom_reference(corner)
                marker = {
                    'id': int(marker_id),
                    'center_px': self.round_point(corner.mean(axis=0)),
                    'reference_px': self.round_point(marker_reference),
                    'marker_corners_px': self.round_points(corner),
                }
                if int(marker_id) == self.args.target_marker_id:
                    reference = marker_reference
                    square_points = self.square_points_from_reference(reference)
                    marker['square_corners_px'] = self.round_points(square_points)
                else:
                    marker['square_corners_px'] = None
                markers.append(marker)

        payload = {
            'schema_version': 'globalcam_map_line.v1',
            'aruco_dictionary': self.args.aruco_dictionary,
            'square_mode': 'marker_bottom_reference',
            'target_marker_id': self.args.target_marker_id,
            'square_offsets': {
                'left': self.args.left_offset,
                'right': self.args.right_offset,
                'down': self.args.down_offset,
                'bottom_line_length': self.args.bottom_line_length,
            },
            'marker_count': len(markers),
            'markers': markers,
            'target_found': square_points is not None,
            'coordinate_origin': 'map_calibrated_from_corners',
            'coordinate_axis': {'unit': 'map', 'method': 'perspective_homography'},
            'map_corner_calibration': self.real_map_corners(),
            'reference_px': self.round_point(reference) if reference is not None else None,
            'square_corners_px': self.round_points(square_points) if square_points is not None else None,
        }
        if square_points is not None:
            payload['_square_points_np'] = square_points
        return payload

    @staticmethod
    def marker_bottom_reference(corner: np.ndarray) -> np.ndarray:
        center_x = float(corner[:, 0].mean())
        bottom_y = float(corner[:, 1].max())
        return np.array([center_x, bottom_y], dtype=np.float32)

    def square_points_from_reference(self, reference: np.ndarray) -> np.ndarray:
        ref_x, ref_y = reference
        bottom_half = self.args.bottom_line_length / 2.0
        return np.array(
            [
                [ref_x - self.args.left_offset, ref_y],
                [ref_x + self.args.right_offset, ref_y],
                [ref_x + bottom_half, ref_y + self.args.down_offset],
                [ref_x - bottom_half, ref_y + self.args.down_offset],
            ],
            dtype=np.float32,
        )

    def real_map_corners(self) -> dict[str, list[float]]:
        return {
            'BR': [self.args.map_br_x, self.args.map_br_y],
            'TR': [self.args.map_tr_x, self.args.map_tr_y],
            'TL': [self.args.map_tl_x, self.args.map_tl_y],
            'BL': [self.args.map_bl_x, self.args.map_bl_y],
        }

    def image_to_map_xy(self, square_points: np.ndarray, point_px: tuple[float, float] | list[float]) -> list[float]:
        top_left, top_right, bottom_right, bottom_left = square_points.astype(np.float32)
        src = np.array([top_left, top_right, bottom_right, bottom_left], dtype=np.float32)
        corners = self.real_map_corners()
        dst = np.array([corners['TL'], corners['TR'], corners['BR'], corners['BL']], dtype=np.float32)
        homography = cv2.getPerspectiveTransform(src, dst)
        point = np.array([[[float(point_px[0]), float(point_px[1])]]], dtype=np.float32)
        projected = cv2.perspectiveTransform(point, homography)[0][0]
        return [round(float(projected[0]), 3), round(float(projected[1]), 3)]

    def project_point(self, point_px: tuple[float, float] | list[float], map_line: dict[str, Any]) -> dict[str, Any] | None:
        square_points = map_line.get('_square_points_np')
        if square_points is None:
            return None

        map_xy = self.image_to_map_xy(square_points, point_px)
        inside = cv2.pointPolygonTest(square_points.astype(np.float32), (float(point_px[0]), float(point_px[1])), False) >= 0
        return {
            'x': map_xy[0],
            'y': map_xy[1],
            'map_xy': map_xy,
            'unit': 'map',
            'method': 'perspective_homography',
            'inside': bool(inside),
        }

    def enrich_detection(self, detection: dict[str, Any], map_line: dict[str, Any]) -> None:
        reference_px = detection['center_px']
        detection['map_reference_px'] = self.round_point(reference_px)
        detection['map_position'] = self.project_point(reference_px, map_line)

    def draw(self, frame: np.ndarray, map_line: dict[str, Any], draw_markers: bool = True) -> None:
        square_points = map_line.get('_square_points_np')
        if square_points is None and map_line.get('square_corners_px') is not None:
            square_points = np.array(map_line['square_corners_px'], dtype=np.float32)
        if draw_markers:
            for marker in map_line.get('markers', []):
                marker_points = np.array(marker['marker_corners_px'], dtype=np.int32)
                cv2.polylines(frame, [marker_points], isClosed=True, color=(0, 255, 0), thickness=2)
                center = tuple(int(round(value)) for value in marker['center_px'])
                cv2.circle(frame, center, 4, (0, 255, 255), -1)

        if square_points is None:
            return

        cv2.polylines(
            frame,
            [np.round(square_points).astype(np.int32)],
            isClosed=True,
            color=(0, 0, 255),
            thickness=self.args.square_thickness,
        )
        reference = map_line.get('reference_px')
        if reference is not None:
            cv2.circle(frame, tuple(int(round(value)) for value in reference), 5, (0, 255, 255), -1)
        if self.args.show_square_ticks:
            self.draw_square_ticks(frame, square_points)

    def draw_detection_reference(self, frame: np.ndarray, detection: dict[str, Any]) -> None:
        point = detection.get('map_reference_px')
        if point is None:
            return
        x, y = [int(round(value)) for value in point]
        cv2.drawMarker(frame, (x, y), (255, 255, 0), cv2.MARKER_CROSS, 12, 2)

    def draw_square_ticks(self, frame: np.ndarray, square_points: np.ndarray) -> None:
        top_left, top_right, bottom_right, bottom_left = square_points
        ref_x = float((top_left[0] + top_right[0]) / 2.0)
        ref_y = float(top_left[1])
        bottom_y = float(bottom_left[1])
        bottom_half = self.args.bottom_line_length / 2.0
        tick_interval = max(1, int(self.args.tick_interval))
        major_interval = max(tick_interval, int(self.args.major_tick_interval))

        for offset_x in range(-int(self.args.left_offset), int(self.args.right_offset) + 1, tick_interval):
            x = int(round(ref_x + offset_x))
            is_major = offset_x % major_interval == 0
            tick_len = 14 if is_major else 7
            color = (255, 255, 0) if is_major else (160, 160, 160)
            cv2.line(frame, (x, int(round(ref_y))), (x, int(round(ref_y + tick_len))), color, 1)
            if is_major:
                cv2.putText(frame, str(offset_x), (x - 14, int(round(ref_y - 8))), cv2.FONT_HERSHEY_SIMPLEX, 0.35, color, 1, cv2.LINE_AA)

        for offset_x in range(-int(bottom_half), int(bottom_half) + 1, tick_interval):
            x = int(round(ref_x + offset_x))
            is_major = offset_x % major_interval == 0
            tick_len = 14 if is_major else 7
            color = (255, 255, 0) if is_major else (160, 160, 160)
            cv2.line(frame, (x, int(round(bottom_y))), (x, int(round(bottom_y - tick_len))), color, 1)
            if is_major:
                cv2.putText(frame, str(offset_x), (x - 14, int(round(bottom_y + 18))), cv2.FONT_HERSHEY_SIMPLEX, 0.35, color, 1, cv2.LINE_AA)

        for offset_y in range(0, int(self.args.down_offset) + 1, tick_interval):
            ratio = offset_y / max(float(self.args.down_offset), 1.0)
            left_x = float(top_left[0] + (bottom_left[0] - top_left[0]) * ratio)
            right_x = float(top_right[0] + (bottom_right[0] - top_right[0]) * ratio)
            y = int(round(ref_y + offset_y))
            is_major = offset_y % major_interval == 0
            tick_len = 14 if is_major else 7
            color = (255, 255, 0) if is_major else (160, 160, 160)
            cv2.line(frame, (int(round(left_x)), y), (int(round(left_x + tick_len)), y), color, 1)
            cv2.line(frame, (int(round(right_x)), y), (int(round(right_x - tick_len)), y), color, 1)
            if is_major:
                cv2.putText(frame, str(offset_y), (int(round(left_x - 36)), y + 4), cv2.FONT_HERSHEY_SIMPLEX, 0.35, color, 1, cv2.LINE_AA)

    @staticmethod
    def serializable(map_line: dict[str, Any] | None) -> dict[str, Any] | None:
        if map_line is None:
            return None
        return {key: value for key, value in map_line.items() if not key.startswith('_')}

    @staticmethod
    def round_point(point: np.ndarray | list[float] | tuple[float, float]) -> list[float]:
        return [round(float(point[0]), 1), round(float(point[1]), 1)]

    @classmethod
    def round_points(cls, points: np.ndarray) -> list[list[float]]:
        return [cls.round_point(point) for point in points]


def add_map_line_arguments(parser) -> None:
    parser.add_argument('--enable-map-line', action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument('--aruco-dictionary', default='DICT_4X4_50', choices=sorted(ARUCO_DICTIONARIES))
    parser.add_argument('--target-marker-id', type=int, default=0)
    parser.add_argument('--left-offset', type=float, default=185.0)
    parser.add_argument('--right-offset', type=float, default=185.0)
    parser.add_argument('--down-offset', type=float, default=390.0)
    parser.add_argument('--bottom-line-length', type=float, default=570.0)
    parser.add_argument('--square-thickness', type=int, default=3)
    parser.add_argument('--show-square-ticks', action=argparse.BooleanOptionalAction, default=False)
    parser.add_argument('--tick-interval', type=int, default=10)
    parser.add_argument('--major-tick-interval', type=int, default=50)
    parser.add_argument('--map-line-recheck-interval', type=float, default=60.0)
    parser.add_argument('--map-br-x', type=float, default=-0.151)
    parser.add_argument('--map-br-y', type=float, default=-0.093)
    parser.add_argument('--map-tr-x', type=float, default=1.599)
    parser.add_argument('--map-tr-y', type=float, default=-0.223)
    parser.add_argument('--map-tl-x', type=float, default=1.721)
    parser.add_argument('--map-tl-y', type=float, default=1.527)
    parser.add_argument('--map-bl-x', type=float, default=-0.041)
    parser.add_argument('--map-bl-y', type=float, default=1.527)
