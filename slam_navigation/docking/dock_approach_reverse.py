#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
dock_approach_reverse.py — ArUco 정면 비콘 '후진' 도킹 접근 노드 (PC 실행)
==========================================================================
★ 후면 도킹: 충전단자가 로봇 뒤 → 뒤로(후진) 진입. 마커는 충전소 반대편(로봇 정면)에.
  로봇은 마커를 화면 중앙에 유지하며 똑바로 후진 → 등이 충전소로. 제자리회전 없음.

★ 정지 방식 = odom '거리 기반' (2026-07-02 개선):
  TB3 베이스는 cmd_vel 타임아웃이 없어 마지막 속도를 계속 물고 감 →
  ① 정확히 reverse_distance(m)만큼 후진하면 스스로 멈춤(odom 측정, 근사K 무관·정밀).
  ② 완료/비활성/소실/타임아웃 등 '모든 정지'에서 0속도를 확실히 발행(버스트) → 안 물고 감.

입력: /detected_dock_pose (PoseStamped, 광학프레임)  +  /odom (Odometry, 거리측정)
출력: /cmd_vel (TwistStamped)    상태: /dock/approach_state (String)

리맵: x_base=z_opt(전방=마커거리), y_base=-x_opt(횡,좌+). 조향은 bearing으로 마커 센터링.
사용: reverse_distance(m) 파라미터로 후진거리 지정 후 active=true.
"""
import math
import numpy as np
import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped, TwistStamped
from nav_msgs.msg import Odometry
from std_msgs.msg import String

R_BASE_OPT = np.array([
    [0.0,  0.0, 1.0],
    [-1.0, 0.0, 0.0],
    [0.0, -1.0, 0.0],
])


class DockApproachReverse(Node):
    def __init__(self):
        super().__init__('dock_approach_reverse')
        # 거리/속도/게인
        self.declare_parameter('reverse_distance', 0.10)   # ★후진할 거리(m) — 실행 전 set
        self.declare_parameter('v_reverse', 0.05)          # 후진 속도(m/s)
        self.declare_parameter('v_min', 0.03)
        self.declare_parameter('k_bearing', 1.0)           # 마커 센터링 조향
        self.declare_parameter('reverse_steer_sign', 1.0)
        self.declare_parameter('w_max', 0.4)
        self.declare_parameter('slowdown_margin', 0.05)    # 도착 이 전부터 감속(m)
        self.declare_parameter('use_marker_steer', True)   # 마커로 조향(소실 시 직진후진)
        self.declare_parameter('marker_timeout', 1.5)
        self.declare_parameter('lpf_alpha', 0.3)
        self.declare_parameter('max_reverse_time', 30.0)
        self.declare_parameter('active', False)

        self.filt = None            # (x, y) 마커 base
        self.last_seen = None
        self.odom_xy = None         # 최신 odom 위치
        self.start_odom = None      # 후진 시작 위치
        self.state = 'IDLE'
        self.active_since = None
        self.prev_active = False
        self.stop_burst = 0         # 정지 시 0속도 반복 발행 카운터

        self.create_subscription(PoseStamped, '/detected_dock_pose', self.on_marker, 10)
        self.create_subscription(Odometry, '/odom', self.on_odom, 10)
        self.cmd_pub = self.create_publisher(TwistStamped, '/cmd_vel', 10)
        self.state_pub = self.create_publisher(String, '/dock/approach_state', 10)
        self.create_timer(0.05, self.control_loop)   # 20Hz
        self.get_logger().info('dock_approach_reverse 시작 (active=False / odom 거리기반 후진)')

    def on_marker(self, msg):
        p = R_BASE_OPT @ np.array([msg.pose.position.x, msg.pose.position.y, msg.pose.position.z])
        x, y = float(p[0]), float(p[1])
        a = self.get_parameter('lpf_alpha').value
        if self.filt is None:
            self.filt = (x, y)
        else:
            px, py = self.filt
            self.filt = (a*x + (1-a)*px, a*y + (1-a)*py)
        self.last_seen = self.get_clock().now()

    def on_odom(self, msg):
        self.odom_xy = (msg.pose.pose.position.x, msg.pose.pose.position.y)

    def stop(self):
        """0속도 발행 (base가 마지막 속도 물지 않게)."""
        self.cmd_pub.publish(TwistStamped())

    def set_state(self, s):
        if s != self.state:
            self.state = s
            self.state_pub.publish(String(data=s))
            self.get_logger().info(f'dock state → {s}')

    def control_loop(self):
        now = self.get_clock().now()
        active = self.get_parameter('active').value

        # 비활성 전환 감지 → 0속도 버스트 예약
        if self.prev_active and not active:
            self.stop_burst = 20   # ~1초간 0속도 확실히 발행
        self.prev_active = active

        # ── 비활성 ──
        if not active:
            self.active_since = None
            self.start_odom = None
            if self.stop_burst > 0:
                self.stop(); self.stop_burst -= 1   # 정지명령 확실히
            else:
                self.set_state('IDLE')              # 그 후엔 조용(Nav2에 cmd_vel 양보)
            return

        # ── 활성 시작(라이징 엣지) ──
        if self.active_since is None:
            if self.odom_xy is None:
                self.get_logger().warn('odom 없음 — 거리측정 불가, 후진 보류', throttle_duration_sec=1.0)
                self.stop(); return
            self.active_since = now
            self.start_odom = self.odom_xy
            self.get_logger().info(
                f'후진 시작: 목표거리 {self.get_parameter("reverse_distance").value:.3f}m')

        # 안전: 최대시간
        if (now - self.active_since).nanoseconds/1e9 > self.get_parameter('max_reverse_time').value:
            self.set_state('ABORT'); self.stop(); return
        # 안전: odom 끊김
        if self.odom_xy is None or self.start_odom is None:
            self.stop(); return

        traveled = math.hypot(self.odom_xy[0]-self.start_odom[0],
                              self.odom_xy[1]-self.start_odom[1])
        target = self.get_parameter('reverse_distance').value
        remaining = target - traveled

        # ── 완료: 목표거리 도달 → 정지(0속도 계속 발행) ──
        if remaining <= 0.0:
            self.set_state('DOCKED'); self.stop(); return
        self.set_state('REVERSING')

        # 조향: 마커 센터링 (소실/off면 직진)
        w = 0.0
        use_steer = self.get_parameter('use_marker_steer').value
        marker_ok = (self.last_seen is not None and
                     (now - self.last_seen).nanoseconds/1e9 <= self.get_parameter('marker_timeout').value)
        if use_steer and marker_ok and self.filt is not None:
            x, y = self.filt
            bearing = math.atan2(y, x)
            w = self.get_parameter('reverse_steer_sign').value * self.get_parameter('k_bearing').value * bearing

        # 후진속도: 도착 근처 감속
        margin = self.get_parameter('slowdown_margin').value
        frac = max(0.0, min(1.0, remaining / margin)) if margin > 0 else 1.0
        v_rev = self.get_parameter('v_reverse').value
        v_min = self.get_parameter('v_min').value
        mag = max(v_min, v_rev * frac) if frac > 0.1 else v_min
        v = -mag

        w_max = self.get_parameter('w_max').value
        w = max(-w_max, min(w_max, w))

        cmd = TwistStamped()
        cmd.header.stamp = now.to_msg()
        cmd.twist.linear.x = float(v)
        cmd.twist.angular.z = float(w)
        self.cmd_pub.publish(cmd)
        self.get_logger().info(
            f'이동 {traveled*100:.1f}/{target*100:.1f}cm  v={v:.3f} w={w:.2f}',
            throttle_duration_sec=0.3)


def main():
    rclpy.init()
    node = DockApproachReverse()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        try:
            node.stop()   # 종료 시에도 0속도
        except Exception:
            pass
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
