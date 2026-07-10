from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.substitutions import LaunchConfiguration


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"

    udp_bind = LaunchConfiguration("udp_bind")
    udp_port = LaunchConfiguration("udp_port")
    udp_allowed_host = LaunchConfiguration("udp_allowed_host")
    udp_timeout_sec = LaunchConfiguration("udp_timeout_sec")
    udp_max_frames_buffer = LaunchConfiguration("udp_max_frames_buffer")
    udp_socket_buffer = LaunchConfiguration("udp_socket_buffer")
    window_name = LaunchConfiguration("window_name")
    show_image = LaunchConfiguration("show_image")
    display_fps = LaunchConfiguration("display_fps")
    live_fps = LaunchConfiguration("live_fps")
    live_topic = LaunchConfiguration("live_topic")
    detections_topic = LaunchConfiguration("detections_topic")
    event_topic = LaunchConfiguration("event_topic")
    annotated_topic = LaunchConfiguration("annotated_topic")
    flip_mode = LaunchConfiguration("flip_mode")
    publish_annotated = LaunchConfiguration("publish_annotated")
    safety_model_path = LaunchConfiguration("safety_model_path")
    person_model_path = LaunchConfiguration("person_model_path")
    safety_device = LaunchConfiguration("safety_device")
    safety_confidence = LaunchConfiguration("safety_confidence")
    safety_imgsz = LaunchConfiguration("safety_imgsz")
    safety_roi_confidence = LaunchConfiguration("safety_roi_confidence")
    safety_roi_imgsz = LaunchConfiguration("safety_roi_imgsz")
    person_confidence = LaunchConfiguration("person_confidence")
    person_imgsz = LaunchConfiguration("person_imgsz")
    enable_person_roi_safety = LaunchConfiguration("enable_person_roi_safety")
    person_roi_expand = LaunchConfiguration("person_roi_expand")
    person_roi_max_count = LaunchConfiguration("person_roi_max_count")
    safety_dedupe_iou = LaunchConfiguration("safety_dedupe_iou")
    safety_fps = LaunchConfiguration("safety_fps")
    image_qos_depth = LaunchConfiguration("image_qos_depth")
    camera_id = LaunchConfiguration("camera_id")
    event_cooldown_sec = LaunchConfiguration("event_cooldown_sec")
    log_interval = LaunchConfiguration("log_interval")
    torch_num_threads = LaunchConfiguration("torch_num_threads")
    torch_num_interop_threads = LaunchConfiguration("torch_num_interop_threads")
    opencv_num_threads = LaunchConfiguration("opencv_num_threads")
    enable_face_recognition = LaunchConfiguration("enable_face_recognition")
    face_fps = LaunchConfiguration("face_fps")
    face_result_ttl = LaunchConfiguration("face_result_ttl")
    face_similarity = LaunchConfiguration("face_similarity")
    face_temporal_frames = LaunchConfiguration("face_temporal_frames")
    face_temporal_votes = LaunchConfiguration("face_temporal_votes")
    providers = LaunchConfiguration("providers")
    det_size = LaunchConfiguration("det_size")
    registered_dir = LaunchConfiguration("registered_dir")
    margin_threshold = LaunchConfiguration("margin_threshold")
    top_k = LaunchConfiguration("top_k")
    face_min_roi_width = LaunchConfiguration("face_min_roi_width")
    face_min_roi_height = LaunchConfiguration("face_min_roi_height")
    face_min_roi_area_ratio = LaunchConfiguration("face_min_roi_area_ratio")
    face_cache_reset_gap_sec = LaunchConfiguration("face_cache_reset_gap_sec")
    server_safety_event_topic = LaunchConfiguration("server_safety_event_topic")
    server_center_overlap_ratio = LaunchConfiguration("server_center_overlap_ratio")
    server_fire_min_height_ratio = LaunchConfiguration("server_fire_min_height_ratio")
    server_fall_min_height_ratio = LaunchConfiguration("server_fall_min_height_ratio")
    server_no_helmet_min_height_ratio = LaunchConfiguration("server_no_helmet_min_height_ratio")
    server_event_cooldown_sec = LaunchConfiguration("server_event_cooldown_sec")

    return LaunchDescription(
        [
            DeclareLaunchArgument("udp_bind", default_value="0.0.0.0"),
            DeclareLaunchArgument("udp_port", default_value="5006"),
            DeclareLaunchArgument("udp_allowed_host", default_value=""),
            DeclareLaunchArgument("udp_timeout_sec", default_value="0.5"),
            DeclareLaunchArgument("udp_max_frames_buffer", default_value="32"),
            DeclareLaunchArgument("udp_socket_buffer", default_value="4194304"),
            DeclareLaunchArgument("window_name", default_value="TurtleBot-Camera"),
            DeclareLaunchArgument("show_image", default_value="true"),
            DeclareLaunchArgument("display_fps", default_value="25"),
            DeclareLaunchArgument("live_fps", default_value="20"),
            DeclareLaunchArgument("live_topic", default_value="/turtlebot_camera/live/image"),
            DeclareLaunchArgument("detections_topic", default_value="/turtlebot_camera/safety/detections"),
            DeclareLaunchArgument("event_topic", default_value="/turtlebot_camera/safety/events"),
            DeclareLaunchArgument(
                "annotated_topic",
                default_value="/turtlebot_camera/safety/annotated_image",
            ),
            DeclareLaunchArgument("flip_mode", default_value="none"),
            DeclareLaunchArgument("publish_annotated", default_value="true"),
            DeclareLaunchArgument(
                "safety_model_path",
                default_value="/home/gyul/yolo_test/runs/safety_continue45_plus_nohelmet_e20/weights/best.pt",
            ),
            DeclareLaunchArgument("person_model_path", default_value="/home/gyul/yolo11n.pt"),
            DeclareLaunchArgument("safety_device", default_value="auto"),
            DeclareLaunchArgument("safety_confidence", default_value="0.6"),
            DeclareLaunchArgument("safety_imgsz", default_value="640"),
            DeclareLaunchArgument("safety_roi_confidence", default_value="0.25"),
            DeclareLaunchArgument("safety_roi_imgsz", default_value="1280"),
            DeclareLaunchArgument("person_confidence", default_value="0.35"),
            DeclareLaunchArgument("person_imgsz", default_value="640"),
            DeclareLaunchArgument("enable_person_roi_safety", default_value="false"),
            DeclareLaunchArgument("person_roi_expand", default_value="1.1"),
            DeclareLaunchArgument("person_roi_max_count", default_value="4"),
            DeclareLaunchArgument("safety_dedupe_iou", default_value="0.6"),
            DeclareLaunchArgument("safety_fps", default_value="2"),
            DeclareLaunchArgument("image_qos_depth", default_value="1"),
            DeclareLaunchArgument("camera_id", default_value="turtlebot-camera-001"),
            DeclareLaunchArgument("event_cooldown_sec", default_value="2.0"),
            DeclareLaunchArgument("log_interval", default_value="1.0"),
            DeclareLaunchArgument("torch_num_threads", default_value="1"),
            DeclareLaunchArgument("torch_num_interop_threads", default_value="1"),
            DeclareLaunchArgument("opencv_num_threads", default_value="1"),
            DeclareLaunchArgument("enable_face_recognition", default_value="true"),
            DeclareLaunchArgument("face_fps", default_value="1.0"),
            DeclareLaunchArgument("face_result_ttl", default_value="4.0"),
            DeclareLaunchArgument("face_similarity", default_value="0.34"),
            DeclareLaunchArgument("face_temporal_frames", default_value="6"),
            DeclareLaunchArgument("face_temporal_votes", default_value="2"),
            DeclareLaunchArgument("providers", default_value="CUDAExecutionProvider CPUExecutionProvider"),
            DeclareLaunchArgument("det_size", default_value="320"),
            DeclareLaunchArgument("registered_dir", default_value="/home/gyul/registered_faces"),
            DeclareLaunchArgument("margin_threshold", default_value="0.03"),
            DeclareLaunchArgument("top_k", default_value="3"),
            DeclareLaunchArgument("face_min_roi_width", default_value="70"),
            DeclareLaunchArgument("face_min_roi_height", default_value="55"),
            DeclareLaunchArgument("face_min_roi_area_ratio", default_value="0.015"),
            DeclareLaunchArgument("face_cache_reset_gap_sec", default_value="2.5"),
            DeclareLaunchArgument(
                "server_safety_event_topic",
                default_value="/turtlebot_camera/server/safety_events",
            ),
            DeclareLaunchArgument("server_center_overlap_ratio", default_value="0.6"),
            DeclareLaunchArgument("server_fire_min_height_ratio", default_value="0.5"),
            DeclareLaunchArgument("server_fall_min_height_ratio", default_value="0.5"),
            DeclareLaunchArgument("server_no_helmet_min_height_ratio", default_value="0.03"),
            DeclareLaunchArgument("server_event_cooldown_sec", default_value="3.0"),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/turtlebot_udp_display_node",
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
                    "--window-name",
                    window_name,
                    "--show-image",
                    show_image,
                    "--display-fps",
                    display_fps,
                    "--live-fps",
                    live_fps,
                    "--live-topic",
                    live_topic,
                    "--detections-topic",
                    detections_topic,
                    "--server-safety-event-topic",
                    server_safety_event_topic,
                    "--flip-mode",
                    flip_mode,
                    "--image-qos-depth",
                    image_qos_depth,
                    "--log-interval",
                    log_interval,
                ],
                name="turtlebot_udp_display_node",
                output="both",
            ),
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/turtlebot_safety_result_node",
                    "--live-topic",
                    live_topic,
                    "--detections-topic",
                    detections_topic,
                    "--event-topic",
                    event_topic,
                    "--annotated-topic",
                    annotated_topic,
                    "--publish-annotated",
                    publish_annotated,
                    "--safety-model-path",
                    safety_model_path,
                    "--person-model-path",
                    person_model_path,
                    "--safety-device",
                    safety_device,
                    "--safety-confidence",
                    safety_confidence,
                    "--safety-imgsz",
                    safety_imgsz,
                    "--safety-roi-confidence",
                    safety_roi_confidence,
                    "--safety-roi-imgsz",
                    safety_roi_imgsz,
                    "--person-confidence",
                    person_confidence,
                    "--person-imgsz",
                    person_imgsz,
                    "--enable-person-roi-safety",
                    enable_person_roi_safety,
                    "--person-roi-expand",
                    person_roi_expand,
                    "--person-roi-max-count",
                    person_roi_max_count,
                    "--safety-dedupe-iou",
                    safety_dedupe_iou,
                    "--safety-fps",
                    safety_fps,
                    "--image-qos-depth",
                    image_qos_depth,
                    "--camera-id",
                    camera_id,
                    "--event-cooldown-sec",
                    event_cooldown_sec,
                    "--log-interval",
                    log_interval,
                    "--torch-num-threads",
                    torch_num_threads,
                    "--torch-num-interop-threads",
                    torch_num_interop_threads,
                    "--opencv-num-threads",
                    opencv_num_threads,
                    "--enable-face-recognition",
                    enable_face_recognition,
                    "--face-fps",
                    face_fps,
                    "--face-result-ttl",
                    face_result_ttl,
                    "--face-similarity",
                    face_similarity,
                    "--face-temporal-frames",
                    face_temporal_frames,
                    "--face-temporal-votes",
                    face_temporal_votes,
                    "--providers",
                    providers,
                    "--det-size",
                    det_size,
                    "--registered-dir",
                    registered_dir,
                    "--margin-threshold",
                    margin_threshold,
                    "--top-k",
                    top_k,
                    "--face-min-roi-width",
                    face_min_roi_width,
                    "--face-min-roi-height",
                    face_min_roi_height,
                    "--face-min-roi-area-ratio",
                    face_min_roi_area_ratio,
                    "--face-cache-reset-gap-sec",
                    face_cache_reset_gap_sec,
                    "--server-safety-event-topic",
                    server_safety_event_topic,
                    "--server-center-overlap-ratio",
                    server_center_overlap_ratio,
                    "--server-fire-min-height-ratio",
                    server_fire_min_height_ratio,
                    "--server-fall-min-height-ratio",
                    server_fall_min_height_ratio,
                    "--server-no-helmet-min-height-ratio",
                    server_no_helmet_min_height_ratio,
                    "--server-event-cooldown-sec",
                    server_event_cooldown_sec,
                ],
                name="turtlebot_safety_result_node",
                output="both",
            ),
        ]
    )
