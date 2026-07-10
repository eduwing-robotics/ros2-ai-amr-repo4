# Edge Face System

ROS2 기반 안면인식 출입 시스템 코드이다. RealSense RGB-D 입력, InsightFace 인식, PAD(depth liveness), 로컬 출입 UI/API, 결과 업로더, DroidCam/GlobalCam 보조 송신 스크립트를 포함한다.

## Git 포함/제외

이 폴더에는 실행 코드, 런처, systemd/logrotate 템플릿, 문서만 포함한다. 실제 운영 데이터는 커밋하지 않는다.

제외 대상:

- `data/`, `logs/`
- `registered_faces/`, `face_registry.json`
- `*.pt`, `*.onnx`, `.insightface/`
- 실제 `config/edge_local.yaml`

## 설정

```bash
cp config/edge_local.example.yaml config/edge_local.yaml
```

그 다음 `edge_local.yaml`에서 서버 URL, 장치 ID, 토큰, 카메라 장치, 로컬 데이터 경로를 현장에 맞게 수정한다.

## 기본 실행

```bash
./scripts/run_attendance_venv
```

ROS launch 직접 실행:

```bash
ros2 launch launch/attendance_local.launch.py config:=config/edge_local.yaml
```

## UDP 수신 버퍼 참고

GlobalCam/TurtleBot UDP 영상 수신 PC는 커널 수신 버퍼 상한이 낮으면 `UdpRcvbufErrors`가 증가할 수 있다. 수신 PC에서 아래 값 이상을 권장한다.

```bash
sudo sysctl -w net.core.rmem_max=8388608
sudo sysctl -w net.core.rmem_default=1048576
```

영구 적용은 `/etc/sysctl.d/99-robot-face-udp.conf`에 같은 값을 넣고 수신 런처를 재시작한다.

---

# Robot Face System

## Documents

- [Frontend-backend API contract draft](docs/frontend_backend_api_contract_draft.md)
- [Server and database architecture draft](docs/server_database_architecture_draft.md)

This project separates the robot camera sender from the local PC recognition node.

## Layout

```text
robot_side/
  camera_publisher.py        # Runs on the robot Raspberry Pi

pc_side/
  perception_node.py         # Runs on the local PC with /home/gyul/face-env
  ros_image_utils.py

scripts/
  robot_camera_publisher     # Robot-side launcher
  robot_face_perception      # PC-side launcher
```

## Runtime Flow

```text
Robot Raspberry Pi
  USB webcam or Pi camera
  -> /robot/camera/image_raw

Local PC
  /robot/camera/image_raw
  -> person detection
  -> quick face recognition
  -> detail recognition if unknown or ambiguous
  -> /face/recognition_state
```

The existing web UI is not connected to this robot flow. Later, both systems can read registered faces or embeddings from the same server DB.

## Robot Side

SSH into the robot and run:

```bash
/home/gyul/robot_face_system/scripts/robot_camera_publisher --camera 0 --width 640 --height 480 --fps 10
```

Default image topic:

```text
/robot/camera/image_raw
```

The robot launcher uses `python3` by default. If the robot needs a specific interpreter, run it with `ROBOT_FACE_PYTHON=/path/to/python`. For Pi Camera later, keep the same ROS2 topic and replace only the camera input implementation inside `robot_side/camera_publisher.py`.

## Local PC Side

Run the perception node on the local PC:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception
```

It publishes JSON state messages:

```text
/face/recognition_state
```

Example:

```json
{"state": "detail_recognition", "message": "등록자 확인 실패, 정밀 인식 중"}
```

Optional annotated image publishing:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception --publish-annotated
```

Annotated image topic:

```text
/face/annotated_image
```

## Topic Checks

```bash
ros2 topic list
ros2 topic hz /robot/camera/image_raw
ros2 topic echo /face/recognition_state
```

## Current Stack

- Person detection: YOLO11n by default on the local PC, OpenCV HOG fallback with `--person-detector hog`
- Face recognition: existing InsightFace code in `/home/gyul/face_recognize_webcam.py`
- Registered faces: `$HOME/registered_faces`
- ROS image conversion: manual NumPy conversion, no `cv_bridge` dependency

YOLO11n is the default person detector because it is much stronger than OpenCV HOG for a moving robot. It runs on the local PC, not on the robot. The current default uses CPU PyTorch to avoid CUDA library conflicts with InsightFace/ONNXRuntime; use `--person-device cuda:0` only after the local CUDA stack is validated.

## Final Robot Camera Path

For the final system, the robot should not run OpenCV-based image processing. Use a ROS2 camera driver on the robot and publish only the image topic:

```bash
ros2 run v4l2_camera v4l2_camera_node --ros-args \
  -p video_device:=/dev/video0 \
  -p image_size:="[640,480]" \
  -r /image_raw:=/robot/camera/image_raw
```

The local PC runs all person detection, face recognition, and detail recognition:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception
```

The PC perception node loads only InsightFace detection and recognition models for this robot flow. Landmark, gender, and age models are intentionally not loaded.

## Local Screen Viewer

To see the camera and recognition overlay on the local PC, run the perception node with annotated image publishing:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception --publish-annotated
```

In another local PC terminal, open the viewer:

```bash
/home/gyul/robot_face_system/scripts/robot_face_viewer
```

The viewer subscribes to:

```text
/face/annotated_image
```

To view the raw robot camera instead:

```bash
/home/gyul/robot_face_system/scripts/robot_face_viewer --topic /robot/camera/image_raw
```

Press `q` or `Esc` in the viewer window to close it.

## Person Detection

The perception node now uses YOLO by default:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception --publish-annotated
```

Useful options:

```bash
--person-detector yolo
--person-model yolo11n.pt
--person-device cpu
--person-confidence 0.35
```

Fallback to the old OpenCV HOG detector:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception --person-detector hog --publish-annotated
```


## Server/App Ready Event Flow

The current ROS-only flow still works, but recognition events now include stable fields for a future backend, database, and HTML/app UI:

```text
schema_version
event_id
device_id
source
created_at
timestamp
state
recognized
user
subject
best_similarity
pad
decision
```

A confirmed known event includes both the future-friendly `user` object and the existing `subject` object for compatibility:

```json
{
  "event_id": "evt_...",
  "device_id": "edge-dev-001",
  "timestamp": "2026-06-05T01:30:00+00:00",
  "state": "confirmed_known",
  "recognized": true,
  "user": {
    "name": "홍길동",
    "number": "001"
  },
  "best_similarity": 0.75,
  "pad": {
    "live": true,
    "score": 0.75,
    "reason": "depth_shape_pass",
    "distance_m": 0.75,
    "valid_depth_ratio": 0.98
  }
}
```

Recommended edge architecture:

```text
RealSense ROS node
  -> robot_face_perception
  -> /face/recognition_state
  -> robot_face_result_uploader edge agent
     - normalize event_id / timestamp / device_id
     - attach active auth session
     - upload to local or remote server
     - queue failed uploads
     - write local uploader log
  -> Backend API / Database / HTML app
```

Run RealSense:

```bash
source /opt/ros/jazzy/setup.bash
ros2 launch realsense2_camera rs_launch.py   enable_color:=true   enable_depth:=true   align_depth.enable:=true
```

Or run the full local attendance stack with one ROS launch file:

```bash
source /opt/ros/jazzy/setup.bash
ros2 launch /home/gyul/robot_face_system/launch/attendance_local.launch.py
```

The launch file reads defaults from:

```text
/home/gyul/robot_face_system/config/edge_local.yaml
```

Use a different config file when needed:

```bash
ros2 launch /home/gyul/robot_face_system/launch/attendance_local.launch.py \
  config:=/path/to/edge_local.yaml
```

This starts:

```text
RealSense D435
robot_face_perception
robot_face_annotated_snapshot
face_system_server.py
robot_face_result_uploader
```

The browser UI is available at:

```text
http://127.0.0.1:8090
```

Useful launch overrides:

```bash
ros2 launch /home/gyul/robot_face_system/launch/attendance_local.launch.py \
  device_id:=edge-dev-001 \
  image_topic:=/camera/camera/color/image_raw \
  depth_topic:=/camera/camera/aligned_depth_to_color/image_raw \
  server_host:=127.0.0.1 \
  server_port:=8090 \
  server_url:=http://127.0.0.1:8090
```

If RealSense is already running in another terminal, skip launching it:

```bash
ros2 launch /home/gyul/robot_face_system/launch/attendance_local.launch.py start_realsense:=false
```

Local attendance auth sessions are exposed by the UI server:

```text
GET /api/attendance/session
```

When the browser starts attendance, the server creates a temporary session like:

```json
{
  "session_id": "sess_...",
  "mode": "clock_in",
  "status": "waiting",
  "started_at": "2026-06-05T01:30:00+00:00",
  "expires_at": "2026-06-05T01:30:15+00:00"
}
```

`robot_face_result_uploader` reads that active session before uploading recognition events and adds `session_id`, `mode`, `session_started_at`, and `session_expires_at`. The UI server also adds these fields on receipt for compatibility with older uploaders.

Local attendance policy currently accepts only the first valid `confirmed_known` event for the active session. The server rejects events when:

```text
session_id does not match the active session
the session already recorded attendance
best_similarity < 0.70
pad.live is not true
pad.score < 0.60
the same user has the same attendance mode within 30 seconds
```

Rejected events are still kept in `recognition_events.jsonl` with an `attendance_policy` reason, but they are not written to the final attendance log.

Local JSONL files are split by purpose under the project data directory:

```text
data/recognition_events.jsonl
data/attendance_records.jsonl
```

`recognition_events.jsonl` stores every uploaded recognition event after local policy evaluation. `attendance_records.jsonl` stores only final accepted attendance records with fields such as `record_id`, `user_id`, `record_type`, `session_id`, `source_event_id`, `confidence`, and `pad_score`. The legacy CSV at `/home/gyul/attendance_log.csv` is still written for the current UI.

The UI server exposes the annotated snapshot state for the local kiosk screen:

```text
GET /api/face/status
```

The response includes whether `/tmp/robot_face_annotated.jpg` exists, its size, last update time, age in seconds, and whether it is stale. The browser UI shows this as a camera health line below the preview.

Local edge settings are centralized in:

```text
/home/gyul/robot_face_system/config/edge_local.yaml
```

The config currently controls device ID, ROS topics, perception defaults, UI host/port, snapshot path, auth thresholds, and uploader queue/log paths. `attendance_local.launch.py`, `face_system_server.py`, `robot_face_result_uploader`, and `check_attendance_local` read this file. Command-line arguments can still override launch defaults.

Check the local attendance stack status from another terminal:

```bash
/home/gyul/robot_face_system/scripts/check_attendance_local
```

The launch file writes process output to the terminal and the ROS log directory under `~/.ros/log`. The check script verifies expected processes, ROS topics, the annotated snapshot file, snapshot freshness, and the local UI server status endpoint.

Run perception with a stable device ID:

```bash
/home/gyul/robot_face_system/scripts/robot_face_perception   --device-id edge-dev-001   --image-topic /camera/camera/color/image_raw   --depth-topic /camera/camera/aligned_depth_to_color/image_raw   --person-detector none   --det-size 320   --emit-interval 0.1   --publish-annotated
```

Run the future server uploader in dry-run mode before a backend exists:

```bash
/home/gyul/robot_face_system/scripts/robot_face_result_uploader --dry-run
```

When the backend exists, replace dry-run with the API URL and device token:

```bash
/home/gyul/robot_face_system/scripts/robot_face_result_uploader   --server-url https://your-server.example.com   --token <device-token>
```

The uploader now acts as a small edge agent. It normalizes missing `event_id`, `timestamp`, `device_id`, `user`, and `user_id` fields before upload, attaches the active local auth session, and keeps a local upload log.

The uploader only sends durable states by default: `confirmed_known`, `confirmed_unknown`, `spoof_rejected`, and errors. Transient states such as `searching` and `quick_recognition` stay local unless `--upload-all` is used. If the server is down, events are queued in:

```text
data/pending_events.sqlite
```

Uploader activity is logged in:

```text
data/uploader_events.jsonl
```

## GlobalCam CCTV Sender

The attendance stack keeps using Intel RealSense through `realsense2_camera`. The separate GlobalCam webcam is used by `scripts/cctv_sender` for periodic JPEG uploads.

Check camera device mapping first:

```bash
v4l2-ctl --list-devices
```

Current local mapping observed on this notebook:

```text
Intel RealSense D435: /dev/video0 ... /dev/video5
USB2.0 PC CAMERA: /dev/video6, /dev/video7
```

The CCTV sender is configured in `config/edge_local.yaml` under `cctv`. Set `cctv.server_url` to the main server base URL before enabling uploads.

Single-frame local test without uploading:

```bash
/home/gyul/robot_face_system/scripts/cctv_sender --dry-run --once
```

Single-frame test using the configured server URL:

```bash
/home/gyul/robot_face_system/scripts/cctv_sender --once
```

Continuous CCTV upload loop:

```bash
/home/gyul/robot_face_system/scripts/cctv_sender
```

The sender posts multipart form data to `cctv.images_path` with `device_id`, `camera_id`, `captured_at`, and JPEG file field `image`. It also writes the latest captured JPEG to `/tmp/globalcam_cctv_latest.jpg`.
