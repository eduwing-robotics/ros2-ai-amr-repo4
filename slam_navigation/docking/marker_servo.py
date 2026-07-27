#!/usr/bin/env python3
"""
marker_servo.py — 3D 마커 단계적 비주얼서보 (공용 모듈, 2026-07-04 설계 C 구현)

목표: 마커 full 6DOF(법선)를 써서 "마커 면에 수직인 정면, standoff 거리" 지점에
로봇을 정밀 정렬. AMCL 독립(모든 계산이 로봇 base 프레임 상대좌표).

★단계 분리 (동시서보 발산 방지 — 후진서보 사고 교훈):
    TURN_P  : 접근점 P(마커중심 + 법선*standoff)를 향해 제자리 회전
    DRIVE_P : P까지 저속 전진(헤딩 미세보정만)
    FACE    : 마커 중심을 정면으로 제자리 회전
    CREEP   : (선택) 정면 유지하며 stop_dist까지 미세 전진
    DONE / FAIL

사용(틱 방식): 매 제어주기(예: 20Hz)에
    v, w = servo.tick(now_sec, marker)      # marker=None이면 미검출 틱
    servo.state 로 진행상태 확인 (DONE/FAIL 이면 종료)

marker 입력 = MarkerObs(mx, my, nx, ny):
    (mx,my) = 마커 중심의 로봇 base 프레임 위치 [m]
    (nx,ny) = 마커 면 법선(마커→로봇 방향)의 base 프레임 XY (정규화 불필요)
    법선의 부호가 반대로 들어와도 내부에서 로봇쪽으로 자동 플립.

법선 노이즈 대책: EMA 필터(alpha) + 근거리에서만 사용 전제 + 단계이동(연속서보 아님).
"""
import math
from dataclasses import dataclass, field


# ── 단계 상수 ──
TURN_P, DRIVE_P, FACE, CREEP = 'TURN_P', 'DRIVE_P', 'FACE', 'CREEP'
DONE, FAIL = 'DONE', 'FAIL'


@dataclass
class MarkerObs:
    mx: float   # 마커 중심 x (base)
    my: float   # 마커 중심 y (base)
    nx: float   # 마커 법선 x (base, 마커→로봇 방향이 이상적)
    ny: float   # 마커 법선 y (base)


@dataclass
class ServoParams:
    standoff: float = 0.45        # 접근점 = 마커중심 + 법선*standoff [m]
    stop_dist: float = 0.30      # CREEP 최종 목표거리 [m] (standoff와 같으면 CREEP 생략)
    use_creep: bool = True
    # 속도/게인 (검증된 pi_dock ALIGN 계열 저속 값)
    w_max: float = 0.12
    k_w: float = 0.6
    v_max: float = 0.06
    v_creep: float = 0.04
    k_v: float = 0.8
    # 허용오차
    turn_tol: float = math.radians(3.0)     # TURN_P/FACE 회전 완료각
    pos_tol: float = 0.03                    # DRIVE_P 도달 판정 [m]
    range_tol: float = 0.02                  # CREEP 거리 판정 [m]
    ok_count: int = 3                        # 연속 N틱 만족 시 단계전환(순간노이즈 방지)
    # 노이즈/안전
    lpf_alpha: float = 0.35                  # 마커관측 EMA (pi_dock 검증 0.3~0.5 대역)
    marker_timeout: float = 1.5             # [s] 이보다 오래 미검출 → 정지 대기
    lost_fail: float = 6.0                   # [s] 이보다 오래 미검출 → FAIL
    stage_timeout: float = 25.0             # [s] 단계별 제한
    normal_min_xy: float = 0.5              # 법선 XY성분 크기 미달(마커가 위/아래 봄) → 법선 무시(FACE-only)
    deadband_w: float = 0.02
    deadband_v: float = 0.005


class StagedServo:
    """틱 기반 단계서보. rclpy 비의존(테스트 쉬움) — 호출측이 시각/마커/명령발행 담당."""

    def __init__(self, p: ServoParams = None, log=None, point_check=None):
        """point_check(base_x, base_y)->bool: 이동 목표점 허용 여부(금지존/코스트맵 가드).
        None이면 무조건 허용. False면 이동 생략(FACE 직행) — 서보는 cmd_vel 직발행이라
        Nav2 코스트맵을 안 보므로 호출측이 이 훅으로 금지존을 막아야 함."""
        self.p = p or ServoParams()
        self.log = log or (lambda s: None)
        self.point_check = point_check
        self.reset()

    def reset(self):
        self.state = TURN_P
        self._t_stage = None
        self._t_marker = None
        self._m = None          # (mx,my) EMA
        self._n = None          # (nx,ny) EMA (플립정규화 후)
        self._ok = 0
        self._normal_bad = False
        self.fail_reason = ''

    # ── 내부 ──
    def _ema(self, old, new, a):
        return new if old is None else (a * new[0] + (1 - a) * old[0],
                                        a * new[1] + (1 - a) * old[1])

    def _update_obs(self, now, obs: MarkerObs):
        # 법선을 '마커→로봇' 방향으로 플립 정규화 (로봇=base 원점이므로 마커→로봇 = -m)
        nx, ny = obs.nx, obs.ny
        nrm = math.hypot(nx, ny)
        if nrm < self.p.normal_min_xy:
            self._normal_bad = True          # 법선 못믿음 → B1식(로봇→마커 직선)으로 대체
        else:
            nx, ny = nx / nrm, ny / nrm
            if nx * (-obs.mx) + ny * (-obs.my) < 0.0:   # 로봇 반대편이면 플립
                nx, ny = -nx, -ny
            self._normal_bad = False
            self._n = self._ema(self._n, (nx, ny), self.p.lpf_alpha)
        self._m = self._ema(self._m, (obs.mx, obs.my), self.p.lpf_alpha)
        self._t_marker = now

    def _approach_point(self):
        mx, my = self._m
        d = math.hypot(mx, my)
        if d < 1e-6:
            return None
        # ★v2(발산픽스): 로봇이 이미 standoff보다 가까우면 P가 로봇 뒤로 떨어져
        #   ±180° 널뛰기 발산(7/4 도킹 실주행). → standoff_eff=max(standoff, 현재거리):
        #   가까울 땐 '같은 거리의 중심선 위 지점'으로 옆 호 이동(후진 불필요).
        so = min(self.p.standoff, d)   # ★min: 안쪽이면 현재거리(옆 호), 밖이면 standoff(접근)
        if self._normal_bad or self._n is None:
            # 법선 불가 → 로봇→마커 직선상 (B1 폴백. so=d면 P≈현위치→FACE 직행)
            return (mx - so * mx / d, my - so * my / d)
        nx, ny = self._n
        return (mx + so * nx, my + so * ny)

    def _fail(self, why):
        self.state = FAIL
        self.fail_reason = why
        self.log(f'서보 FAIL: {why}')
        return 0.0, 0.0

    def _goto(self, st):
        self.state = st
        self._t_stage = None
        self._ok = 0
        self.log(f'서보 단계 → {st}')

    # ── 메인 틱 ──
    def tick(self, now: float, obs: MarkerObs = None):
        """반환 (v, w). state가 DONE/FAIL이면 (0,0)."""
        p = self.p
        if self.state in (DONE, FAIL):
            return 0.0, 0.0
        if self._t_stage is None:
            self._t_stage = now
        if obs is not None:
            self._update_obs(now, obs)

        # 마커 신선도
        if self._t_marker is None or (now - self._t_marker) > p.marker_timeout:
            if self._t_marker is not None and (now - self._t_marker) > p.lost_fail:
                return self._fail('마커 장기소실')
            return 0.0, 0.0          # 잠깐 소실 → 정지 대기(발산 금지)
        # 단계 타임아웃
        if (now - self._t_stage) > p.stage_timeout:
            return self._fail(f'{self.state} 타임아웃')

        mx, my = self._m
        ap = self._approach_point()
        if ap is None:
            return self._fail('기하 퇴화(마커가 원점)')
        ax, ay = ap

        # ★금지존 가드: 접근점이 금지구역이면 이동 생략(FACE만 — 제자리 회전은 안전)
        if self.state in (TURN_P, DRIVE_P) and self.point_check is not None:
            if not self.point_check(ax, ay):
                self.log('접근점이 금지구역 — 이동 생략, FACE 직행')
                self._goto(FACE)
                return 0.0, 0.0

        if self.state == TURN_P:
            # ★v2 가드: P가 사실상 현위치(<8cm)면 방향이 노이즈 → 이동 생략, FACE 직행
            if math.hypot(ax, ay) < max(p.pos_tol, 0.08):
                self._goto(FACE)
                return 0.0, 0.0
            hdg = math.atan2(ay, ax)
            if abs(hdg) < p.turn_tol:
                self._ok += 1
                if self._ok >= p.ok_count:
                    self._goto(DRIVE_P)
                return 0.0, 0.0
            self._ok = 0
            w = max(-p.w_max, min(p.w_max, p.k_w * hdg))
            if abs(w) < p.deadband_w:
                w = math.copysign(p.deadband_w, w)
            return 0.0, w

        if self.state == DRIVE_P:
            d = math.hypot(ax, ay)
            hdg = math.atan2(ay, ax)
            if d < p.pos_tol:
                self._ok += 1
                if self._ok >= p.ok_count:
                    self._goto(FACE)
                return 0.0, 0.0
            self._ok = 0
            if abs(hdg) > math.radians(25.0):     # 크게 틀어짐 → 회전부터 다시
                self._goto(TURN_P)
                return 0.0, 0.0
            v = max(0.0, min(p.v_max, p.k_v * d))
            if v < p.deadband_v:
                v = p.deadband_v
            w = max(-p.w_max, min(p.w_max, p.k_w * hdg))
            return v, w

        if self.state == FACE:
            bear = math.atan2(my, mx)
            if abs(bear) < p.turn_tol:
                self._ok += 1
                if self._ok >= p.ok_count:
                    if p.use_creep and (p.standoff - p.stop_dist) > p.range_tol:
                        self._goto(CREEP)
                    else:
                        self.state = DONE
                        self.log('서보 DONE (정면정렬 완료)')
                return 0.0, 0.0
            self._ok = 0
            w = max(-p.w_max, min(p.w_max, p.k_w * bear))
            if abs(w) < p.deadband_w:
                w = math.copysign(p.deadband_w, w)
            return 0.0, w

        if self.state == CREEP:
            rng = math.hypot(mx, my)
            bear = math.atan2(my, mx)
            # ★금지존 가드: 전진 앞구간이 금지구역이면 현위치서 종료(정면은 이미 잡힘)
            if self.point_check is not None:
                ahead = min(max(rng - self.p.stop_dist, 0.0), 0.12)
                if ahead > 0.01 and not self.point_check(ahead, 0.0):
                    self.state = DONE
                    self.log('CREEP 전방 금지구역 — 현위치 종료(DONE)')
                    return 0.0, 0.0
            if rng <= p.stop_dist + p.range_tol:
                self._ok += 1
                if self._ok >= p.ok_count:
                    self.state = DONE
                    self.log(f'서보 DONE (정면 {rng:.3f}m)')
                return 0.0, 0.0
            self._ok = 0
            w = max(-p.w_max, min(p.w_max, p.k_w * bear))
            return p.v_creep, w

        return 0.0, 0.0
