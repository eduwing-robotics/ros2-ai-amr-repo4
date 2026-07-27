#!/usr/bin/env bash
# =============================================================================
# CLEAN_START_ROBOT.sh — 로봇ID 지정 기동 (멀티 로봇 통합)
# 사용: bash ~/team_ws/CLEAN_START_ROBOT.sh <robot_id>
#
# 로봇1: CLEAN_START_ROBOT.sh 1
# 로봇2: CLEAN_START_ROBOT.sh 2
# 로봇3: CLEAN_START_ROBOT.sh 3
# =============================================================================

ROBOT_ID="${1:-1}"

# 로봇 설정 로드
source "$HOME/team_ws/ROBOT_CONFIG.sh" "$ROBOT_ID" || exit 1

# ★바뀌는 부분만 재설정
PI="$ROBOT_PI"
ROBOT_IP="$ROBOT_IP"
LOG="$HOME/team_ws/run_logs"; mkdir -p "$LOG"
S(){ ssh -o ConnectTimeout=10 -o ServerAliveInterval=4 "$PI" "$@"; }

# ---- 게이트 헬퍼 ----
gate(){
  local desc="$1" tmax="$2"; shift 2
  [ "$1" = "--" ] && shift
  local t=0
  printf "   ⏳ %s " "$desc"
  while [ "$t" -lt "$tmax" ]; do
    if "$@" >/dev/null 2>&1; then printf " ✅(%ss)\n" "$t"; return 0; fi
    sleep 3; t=$((t+3)); printf "."
  done
  printf " ⚠️타임아웃(%ss) — 계속 진행(수동확인 필요)\n" "$tmax"; return 1
}
chk_scan(){   timeout 5 ros2 topic hz /scan 2>&1 | grep -q "average rate"; }
chk_nav_up(){ ros2 lifecycle get /planner_server 2>&1 | grep -q "active"; }
chk_node(){ ros2 node list 2>&1 | grep -q "$1"; }
chk_topic(){ timeout 5 ros2 topic hz "$1" 2>&1 | grep -q "average rate"; }

echo "🤖 로봇$ROBOT_ID 기동 시작 (도메인$ROS_DOMAIN_ID, Pi $ROBOT_IP, 카메라=$ROBOT_CAM_ENABLED)"
echo ""

# --- 기동 순서는 기존과 동일 (01~06) ---
echo "===[0] ★전체 강제종료 (잔존 노드 제거) ==="
pkill -9 -f 'patrol_commander|status_reporter|component_container|controller_server|planner_server' 2>/dev/null
pkill -9 -f 'nav2_amcl|map_server|filter_mask_server|costmap_filter_info|lifecycle_manager' 2>/dev/null
pkill -9 -f 'udp_camera_bridge|aruco_dock_detector|dock_detector_manager|safety_dispatch_fusion' 2>/dev/null
pkill -9 -f 'rviz2|rqt_image_view' 2>/dev/null
S "pkill -9 -f '[r]obot_bringup|[p]i_dock_executor|[t]urtlebot_udp_camera|[t]urtlebot3_node'" 2>/dev/null
pkill -9 -f '_ros2_daemon' 2>/dev/null
sleep 2
echo "✅ 정소 완료"
echo ""

echo "===[1] ★로봇 코어(카메라 X) ==="
S "export ROS_DOMAIN_ID=$ROS_DOMAIN_ID; cd ~/turtlebot3_ws && source install/setup.bash && ros2 launch teamproject_robot_bringup robot_bringup.launch.py use_camera:=false > ~/bringup.log 2>&1 < /dev/null &"
sleep 2
gate "로봇 코어(scan+odom+ping)" 30 -- chk_scan
echo ""

echo "===[2] ★Nav2 ==="
setsid ros2 launch turtlebot3_navigation2 navigation2.launch.py use_sim_time:=false map:=$HOME/team_ws/maps/factory_map_0621.yaml params_file:=$HOME/nav_params/burger_rpp.yaml > "$LOG/nav2.log" 2>&1 < /dev/null &
gate "Nav2 플래너/BT 활성화" 60 -- chk_nav_up
echo ""

echo "===[3] ★KeepoutFilter ==="
setsid ros2 launch teamproject_navigation keepout_filter.launch.py > "$LOG/keepout.log" 2>&1 < /dev/null &
gate "KeepoutFilter 마스크 서버" 30 -- "chk_node filter_mask_server"
echo ""

echo "===[4] ★순찰 + 융합 + 브릿지 + 검출 ==="
setsid ros2 launch teamproject_navigation robot_nodes.launch.py robot_id:=${ROBOT_ID} > "$LOG/patrol.log" 2>&1 < /dev/null &
gate "patrol_commander" 30 -- "chk_node patrol_commander"
setsid python3 $HOME/team_ws/aruco_docking/udp_camera_bridge.py    > "$LOG/bridge.log"   2>&1 < /dev/null &
gate "udp_camera_bridge" 15 -- "chk_node udp_camera_bridge"
setsid python3 $HOME/team_ws/aruco_docking/aruco_dock_detector.py  > "$LOG/detector.log" 2>&1 < /dev/null &
setsid python3 $HOME/team_ws/aruco_docking/safety_dispatch_fusion.py --ros-args -p consecutive_frames:=1 > "$LOG/fusion.log" 2>&1 < /dev/null &
gate "safety_dispatch_fusion" 15 -- "chk_node safety_dispatch_fusion"
echo ""

echo "===[5] ★카메라 (로봇1만 ON, 로봇2/3는 서버YOLO) ==="
if [ "$ROBOT_CAM_ENABLED" = "1" ]; then
  echo "   (로봇$ROBOT_ID 카메라 ON — fps10/q50, UDP→192.168.40.7:5007)"
  S "pkill -9 -f '[t]urtlebot_udp_camera_sender' 2>/dev/null; sleep 1; \
     export ROS_DOMAIN_ID=$ROS_DOMAIN_ID RMW_IMPLEMENTATION=rmw_cyclonedds_cpp CYCLONEDDS_URI=file:///home/codelab/cyclonedds_unicast.xml; \
     source /opt/ros/jazzy/setup.bash; source ~/turtlebot3_ws/install/setup.bash; \
     python3 ~/turtlebot_udp_camera_sender.py --backend gstreamer-libcamera --host 192.168.40.7 --port 5007 \
       --width 640 --height 480 --fps 10 --jpeg-quality 35 --extra-host 192.168.40.5 --chunk-size 60000 \
       > ~/cam_sender.log 2>&1 &"
  gate "카메라 이미지 수신" 20 -- "timeout 5 ros2 topic hz /robot1/camera/image_raw/compressed 2>&1 | grep -q 'average rate'"
else
  echo "   (로봇$ROBOT_ID 카메라 OFF — 서버 YOLO만 사용, 네트워크 부하 절감)"
fi
echo ""

echo "===[6] RViz + 카메라뷰 (GUI 세션에서만) ==="
RVIZ_CFG="$HOME/turtlebot3_ws/install/turtlebot3_navigation2/share/turtlebot3_navigation2/rviz/tb3_navigation2.rviz"
if [ -n "$DISPLAY" ]; then
  export DISPLAY=:1 XAUTHORITY=/run/user/1000/gdm/Xauthority
  setsid rviz2 -d "$RVIZ_CFG" > "$LOG/rviz.log" 2>&1 < /dev/null &
  sleep 3
  echo "✅ RViz 기동 (로봇$ROBOT_ID)"
else
  echo "⚠️ DISPLAY 없음 (headless) — RViz 생략"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ 로봇$ROBOT_ID 기동 완료!"
echo "   도메인=$ROS_DOMAIN_ID, Pi=$ROBOT_IP"
echo "   확인: ros2 topic delay /scan"
echo "   정리: kill_all_robots"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
