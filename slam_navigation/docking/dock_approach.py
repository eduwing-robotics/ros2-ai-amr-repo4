#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
dock_approach.py — ArUco 마커 커스텀 P제어 도킹 접근 노드 (PC 실행)
====================================================================
제자리회전 없이 마커를 향해 부드러운 곡선 접근 → AMCL 우회 정밀 도킹.

입력: /detected_dock_pose (geometry_msgs/PoseStamped)  ← aruco_dock_detector 가 발행
      * 카메라 광학프레임 기준 (z=전방, x=우, y=하).
출력: /cmd_vel (geometry_msgs/TwistStamped)  ← enable_stamped_cmd_vel=true 라 Stamped.
상태: /dock/approach_state (std_msgs/String) — IDLE/APPROACHING/DOCKED/LOST

★ 프레임: 로봇 URDF에 카메라 링크가 없어 TF2 변환 불가 → 카메라가 전방 수평
  고정장착인 걸 이용해 광학→base 고정축 리맵을 직접 적용한다.
      x_base = z_opt(전방),  y_base = -x_opt(횡, 좌+)
  (나중에 URDF에 카메라 조인트를 넣으면 TF2 정석으로 승격 가능.)

★ 첫 테스트 제어법: 순수 추종(pursuit)
      bearing = atan2(y, x)              # 마커 방위각
      w = kp_bearing * bearing + kp_theta * marker_yaw
      v = kp_x * (x - x_stop),  |bearing| 크면 감속(정렬 우선 = 2단계-lite)
  마커-yaw 정렬항(kp_theta)은 부호 확인 전까지 0. 실기서 켜며 튜닝.
  pre-dock 을 마커 정면에 두면 pursuit 만으로 곡선접근 됨(AMCL 보호).

OpenCV 4.6 구 API 환경. active=False 로 시작 → FSM/사용자가 켜야 동작.
"""
import math
import numpy as np
import rclpy
from rclpy.node import Node
from geometry_msgs.msg import PoseStamped, TwistStamped
from std_msgs.msg import String

# 광학프레임 → base 고정 회전 (열 = 광학 기저벡터의 base 표현)
#   opt_x(우)→base(0,-1,0), opt_y(하)→base(0,0,-1), opt_z(전)→base(1,0,0)
R_BASE_OPT = np.array([
    [0.0,  0.0, 1.0],
    [-1.0, 0.0, 0.0],
    [0.0, -1.0, 0.0],
])


def quat_to_rotmat(q):
    x, y, z, w = q.x, q.y, q.z, q.w
    return np.array([
        [1 - 2*(y*y + z*z),     2*(x*y - z*w),     2*(x*z + y*w)],
        [2*(x*y + z*w),     1 - 2*(x*x + z*z),     2*(y*z - x*w)],
        [2*(x*z - y*w),         2*(y*z + x*w), 1 - 2*(x*x + y*y)],
    ])


class DockApproach(Node):
    def __init__(self):
        super().__init__('dock_approach')
        # --- 게인/한계 (실기 튜닝) ---
        self.declare_parameter('kp_bearing', 1.2)   # 마커 방위각 → 조향(주 조향항)
        self.declare_parameter('kp_theta', 0.0)     # 마커 상대yaw → 정렬(부호확인 전 0)
        self.declare_parameter('kp_x', 0.4)         # 전방거리 → 전진속도
        self.declare_parameter('v_max', 0.10)       # Burger는 0.08~0.10 권장
        self.declare_parameter('v_min', 0.03)
        self.declare_parameter('w_max', 0.5)
        self.declare_parameter('x_stop', 0.18)      # 카메라-마커 이 거리면 도킹완료(접촉 standoff)
        self.declare_parameter('align_gate', 0.35)  # |bearing|>이 값(rad)이면 전진 감속(정렬 우선)
        self.declare_parameter('marker_timeout', 1.0)
        self.declare_parameter('lpf_alpha', 0.3)    # pose 저역통과(0~1, 낮을수록 부드럽)
        self.declare_parameter('active', False)     # pre-dock 도착 시 True

        self.filt = None            # 필터된 (x, y, yaw)  [base 기준]
        self.last_seen = None
        self.state = 'IDLE'

        self.create_subscription(PoseStamped, '/detected_dock_pose', self.on_marker, 10)
        self.cmd_pub = self.create_publisher(TwistStamped, '/cmd_vel', 10)
        self.state_pub = self.create_publisher(String, '/dock/approach_state', 10)
        self.create_timer(0.05, self.control_loop)   # 20Hz
        self.get_logger().info('dock_approach 시작 (active=False, 켜야 동작)')

    def on_marker(self, msg):
        # 광학프레임 pose → base 리맵
        p_opt = np.array([msg.pose.position.x,
                          msg.pose.position.y,
                          msg.pose.position.z])
        p_base = R_BASE_OPT @ p_opt
        x, y = float(p_base[0]), float(p_base[1])   # 전방, 횡(좌+)

        # 마커 yaw(base 지면축 기준) — 정렬항 튜닝용
        R_marker = R_BASE_OPT @ quat_to_rotmat(msg.pose.orientation)
        yaw = math.atan2(R_marker[1, 0], R_marker[0, 0])

        a = self.get_parameter('lpf_alpha').value
        if self.filt is None:
            self.filt = (x, y, yaw)
        else:
            px, py, pyaw = self.filt
            # yaw 는 wrap 고려해 각도 보간
            dyaw = math.atan2(math.sin(yaw - pyaw), math.cos(yaw - pyaw))
            self.filt = (a*x + (1-a)*px, a*y + (1-a)*py, pyaw + a*dyaw)
        self.last_seen = self.get_clock().now()

    def stop(self):
        self.cmd_pub.publish(TwistStamped())

    def set_state(self, s):
        if s != self.state:
            self.state = s
            self.state_pub.publish(String(data=s))
            self.get_logger().info(f'dock state → {s}')

    def control_loop(self):
        if not self.get_parameter('active').value:
            self.set_state('IDLE')
            return
        now = self.get_clock().now()
        if (self.last_seen is None or
                (now - self.last_seen).nanoseconds/1e9 >
                self.get_parameter('marker_timeout').value):
            self.set_state('LOST'); self.stop(); return

        x, y, yaw = self.filt
        # 도킹 완료 판정 (전방거리 기준)
        if x <= self.get_parameter('x_stop').value:
            self.set_state('DOCKED'); self.stop(); return
        self.set_state('APPROACHING')

        bearing = math.atan2(y, x)
        kp_b = self.get_parameter('kp_bearing').value
        kp_th = self.get_parameter('kp_theta').value
        kp_x = self.get_parameter('kp_x').value
        w = kp_b * bearing + kp_th * yaw

        # 전진속도: 거리비례 + 정렬 게이트(방위 크면 감속해 먼저 정렬 = 2단계-lite)
        x_stop = self.get_parameter('x_stop').value
        v = kp_x * (x - x_stop)
        gate = self.get_parameter('align_gate').value
        if gate > 0:
            v *= max(0.0, 1.0 - abs(bearing) / gate) if abs(bearing) < gate else 0.0

        # 클램프
        w_max = self.get_parameter('w_max').value
        v_max = self.get_parameter('v_max').value
        v_min = self.get_parameter('v_min').value
        w = max(-w_max, min(w_max, w))
        if v > 0:
            v = max(v_min, min(v_max, v))
        else:
            v = 0.0

        cmd = TwistStamped()
        cmd.header.stamp = now.to_msg()
        cmd.twist.linear.x = float(v)
        cmd.twist.angular.z = float(w)
        self.cmd_pub.publish(cmd)
        self.get_logger().info(
            f'x={x:.2f} y={y:.2f} yaw={math.degrees(yaw):.0f}° '
            f'bear={math.degrees(bearing):.0f}° → v={v:.3f} w={w:.2f}',
            throttle_duration_sec=0.5)


def main():
    rclpy.init()
    node = DockApproach()
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
