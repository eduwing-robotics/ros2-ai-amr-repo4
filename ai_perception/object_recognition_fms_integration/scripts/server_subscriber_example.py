#!/usr/bin/env python3
from __future__ import annotations

import json

import rclpy
from rclpy.node import Node
from std_msgs.msg import String


class GlobalCamServerSubscriber(Node):
    def __init__(self) -> None:
        super().__init__("globalcam_server_subscriber")
        self.create_subscription(
            String,
            "/globalcam/server/object_events",
            self.on_object_event,
            10,
        )
        self.create_subscription(
            String,
            "/globalcam/turtlebot_goal/coordinates",
            self.on_goal,
            10,
        )
        self.create_subscription(
            String,
            "/globalcam/turtlebot_proximity/alerts",
            self.on_proximity,
            10,
        )
        self.get_logger().info("GlobalCam server topics connected.")

    def parse(self, msg: String, topic_name: str) -> dict | None:
        try:
            payload = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().error(f"Invalid JSON from {topic_name}: {exc}")
            return None
        if not isinstance(payload, dict):
            self.get_logger().error(f"Invalid payload from {topic_name}: expected object")
            return None
        return payload

    def on_object_event(self, msg: String) -> None:
        payload = self.parse(msg, "/globalcam/server/object_events")
        if payload is None:
            return
        print("[object-event]", json.dumps(payload, ensure_ascii=False), flush=True)
        # TODO: Call the backend HTTP/MQTT/DB adapter here.

    def on_goal(self, msg: String) -> None:
        payload = self.parse(msg, "/globalcam/turtlebot_goal/coordinates")
        if payload is None:
            return
        print("[turtlebot-goal]", json.dumps(payload, ensure_ascii=False), flush=True)
        # TODO: Send the goal to the robot/task server here.

    def on_proximity(self, msg: String) -> None:
        payload = self.parse(msg, "/globalcam/turtlebot_proximity/alerts")
        if payload is None:
            return
        print("[proximity-alert]", json.dumps(payload, ensure_ascii=False), flush=True)
        # TODO: Persist or forward the alert here.


def main() -> None:
    rclpy.init()
    node = GlobalCamServerSubscriber()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
