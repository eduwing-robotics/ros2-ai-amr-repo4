#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export ROS_DOMAIN_ID="${ROS_DOMAIN_ID:-4}"
export GLOBALCAM_PYTHON="${GLOBALCAM_PYTHON:-$ROOT_DIR/.venv/bin/python}"

if ! command -v ros2 >/dev/null 2>&1; then
  echo "ROS 2 환경을 먼저 source한 후 실행하십시오." >&2
  exit 1
fi

if [ ! -x "$GLOBALCAM_PYTHON" ]; then
  GLOBALCAM_PYTHON="python3"
  export GLOBALCAM_PYTHON
fi

exec ros2 launch "$ROOT_DIR/launch/globalcam_object_map.launch.py" \
  safety_imgsz:=1280 \
  turtlebot_imgsz:=1280 \
  "$@"
