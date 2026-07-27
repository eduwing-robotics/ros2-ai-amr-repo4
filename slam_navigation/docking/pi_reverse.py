#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
pi_reverse.py — 로봇(Pi) 로컬 '거리 후진' + 마커 조향 도킹 노드 (안전판)
========================================================================
왜 Pi에서? PC→WiFi cmd_vel 제어는 지연+베이스 속도무름으로 오버슛(2026-07-02 벽충돌
직전). 제어를 Pi 로컬로 돌려 목표거리서 즉시·확실히 정지.

역할 분리:
  - 정지 = 로컬 /odom '거리 기반' (지연0, 1mm 정밀) — 안전의 핵심.
  - 조향 = PC 검출노드의 /detected_dock_pose(마커) bearing 센터링 — WiFi 타도 OK
    (조향은 느린 보정이라 지연 허용, 정지는 로컬이라 안전).
  - 마커 소실/off → w=0 직진 폴백.

동작(원샷): 실행 즉시 뒤로 distance(m) 후진(마커로 직진유지) → 자동정지 → 0속도 계속.
안전: 신선한 타임스탬프 / 하드 안전캡(distance+hard_margin) / 최대시간 / 완료 후 0속도.
⚠️ 후진 조향 부호는 발산 위험 → reverse_steer_sign(±1), 낮은 게인으로 짧게 먼저 검증.

실행(로봇): python3 ~/pi_reverse.py --ros-args -p distance:=0.40 -p speed:=0.07
"""
import math
import numpy as np
import rclpy
from rclpy.node import Node
from geometry_msgs.msg import TwistStamped, PoseStamped
from nav_msgs.msg import Odometry

# 광학→base 고정 리맵 (x_base=z_opt 전방, y_base=-x_opt 좌+)
R_BASE_OPT = np.array([[0., 0., 1.], [-1., 0., 0.], [0., -1., 0.]])


class PiReverse(Node):
    def __init__(self):
        super().__init__('pi_reverse')
        self.declare_parameter('distance', 0.40)      # 후진 목표(m)
        self.declare_parameter('speed', 0.07)         # 후진 속도(m/s)
        self.declare_parameter('slowdown', 0.08)      # 도착 전 감속 구간(m)
        self.declare_parameter('min_speed', 0.045)
        self.declare_parameter('hard_margin', 0.03)   # 목표+이 값 넘으면 급정지
        self.declare_parameter('max_time', 25.0)
        # 마커 조향
        self.declare_parameter('use_marker_steer', True)
        self.declare_parameter('k_bearing', 0.6)      # 낮게 시작(발산방지)
        self.declare_parameter('reverse_steer_sign', 1.0)  # 발산하면 -1로
        self.declare_parameter('w_max', 0.25)
        self.declare_parameter('marker_timeout', 1.0)
        self.declare_parameter('lpf_alpha', 0.3)

        g = lambda n: self.get_parameter(n).value
        self.distance = float(g('distance')); self.speed = float(g('speed'))
        self.slowdown = float(g('slowdown')); self.min_speed = float(g('min_speed'))
        self.hard_margin = float(g('hard_margin')); self.max_time = float(g('max_time'))

        self.odom_xy = None
        self.start_xy = None
        self.t0 = None
        self.done = False
        self.done_ticks = 0
        self.bearing_f = None       # 필터된 bearing
        self.last_marker = None

        self.create_subscription(Odometry, '/odom', self.on_odom, 10)
        self.create_subscription(PoseStamped, '/detected_dock_pose', self.on_marker, 10)
        self.pub = self.create_publisher(TwistStamped, '/cmd_vel', 10)
        self.create_timer(0.02, self.loop)   # 50Hz
        self.get_logger().info(
            f'pi_reverse: 뒤로 {self.distance*100:.0f}cm, 속도 {self.speed}, '
            f'마커조향={g("use_marker_steer")} (안전캡 {(self.distance+self.hard_margin)*100:.0f}cm)')

    def on_odom(self, msg):
        self.odom_xy = (msg.pose.pose.position.x, msg.pose.pose.position.y)

    def on_marker(self, msg):
        p = R_BASE_OPT @ np.array([msg.pose.position.x, msg.pose.position.y, msg.pose.position.z])
        x, y = float(p[0]), float(p[1])
        bearing = math.atan2(y, x)
        a = self.get_parameter('lpf_alpha').value
        self.bearing_f = bearing if self.bearing_f is None else (a*bearing + (1-a)*self.bearing_f)
        self.last_marker = self.get_clock().now()

    def send(self, v, w=0.0):
        cmd = TwistStamped()
        cmd.header.stamp = self.get_clock().now().to_msg()   # ★항상 신선한 스탬프
        cmd.header.frame_id = 'base_link'
        cmd.twist.linear.x = float(v)
        cmd.twist.angular.z = float(w)
        self.pub.publish(cmd)

    def loop(self):
        now = self.get_clock().now()
        if self.odom_xy is None:
            return
        if self.start_xy is None:
            self.start_xy = self.odom_xy; self.t0 = now
            self.get_logger().info('후진 시작'); return

        traveled = math.hypot(self.odom_xy[0]-self.start_xy[0], self.odom_xy[1]-self.start_xy[1])

        if self.done:
            self.send(0.0)
            self.done_ticks += 1
            if self.done_ticks == 1:
                self.get_logger().info(f'정지 완료: 이동 {traveled*100:.1f}cm')
            return

        # 안전 정지들
        if traveled >= self.distance + self.hard_margin:
            self.get_logger().warn(f'안전캡 급정지! {traveled*100:.1f}cm'); self.done=True; self.send(0.0); return
        if (now - self.t0).nanoseconds/1e9 > self.max_time:
            self.get_logger().warn('최대시간 급정지'); self.done=True; self.send(0.0); return
        if traveled >= self.distance:
            self.get_logger().info(f'목표 도달: {traveled*100:.1f}cm → 정지'); self.done=True; self.send(0.0); return

        # 조향(마커 센터링) — 신선할 때만, 아니면 직진
        w = 0.0; steer_info = 'straight'
        if self.get_parameter('use_marker_steer').value and self.bearing_f is not None and self.last_marker is not None:
            if (now - self.last_marker).nanoseconds/1e9 <= self.get_parameter('marker_timeout').value:
                w = self.get_parameter('reverse_steer_sign').value * self.get_parameter('k_bearing').value * self.bearing_f
                wm = self.get_parameter('w_max').value
                w = max(-wm, min(wm, w))
                steer_info = f'bear={math.degrees(self.bearing_f):+.0f}° w={w:+.2f}'
            else:
                steer_info = 'marker_lost→straight'

        # 후진 속도(감속)
        remaining = self.distance - traveled
        frac = min(1.0, remaining/self.slowdown) if self.slowdown > 0 else 1.0
        mag = max(self.min_speed, self.speed*frac)
        self.send(-mag, w)
        self.get_logger().info(f'{traveled*100:.1f}/{self.distance*100:.0f}cm  {steer_info}',
                               throttle_duration_sec=0.25)


def main():
    rclpy.init()
    node = PiReverse()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        try:
            for _ in range(10):
                node.send(0.0)
        except Exception:
            pass
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
