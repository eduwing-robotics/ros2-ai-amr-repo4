from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.conditions import IfCondition
from launch.substitutions import LaunchConfiguration


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"

    start_globalcam = LaunchConfiguration("start_globalcam")
    start_droidcam = LaunchConfiguration("start_droidcam")
    droidcam_mode = LaunchConfiguration("droidcam_mode")
    droidcam_video_device = LaunchConfiguration("droidcam_video_device")
    droidcam_wifi_ip = LaunchConfiguration("droidcam_wifi_ip")
    droidcam_wifi_port = LaunchConfiguration("droidcam_wifi_port")
    droidcam_adb_port = LaunchConfiguration("droidcam_adb_port")
    droidcam_adb_serial = LaunchConfiguration("droidcam_adb_serial")
    show_viewer = LaunchConfiguration("show_viewer")
    camera = LaunchConfiguration("camera")
    global_image_topic = LaunchConfiguration("global_image_topic")
    robot_image_topic = LaunchConfiguration("robot_image_topic")
    event_topic = LaunchConfiguration("event_topic")
    annotated_topic = LaunchConfiguration("annotated_topic")
    detector_model_path = LaunchConfiguration("detector_model_path")
    detector_device = LaunchConfiguration("detector_device")
    detector_confidence = LaunchConfiguration("detector_confidence")
    detector_imgsz = LaunchConfiguration("detector_imgsz")
    detector_fps = LaunchConfiguration("detector_fps")
    image_qos_depth = LaunchConfiguration("image_qos_depth")
    globalcam_jpeg_quality = LaunchConfiguration("globalcam_jpeg_quality")
    viewer_window_name = LaunchConfiguration("viewer_window_name")

    return LaunchDescription(
        [
            DeclareLaunchArgument("start_globalcam", default_value="false"),
            DeclareLaunchArgument("start_droidcam", default_value="false"),
            DeclareLaunchArgument("droidcam_mode", default_value="wifi"),
            DeclareLaunchArgument("droidcam_video_device", default_value="auto"),
            DeclareLaunchArgument("droidcam_wifi_ip", default_value="192.168.0.10"),
            DeclareLaunchArgument("droidcam_wifi_port", default_value="4747"),
            DeclareLaunchArgument("droidcam_adb_port", default_value="4747"),
            DeclareLaunchArgument("droidcam_adb_serial", default_value="__auto__"),
            DeclareLaunchArgument("show_viewer", default_value="true"),
            DeclareLaunchArgument("camera", default_value="auto"),
            DeclareLaunchArgument("global_image_topic", default_value="/globalcam/image_raw/compressed"),
            DeclareLaunchArgument("robot_image_topic", default_value="/robot/picam/image_raw"),
            DeclareLaunchArgument("event_topic", default_value="/safety_identity/events"),
            DeclareLaunchArgument("annotated_topic", default_value="/safety_identity/annotated_image"),
            DeclareLaunchArgument("detector_model_path", default_value="/home/gyul/yolo_test/models/hardhat_turtlebot_best.pt"),
            DeclareLaunchArgument("detector_device", default_value="auto"),
            DeclareLaunchArgument("detector_confidence", default_value="0.5"),
            DeclareLaunchArgument("detector_imgsz", default_value="640"),
            DeclareLaunchArgument("detector_fps", default_value="5"),
            DeclareLaunchArgument("image_qos_depth", default_value="1"),
            DeclareLaunchArgument("globalcam_jpeg_quality", default_value="65"),
            DeclareLaunchArgument("viewer_window_name", default_value="Safety-Identity"),
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
                ],
                condition=IfCondition(start_droidcam),
                name="droidcam_connector",
                output="both",
            ),

            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_camera_publisher",
                    "--camera",
                    camera,
                    "--topic",
                    global_image_topic,
                    "--frame-id",
                    "globalcam",
                    "--width",
                    "640",
                    "--height",
                    "480",
                    "--fps",
                    "10",
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
                    f"{robot_face_dir}/scripts/safety_identity_node",
                    "--global-image-topic",
                    global_image_topic,
                    "--robot-image-topic",
                    robot_image_topic,
                    "--detector-model-path",
                    detector_model_path,
                    "--detector-device",
                    detector_device,
                    "--detector-confidence",
                    detector_confidence,
                    "--detector-imgsz",
                    detector_imgsz,
                    "--helmet-classes",
                    "helmet",
                    "--no-helmet-classes",
                    "head",
                    "--extra-object-classes",
                    "turtlebot",
                    "--event-topic",
                    event_topic,
                    "--annotated-topic",
                    annotated_topic,
                    "--image-qos-depth",
                    image_qos_depth,
                    "--detector-fps",
                    detector_fps,
                    "--helmet-confidence",
                    "0.65",
                    "--no-helmet-confidence",
                    "0.60",
                    "--safe-confirm-frames",
                    "2",
                    "--unsafe-confirm-frames",
                    "5",
                    "--face-fps",
                    "2",
                    "--publish-annotated",
                ],
                name="safety_identity_node",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/robot_face_viewer",
                    "--topic",
                    annotated_topic,
                    "--window-name",
                    viewer_window_name,
                    "--max-width",
                    "960",
                ],
                condition=IfCondition(show_viewer),
                name="safety_identity_viewer",
                output="both",
            ),
        ]
    )
