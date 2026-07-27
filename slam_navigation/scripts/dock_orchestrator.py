#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
dock_orchestrator.py  —  "도킹하자" 자동 도킹 오케스트레이터 (PC)  [v2: 위치 조정 존 추가]

흐름:  pre-dock(wp28=0.40,0) 도착 감지
       → ① 순찰 정지(set_mode MANUAL_ENTER, goal 취소)
       → ② [조정 존] yaw 0°(마커 정면)로 제자리 정렬(/spin, odom기반)  ★오늘 -85° 틀어짐 방지
       → ③ 정렬·정지 확인 + (0,0)까지 실제거리 재계산
       → ④ Pi에서 pi_reverse.py 후진(직진, 하드캡) → 도킹

★v2 수정: (1) 콜백 안에서 spin 호출하던 크래시버그 제거 — 콜백은 감지만, 시퀀스는 메인루프에서 실행
          (2) 위치만 보고 발동하던 것 → 발동 후 '조정 존'에서 yaw 정렬까지 하고 후진

실행:  cd ~/team_ws && source install/setup.bash
       python3 dock_orchestrator.py            # 실동작
       python3 dock_orchestrator.py --dry-run  # 감지·정렬각 계산만(명령 안 보냄)
       python3 dock_orchestrator.py --no-align # 조정존 생략(정렬 안 함) — 비권장
"""
import sys, math, time, subprocess, os, yaml
import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from teamproject_interfaces.msg import RobotStatus
from teamproject_interfaces.srv import SetMode
from nav2_msgs.action import Spin
from builtin_interfaces.msg import Duration as MsgDuration

ROBOT_SSH   = "codelab@192.168.40.101"
WP_YAML     = os.path.expanduser('~/team_ws/install/teamproject_navigation/share/teamproject_navigation/config/waypoints.yaml')
DOCK        = (0.0, 0.0)
ARRIVE_TOL  = 0.12          # pre-dock 근접 발동 반경
YAW_OK      = math.radians(12)   # 이 안이면 정렬 완료로 봄
SPEED       = 0.07
HARD_MARGIN = 0.03
REV_MIN, REV_MAX = 0.15, 0.50

def norm(a):   # [-pi,pi]
    return math.atan2(math.sin(a), math.cos(a))

class DockOrch(Node):
    def __init__(self, dry, align):
        super().__init__('dock_orchestrator')
        self.dry, self.align = dry, align
        self.latest = None
        self.should_fire = False
        self.done = False
        self.spin_since = None
        self.last_log = 0.0
        # ★pre-dock = waypoints.yaml의 마지막 wp (인덱스·좌표 자동) — wp개수 바뀌어도 안전
        wps = yaml.safe_load(open(WP_YAML))['patrol_waypoints']
        self.pre_dock_wp = len(wps) - 1
        self.pre_dock = (float(wps[-1]['x']), float(wps[-1]['y']))
        # 후진거리 = pre-dock → dock(0,0) 거리 (자동)
        self.distance = math.hypot(self.pre_dock[0]-DOCK[0], self.pre_dock[1]-DOCK[1])
        self.mode_cli = self.create_client(SetMode, '/robot1/set_mode')
        self.spin_cli = ActionClient(self, Spin, '/spin')
        self.create_subscription(RobotStatus, '/robot1/robot_status', self.cb, 10)
        print("="*60)
        print("  자동 도킹 오케스트레이터 v2 ARMED (위치 조정 존 포함)")
        print(f"  pre-dock(wp{self.pre_dock_wp},{self.pre_dock}) 대기 | 정렬={align} dry_run={dry}")
        print("="*60)

    # 콜백: 감지·모니터링만 (무거운 작업 금지 → 크래시버그 방지)
    def cb(self, m):
        self.latest = m
        now = time.time()
        d_pre = math.hypot(m.x - self.pre_dock[0], m.y - self.pre_dock[1])
        if now - self.last_log > 1.0:
            self.last_log = now
            print(f"[{m.status:15s}] tgt=wp{m.current_target_wp:<2d} pos=({m.x:+.2f},{m.y:+.2f}) "
                  f"predock={d_pre:.2f} yaw={math.degrees(m.yaw):+.0f}° v={m.linear_vel:+.2f} "
                  f"w={m.angular_vel:+.2f} bat={m.battery:.0f}%")
        if abs(m.angular_vel) > 0.3 and abs(m.linear_vel) < 0.03:
            self.spin_since = self.spin_since or now
            if now - self.spin_since > 6.0:
                print("  ⚠️ 제자리회전 6s+ — AMCL 드리프트 의심(검출노드/RViz Image OFF·scan 확인)")
                self.spin_since = now + 4.0
        else:
            self.spin_since = None
        if (not self.should_fire and not self.done
                and m.current_target_wp == self.pre_dock_wp and d_pre <= ARRIVE_TOL):
            self.should_fire = True   # 메인루프가 시퀀스 실행

    # ---- 아래는 전부 메인루프(콜백 밖)에서 호출 → spin 재진입 안전 ----
    def wait_future(self, fut, tmo=8.0):
        t = time.time()
        while rclpy.ok() and not fut.done() and time.time()-t < tmo:
            rclpy.spin_once(self, timeout_sec=0.1)
        return fut.result() if fut.done() else None

    def fresh(self, sec=1.5):   # sec초 동안 최신 robot_status 갱신
        t = time.time()
        while time.time()-t < sec:
            rclpy.spin_once(self, timeout_sec=0.1)
        return self.latest

    def call_mode(self, mode):
        if not self.mode_cli.wait_for_service(timeout_sec=5.0):
            print(f"  ❌ set_mode 서비스 없음"); return
        r = self.wait_future(self.mode_cli.call_async(SetMode.Request(mode=mode)), 6.0)
        print(f"     set_mode({mode}) → {getattr(r,'message','(무응답)')}")

    def do_spin(self, target_yaw):
        """현재 yaw에서 target_yaw(0)로 제자리 정렬. Spin은 상대회전(odom기반)."""
        m = self.fresh(0.6)
        rel = norm(target_yaw - m.yaw)
        print(f"  [조정존] 현재 yaw={math.degrees(m.yaw):+.0f}° → 목표 0° | 회전 {math.degrees(rel):+.0f}°")
        if abs(rel) < YAW_OK:
            print("  [조정존] 이미 정렬됨 — 회전 생략"); return True
        if self.dry:
            print("  [dry-run] spin 생략"); return True
        if not self.spin_cli.wait_for_server(timeout_sec=5.0):
            print("  ❌ /spin 액션서버 없음 — 정렬 실패"); return False
        g = Spin.Goal(); g.target_yaw = float(rel); g.time_allowance = MsgDuration(sec=20)
        sg = self.wait_future(self.spin_cli.send_goal_async(g), 6.0)
        if sg is None or not sg.accepted:
            print("  ❌ spin goal 거부"); return False
        self.wait_future(sg.get_result_async(), 25.0)
        m = self.fresh(1.2)
        err = abs(norm(0 - m.yaw))
        print(f"  [조정존] 정렬후 yaw={math.degrees(m.yaw):+.0f}° (오차 {math.degrees(err):.0f}°)")
        return err < math.radians(18)

    def run_sequence(self):
        self.done = True
        m = self.latest
        print("\n" + "★"*60)
        print(f"  ★ pre-dock 도착! pos=({m.x:.3f},{m.y:.3f})")
        print("★"*60)
        # ① 순찰 정지 (goal 취소 → 로봇 정지)
        print("  ① 순찰 정지: MANUAL_ENTER")
        if not self.dry: self.call_mode('MANUAL_ENTER')
        self.fresh(1.5)
        # ② Pi 로컬 자동도킹: SEARCH(회전하며 마커찾기)→ALIGN(마커정렬)→REVERSE(후진)
        #    정렬/후진 전부 마커+odom 기준(AMCL 안 씀). 검출노드(ON) 필요.
        cmd = (f"python3 ~/pi_dock.py --ros-args "
               f"-p distance:={self.distance:.3f} -p speed:={SPEED} "
               f"-p hard_margin:={HARD_MARGIN} -p search_speed:=0.20")
        print(f"  ② Pi 자동도킹(마커 search→align→reverse):\n     ssh {ROBOT_SSH} '{cmd}'")
        if self.dry:
            print("  [dry-run] 종료."); rclpy.shutdown(); return
        try:
            r = subprocess.run(["ssh","-o","ConnectTimeout=8",ROBOT_SSH,cmd],
                               timeout=120, capture_output=True, text=True)
            print("  --- pi_dock tail ---"); print((r.stdout or "")[-2000:])
            if r.stderr.strip(): print("  [stderr]", r.stderr[-400:])
        except Exception as e:
            print(f"  ❌ Pi 실행 실패: {e}\n     수동: ssh {ROBOT_SSH} '{cmd}'")
        print("\n  ✅ 도킹 시퀀스 종료 — 위치/충전단자 육안 확인 부탁.")
        rclpy.shutdown()

def main():
    dry = '--dry-run' in sys.argv
    align = '--no-align' not in sys.argv
    rclpy.init()
    node = DockOrch(dry, align)
    try:
        while rclpy.ok():
            rclpy.spin_once(node, timeout_sec=0.1)
            if node.should_fire and not node.done:
                node.run_sequence()
                break
    except KeyboardInterrupt:
        print("\n중단(Ctrl+C).")
    finally:
        if rclpy.ok(): rclpy.shutdown()

if __name__ == '__main__':
    main()
