from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.substitutions import LaunchConfiguration


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"
    default_safety_model = (
        "/home/gyul/yolo_test/runs/safety_continue45_plus_nohelmet_e20/weights/best.pt"
    )
    default_turtlebot_model = (
        "/home/gyul/yolo_test/runs/turtlebot_edge_aug_plus70ep/weights/best.pt"
    )

    enable_safety_detector = LaunchConfiguration("enable_safety_detector")
    enable_turtlebot_proximity = LaunchConfiguration("enable_turtlebot_proximity")
    event_topic = LaunchConfiguration("event_topic")
    alert_topic = LaunchConfiguration("alert_topic")
    server_object_event_topic = LaunchConfiguration("server_object_event_topic")
    turtlebot_goal_topic = LaunchConfiguration("turtlebot_goal_topic")
    turtlebot_goal_offset_x = LaunchConfiguration("turtlebot_goal_offset_x")
    annotated_topic = LaunchConfiguration("annotated_topic")
    live_topic = LaunchConfiguration("live_topic")
    live_compressed = LaunchConfiguration("live_compressed")
    live_width = LaunchConfiguration("live_width")
    live_height = LaunchConfiguration("live_height")
    live_jpeg_quality = LaunchConfiguration("live_jpeg_quality")
    detections_topic = LaunchConfiguration("detections_topic")
    map_line_topic = LaunchConfiguration("map_line_topic")
    safety_model_path = LaunchConfiguration("safety_model_path")
    person_model_path = LaunchConfiguration("person_model_path")
    safety_device = LaunchConfiguration("safety_device")
    safety_confidence = LaunchConfiguration("safety_confidence")
    person_confidence = LaunchConfiguration("person_confidence")
    safety_imgsz = LaunchConfiguration("safety_imgsz")
    person_imgsz = LaunchConfiguration("person_imgsz")
    safety_fps = LaunchConfiguration("safety_fps")
    enable_person_roi_safety = LaunchConfiguration("enable_person_roi_safety")
    person_roi_expand = LaunchConfiguration("person_roi_expand")
    person_roi_max_count = LaunchConfiguration("person_roi_max_count")
    turtlebot_model_path = LaunchConfiguration("turtlebot_model_path")
    turtlebot_device = LaunchConfiguration("turtlebot_device")
    turtlebot_confidence = LaunchConfiguration("turtlebot_confidence")
    turtlebot_imgsz = LaunchConfiguration("turtlebot_imgsz")
    turtlebot_fps = LaunchConfiguration("turtlebot_fps")
    proximity_enter_distance = LaunchConfiguration("proximity_enter_distance")
    proximity_exit_distance = LaunchConfiguration("proximity_exit_distance")
    server_required_consecutive = LaunchConfiguration("server_required_consecutive")
    server_required_duration_sec = LaunchConfiguration("server_required_duration_sec")
    server_pixel_tolerance = LaunchConfiguration("server_pixel_tolerance")
    server_detection_gap_sec = LaunchConfiguration("server_detection_gap_sec")
    server_track_stale_sec = LaunchConfiguration("server_track_stale_sec")
    server_position_tolerance = LaunchConfiguration("server_position_tolerance")
    image_qos_depth = LaunchConfiguration("image_qos_depth")
    window_name = LaunchConfiguration("window_name")
    display_fps = LaunchConfiguration("display_fps")
    display_width = LaunchConfiguration("display_width")
    display_height = LaunchConfiguration("display_height")
    display_scale = LaunchConfiguration("display_scale")
    live_fps = LaunchConfiguration("live_fps")
    map_line_publish_fps = LaunchConfiguration("map_line_publish_fps")
    left_offset = LaunchConfiguration("left_offset")
    right_offset = LaunchConfiguration("right_offset")
    down_offset = LaunchConfiguration("down_offset")
    bottom_line_length = LaunchConfiguration("bottom_line_length")
    square_thickness = LaunchConfiguration("square_thickness")
    show_image = LaunchConfiguration("show_image")
    show_debug_overlay = LaunchConfiguration("show_debug_overlay")
    publish_annotated = LaunchConfiguration("publish_annotated")
    udp_bind = LaunchConfiguration("udp_bind")
    udp_port = LaunchConfiguration("udp_port")
    udp_allowed_host = LaunchConfiguration("udp_allowed_host")
    udp_timeout_sec = LaunchConfiguration("udp_timeout_sec")
    udp_max_frames_buffer = LaunchConfiguration("udp_max_frames_buffer")
    udp_socket_buffer = LaunchConfiguration("udp_socket_buffer")
    log_interval = LaunchConfiguration("log_interval")
    torch_num_threads = LaunchConfiguration("torch_num_threads")
    torch_num_interop_threads = LaunchConfiguration("torch_num_interop_threads")
    opencv_num_threads = LaunchConfiguration("opencv_num_threads")

    return LaunchDescription(
        [
            DeclareLaunchArgument("enable_safety_detector", default_value="true"),
            DeclareLaunchArgument("enable_turtlebot_proximity", default_value="true"),
            DeclareLaunchArgument("udp_bind", default_value="0.0.0.0"),
            DeclareLaunchArgument("udp_port", default_value="5005"),
            DeclareLaunchArgument("udp_allowed_host", default_value=""),
            DeclareLaunchArgument("udp_timeout_sec", default_value="0.5"),
            DeclareLaunchArgument("udp_max_frames_buffer", default_value="32"),
            DeclareLaunchArgument("udp_socket_buffer", default_value="4194304"),
            DeclareLaunchArgument("log_interval", default_value="1.0"),
            DeclareLaunchArgument("live_topic", default_value="/globalcam/live/image"),
            DeclareLaunchArgument("live_compressed", default_value="true"),
            DeclareLaunchArgument("live_width", default_value="1280"),
            DeclareLaunchArgument("live_height", default_value="960"),
            DeclareLaunchArgument("live_jpeg_quality", default_value="80"),
            DeclareLaunchArgument("detections_topic", default_value="/globalcam/combined/detections"),
            DeclareLaunchArgument("map_line_topic", default_value="/globalcam/map_line"),
            DeclareLaunchArgument("event_topic", default_value="/globalcam/object_map/events"),
            DeclareLaunchArgument(
                "alert_topic",
                default_value="/globalcam/turtlebot_proximity/alerts",
            ),
            DeclareLaunchArgument(
                "server_object_event_topic",
                default_value="/globalcam/server/object_events",
            ),
            DeclareLaunchArgument(
                "turtlebot_goal_topic",
                default_value="/globalcam/turtlebot_goal/coordinates",
            ),
            DeclareLaunchArgument("turtlebot_goal_offset_x", default_value="0.3"),
            DeclareLaunchArgument(
                "annotated_topic",
                default_value="/globalcam/combined/annotated_image",
            ),
            DeclareLaunchArgument("window_name", default_value="GlobalCam-Combined"),
            DeclareLaunchArgument("display_fps", default_value="30"),
            DeclareLaunchArgument("display_width", default_value="0"),
            DeclareLaunchArgument("display_height", default_value="0"),
            DeclareLaunchArgument("display_scale", default_value="1.0"),
            DeclareLaunchArgument("live_fps", default_value="5"),
            DeclareLaunchArgument("map_line_publish_fps", default_value="5"),
            DeclareLaunchArgument("left_offset", default_value="370.0"),
            DeclareLaunchArgument("right_offset", default_value="370.0"),
            DeclareLaunchArgument("down_offset", default_value="780.0"),
            DeclareLaunchArgument("bottom_line_length", default_value="1140.0"),
            DeclareLaunchArgument("square_thickness", default_value="6"),
            DeclareLaunchArgument("show_image", default_value="true"),
            DeclareLaunchArgument("show_debug_overlay", default_value="false"),
            DeclareLaunchArgument("publish_annotated", default_value="false"),
            DeclareLaunchArgument("safety_model_path", default_value=default_safety_model),
            DeclareLaunchArgument("person_model_path", default_value="/home/gyul/yolo11n.pt"),
            DeclareLaunchArgument("safety_device", default_value="auto"),
            DeclareLaunchArgument("safety_confidence", default_value="0.6"),
            DeclareLaunchArgument("person_confidence", default_value="0.35"),
            DeclareLaunchArgument("safety_imgsz", default_value="1280"),
            DeclareLaunchArgument("person_imgsz", default_value="1280"),
            DeclareLaunchArgument("safety_fps", default_value="2"),
            DeclareLaunchArgument("enable_person_roi_safety", default_value="false"),
            DeclareLaunchArgument("person_roi_expand", default_value="1.6"),
            DeclareLaunchArgument("person_roi_max_count", default_value="8"),
            DeclareLaunchArgument("turtlebot_model_path", default_value=default_turtlebot_model),
            DeclareLaunchArgument("turtlebot_device", default_value="auto"),
            DeclareLaunchArgument("turtlebot_confidence", default_value="0.6"),
            DeclareLaunchArgument("turtlebot_imgsz", default_value="1280"),
            DeclareLaunchArgument("turtlebot_fps", default_value="2"),
            DeclareLaunchArgument("proximity_enter_distance", default_value="0.2"),
            DeclareLaunchArgument("proximity_exit_distance", default_value="0.3"),
            DeclareLaunchArgument("server_required_consecutive", default_value="10"),
            DeclareLaunchArgument("server_required_duration_sec", default_value="5.0"),
            DeclareLaunchArgument("server_pixel_tolerance", default_value="80.0"),
            DeclareLaunchArgument("server_detection_gap_sec", default_value="5.0"),
            DeclareLaunchArgument("server_track_stale_sec", default_value="12.0"),
            DeclareLaunchArgument("server_position_tolerance", default_value="0.15"),
            DeclareLaunchArgument("image_qos_depth", default_value="1"),
            DeclareLaunchArgument("torch_num_threads", default_value="1"),
            DeclareLaunchArgument("torch_num_interop_threads", default_value="1"),
            DeclareLaunchArgument("opencv_num_threads", default_value="1"),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/globalcam_udp_display_node",
                    "--udp-bind",
                    udp_bind,
                    "--udp-port",
                    udp_port,
                    "--udp-allowed-host",
                    udp_allowed_host,
                    "--udp-timeout-sec",
                    udp_timeout_sec,
                    "--udp-max-frames-buffer",
                    udp_max_frames_buffer,
                    "--udp-socket-buffer",
                    udp_socket_buffer,
                    "--log-interval",
                    log_interval,
                    "--window-name",
                    window_name,
                    "--show-image",
                    show_image,
                    "--display-fps",
                    display_fps,
                    "--display-width",
                    display_width,
                    "--display-height",
                    display_height,
                    "--display-scale",
                    display_scale,
                    "--live-fps",
                    live_fps,
                    "--live-topic",
                    live_topic,
                    "--live-compressed",
                    live_compressed,
                    "--live-width",
                    live_width,
                    "--live-height",
                    live_height,
                    "--live-jpeg-quality",
                    live_jpeg_quality,
                    "--map-line-topic",
                    map_line_topic,
                    "--annotated-topic",
                    annotated_topic,
                    "--detections-topic",
                    detections_topic,
                    "--image-qos-depth",
                    image_qos_depth,
                    "--show-debug-overlay",
                    show_debug_overlay,
                    "--publish-annotated",
                    publish_annotated,
                    "--left-offset",
                    left_offset,
                    "--right-offset",
                    right_offset,
                    "--down-offset",
                    down_offset,
                    "--bottom-line-length",
                    bottom_line_length,
                    "--square-thickness",
                    square_thickness,
                ],
                name="globalcam_udp_display_node",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/globalcam_yolo_result_node",
                    "--live-topic",
                    live_topic,
                    "--live-compressed",
                    live_compressed,
                    "--map-line-topic",
                    map_line_topic,
                    "--detections-topic",
                    detections_topic,
                    "--event-topic",
                    event_topic,
                    "--alert-topic",
                    alert_topic,
                    "--server-object-event-topic",
                    server_object_event_topic,
                    "--turtlebot-goal-topic",
                    turtlebot_goal_topic,
                    "--turtlebot-goal-offset-x",
                    turtlebot_goal_offset_x,
                    "--enable-safety-detector",
                    enable_safety_detector,
                    "--enable-turtlebot-proximity",
                    enable_turtlebot_proximity,
                    "--safety-model-path",
                    safety_model_path,
                    "--person-model-path",
                    person_model_path,
                    "--safety-device",
                    safety_device,
                    "--safety-confidence",
                    safety_confidence,
                    "--person-confidence",
                    person_confidence,
                    "--safety-imgsz",
                    safety_imgsz,
                    "--person-imgsz",
                    person_imgsz,
                    "--safety-fps",
                    safety_fps,
                    "--enable-person-roi-safety",
                    enable_person_roi_safety,
                    "--person-roi-expand",
                    person_roi_expand,
                    "--person-roi-max-count",
                    person_roi_max_count,
                    "--turtlebot-model-path",
                    turtlebot_model_path,
                    "--turtlebot-device",
                    turtlebot_device,
                    "--turtlebot-confidence",
                    turtlebot_confidence,
                    "--turtlebot-imgsz",
                    turtlebot_imgsz,
                    "--turtlebot-fps",
                    turtlebot_fps,
                    "--map-line-publish-fps",
                    map_line_publish_fps,
                    "--left-offset",
                    left_offset,
                    "--right-offset",
                    right_offset,
                    "--down-offset",
                    down_offset,
                    "--bottom-line-length",
                    bottom_line_length,
                    "--square-thickness",
                    square_thickness,
                    "--proximity-enter-distance",
                    proximity_enter_distance,
                    "--proximity-exit-distance",
                    proximity_exit_distance,
                    "--server-required-consecutive",
                    server_required_consecutive,
                    "--server-required-duration-sec",
                    server_required_duration_sec,
                    "--server-pixel-tolerance",
                    server_pixel_tolerance,
                    "--server-detection-gap-sec",
                    server_detection_gap_sec,
                    "--server-track-stale-sec",
                    server_track_stale_sec,
                    "--server-position-tolerance",
                    server_position_tolerance,
                    "--image-qos-depth",
                    image_qos_depth,
                    "--torch-num-threads",
                    torch_num_threads,
                    "--torch-num-interop-threads",
                    torch_num_interop_threads,
                    "--opencv-num-threads",
                    opencv_num_threads,
                    "--log-interval",
                    log_interval,
                ],
                name="globalcam_yolo_result_node",
                output="both",
            ),
        ]
    )
