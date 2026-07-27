#!/usr/bin/env bash
# =============================================================================
# MASTER_CLEAN_START.sh — 모든 로봇 순차 기동 (네트워크 안정성 최우선)
# 사용: bash ~/team_ws/MASTER_CLEAN_START.sh
#
# ★네트워크 안정성 전략:
#   1. 로봇1 완전 안정화 (scan hz 체크) → 로봇2 → 로봇3
#   2. 각 로봇 사이 30초 대기 (WiFi 부하 분산)
#   3. scan 9Hz+ 확인 후 다음 로봇 기동
# =============================================================================

check_scan_hz() {
  local robot_id="$1" domain="$2"
  local hz
  if [ "$domain" = "97" ]; then
    hz=$(timeout 5 ros2 topic hz /scan 2>&1 | grep "average rate" | awk '{print $2}' | cut -d. -f1)
  else
    hz=$(timeout 5 bash -c "ROS_DOMAIN_ID=$domain ros2 topic hz /scan" 2>&1 | grep "average rate" | awk '{print $2}' | cut -d. -f1)
  fi
  [ -n "$hz" ] && [ "$hz" -ge 8 ]
}

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "🚀 마스터 CLEAN_START (네트워크 안정성 모드)"
echo "   각 로봇 사이 30초 대기 + scan hz 체크"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# 로봇1
echo "[ 1/3 ] 로봇1 기동 (도메인97, Pi .101)"
bash ~/team_ws/CLEAN_START_ROBOT.sh 1
echo ""
echo "   ⏳ 로봇1 scan 안정화 대기 (최대 30초)..."
for i in {1..10}; do
  if check_scan_hz 1 97; then
    echo "   ✅ 로봇1 scan 안정 (9Hz+)"
    break
  fi
  [ $i -lt 10 ] && sleep 3
done
echo ""

# 로봇2
echo "   ⏳ 다음 로봇 기동 전 30초 대기 (WiFi 부하 분산)..."
for i in {30..1}; do
  printf "\r   %2d초 대기 중..." $i
  sleep 1
done
echo ""
echo ""
echo "[ 2/3 ] 로봇2 기동 (도메인88, Pi .102)"
bash ~/team_ws/CLEAN_START_ROBOT.sh 2
echo ""
echo "   ⏳ 로봇2 scan 안정화 대기 (최대 30초)..."
for i in {1..10}; do
  if check_scan_hz 2 88; then
    echo "   ✅ 로봇2 scan 안정 (9Hz+)"
    break
  fi
  [ $i -lt 10 ] && sleep 3
done
echo ""

# 로봇3
echo "   ⏳ 다음 로봇 기동 전 30초 대기 (WiFi 부하 분산)..."
for i in {30..1}; do
  printf "\r   %2d초 대기 중..." $i
  sleep 1
done
echo ""
echo ""
echo "[ 3/3 ] 로봇3 기동 (도메인4, Pi .103)"
bash ~/team_ws/CLEAN_START_ROBOT.sh 3
echo ""
echo "   ⏳ 로봇3 scan 안정화 대기 (최대 30초)..."
for i in {1..10}; do
  if check_scan_hz 3 4; then
    echo "   ✅ 로봇3 scan 안정 (9Hz+)"
    break
  fi
  [ $i -lt 10 ] && sleep 3
done
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ 모든 로봇 기동 완료! (네트워크 안정성 확보)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "📊 상태 확인 (전부 9Hz+ 이상이어야 함):"
echo "   로봇1: ros2 topic hz /scan"
echo "   로봇2: ROS_DOMAIN_ID=88 ros2 topic hz /scan"
echo "   로봇3: ROS_DOMAIN_ID=4 ros2 topic hz /scan"
echo ""
echo "📌 주의사항:"
echo "   · scan이 불안정(< 8Hz)이면 기동 지연 후 재시도"
echo "   · 개별 로봇만 켜려면: clean_start 1 (또는 2, 3)"
echo "   · 정리할 땐: kill_all_robots"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
