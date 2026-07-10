from __future__ import annotations

import argparse
import json
import sqlite3
import time
import uuid
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import rclpy
import yaml
from rclpy.executors import ExternalShutdownException
from rclpy.node import Node
from std_msgs.msg import String


INTERESTING_STATES = {"confirmed_known", "confirmed_unknown", "spoof_rejected", "model_error", "person_detector_error"}


DEFAULT_CONFIG_PATH = "/home/gyul/robot_face_system/config/edge_local.yaml"


def nested_config(config, path, default):
    value = config
    for key in path.split("."):
        if not isinstance(value, dict) or key not in value:
            return default
        value = value[key]
    return value


def load_edge_config(path):
    try:
        with Path(path).open("r", encoding="utf-8") as file:
            return yaml.safe_load(file) or {}
    except FileNotFoundError:
        return {}


class EventQueue:
    def __init__(self, path: str):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.path)
        self.conn.execute(
            """
            CREATE TABLE IF NOT EXISTS pending_events (
                event_id TEXT PRIMARY KEY,
                payload TEXT NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                created_at REAL NOT NULL,
                updated_at REAL NOT NULL
            )
            """
        )
        self.conn.commit()

    def put(self, event: dict, error: str | None = None):
        payload = json.dumps(event, ensure_ascii=False)
        now = time.time()
        self.conn.execute(
            """
            INSERT INTO pending_events(event_id, payload, retry_count, last_error, created_at, updated_at)
            VALUES (?, ?, 0, ?, ?, ?)
            ON CONFLICT(event_id) DO UPDATE SET
                payload=excluded.payload,
                last_error=excluded.last_error,
                updated_at=excluded.updated_at
            """,
            (event["event_id"], payload, error, now, now),
        )
        self.conn.commit()

    def peek(self, limit: int):
        cur = self.conn.execute(
            "SELECT event_id, payload FROM pending_events ORDER BY created_at ASC LIMIT ?",
            (limit,),
        )
        for event_id, payload in cur.fetchall():
            yield event_id, json.loads(payload)

    def mark_sent(self, event_id: str):
        self.conn.execute("DELETE FROM pending_events WHERE event_id = ?", (event_id,))
        self.conn.commit()

    def mark_failed(self, event_id: str, error: str):
        self.conn.execute(
            """
            UPDATE pending_events
            SET retry_count = retry_count + 1, last_error = ?, updated_at = ?
            WHERE event_id = ?
            """,
            (error, time.time(), event_id),
        )
        self.conn.commit()


class ResultUploader(Node):
    def __init__(self, args):
        super().__init__("robot_face_result_uploader")
        self.args = args
        self.queue = EventQueue(args.queue_path)
        self.local_log_path = Path(args.local_log_path)
        self.local_log_path.parent.mkdir(parents=True, exist_ok=True)
        self.subscription = self.create_subscription(String, args.topic, self.on_event, 10)
        self.timer = self.create_timer(args.retry_interval, self.flush_queue)
        self.session_cache = None
        self.session_cache_expires_at = 0.0
        mode = "dry-run" if args.dry_run else args.server_url
        self.get_logger().info(f"Uploading {args.topic} events to {mode}")

    def should_upload(self, event: dict) -> bool:
        if self.args.upload_all:
            return True
        return event.get("state") in INTERESTING_STATES

    def normalize_event(self, event: dict) -> dict:
        event = dict(event)
        now_iso = datetime.now(timezone.utc).isoformat()
        event.setdefault("schema_version", "robot_face_event.v1")
        event.setdefault("event_id", f"evt_{uuid.uuid4().hex}")
        event.setdefault("device_id", self.args.device_id)
        event.setdefault("source", "edge_ros")
        event.setdefault("created_at", now_iso)
        event.setdefault("timestamp", event.get("created_at") or now_iso)
        subject = event.get("subject") if isinstance(event.get("subject"), dict) else {}
        user = event.get("user") if isinstance(event.get("user"), dict) else {}
        name = event.get("name") or subject.get("display_name") or user.get("name")
        number = event.get("number") or subject.get("person_id") or user.get("number")
        identity_key = event.get("identity_key") or subject.get("identity_key") or user.get("identity_key")
        if name or number:
            event.setdefault("user", {"name": name, "number": number})
        if identity_key or number or name:
            event.setdefault(
                "subject",
                {
                    "person_id": number,
                    "display_name": name,
                    "identity_key": identity_key,
                },
            )
        event.setdefault("user_id", number)
        return event

    def log_local_event(self, status: str, event: dict, error: str | None = None):
        entry = {
            "logged_at": datetime.now(timezone.utc).isoformat(),
            "status": status,
            "event_id": event.get("event_id"),
            "state": event.get("state"),
            "device_id": event.get("device_id"),
            "session_id": event.get("session_id"),
            "mode": event.get("mode"),
            "user_id": event.get("user_id"),
            "error": error,
        }
        with self.local_log_path.open("a", encoding="utf-8") as file:
            file.write(json.dumps(entry, ensure_ascii=False) + "\n")

    def on_event(self, msg: String):
        try:
            event = json.loads(msg.data)
        except json.JSONDecodeError as exc:
            self.get_logger().warning(f"Ignoring invalid event JSON: {exc}")
            return
        event = self.normalize_event(event)
        if not self.should_upload(event):
            return
        if self.args.dry_run:
            self.log_local_event("dry_run", event)
            self.get_logger().info(json.dumps({"dry_run_event": event}, ensure_ascii=False))
            return
        if not self.args.server_url:
            self.queue.put(event, "server_url_not_configured")
            self.log_local_event("queued", event, "server_url_not_configured")
            return
        event = self.attach_active_session(event)
        if self.args.only_active_session and not event.get("session_id"):
            return
        try:
            self.post_event(event)
            self.log_local_event("uploaded", event)
        except Exception as exc:
            self.queue.put(event, str(exc))
            self.log_local_event("queued", event, str(exc))
            self.get_logger().warning(f"Queued event {event['event_id']}: {exc}")

    def active_session_url(self):
        return self.args.session_url.rstrip("/") + self.args.session_path

    def fetch_active_session(self):
        if not self.args.session_url or not self.args.session_path:
            return None
        now = time.monotonic()
        if now < self.session_cache_expires_at:
            return self.session_cache
        request = urllib.request.Request(self.active_session_url(), method="GET")
        if self.args.token:
            request.add_header("Authorization", f"Bearer {self.args.token}")
        with urllib.request.urlopen(request, timeout=self.args.timeout) as response:
            if response.status >= 300:
                raise RuntimeError(f"HTTP {response.status}")
            payload = json.loads(response.read().decode("utf-8"))
        if not payload.get("active"):
            self.session_cache = None
        else:
            session = payload.get("session")
            self.session_cache = session if isinstance(session, dict) else None
        self.session_cache_expires_at = now + self.args.session_cache_seconds
        return self.session_cache

    def attach_active_session(self, event: dict):
        event = dict(event)
        try:
            session = self.fetch_active_session()
        except Exception as exc:
            self.get_logger().warning(f"Active auth session lookup failed: {exc}")
            return event
        if not session:
            return event
        event.setdefault("session_id", session.get("session_id"))
        event.setdefault("mode", session.get("mode"))
        event.setdefault("session_started_at", session.get("started_at"))
        event.setdefault("session_expires_at", session.get("expires_at"))
        event.setdefault("session_status", session.get("status"))
        subject = event.get("subject") or {}
        user = event.get("user") or {}
        event.setdefault("user_id", subject.get("person_id") or user.get("number") or event.get("number"))
        return event

    def post_event(self, event: dict):
        body = json.dumps(event, ensure_ascii=False).encode("utf-8")
        url = self.args.server_url.rstrip("/") + self.args.events_path
        request = urllib.request.Request(
            url,
            data=body,
            method="POST",
            headers={"Content-Type": "application/json"},
        )
        if self.args.token:
            request.add_header("Authorization", f"Bearer {self.args.token}")
        with urllib.request.urlopen(request, timeout=self.args.timeout) as response:
            if response.status >= 300:
                raise RuntimeError(f"HTTP {response.status}")

    def flush_queue(self):
        if self.args.dry_run or not self.args.server_url:
            return
        for event_id, event in self.queue.peek(self.args.retry_batch_size):
            event = self.attach_active_session(event)
            try:
                self.post_event(event)
            except (urllib.error.URLError, TimeoutError, RuntimeError) as exc:
                self.queue.mark_failed(event_id, str(exc))
                self.log_local_event("queue_retry_failed", event, str(exc))
                return
            self.queue.mark_sent(event_id)
            self.log_local_event("uploaded_from_queue", event)
            self.get_logger().info(f"Uploaded queued event {event_id}")


def parse_args():
    pre_parser = argparse.ArgumentParser(add_help=False)
    pre_parser.add_argument("--config", default=DEFAULT_CONFIG_PATH)
    pre_args, _ = pre_parser.parse_known_args()
    config = load_edge_config(pre_args.config)

    parser = argparse.ArgumentParser(description="Upload face recognition events to a future backend server.", parents=[pre_parser])
    parser.add_argument("--topic", default=str(nested_config(config, "ros.state_topic", "/face/recognition_state")))
    parser.add_argument("--server-url", default=str(nested_config(config, "server.url", "")), help="Backend base URL, e.g. https://api.example.com")
    parser.add_argument("--session-url", default=str(nested_config(config, "uploader.session_url", "http://127.0.0.1:8090")), help="Local edge UI base URL used to query the active attendance session")
    parser.add_argument("--device-id", default=str(nested_config(config, "device.id", "edge-dev-001")), help="Stable device ID attached when incoming events omit one")
    parser.add_argument("--events-path", default="/api/recognition-events")
    parser.add_argument("--session-path", default=str(nested_config(config, "uploader.session_path", "/api/attendance/session")))
    parser.add_argument("--token", default="")
    parser.add_argument("--queue-path", default=str(nested_config(config, "uploader.queue_path", "data/pending_events.sqlite")))
    parser.add_argument("--local-log-path", default=str(nested_config(config, "uploader.local_log_path", "data/uploader_events.jsonl")))
    parser.add_argument("--retry-interval", type=float, default=5.0)
    parser.add_argument("--retry-batch-size", type=int, default=20)
    parser.add_argument("--timeout", type=float, default=2.0)
    parser.add_argument("--session-cache-seconds", type=float, default=0.5)
    parser.add_argument("--only-active-session", action="store_true", default=bool(nested_config(config, "uploader.only_active_session", True)), help="Only upload events while an auth session is active")
    parser.add_argument("--upload-all", action="store_true", help="Upload transient states such as searching/quick_recognition too")
    parser.add_argument("--dry-run", action="store_true", help="Log uploadable events without sending HTTP requests")
    return parser.parse_args()


def main():
    args = parse_args()
    rclpy.init()
    node = ResultUploader(args)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        if rclpy.ok():
            node.destroy_node()
            rclpy.shutdown()


if __name__ == "__main__":
    main()
