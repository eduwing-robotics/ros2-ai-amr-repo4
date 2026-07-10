from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, ExecuteProcess
from launch.conditions import IfCondition, UnlessCondition
from launch.substitutions import LaunchConfiguration


def generate_launch_description():
    robot_face_dir = "/home/gyul/robot_face_system"

    image_topic = LaunchConfiguration("image_topic")
    dictionary = LaunchConfiguration("dictionary")
    show_viewer = LaunchConfiguration("show_viewer")
    show_remote_viewer = LaunchConfiguration("show_remote_viewer")
    window_name = LaunchConfiguration("window_name")
    viewer_window_name = LaunchConfiguration("viewer_window_name")
    max_width = LaunchConfiguration("max_width")

    detector_with_viewer = ExecuteProcess(
        cmd=[
            f"{robot_face_dir}/scripts/aruco_globalcam",
            "--image-topic",
            image_topic,
            "--dictionary",
            dictionary,
            "--window-name",
            window_name,
            "--max-width",
            max_width,
        ],
        name="aruco_detector",
        output="both",
        condition=IfCondition(show_viewer),
    )

    detector_headless = ExecuteProcess(
        cmd=[
            f"{robot_face_dir}/scripts/aruco_globalcam",
            "--image-topic",
            image_topic,
            "--dictionary",
            dictionary,
            "--no-viewer",
            "--max-width",
            max_width,
        ],
        name="aruco_detector",
        output="both",
        condition=UnlessCondition(show_viewer),
    )

    return LaunchDescription(
        [
            DeclareLaunchArgument("image_topic", default_value="/globalcam/image_raw/compressed"),
            DeclareLaunchArgument("dictionary", default_value="4x4_50"),
            DeclareLaunchArgument("show_viewer", default_value="true"),
            DeclareLaunchArgument("show_remote_viewer", default_value="false"),
            DeclareLaunchArgument("window_name", default_value="ArUco-GlobalCam"),
            DeclareLaunchArgument("viewer_window_name", default_value="ArUco-Viewer"),
            DeclareLaunchArgument("max_width", default_value="960"),
            detector_with_viewer,
            detector_headless,
            ExecuteProcess(
                cmd=[
                    f"{robot_face_dir}/scripts/aruco_viewer",
                    "--window-name",
                    viewer_window_name,
                    "--max-width",
                    max_width,
                ],
                name="aruco_viewer",
                output="both",
                condition=IfCondition(show_remote_viewer),
            ),
        ]
    )
