from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.conditions import IfCondition, UnlessCondition
from launch.substitutions import LaunchConfiguration


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"

    image_topic = LaunchConfiguration("image_topic")
    show_viewer = LaunchConfiguration("show_viewer")
    show_remote_viewer = LaunchConfiguration("show_remote_viewer")
    window_name = LaunchConfiguration("window_name")
    viewer_window_name = LaunchConfiguration("viewer_window_name")
    max_width = LaunchConfiguration("max_width")
    roi_top_ratio = LaunchConfiguration("roi_top_ratio")
    green_s_min = LaunchConfiguration("green_s_min")
    green_v_min = LaunchConfiguration("green_v_min")
    green_h_max = LaunchConfiguration("green_h_max")
    min_line_length = LaunchConfiguration("min_line_length")
    max_segments = LaunchConfiguration("max_segments")

    detector_with_viewer = ExecuteProcess(
        cmd=[
            f"{robot_face_dir}/scripts/map_line_globalcam",
            "--image-topic",
            image_topic,
            "--window-name",
            window_name,
            "--max-width",
            max_width,
            "--roi-top-ratio",
            roi_top_ratio,
            "--green-s-min",
            green_s_min,
            "--green-v-min",
            green_v_min,
            "--green-h-max",
            green_h_max,
            "--min-line-length",
            min_line_length,
            "--max-segments",
            max_segments,
        ],
        name="map_line_detector",
        output="both",
        condition=IfCondition(show_viewer),
    )

    detector_headless = ExecuteProcess(
        cmd=[
            f"{robot_face_dir}/scripts/map_line_globalcam",
            "--image-topic",
            image_topic,
            "--no-viewer",
            "--max-width",
            max_width,
            "--roi-top-ratio",
            roi_top_ratio,
            "--green-s-min",
            green_s_min,
            "--green-v-min",
            green_v_min,
            "--green-h-max",
            green_h_max,
            "--min-line-length",
            min_line_length,
            "--max-segments",
            max_segments,
        ],
        name="map_line_detector",
        output="both",
        condition=UnlessCondition(show_viewer),
    )

    return LaunchDescription(
        [
            DeclareLaunchArgument("image_topic", default_value="/globalcam/image_raw/compressed"),
            DeclareLaunchArgument("show_viewer", default_value="true"),
            DeclareLaunchArgument("show_remote_viewer", default_value="false"),
            DeclareLaunchArgument("window_name", default_value="Green-Line-GlobalCam"),
            DeclareLaunchArgument("viewer_window_name", default_value="Green-Line-Viewer"),
            DeclareLaunchArgument("max_width", default_value="960"),
            DeclareLaunchArgument("roi_top_ratio", default_value="0.0"),
            DeclareLaunchArgument("green_s_min", default_value="25"),
            DeclareLaunchArgument("green_v_min", default_value="15"),
            DeclareLaunchArgument("green_h_max", default_value="95"),
            DeclareLaunchArgument("min_line_length", default_value="220"),
            DeclareLaunchArgument("max_segments", default_value="2"),
            detector_with_viewer,
            detector_headless,
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/map_line_viewer",
                    "--window-name",
                    viewer_window_name,
                    "--max-width",
                    max_width,
                ],
                name="map_line_viewer",
                output="both",
                condition=IfCondition(show_remote_viewer),
            ),
        ]
    )
