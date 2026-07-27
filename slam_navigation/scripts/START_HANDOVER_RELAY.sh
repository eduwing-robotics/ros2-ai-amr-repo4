#!/usr/bin/env bash
# =============================================================================
# START_HANDOVER_RELAY.sh — 교대 릴레이 싱글턴 기동 (2026-07-16 박제)
#
# ★왜 별도 스크립트인가:
#   handover_relay.py 는 한 프로세스가 도메인 97(로봇1)·88(로봇2) 두 컨텍스트를 직접 만든다.
#   즉 "로봇당 1개"가 아니라 "PC당 1개"다. 그래서 CLEAN_START.sh(로봇1)/CLEAN_START_COMMON.sh(로봇2)
#   양쪽에 그냥 박으면 릴레이가 2개 떠서 같은 교대요청을 이중 처리(=이중 순찰 위험).
#   → 두 런처가 이 스크립트를 호출하고, 여기서 PID 락파일로 "항상 정확히 1개"를 강제한다.
#
# ★킬 방식: pkill -f '패턴' 금지(자기매칭 3회 재발 전과). PID + /proc cmdline 검증 후에만 kill.
#
# 사용법: bash ~/team_ws/START_HANDOVER_RELAY.sh
#   (런처가 자동 호출하므로 보통 직접 칠 일은 없음. 릴레이만 살릴 때 수동 사용)
# =============================================================================
set -u

RELAY_PY="$HOME/team_ws/handover_relay.py"
LOCKF="/tmp/handover_relay_${USER}.pid"
LOG="$HOME/team_ws/run_logs"; mkdir -p "$LOG"
RELAY_LOG="$LOG/handover_relay.log"

# ---- 환경: 릴레이는 rclpy.init(domain_id=..)로 도메인을 직접 지정하므로 ROS_DOMAIN_ID는 무관.
#      단 RMW/유니캐스트 설정과 workspace(teamproject_interfaces)는 반드시 필요.
export RMW_IMPLEMENTATION=rmw_cyclonedds_cpp
export CYCLONEDDS_URI="file://$HOME/cyclonedds_unicast.xml"
# ★bashrc 오염 방어(7/8 실증): RANGE=OFF면 PC 로컬노드 discovery 사망 → 릴레이가 아무것도 못 받음
unset ROS_AUTOMATIC_DISCOVERY_RANGE ROS_STATIC_PEERS
# ★set -u 해제 구간: ROS setup.bash(ament)가 미정의 변수를 참조해 set -u면 즉사한다.
set +u
source /opt/ros/jazzy/setup.bash
source "$HOME/team_ws/install/setup.bash"
set -u

# ---- [1] 기존 릴레이 종료 (PID 락파일 + cmdline 검증 — PID 재사용/자기매칭 방어)
if [ -f "$LOCKF" ]; then
  OLD="$(cat "$LOCKF" 2>/dev/null || true)"
  if [ -n "${OLD:-}" ] && kill -0 "$OLD" 2>/dev/null \
     && tr '\0' ' ' < "/proc/$OLD/cmdline" 2>/dev/null | grep -q 'handover_relay'; then
    echo "   기존 교대 릴레이(PID $OLD) 종료 → 재기동"
    kill -9 "$OLD" 2>/dev/null || true
    sleep 1
  fi
fi
# 락파일이 유실된 고아 릴레이도 회수(락파일 방식만으론 못 잡는 케이스)
for pid in $(pgrep -f 'python3?.*handover_relay\.py' 2>/dev/null || true); do
  [ "$pid" = "$$" ] && continue
  kill -9 "$pid" 2>/dev/null || true
done
sleep 1

# ---- [2] 기동
if [ ! -f "$RELAY_PY" ]; then
  echo "   ⚠️ $RELAY_PY 없음 — 교대 릴레이 생략"; exit 1
fi
setsid python3 "$RELAY_PY" > "$RELAY_LOG" 2>&1 < /dev/null &
NEW=$!
echo "$NEW" > "$LOCKF"

# ---- [3] 게이트: 실제로 살아서 가동 로그를 뱉었는지 확인 (뜬 척 방지)
t=0
printf "   ⏳ 교대 릴레이 가동 "
while [ "$t" -lt 20 ]; do
  if grep -q '교대 릴레이 가동' "$RELAY_LOG" 2>/dev/null; then
    printf " ✅(%ss, PID %s)\n" "$t" "$NEW"
    echo "      robot1(97) ↔ robot2(88) 자동 교대 — 로그: $RELAY_LOG"
    exit 0
  fi
  if ! kill -0 "$NEW" 2>/dev/null; then
    printf " ❌ 즉사\n"; echo "   --- 릴레이 로그 마지막 15줄 ---"
    tail -15 "$RELAY_LOG" 2>/dev/null; exit 1
  fi
  sleep 2; t=$((t+2)); printf "."
done
printf " ⚠️타임아웃 — 프로세스는 살아있음(PID %s), 로그 확인 필요: %s\n" "$NEW" "$RELAY_LOG"
exit 0
