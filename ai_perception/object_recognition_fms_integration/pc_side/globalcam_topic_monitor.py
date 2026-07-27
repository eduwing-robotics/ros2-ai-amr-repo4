from __future__ import annotations

import argparse
import json
import time
from typing import Any

import rclpy
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from rclpy.utilities import ok as rclpy_ok
from std_msgs.msg import String


def parse_json(data: str) -> dict[str, Any] | None:
    try:
        value = json.loads(data)
    except json.JSONDecodeError:
        return None
    return value if isinstance(value, dict) else None


def fmt_bbox(detection: dict[str, Any]) -> str:
    bbox = detection.get("bbox_xyxy") or detection.get("bbox")
    if not bbox:
        return "bbox=-"
    return "bbox=[" + ",".join(str(int(round(float(v)))) for v in bbox[:4]) + "]"


def fmt_map(detection: dict[str, Any]) -> str:
    pos = detection.get("map_position")
    if not isinstance(pos, dict):
        return "map=-"
    x = pos.get("x")
    y = pos.get("y")
    inside = pos.get("inside")
    if x is None or y is None:
        return f"map=inside:{inside}"
    return f"map=({x},{y}) inside:{inside}"


def fmt_detection(index: int, detection: dict[str, Any]) -> str:
    cls = detection.get("class", "?")
    conf = detection.get("confidence")
    conf_text = f"{float(conf):.3f}" if isinstance(conf, (int, float)) else "-"
    center = detection.get("center_px")
    center_text = f"center={center}" if center is not None else "center=-"
    return (
        f"  [{index}] {cls} conf={conf_text} {fmt_bbox(detection)} "
        f"{center_text} {fmt_map(detection)}"
    )


class GlobalCamTopicMonitor(Node):
    def __init__(self, args: argparse.Namespace):
        super().__init__("globalcam_topic_monitor")
        self.args = args
        self.last_map_print = 0.0
        self.create_subscription(String, args.detections_topic, self.on_detections, 10)
        self.create_subscription(String, args.event_topic, self.on_event, 10)
        self.create_subscription(String, args.alert_topic, self.on_alert, 10)
        self.create_subscription(String, args.server_object_event_topic, self.on_server_object_event, 10)
        self.create_subscription(String, args.map_line_topic, self.on_map_line, 10)
        print("Monitoring GlobalCam topics. Ctrl-C to stop.", flush=True)
        print(f"- detections: {args.detections_topic}", flush=True)
        print(f"- safety events: {args.event_topic}", flush=True)
        print(f"- proximity alerts: {args.alert_topic}", flush=True)
        print(f"- server object events: {args.server_object_event_topic}", flush=True)
        print(f"- map line: {args.map_line_topic}", flush=True)

    def print_raw(self, topic: str, payload: dict[str, Any]) -> None:
        if self.args.raw:
            print(f"\nRAW {topic}\n{json.dumps(payload, ensure_ascii=False, indent=2)}", flush=True)

    def on_detections(self, msg: String) -> None:
        payload = parse_json(msg.data)
        if payload is None:
            print(f"\n[combined] non-json: {msg.data[:200]}", flush=True)
            return
        self.print_raw("combined", payload)
        safety = payload.get("safety_detections") or []
        turtlebots = payload.get("turtlebot_detections") or []
        proximity = payload.get("proximity") or {}
        if not self.args.show_empty and not safety and not turtlebots:
            return
        created = payload.get("created_at")
        print(
            f"\n[combined] created={created} safety={len(safety)} "
            f"turtlebot={len(turtlebots)} proximity={proximity}",
            flush=True,
        )
        for idx, detection in enumerate(safety):
            print(fmt_detection(idx, detection), flush=True)
        for idx, detection in enumerate(turtlebots):
            print(fmt_detection(idx, detection), flush=True)

    def on_event(self, msg: String) -> None:
        payload = parse_json(msg.data)
        if payload is None:
            print(f"\n[safety-event] non-json: {msg.data[:200]}", flush=True)
            return
        self.print_raw("safety-event", payload)
        detections = payload.get("safety_detections") or []
        if not self.args.show_empty_events and not detections and payload.get("state") != "detector_error":
            return
        print(
            f"\n[safety-event] id={payload.get('event_id')} created={payload.get('created_at')} "
            f"state={payload.get('state', 'ok')} detections={len(detections)} "
            f"timing={payload.get('timing')}",
            flush=True,
        )
        for idx, detection in enumerate(detections):
            print(fmt_detection(idx, detection), flush=True)

    def on_alert(self, msg: String) -> None:
        payload = parse_json(msg.data)
        if payload is None:
            print(f"\n[proximity-alert] non-json: {msg.data[:200]}", flush=True)
            return
        self.print_raw("proximity-alert", payload)
        print(
            f"\n[proximity-alert] id={payload.get('event_id')} state={payload.get('state')} "
            f"distance={payload.get('distance')} threshold={payload.get('threshold')}",
            flush=True,
        )
        print(f"  message={payload.get('message')}", flush=True)
        for item in payload.get("turtlebot_pair") or []:
            print(f"  pair[{item.get('index')}] map=({item.get('map_x')},{item.get('map_y')}) conf={item.get('confidence')}", flush=True)

    def on_server_object_event(self, msg: String) -> None:
        payload = parse_json(msg.data)
        if payload is None:
            print(f"\n[server-object-event] non-json: {msg.data[:200]}", flush=True)
            return
        self.print_raw("server-object-event", payload)
        coordinate = payload.get("coordinate") or {}
        print(
            f"\n[server-object-event] id={payload.get('event_id')} "
            f"type={payload.get('event_type')} created={payload.get('created_at')} "
            f"coord=({coordinate.get('x')},{coordinate.get('y')}) "
            f"count={payload.get('consecutive_count')} tolerance={payload.get('position_tolerance')}",
            flush=True,
        )
        detection = payload.get("last_detection") or {}
        print(f"  last {fmt_detection(0, detection).strip()}", flush=True)

    def on_map_line(self, msg: String) -> None:
        if not self.args.show_map_line:
            return
        now = time.monotonic()
        if now - self.last_map_print < self.args.map_line_interval:
            return
        self.last_map_print = now
        payload = parse_json(msg.data)
        if payload is None:
            print(f"\n[map-line] non-json: {msg.data[:200]}", flush=True)
            return
        self.print_raw("map-line", payload)
        print(
            f"\n[map-line] keys={sorted(payload.keys())} "
            f"reference={payload.get('reference_px')} square={payload.get('square_points')}",
            flush=True,
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Pretty-print GlobalCam object detection topics.")
    parser.add_argument("--detections-topic", default="/globalcam/combined/detections")
    parser.add_argument("--event-topic", default="/globalcam/object_map/events")
    parser.add_argument("--alert-topic", default="/globalcam/turtlebot_proximity/alerts")
    parser.add_argument("--server-object-event-topic", default="/globalcam/server/object_events")
    parser.add_argument("--map-line-topic", default="/globalcam/map_line")
    parser.add_argument("--show-empty", action="store_true", help="Print combined messages even when no detections exist.")
    parser.add_argument("--show-empty-events", action="store_true", help="Print safety events even when no safety detections exist.")
    parser.add_argument("--show-map-line", action="store_true", help="Print throttled map-line summaries.")
    parser.add_argument("--map-line-interval", type=float, default=2.0)
    parser.add_argument("--raw", action="store_true", help="Also print full JSON payloads.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    rclpy.init()
    node = GlobalCamTopicMonitor(args)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        node.destroy_node()
        if rclpy_ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
