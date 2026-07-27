#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
pi_dock.py — 로봇(Pi) 로컬 '마커 서치 → 정렬 → 후진' 자동 도킹 (AMCL 완전 배제)
==============================================================================
왜? AMCL이 흔들리면 pre-dock에 삐뚤게 서서 마커도 안 보이고 /spin 정렬도 실패.
    → 도킹 정렬/후진을 전부 '마커(절대각)+odom(근거리)'로만. AMCL 안 씀.

단계(원샷):
  1) SEARCH : 제자리에서 '천천히' 회전하며 ArUco 마커를 찾는다. (검출노드 /detected_dock_pose)
  2) ALIGN  : 마커 bearing(정면=0)을 보며 그쪽으로 천천히 정렬. |bearing|<tol 안정되면 정지.
  3) REVERSE: odom 거리기반으로 뒤로 distance(m) 후진(마커로 직진유지) → 자동정지 → 0속도.
안전: 신선한 스탬프 / 후진 하드캡(distance+hard_margin) / 최대시간 / 각 단계 타임아웃 / 종료 시 0속도.

실행(로봇): python3 ~/pi_dock.py --ros-args -p distance:=0.40 -p search_speed:=0.20
전제: PC 검출노드(aruco_dock_detector) ON → /detected_dock_pose 발행 중.
"""
import math
import numpy as np
import rclpy
from rclpy.node import Node
from geometry_msgs.msg import TwistStamped, PoseStamped
from nav_msgs.msg import Odometry

# 광학→base 리맵 (x_base=z_opt 전방, y_base=-x_opt 좌+)
R_BASE_OPT = np.array([[0., 0., 1.], [-1., 0., 0.], [0., -1., 0.]])

SEARCH, ALIGN, REVERSE, DONE = 'SEARCH', 'ALIGN', 'REVERSE', 'DONE'


class PiDock(Node):
    def __init__(self):
        super().__init__('pi_dock')
        # --- 후진(검증된 pi_reverse와 동일) ---
        self.declare_parameter('distance', 0.40)
        self.declare_parameter('speed', 0.07)
        self.declare_parameter('slowdown', 0.08)
        self.declare_parameter('min_speed', 0.045)
        self.declare_parameter('hard_margin', 0.03)
        self.declare_parameter('max_time', 25.0)
        # --- 마커 조향(후진 중) ---
        self.declare_parameter('use_marker_steer', True)
        self.declare_parameter('k_bearing', 0.6)
        self.declare_parameter('reverse_steer_sign', 1.0)
        self.declare_parameter('w_max', 0.25)
        self.declare_parameter('marker_timeout', 1.0)
        self.declare_parameter('lpf_alpha', 0.3)
        # --- SEARCH(마커 탐색 회전) ---
        self.declare_parameter('search_speed', 0.20)     # 느린 회전(rad/s) ★사용자 요청
        self.declare_parameter('search_dir', 1.0)        # +1=CCW(좌), -1=CW(우)
        self.declare_parameter('search_timeout', 40.0)   # 못 찾으면 중단(초, ~1.3바퀴)
        self.declare_parameter('search_confirm', 3)      # 연속 N프레임 검출돼야 ALIGN
        # --- ALIGN(마커 정렬) ---
        self.declare_parameter('align_k', 0.9)           # 비례게인
        self.declare_parameter('align_w_max', 0.22)      # 정렬 최대속도(느리게)
        self.declare_parameter('align_w_min', 0.05)      # 정지마찰 넘기는 최소
        self.declare_parameter('align_tol_deg', 3.5)     # 이 안이면 정렬 OK
        self.declare_parameter('align_settle', 8)        # 연속 N프레임 tol 안이면 완료
        self.declare_parameter('align_timeout', 20.0)

        g = lambda n: self.get_parameter(n).value
        self.distance=float(g('distance')); self.speed=float(g('speed'))
        self.slowdown=float(g('slowdown')); self.min_speed=float(g('min_speed'))
        self.hard_margin=float(g('hard_margin')); self.max_time=float(g('max_time'))

        self.phase = SEARCH
        self.odom_xy = None
        self.start_xy = None            # 후진 시작 위치
        self.phase_t0 = None
        self.bearing_f = None
        self.last_marker = None
        self.marker_hits = 0
        self.align_ok = 0
        self.done_ticks = 0

        self.create_subscription(Odometry, '/odom', self.on_odom, 10)
        self.create_subscription(PoseStamped, '/detected_dock_pose', self.on_marker, 10)
        self.pub = self.create_publisher(TwistStamped, '/cmd_vel', 10)
        self.create_timer(0.02, self.loop)   # 50Hz
        self.get_logger().info(
            f'pi_dock 시작: SEARCH(회전 {g("search_speed")}rad/s) → ALIGN(±{g("align_tol_deg")}°) '
            f'→ REVERSE({self.distance*100:.0f}cm, 캡 {(self.distance+self.hard_margin)*100:.0f}cm)')

    def on_odom(self, msg):
        self.odom_xy = (msg.pose.pose.position.x, msg.pose.pose.position.y)

    def on_marker(self, msg):
        p = R_BASE_OPT @ np.array([msg.pose.position.x, msg.pose.position.y, msg.pose.position.z])
        x, y = float(p[0]), float(p[1])
        bearing = math.atan2(y, x)      # 좌+
        a = self.get_parameter('lpf_alpha').value
        self.bearing_f = bearing if self.bearing_f is None else (a*bearing + (1-a)*self.bearing_f)
        self.last_marker = self.get_clock().now()

    def marker_fresh(self, now):
        return (self.last_marker is not None and self.bearing_f is not None and
                (now - self.last_marker).nanoseconds/1e9 <= self.get_parameter('marker_timeout').value)

    def send(self, v, w=0.0):
        cmd = TwistStamped()
        cmd.header.stamp = self.get_clock().now().to_msg()
        cmd.header.frame_id = 'base_link'
        cmd.twist.linear.x = float(v); cmd.twist.angular.z = float(w)
        self.pub.publish(cmd)

    def set_phase(self, ph):
        self.phase = ph; self.phase_t0 = self.get_clock().now()
        self.get_logger().info(f'--- 단계: {ph} ---')

    def loop(self):
        now = self.get_clock().now()
        if self.odom_xy is None:
            return
        if self.phase_t0 is None:
            self.phase_t0 = now
        el = (now - self.phase_t0).nanoseconds/1e9

        # ---------- SEARCH ----------
        if self.phase == SEARCH:
            if self.marker_fresh(now):
                self.marker_hits += 1
                if self.marker_hits >= self.get_parameter('search_confirm').value:
                    self.send(0.0)
                    self.get_logger().info(f'마커 발견 (bear={math.degrees(self.bearing_f):+.0f}°) → ALIGN')
                    self.set_phase(ALIGN); self.align_ok = 0; return
            else:
                self.marker_hits = 0
            if el > self.get_parameter('search_timeout').value:
                self.get_logger().warn('SEARCH 타임아웃 — 마커 못 찾음. 중단(0속도).')
                self.send(0.0); self.phase = DONE; return
            self.send(0.0, self.get_parameter('search_dir').value * self.get_parameter('search_speed').value)
            self.get_logger().info(f'SEARCH 회전중 {el:.0f}s', throttle_duration_sec=1.0)
            return

        # ---------- ALIGN ----------
        if self.phase == ALIGN:
            if not self.marker_fresh(now):
                # 마커 잃음 → 다시 SEARCH
                if el > 1.5:
                    self.get_logger().warn('ALIGN 중 마커 소실 → SEARCH 복귀')
                    self.send(0.0); self.set_phase(SEARCH); self.marker_hits=0; return
                self.send(0.0); return
            tol = math.radians(self.get_parameter('align_tol_deg').value)
            b = self.bearing_f
            if abs(b) < tol:
                self.align_ok += 1
                self.send(0.0)
                if self.align_ok >= self.get_parameter('align_settle').value:
                    self.get_logger().info(f'정렬 완료 (bear={math.degrees(b):+.1f}°) → REVERSE')
                    self.start_xy = None; self.set_phase(REVERSE); return
                return
            self.align_ok = 0
            if el > self.get_parameter('align_timeout').value:
                self.get_logger().warn('ALIGN 타임아웃 — 정렬 실패. 중단(0속도).')
                self.send(0.0); self.phase = DONE; return
            # 마커 쪽으로 회전(좌+ 이면 좌회전 w+)
            k = self.get_parameter('align_k').value
            wmax = self.get_parameter('align_w_max').value; wmin = self.get_parameter('align_w_min').value
            w = k * b
            w = max(-wmax, min(wmax, w))
            if 0 < abs(w) < wmin: w = math.copysign(wmin, w)
            self.send(0.0, w)
            self.get_logger().info(f'ALIGN bear={math.degrees(b):+.1f}° w={w:+.2f}', throttle_duration_sec=0.3)
            return

        # ---------- REVERSE (검증된 pi_reverse 로직) ----------
        if self.phase == REVERSE:
            if self.start_xy is None:
                self.start_xy = self.odom_xy
                self.get_logger().info('후진 시작'); return
            traveled = math.hypot(self.odom_xy[0]-self.start_xy[0], self.odom_xy[1]-self.start_xy[1])
            if traveled >= self.distance + self.hard_margin:
                self.get_logger().warn(f'안전캡 급정지! {traveled*100:.1f}cm'); self.send(0.0); self.phase=DONE; return
            if el > self.max_time:
                self.get_logger().warn('후진 최대시간 급정지'); self.send(0.0); self.phase=DONE; return
            if traveled >= self.distance:
                self.get_logger().info(f'도킹 완료: 후진 {traveled*100:.1f}cm'); self.send(0.0); self.phase=DONE; return
            # 후진 중 마커 직진유지
            w = 0.0
            if self.get_parameter('use_marker_steer').value and self.marker_fresh(now):
                w = self.get_parameter('reverse_steer_sign').value * self.get_parameter('k_bearing').value * self.bearing_f
                wm = self.get_parameter('w_max').value; w = max(-wm, min(wm, w))
            remaining = self.distance - traveled
            frac = min(1.0, remaining/self.slowdown) if self.slowdown > 0 else 1.0
            mag = max(self.min_speed, self.speed*frac)
            self.send(-mag, w)
            self.get_logger().info(f'후진 {traveled*100:.1f}/{self.distance*100:.0f}cm', throttle_duration_sec=0.25)
            return

        # ---------- DONE ----------
        self.send(0.0); self.done_ticks += 1
        if self.done_ticks == 1:
            self.get_logger().info('시퀀스 종료 (0속도 유지)')


def main():
    import signal
    import time as _t
    rclpy.init()
    node = PiDock()

    # ★안전: SIGTERM/SIGINT를 잡아 KeyboardInterrupt로 → finally서 0속도 확실히 발행.
    #   (예전엔 SIGTERM/SIGKILL로 죽으면 finally 미실행 → 베이스가 마지막 속도 latch → 런어웨이)
    def _stop(signum, frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, _stop)
    signal.signal(signal.SIGINT, _stop)

    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        # 종료 시 확실히 정지: 신선 스탬프 0속도를 0.02s 간격으로 15회(~0.3s) 지속 발행.
        try:
            for _ in range(15):
                node.send(0.0, 0.0)
                _t.sleep(0.02)
        except Exception:
            pass
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
