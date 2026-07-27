#!/usr/bin/env python3
"""★교대 릴레이(7/15, 사용자 확정: "한 대가 돌고 와서 도킹 끝나면 다른 한 대가 교대 출발").

  로봇이 랩+도킹을 마치면 patrol이 handover_request 를 발행(use_lap_swap, 매 랩 무조건).
  이 릴레이가 그걸 받아 상대 로봇이 IDLE(도크 대기)이면 PATROL_START 를 쏜다 — 무한 교대.
    /robot1/handover_request(97) → robot2(88) 투입  /  /robot2/handover_request(88) → robot1(97) 투입
  (서버가 구독을 아직 안 만들어서 PC가 다리 역할. 서버가 구현하면 이 노드를 내리면 됨)

  - max_laps=1 유지 전제(1랩=1교대). 저배터리 복귀 로봇은 CHARGING이라 IDLE 검사에서 자동 제외
    → 상대가 계속 돌고, 충전 완료(IDLE 전환) 후 다음 교대부터 복귀.
  - 중복 방어: 같은 로봇의 교대요청은 120s 내 1회만 처리. 상대가 IDLE 아니면 스킵(이중순찰 방지).
"""
import threading, time
import rclpy
from rclpy.context import Context
from rclpy.node import Node
from rclpy.executors import SingleThreadedExecutor
from std_msgs.msg import Int32, String
from rcl_interfaces.srv import SetParameters
from rcl_interfaces.msg import Parameter, ParameterValue, ParameterType
from teamproject_interfaces.srv import SetMode

SIDES = [
    dict(domain=97, robot=1),
    dict(domain=88, robot=2),
]
DEDUP_SEC = 120.0


class Side:
    """한 도메인 안의 손발: 자기 로봇의 handover_request 구독 + 자기 로봇 set_mode 클라이언트."""

    def __init__(self, domain, robot):
        self.domain, self.robot = domain, robot
        self.ctx = Context()
        rclpy.init(context=self.ctx, domain_id=domain)
        self.node = Node(f'handover_relay_{robot}', context=self.ctx)
        self.state = ''
        self.last_req_t = 0.0
        self.pending_dispatch = False   # ★7/15: 교대가 스킵됨(내가 CHARGING 등) — IDLE 되면 재시도
        self.peer = None   # 반대편 Side (메인에서 연결)
        self.node.create_subscription(Int32, f'/robot{robot}/handover_request', self._on_req, 10)
        self.node.create_subscription(String, f'/robot{robot}/state', self._on_state, 10)
        self.mode_cli = self.node.create_client(SetMode, f'/robot{robot}/set_mode')
        self.param_cli = self.node.create_client(
            SetParameters, f'/robot{robot}/patrol_commander/set_parameters')
        self.exec = SingleThreadedExecutor(context=self.ctx)
        self.exec.add_node(self.node)
        threading.Thread(target=self.exec.spin, daemon=True).start()

    def _on_state(self, msg):
        prev = self.state
        self.state = (msg.data or '').split('|')[0]
        # ★7/15: 교대 스킵분 재시도 — 내가 IDLE로 전환(충전완료/리셋)되고 밀린 교대가 있으면 출발
        if self.pending_dispatch and self.state == 'IDLE' and prev != 'IDLE':
            self.pending_dispatch = False
            self.node.get_logger().warn(
                f'★밀린 교대 재시도: robot{self.robot} IDLE 전환(충전완료 추정) → PATROL_START')
            self.send_patrol_start()

    def _on_req(self, msg):
        now = time.time()
        if now - self.last_req_t < DEDUP_SEC:
            return
        self.last_req_t = now
        p = self.peer
        self.node.get_logger().warn(
            f'★교대요청 수신: robot{self.robot}(배터리 임계 도킹, wp{msg.data}) → '
            f'robot{p.robot} 투입 시도 (상대 상태: {p.state or "미수신"})')
        if p.state != 'IDLE':
            p.pending_dispatch = True   # ★7/15: 상대가 IDLE 되는 순간 재시도(충전완료 자동 복귀)
            self.node.get_logger().error(
                f'교대 스킵: robot{p.robot} 상태={p.state or "미수신"} (IDLE 아님 — 이중순찰 방지, '
                f'IDLE 전환 시 자동 재시도 예약)')
            return
        p.send_patrol_start()

    def send_patrol_start(self):
        if not self.mode_cli.wait_for_service(timeout_sec=5.0):
            self.node.get_logger().error(f'robot{self.robot} set_mode 서비스 없음 — 교대 실패')
            return
        fut = self.mode_cli.call_async(SetMode.Request(mode='PATROL_START'))
        def done(f):
            try:
                r = f.result()
                self.node.get_logger().warn(f'★교대 투입: robot{self.robot} PATROL_START → {r.message}')
            except Exception as e:
                self.node.get_logger().error(f'교대 투입 실패: {e}')
        fut.add_done_callback(done)

    def set_unlimited_laps(self):
        if not self.param_cli.wait_for_service(timeout_sec=8.0):
            self.node.get_logger().warn(f'robot{self.robot} param 서비스 없음 — max_laps 주입 생략')
            return
        pv = ParameterValue(type=ParameterType.PARAMETER_INTEGER, integer_value=0)
        req = SetParameters.Request(parameters=[Parameter(name='max_laps', value=pv)])
        fut = self.param_cli.call_async(req)
        def done(f):
            try:
                ok = f.result().results[0].successful
                self.node.get_logger().info(f'robot{self.robot} max_laps=0(무제한) 주입: {ok}')
            except Exception as e:
                self.node.get_logger().warn(f'robot{self.robot} max_laps 주입 실패: {e}')
        fut.add_done_callback(done)


def main():
    sides = [Side(**s) for s in SIDES]
    sides[0].peer = sides[1]
    sides[1].peer = sides[0]
    sides[0].node.get_logger().info(
        '★교대 릴레이 가동: robot1(97)↔robot2(88) — 랩+도킹 완료마다 상대 자동 투입(1랩=1교대)')
    while True:
        time.sleep(3600)


if __name__ == '__main__':
    main()
