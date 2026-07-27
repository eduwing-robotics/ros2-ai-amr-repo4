from __future__ import annotations

from dataclasses import dataclass
import argparse
from typing import Any

import cv2
import numpy as np


@dataclass(frozen=True)
class MapLineArgs:
    square_thickness: int = 3
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
            square_thickness=args.square_thickness,
            map_br_x=args.map_br_x,
            map_br_y=args.map_br_y,
            map_tr_x=args.map_tr_x,
            map_tr_y=args.map_tr_y,
            map_tl_x=args.map_tl_x,
            map_tl_y=args.map_tl_y,
            map_bl_x=args.map_bl_x,
            map_bl_y=args.map_bl_y,
        )
        self.image_corners = self.parse_image_corners(args.map_image_corners)

    @staticmethod
    def parse_image_corners(value: str) -> np.ndarray | None:
        """Parse TL,TR,BR,BL image points from ``x,y;x,y;x,y;x,y``."""
        text = (value or '').strip()
        if not text:
            return None
        try:
            points = [
                [float(coord) for coord in point.strip().split(',')]
                for point in text.split(';')
            ]
        except ValueError as exc:
            raise ValueError(
                "map_image_corners must be TL,TR,BR,BL as x,y;x,y;x,y;x,y"
            ) from exc
        if len(points) != 4 or any(len(point) != 2 for point in points):
            raise ValueError(
                "map_image_corners must contain exactly four x,y points: TL,TR,BR,BL"
            )
        return np.asarray(points, dtype=np.float32)

    def update(self, _frame: np.ndarray) -> dict[str, Any]:
        square_points = None if self.image_corners is None else self.image_corners.copy()
        payload = {
            'schema_version': 'globalcam_map_line.v1',
            'square_mode': 'fixed_image_corners',
            'configured': square_points is not None,
            'coordinate_origin': 'map_calibrated_from_corners',
            'coordinate_axis': {'unit': 'map', 'method': 'perspective_homography'},
            'map_corner_calibration': self.real_map_corners(),
            'square_corners_px': self.round_points(square_points) if square_points is not None else None,
        }
        if square_points is not None:
            payload['_square_points_np'] = square_points
        return payload

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

    def turtlebot_goal_position(
        self,
        map_position: dict[str, Any] | None,
        offset_x: float,
    ) -> dict[str, Any] | None:
        """Return an approach point on the opposite half of the mapped area."""
        if not isinstance(map_position, dict) or not map_position.get('inside'):
            return None

        try:
            source_x = float(map_position['x'])
            source_y = float(map_position['y'])
        except (KeyError, TypeError, ValueError):
            return None

        corners = self.real_map_corners()
        top_x = (float(corners['TL'][0]) + float(corners['TR'][0])) / 2.0
        bottom_x = (float(corners['BL'][0]) + float(corners['BR'][0])) / 2.0
        half_x = (top_x + bottom_x) / 2.0
        lower_half = source_x < half_x if bottom_x < top_x else source_x > half_x
        toward_top = 1.0 if top_x >= bottom_x else -1.0
        applied_offset = toward_top * abs(float(offset_x))
        if not lower_half:
            applied_offset *= -1.0

        min_x = min(point[0] for point in corners.values())
        max_x = max(point[0] for point in corners.values())
        goal_x = min(max(source_x + applied_offset, min_x), max_x)
        goal_y = source_y
        return {
            'x': round(goal_x, 3),
            'y': round(goal_y, 3),
            'map_xy': [round(goal_x, 3), round(goal_y, 3)],
            'unit': 'map',
            'source_map_xy': [round(source_x, 3), round(source_y, 3)],
            'offset_x': round(goal_x - source_x, 3),
            'source_region': 'lower_half' if lower_half else 'upper_half',
            'map_half_x': round(half_x, 3),
        }

    def draw(self, frame: np.ndarray, map_line: dict[str, Any]) -> None:
        square_points = map_line.get('_square_points_np')
        if square_points is None and map_line.get('square_corners_px') is not None:
            square_points = np.array(map_line['square_corners_px'], dtype=np.float32)

        if square_points is None:
            return

        cv2.polylines(
            frame,
            [np.round(square_points).astype(np.int32)],
            isClosed=True,
            color=(0, 0, 255),
            thickness=self.args.square_thickness,
        )

    def draw_detection_reference(self, frame: np.ndarray, detection: dict[str, Any]) -> None:
        point = detection.get('map_reference_px')
        if point is None:
            return
        x, y = [int(round(value)) for value in point]
        cv2.drawMarker(frame, (x, y), (255, 255, 0), cv2.MARKER_CROSS, 12, 2)

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
    parser.add_argument(
        '--map-image-corners',
        default='',
        help='Fixed map quadrilateral in image pixels: TL,TR,BR,BL as x,y;x,y;x,y;x,y',
    )
    parser.add_argument('--square-thickness', type=int, default=3)
    parser.add_argument('--map-br-x', type=float, default=-0.151)
    parser.add_argument('--map-br-y', type=float, default=-0.093)
    parser.add_argument('--map-tr-x', type=float, default=1.599)
    parser.add_argument('--map-tr-y', type=float, default=-0.223)
    parser.add_argument('--map-tl-x', type=float, default=1.721)
    parser.add_argument('--map-tl-y', type=float, default=1.527)
    parser.add_argument('--map-bl-x', type=float, default=-0.041)
    parser.add_argument('--map-bl-y', type=float, default=1.527)
