import os
import subprocess

from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess, IncludeLaunchDescription, LogInfo, Shutdown
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration, PathJoinSubstitution
from launch_ros.substitutions import FindPackageShare
import yaml


STACK_PATTERNS = (
    "realsense2_camera_node",
    "pc_side.perception_node",
    "pc_side.annotated_snapshot_node",
    "pc_side.result_uploader_node",
    "face_system_server.py",
)


def _running_stack_processes():
    current_pid = os.getpid()
    try:
        output = subprocess.check_output(
            ["ps", "-eo", "pid=,ppid=,pgid=,sid=,cmd="],
            text=True,
            stderr=subprocess.DEVNULL,
        )
    except Exception:
        return []
    rows = []
    for line in output.splitlines():
        parts = line.strip().split(None, 4)
        if len(parts) < 5:
            continue
        pid, ppid, pgid, sid, cmd = parts
        try:
            pid_int = int(pid)
            ppid_int = int(ppid)
        except ValueError:
            continue
        if pid_int == current_pid or ppid_int == current_pid:
            continue
        if any(pattern in cmd for pattern in STACK_PATTERNS):
            rows.append((pid_int, cmd))
    return rows


def duplicate_launch_message(rows):
    detail = "\n".join(f"  PID {pid}: {cmd}" for pid, cmd in rows[:8])
    more = "" if len(rows) <= 8 else f"\n  ... {len(rows) - 8} more process(es)"
    return (
        "출퇴근 전체 스택이 이미 실행 중입니다. 새로 하나 더 실행하지 않습니다.\n"
        "이 launch 파일은 RealSense, perception, snapshot, UI server, uploader를 모두 실행하는 전체 런처입니다.\n"
        "동시에 두 번 실행하면 RealSense를 중복으로 열어서 VIDIOC_S_FMT 오류가 날 수 있습니다.\n"
        "이미 실행 중인 것을 재시작하려면: sudo systemctl restart robot-face-attendance\n"
        "수동 ros2 launch로 직접 실행하려면 먼저: sudo systemctl stop robot-face-attendance\n"
        f"현재 실행 중인 프로세스:\n{detail}{more}"
    )


def nested_get(config, path, default):
    value = config
    for key in path.split("."):
        if not isinstance(value, dict) or key not in value:
            return default
        value = value[key]
    return str(value)


def load_edge_config(path):
    try:
        with open(path, "r", encoding="utf-8") as file:
            return yaml.safe_load(file) or {}
    except FileNotFoundError:
        return {}


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"
    face_python = "/home/gyul/face-env/bin/python"
    ui_server = "/home/gyul/face_system_server.py"
    default_config_path = f"{robot_face_dir}/config/edge_local.yaml"
    config = load_edge_config(default_config_path)
    running_stack = _running_stack_processes()
    if running_stack:
        return LaunchDescription(
            [
                LogInfo(msg=duplicate_launch_message(running_stack)),
                Shutdown(reason="attendance stack already running"),
            ]
        )

    start_realsense = LaunchConfiguration("start_realsense")
    start_snapshot_node = LaunchConfiguration("start_snapshot_node")
    realsense_color_profile = LaunchConfiguration("realsense_color_profile")
    realsense_depth_profile = LaunchConfiguration("realsense_depth_profile")
    config_path = LaunchConfiguration("config")
    device_id = LaunchConfiguration("device_id")
    image_topic = LaunchConfiguration("image_topic")
    depth_topic = LaunchConfiguration("depth_topic")
    annotated_topic = LaunchConfiguration("annotated_topic")
    result_topic = LaunchConfiguration("result_topic")
    snapshot_output = LaunchConfiguration("snapshot_output")
    snapshot_fps = LaunchConfiguration("snapshot_fps")
    server_host = LaunchConfiguration("server_host")
    server_port = LaunchConfiguration("server_port")
    server_url = LaunchConfiguration("server_url")
    det_size = LaunchConfiguration("det_size")
    onnx_threads = LaunchConfiguration("onnx_threads")
    emit_interval = LaunchConfiguration("emit_interval")
    process_fps = LaunchConfiguration("process_fps")
    person_detector = LaunchConfiguration("person_detector")
    crop_width_ratio = LaunchConfiguration("crop_width_ratio")
    crop_height_ratio = LaunchConfiguration("crop_height_ratio")
    start_perception = LaunchConfiguration("start_perception")
    perception_input_mode = LaunchConfiguration("perception_input_mode")
    start_globalcam = LaunchConfiguration("start_globalcam")
    start_droidcam = LaunchConfiguration("start_droidcam")
    droidcam_mode = LaunchConfiguration("droidcam_mode")
    droidcam_video_device = LaunchConfiguration("droidcam_video_device")
    droidcam_wifi_ip = LaunchConfiguration("droidcam_wifi_ip")
    droidcam_wifi_port = LaunchConfiguration("droidcam_wifi_port")
    droidcam_adb_port = LaunchConfiguration("droidcam_adb_port")
    droidcam_adb_serial = LaunchConfiguration("droidcam_adb_serial")
    droidcam_size = LaunchConfiguration("droidcam_size")
    show_globalcam_viewer = LaunchConfiguration("show_globalcam_viewer")
    show_face_viewer = LaunchConfiguration("show_face_viewer")
    start_cctv_sender = LaunchConfiguration("start_cctv_sender")
    globalcam_camera = LaunchConfiguration("globalcam_camera")
    globalcam_topic = LaunchConfiguration("globalcam_topic")
    globalcam_frame_id = LaunchConfiguration("globalcam_frame_id")
    globalcam_width = LaunchConfiguration("globalcam_width")
    globalcam_height = LaunchConfiguration("globalcam_height")
    globalcam_fps = LaunchConfiguration("globalcam_fps")
    globalcam_camera_fps = LaunchConfiguration("globalcam_camera_fps")
    globalcam_jpeg_quality = LaunchConfiguration("globalcam_jpeg_quality")
    globalcam_window_name = LaunchConfiguration("globalcam_window_name")
    globalcam_viewer_max_width = LaunchConfiguration("globalcam_viewer_max_width")
    face_window_name = LaunchConfiguration("face_window_name")
    face_viewer_max_width = LaunchConfiguration("face_viewer_max_width")

    return LaunchDescription(
        [
            DeclareLaunchArgument("config", default_value=default_config_path),
            DeclareLaunchArgument("start_realsense", default_value="true"),
            DeclareLaunchArgument("start_snapshot_node", default_value="false"),
            DeclareLaunchArgument("realsense_color_profile", default_value=nested_get(config, "realsense.color_profile", "424x240x15")),
            DeclareLaunchArgument("realsense_depth_profile", default_value=nested_get(config, "realsense.depth_profile", "480x270x15")),
            DeclareLaunchArgument("device_id", default_value=nested_get(config, "device.id", "edge-dev-001")),
            DeclareLaunchArgument("image_topic", default_value=nested_get(config, "ros.image_topic", "/camera/camera/color/image_raw")),
            DeclareLaunchArgument("depth_topic", default_value=nested_get(config, "ros.depth_topic", "/camera/camera/aligned_depth_to_color/image_raw")),
            DeclareLaunchArgument("annotated_topic", default_value=nested_get(config, "ros.annotated_topic", "/face/annotated_image")),
            DeclareLaunchArgument("result_topic", default_value=nested_get(config, "ros.state_topic", "/face/recognition_state")),
            DeclareLaunchArgument("snapshot_output", default_value=nested_get(config, "ui.snapshot_path", "/tmp/robot_face_annotated.jpg")),
            DeclareLaunchArgument("snapshot_fps", default_value=nested_get(config, "ui.snapshot_fps", "15")),
            DeclareLaunchArgument("server_host", default_value=nested_get(config, "ui.host", "127.0.0.1")),
            DeclareLaunchArgument("server_port", default_value=nested_get(config, "ui.port", "8090")),
            DeclareLaunchArgument("server_url", default_value=nested_get(config, "server.url", "http://127.0.0.1:8090")),
            DeclareLaunchArgument("det_size", default_value=nested_get(config, "perception.det_size", "320")),
            DeclareLaunchArgument("onnx_threads", default_value=nested_get(config, "perception.onnx_threads", "2")),
            DeclareLaunchArgument("emit_interval", default_value=nested_get(config, "perception.emit_interval", "0.1")),
            DeclareLaunchArgument("process_fps", default_value=nested_get(config, "perception.process_fps", "4.0")),
            DeclareLaunchArgument("person_detector", default_value=nested_get(config, "perception.person_detector", "none")),
            DeclareLaunchArgument("crop_width_ratio", default_value=nested_get(config, "perception.crop_width_ratio", "1.0")),
            DeclareLaunchArgument("crop_height_ratio", default_value=nested_get(config, "perception.crop_height_ratio", "1.0")),
            DeclareLaunchArgument("start_perception", default_value="true"),
            DeclareLaunchArgument("perception_input_mode", default_value=nested_get(config, "perception.input_mode", "ros")),
            DeclareLaunchArgument("start_globalcam", default_value="true"),
            DeclareLaunchArgument("start_droidcam", default_value=nested_get(config, "cctv.droidcam.enabled", "true")),
            DeclareLaunchArgument("droidcam_mode", default_value=nested_get(config, "cctv.droidcam.mode", "wifi")),
            DeclareLaunchArgument("droidcam_video_device", default_value=nested_get(config, "cctv.droidcam.video_device", "auto")),
            DeclareLaunchArgument("droidcam_wifi_ip", default_value=nested_get(config, "cctv.droidcam.wifi_ip", "192.168.0.10")),
            DeclareLaunchArgument("droidcam_wifi_port", default_value=nested_get(config, "cctv.droidcam.wifi_port", "4747")),
            DeclareLaunchArgument("droidcam_adb_port", default_value=nested_get(config, "cctv.droidcam.adb_port", "4747")),
            DeclareLaunchArgument("droidcam_adb_serial", default_value=nested_get(config, "cctv.droidcam.adb_serial", "__auto__")),
            DeclareLaunchArgument("droidcam_size", default_value=nested_get(config, "cctv.droidcam.size", "320x240")),
            DeclareLaunchArgument("show_globalcam_viewer", default_value=nested_get(config, "cctv.viewer_enabled", "true")),
            DeclareLaunchArgument("show_face_viewer", default_value=nested_get(config, "face_viewer.enabled", "true")),
            DeclareLaunchArgument("start_cctv_sender", default_value="false"),
            DeclareLaunchArgument("globalcam_camera", default_value=nested_get(config, "cctv.camera", "auto")),
            DeclareLaunchArgument("globalcam_topic", default_value=nested_get(config, "cctv.topic", "/globalcam/image_raw/compressed")),
            DeclareLaunchArgument("globalcam_frame_id", default_value=nested_get(config, "cctv.frame_id", "globalcam")),
            DeclareLaunchArgument("globalcam_width", default_value=nested_get(config, "cctv.width", "640")),
            DeclareLaunchArgument("globalcam_height", default_value=nested_get(config, "cctv.height", "480")),
            DeclareLaunchArgument("globalcam_fps", default_value=nested_get(config, "cctv.fps", "10")),
            DeclareLaunchArgument("globalcam_camera_fps", default_value=nested_get(config, "cctv.camera_fps", "5")),
            DeclareLaunchArgument("globalcam_jpeg_quality", default_value=nested_get(config, "cctv.topic_jpeg_quality", "65")),
            DeclareLaunchArgument("globalcam_window_name", default_value=nested_get(config, "cctv.viewer_window_name", "GlobalCam-CCTV")),
            DeclareLaunchArgument("globalcam_viewer_max_width", default_value=nested_get(config, "cctv.viewer_max_width", "960")),
            DeclareLaunchArgument("face_window_name", default_value=nested_get(config, "face_viewer.window_name", "Face-Recognition")),
            DeclareLaunchArgument("face_viewer_max_width", default_value=nested_get(config, "face_viewer.max_width", "960")),
            IncludeLaunchDescription(
                PythonLaunchDescriptionSource(
                    PathJoinSubstitution(
                        [FindPackageShare("realsense2_camera"), "launch", "rs_launch.py"]
                    )
                ),
                condition=IfCondition(start_realsense),
                launch_arguments={
                    "enable_color": "true",
                    "enable_depth": "true",
                    "align_depth.enable": "true",
                    "rgb_camera.color_profile": realsense_color_profile,
                    "depth_module.depth_profile": realsense_depth_profile,
                }.items(),
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_perception",
                    "--device-id",
                    device_id,
                    "--image-topic",
                    image_topic,
                    "--depth-topic",
                    depth_topic,
                    "--result-topic",
                    result_topic,
                    "--annotated-topic",
                    annotated_topic,
                    "--person-detector",
                    person_detector,
                    "--det-size",
                    det_size,
                    "--onnx-threads",
                    onnx_threads,
                    "--emit-interval",
                    emit_interval,
                    "--process-fps",
                    process_fps,
                    "--crop-width-ratio",
                    crop_width_ratio,
                    "--crop-height-ratio",
                    crop_height_ratio,
                ],
                name="robot_face_perception",
                output="both",
                condition=IfCondition(start_perception),
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_annotated_snapshot",
                    "--topic",
                    image_topic,
                    "--output",
                    snapshot_output,
                    "--fps",
                    snapshot_fps,
                ],
                name="robot_face_annotated_snapshot",
                output="both",
                condition=IfCondition(start_snapshot_node),
            ),
            ExecuteProcess(
                cmd=[
                    face_python,
                    ui_server,
                    "--config",
                    config_path,
                    "--host",
                    server_host,
                    "--port",
                    server_port,
                ],
                name="face_system_server",
                output="both",
            ),

            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/droidcam_connector",
                    "--mode",
                    droidcam_mode,
                    "--video-device",
                    droidcam_video_device,
                    "--wifi-ip",
                    droidcam_wifi_ip,
                    "--wifi-port",
                    droidcam_wifi_port,
                    "--adb-port",
                    droidcam_adb_port,
                    "--adb-serial",
                    droidcam_adb_serial,
                    "--size",
                    droidcam_size,
                ],
                condition=IfCondition(start_droidcam),
                name="droidcam_connector",
                output="both",
            ),

            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_camera_publisher",
                    "--camera",
                    globalcam_camera,
                    "--topic",
                    globalcam_topic,
                    "--frame-id",
                    globalcam_frame_id,
                    "--width",
                    globalcam_width,
                    "--height",
                    globalcam_height,
                    "--fps",
                    globalcam_fps,
                    "--camera-fps",
                    globalcam_camera_fps,
                    "--compressed",
                    "--jpeg-quality",
                    globalcam_jpeg_quality,
                ],
                condition=IfCondition(start_globalcam),
                name="globalcam_image_publisher",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_viewer",
                    "--topic",
                    globalcam_topic,
                    "--window-name",
                    globalcam_window_name,
                    "--max-width",
                    globalcam_viewer_max_width,
                ],
                condition=IfCondition(show_globalcam_viewer),
                name="globalcam_image_viewer",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_viewer",
                    "--topic",
                    annotated_topic,
                    "--window-name",
                    face_window_name,
                    "--max-width",
                    face_viewer_max_width,
                ],
                condition=IfCondition(show_face_viewer),
                name="face_annotated_viewer",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/cctv_sender",
                    "--config",
                    config_path,
                ],
                condition=IfCondition(start_cctv_sender),
                name="globalcam_cctv_sender",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_result_uploader",
                    "--topic",
                    result_topic,
                    "--config",
                    config_path,
                    "--server-url",
                    nested_get(config, "uploader.server_url", "http://127.0.0.1:8090"),
                    "--device-id",
                    device_id,
                    "--session-url",
                    nested_get(config, "uploader.session_url", "http://127.0.0.1:8090"),
                    "--session-path",
                    nested_get(config, "uploader.session_path", "/api/attendance/session"),
                    "--local-log-path",
                    nested_get(config, "uploader.local_log_path", f"{robot_face_dir}/data/uploader_events.jsonl"),
                    "--queue-path",
                    nested_get(config, "uploader.queue_path", f"{robot_face_dir}/data/pending_events.sqlite"),
                ],
                name="robot_face_result_uploader",
                output="both",
            ),
        ]
    )
