#!/bin/bash
# 순찰 경로 스왑 (pre-dock 통합본끼리) + 재빌드
# 사용:  bash ~/team_ws/swap_waypoints.sh test14   # 검증된 WP_test14+predock (dense 뱅글이 재발 시 비상)
#        bash ~/team_ws/swap_waypoints.sh dense     # 현재 dense 29개+predock (되돌리기)
# ⚠️ 스왑 후 patrol_commander 재시작해야 반영됨:
#      robot_nodes 터미널 Ctrl+C → ros2 launch teamproject_navigation robot_nodes.launch.py
set -e
CFG=~/team_ws/src/teamproject_navigation/config
cd "$CFG"
case "$1" in
  test14)
    cp waypoints.yaml "waypoints.yaml.SWAPOUT_$(date +%H%M 2>/dev/null || echo bak)" 2>/dev/null || cp waypoints.yaml waypoints.yaml.SWAPOUT
    cp waypoints_test14_predock.yaml waypoints.yaml
    echo "→ WP_test14+predock 적용(7개, 4바퀴 검증 경로)";;
  dense)
    cp waypoints_dense_predock_MASTER.yaml waypoints.yaml
    echo "→ dense+predock 적용(29개)";;
  *) echo "사용법: bash swap_waypoints.sh [test14|dense]"; exit 1;;
esac
python3 -c "import yaml; yaml.safe_load(open('waypoints.yaml')); print('YAML OK')"
cd ~/team_ws && source /opt/ros/jazzy/setup.bash && colcon build --packages-select teamproject_navigation
echo "✅ 빌드 완료. patrol_commander(robot_nodes) 재시작 필요!"
