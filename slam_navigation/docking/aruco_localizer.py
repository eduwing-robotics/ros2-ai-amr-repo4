#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
aruco_localizer.py — 순찰 중 ArUco 마커로 AMCL 위치 보정 (PC 노드)
==============================================================================
왜? 2D 라이다 AMCL이 제자리회전·좁은맵에서 드리프트 → 2바퀴째부터 흔들림.
    벽에 붙인 '알려진 좌표의 ArUco 마커'를 볼 때마다 로봇 맵위치를 역산해서
    /initialpose 로 AMCL을 보정한다(fiducial correction).

흐름:
  /robot1/camera/image_raw/compressed (브릿지가 UDP→ROS 재발행한 것)
    → detectMarkers(DICT_4X4_50) → config에 있는 ID만 사용(도킹용 42는 config에서 제외)
    → solvePnP로 마커의 카메라기준 pose → 카메라장착 + 마커맵좌표로 로봇 맵pose 역산
    → 게이트 통과 시 /initialpose 발행 (AMCL 보정)

★안전: 기본 publish_correction=False (dry-run: 계산값만 로그, AMCL 안 건드림).
   마커를 알려진 곳에 놓고 계산 로봇pose가 실제와 맞는지 검증 후 True로 켤 것.

프레임 규약:
  - 카메라 광학(optical): x우 y아래 z앞 (solvePnP 결과). 이미지는 sender가 180°회전해 정립됨.
  - base_link: x앞 y좌 z위 (REP-103).
  - R_BASE_OPT: 광학축→base축 회전 (pi_dock과 동일). 카메라가 정면을 봄 전제.
  - 마커맵pose: 마커는 벽에 수직, 그 '정면 법선(+z_marker)'이 맵에서 yaw_deg 방향을 향함.

실행(PC):
  python3 ~/team_ws/aruco_docking/aruco_localizer.py --ros-args \
     -p markers_file:=~/team_ws/aruco_docking/markers_map.yaml -p publish_correction:=false
"""
import math
import os
import yaml
import numpy as np
import cv2

import rclpy
from rclpy.node import Node
from rclpy.qos import qos_profile_sensor_data
from sensor_msgs.msg import CompressedImage
from geometry_msgs.msg import PoseWithCovarianceStamped

# 광학→base 회전 (x_base=z_opt 전방, y_base=-x_opt 좌+, z_base=-y_opt) — pi_dock과 동일
R_BASE_OPT = np.array([[0., 0., 1.],
                       [-1., 0., 0.],
                       [0., -1., 0.]])


def rodrigues_tvec_to_T(rvec, tvec):
    R, _ = cv2.Rodrigues(rvec)
    T = np.eye(4)
    T[:3, :3] = R
    T[:3, 3] = np.asarray(tvec).reshape(3)
    return T


def T_from_map_marker(x, y, z, yaw):
    """마커 맵pose 4x4. 마커 법선(+z)이 맵 yaw 방향, 마커 up(+y)은 맵 +z(위)."""
    c, s = math.cos(yaw), math.sin(yaw)
    # 열 = [x_marker, y_marker, z_marker(법선)]
    #  z_marker = (c, s, 0)  (yaw 방향, 수평)
    #  y_marker = (0, 0, 1)  (위)
    #  x_marker = y × z = (-s, c, 0)
    R = np.array([[-s, 0.0, c],
                  [c,  0.0, s],
                  [0.0, 1.0, 0.0]])
    T = np.eye(4)
    T[:3, :3] = R
    T[:3, 3] = [x, y, z]
    return T


def yaw_from_T(T):
    return math.atan2(T[1, 0], T[0, 0])


class ArucoLocalizer(Node):
    def __init__(self):
        super().__init__('aruco_localizer')

        # ── I/O ──
        self.declare_parameter('image_topic', '/robot1/camera/image_raw/compressed')
        self.declare_parameter('initialpose_topic', '/initialpose')
        self.declare_parameter('markers_file', '')          # 마커 맵좌표 YAML (필수)
        self.declare_parameter('dictionary', 'DICT_4X4_50')
        self.declare_parameter('marker_length', 0.12)        # 순찰마커 변길이(m)
        self.declare_parameter('approx_hfov_deg', 60.0)      # camera_info 없을때 근사K

        # ── 카메라 장착(base_link 기준) ──
        self.declare_parameter('cam_x', 0.05)   # 전방 오프셋(m) — 대략, 검증 후 조정
        self.declare_parameter('cam_y', 0.0)    # 좌우 오프셋
        self.declare_parameter('cam_z', 0.113)  # 높이(메모리 11.3cm)

        # ── 게이트(오검출로 AMCL 튀는 것 방지) ──
        self.declare_parameter('min_range', 0.3)      # 마커 너무 가까우면 부정확
        self.declare_parameter('max_range', 2.5)      # 너무 멀면 부정확
        self.declare_parameter('max_bearing_deg', 35.0)   # 정면서 벗어나면 제외
        self.declare_parameter('confirm_frames', 5)   # 연속 N프레임 일치해야 보정
        self.declare_parameter('consist_xy', 0.10)    # 연속프레임 위치 일치 허용(m)
        self.declare_parameter('consist_yaw_deg', 8.0)
        self.declare_parameter('min_interval_s', 3.0) # 보정 최소 간격
        self.declare_parameter('detect_rate', 8.0)    # 처리 Hz 상한

        # ── ★안전 스위치 ──
        self.declare_parameter('publish_correction', False)  # False=dry-run(로그만)
        # 위치 공분산(보정 신뢰도). 작을수록 AMCL이 강하게 믿음.
        self.declare_parameter('cov_xy', 0.05)
        self.declare_parameter('cov_yaw', 0.03)
        # ★드리프트 게이트: AMCL이 마커계산값과 이만큼 이상 벌어졌을 때만 보정.
        #   (안 그러면 주행 중 마커 보이는 동안 계속 AMCL을 되돌려 '무이동'→ESCAPE 유발)
        self.declare_parameter('drift_xy', 0.12)      # m
        self.declare_parameter('drift_yaw_deg', 8.0)

        g = lambda n: self.get_parameter(n).value
        self.image_topic = g('image_topic')
        self.marker_len = float(g('marker_length'))
        self.cam_x = float(g('cam_x')); self.cam_y = float(g('cam_y')); self.cam_z = float(g('cam_z'))
        self.publish_corr = bool(g('publish_correction'))

        # 카메라 장착 변환 T_base_cam
        self.T_base_cam = np.eye(4)
        self.T_base_cam[:3, :3] = R_BASE_OPT
        self.T_base_cam[:3, 3] = [self.cam_x, self.cam_y, self.cam_z]

        # 마커 맵좌표 로드
        self.markers = self._load_markers(os.path.expanduser(g('markers_file')))

        # ArUco (4.6 구 API — 도킹 검출노드와 동일)
        dict_id = getattr(cv2.aruco, g('dictionary'))
        self.aruco_dict = cv2.aruco.Dictionary_get(dict_id)
        self.aruco_params = cv2.aruco.DetectorParameters_create()
        self.aruco_params.cornerRefinementMethod = cv2.aruco.CORNER_REFINE_SUBPIX
        s = self.marker_len / 2.0
        self.obj_pts = np.array([[-s, s, 0], [s, s, 0], [s, -s, 0], [-s, -s, 0]], dtype=np.float32)

        self.K = None
        self.min_period = 1.0 / g('detect_rate') if g('detect_rate') > 0 else 0.0
        self.last_proc = None
        self.last_pub_t = None
        # 연속 일치 확인용
        self.run_id = None
        self.run_poses = []   # [(x,y,yaw), ...]

        self.amcl = None   # ★discrepancy 게이트용 현재 AMCL pose (x,y,yaw)
        self.pub = self.create_publisher(PoseWithCovarianceStamped, g('initialpose_topic'), 10)
        self.create_subscription(CompressedImage, self.image_topic, self.on_image, qos_profile_sensor_data)
        self.create_subscription(PoseWithCovarianceStamped, '/amcl_pose', self.on_amcl, 10)

        mode = '★발행ON(AMCL 보정)' if self.publish_corr else 'dry-run(로그만, 발행안함)'
        self.get_logger().info(
            f'aruco_localizer 시작 [{mode}] — 마커 {sorted(self.markers.keys())}개 로드, '
            f'cam장착=({self.cam_x},{self.cam_y},{self.cam_z}), 게이트 range[{g("min_range")},{g("max_range")}]m')

    def _load_markers(self, path):
        if not path or not os.path.exists(path):
            self.get_logger().warn(f'markers_file 없음({path}) — 마커 0개. config 채우고 재시작.')
            return {}
        data = yaml.safe_load(open(path)) or {}
        out = {}
        for mid, v in (data.get('markers') or {}).items():
            out[int(mid)] = (float(v['x']), float(v['y']), math.radians(float(v['yaw_deg'])),
                             float(v.get('z', self.cam_z)))
        self.get_logger().info(f'마커 {len(out)}개 로드: {sorted(out.keys())}')
        return out

    def on_amcl(self, msg):
        q = msg.pose.pose.orientation
        yaw = math.atan2(2*(q.w*q.z + q.x*q.y), 1 - 2*(q.y*q.y + q.z*q.z))
        self.amcl = (msg.pose.pose.position.x, msg.pose.pose.position.y, yaw)

    def _build_K(self, w, h):
        hfov = math.radians(self.get_parameter('approx_hfov_deg').value)
        fx = (w / 2.0) / math.tan(hfov / 2.0)
        self.K = np.array([[fx, 0, w / 2.0], [0, fx, h / 2.0], [0, 0, 1.0]], dtype=np.float64)
        self.D = np.zeros((5, 1))

    def on_image(self, msg):
        stamp = msg.header.stamp.sec + msg.header.stamp.nanosec * 1e-9
        if self.last_proc is not None and (stamp - self.last_proc) < self.min_period:
            return
        self.last_proc = stamp
        if not self.markers:
            return

        buf = np.frombuffer(msg.data, np.uint8)
        gray = cv2.imdecode(buf, cv2.IMREAD_GRAYSCALE)
        if gray is None:
            return
        if self.K is None:
            h, w = gray.shape[:2]
            self._build_K(w, h)

        corners, ids, _ = cv2.aruco.detectMarkers(gray, self.aruco_dict, parameters=self.aruco_params)
        if ids is None:
            self._reset_run(); return
        ids = ids.flatten()

        # config에 있는 마커 중 게이트 통과하는 것들 중 '가장 가까운' 하나 사용
        best = None  # (range, mid, x,y,yaw)
        g = lambda n: self.get_parameter(n).value
        for i, mid in enumerate(ids):
            mid = int(mid)
            if mid not in self.markers:
                continue
            img_pts = corners[i].reshape(4, 2).astype(np.float32)
            ok, rvec, tvec = cv2.solvePnP(self.obj_pts, img_pts, self.K, self.D,
                                          flags=cv2.SOLVEPNP_IPPE_SQUARE)
            if not ok:
                continue
            rng = float(np.linalg.norm(tvec))
            bearing = math.degrees(math.atan2(float(tvec[0]), float(tvec[2])))  # 광학 x/z
            if rng < g('min_range') or rng > g('max_range') or abs(bearing) > g('max_bearing_deg'):
                continue
            # 로봇 맵pose 역산
            T_cam_marker = rodrigues_tvec_to_T(rvec, tvec)
            T_base_marker = self.T_base_cam @ T_cam_marker
            mx, my, myaw, mz = self.markers[mid]
            T_map_marker = T_from_map_marker(mx, my, mz, myaw)
            T_map_base = T_map_marker @ np.linalg.inv(T_base_marker)
            rx, ry = float(T_map_base[0, 3]), float(T_map_base[1, 3])
            ryaw = yaw_from_T(T_map_base)
            if best is None or rng < best[0]:
                best = (rng, mid, rx, ry, ryaw)

        if best is None:
            self._reset_run(); return

        rng, mid, rx, ry, ryaw = best
        self._accumulate(mid, rx, ry, ryaw, rng)

    def _reset_run(self):
        self.run_id = None
        self.run_poses = []

    def _accumulate(self, mid, rx, ry, ryaw, rng):
        g = lambda n: self.get_parameter(n).value
        # 같은 마커 연속 + 위치 일관성 확인
        if self.run_id != mid:
            self.run_id = mid
            self.run_poses = [(rx, ry, ryaw)]
            return
        px, py, pyaw = self.run_poses[-1]
        if (math.hypot(rx - px, ry - py) > g('consist_xy') or
                abs(math.degrees(math.atan2(math.sin(ryaw - pyaw), math.cos(ryaw - pyaw)))) > g('consist_yaw_deg')):
            # 튀면 리셋(오검출)
            self.run_poses = [(rx, ry, ryaw)]
            return
        self.run_poses.append((rx, ry, ryaw))
        if len(self.run_poses) < g('confirm_frames'):
            return

        # 중앙값으로 안정화
        arr = np.array(self.run_poses[-int(g('confirm_frames')):])
        mx_, my_ = float(np.median(arr[:, 0])), float(np.median(arr[:, 1]))
        # yaw 중앙값(원형)
        myaw_ = math.atan2(float(np.median(np.sin(arr[:, 2]))), float(np.median(np.cos(arr[:, 2]))))

        now = self.get_clock().now().nanoseconds / 1e9
        if self.last_pub_t is not None and (now - self.last_pub_t) < g('min_interval_s'):
            return
        self.last_pub_t = now
        self.run_poses = self.run_poses[-1:]  # 다음 확인 위해 유지 최소화

        # ★discrepancy 게이트: AMCL과 많이 벌어졌을 때만 보정.
        #   (마커 yaw ~7° 노이즈라, 잘 맞는 AMCL을 계속 건드리면 오히려 틀어짐. 큰 드리프트만 잡음.)
        if self.amcl is not None:
            dxy = math.hypot(mx_ - self.amcl[0], my_ - self.amcl[1])
            dyaw = abs(math.degrees(math.atan2(math.sin(myaw_ - self.amcl[2]),
                                               math.cos(myaw_ - self.amcl[2]))))
            drifted = (dxy > g('drift_xy') or dyaw > g('drift_yaw_deg'))
            disc = f" | AMCL차이 xy={dxy*100:.0f}cm yaw={dyaw:.0f}°"
        else:
            drifted = False; disc = " | AMCL없음(대기, 보정안함)"  # ★부트스트랩 편향주입 방지

        do_pub = self.publish_corr and drifted
        self.get_logger().info(
            f"[마커 {mid}, {rng:.2f}m] 계산 x={mx_:+.3f} y={my_:+.3f} yaw={math.degrees(myaw_):+.1f}°{disc}"
            + ("  → ★보정 발행(드리프트 큼)" if do_pub else
               ("  (게이트OK: AMCL 정확, 스킵)" if self.publish_corr else "  (dry-run)")))

        if do_pub:
            self._publish_initialpose(mx_, my_, myaw_)

    def _publish_initialpose(self, x, y, yaw):
        g = lambda n: self.get_parameter(n).value
        m = PoseWithCovarianceStamped()
        m.header.frame_id = 'map'
        m.header.stamp = self.get_clock().now().to_msg()
        m.pose.pose.position.x = x
        m.pose.pose.position.y = y
        m.pose.pose.orientation.z = math.sin(yaw / 2.0)
        m.pose.pose.orientation.w = math.cos(yaw / 2.0)
        cov = [0.0] * 36
        cov[0] = g('cov_xy'); cov[7] = g('cov_xy'); cov[35] = g('cov_yaw')
        m.pose.covariance = cov
        self.pub.publish(m)


def main():
    rclpy.init()
    node = ArucoLocalizer()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
