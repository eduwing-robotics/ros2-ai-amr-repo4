from __future__ import annotations

import argparse
import json
import time
import uuid
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import cv2
import yaml


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


def utc_now_iso():
    return datetime.now(timezone.utc).isoformat()


def make_multipart(fields: dict[str, str], files: dict[str, tuple[str, bytes, str]]):
    boundary = f"----robot-face-cctv-{uuid.uuid4().hex}"
    chunks: list[bytes] = []
    for name, value in fields.items():
        chunks.append(f"--{boundary}\r\n".encode("utf-8"))
        chunks.append(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode("utf-8"))
        chunks.append(str(value).encode("utf-8"))
        chunks.append(b"\r\n")
    for name, (filename, content, content_type) in files.items():
        chunks.append(f"--{boundary}\r\n".encode("utf-8"))
        chunks.append(
            (
                f'Content-Disposition: form-data; name="{name}"; '
                f'filename="{filename}"\r\n'
                f"Content-Type: {content_type}\r\n\r\n"
            ).encode("utf-8")
        )
        chunks.append(content)
        chunks.append(b"\r\n")
    chunks.append(f"--{boundary}--\r\n".encode("utf-8"))
    return boundary, b"".join(chunks)


class CctvSender:
    def __init__(self, args):
        self.args = args
        self.last_image_path = Path(args.last_image_path) if args.last_image_path else None
        self.local_log_path = Path(args.local_log_path) if args.local_log_path else None
        if self.last_image_path:
            self.last_image_path.parent.mkdir(parents=True, exist_ok=True)
        if self.local_log_path:
            self.local_log_path.parent.mkdir(parents=True, exist_ok=True)

    def log(self, status: str, error: str | None = None, **extra):
        entry = {
            "logged_at": utc_now_iso(),
            "status": status,
            "device_id": self.args.device_id,
            "camera_id": self.args.camera_id,
            "error": error,
            **extra,
        }
        line = json.dumps(entry, ensure_ascii=False)
        print(line, flush=True)
        if self.local_log_path:
            with self.local_log_path.open("a", encoding="utf-8") as file:
                file.write(line + "\n")

    def open_camera(self):
        source = self.args.camera
        if source.isdigit():
            source = int(source)
        cap = cv2.VideoCapture(source)
        if self.args.width > 0:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.args.width)
        if self.args.height > 0:
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.args.height)
        if self.args.fps > 0:
            cap.set(cv2.CAP_PROP_FPS, self.args.fps)
        cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        if not cap.isOpened():
            raise RuntimeError(f"Could not open CCTV camera: {self.args.camera}")
        return cap

    def encode_frame(self, frame):
        encode_params = [int(cv2.IMWRITE_JPEG_QUALITY), self.args.jpeg_quality]
        ok, encoded = cv2.imencode(".jpg", frame, encode_params)
        if not ok:
            raise RuntimeError("JPEG encoding failed")
        return encoded.tobytes()

    def post_image(self, captured_at: str, image: bytes):
        fields = {
            "device_id": self.args.device_id,
            "camera_id": self.args.camera_id,
            "captured_at": captured_at,
        }
        files = {
            "image": (
                f"{self.args.camera_id}_{captured_at.replace(':', '').replace('+', 'Z')}.jpg",
                image,
                "image/jpeg",
            )
        }
        boundary, body = make_multipart(fields, files)
        url = self.args.server_url.rstrip("/") + self.args.images_path
        request = urllib.request.Request(
            url,
            data=body,
            method="POST",
            headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        )
        if self.args.token:
            request.add_header("Authorization", f"Bearer {self.args.token}")
        with urllib.request.urlopen(request, timeout=self.args.timeout) as response:
            if response.status >= 300:
                raise RuntimeError(f"HTTP {response.status}")

    def read_frame(self, cap):
        last_ok = False
        frame = None
        for _ in range(max(1, self.args.frame_read_retries)):
            last_ok, frame = cap.read()
            if last_ok and frame is not None:
                return frame
            time.sleep(self.args.frame_retry_sleep)
        raise RuntimeError("Camera frame capture failed")

    def run_once(self, cap):
        frame = self.read_frame(cap)
        captured_at = utc_now_iso()
        image = self.encode_frame(frame)
        if self.last_image_path:
            self.last_image_path.write_bytes(image)
        if self.args.dry_run:
            self.log("dry_run", bytes=len(image), captured_at=captured_at)
            return
        if not self.args.server_url:
            self.log("skipped", "server_url_not_configured", bytes=len(image), captured_at=captured_at)
            return
        self.post_image(captured_at, image)
        self.log("uploaded", bytes=len(image), captured_at=captured_at)

    def run(self):
        cap = self.open_camera()
        self.log("started", camera=self.args.camera, interval_sec=self.args.interval)
        try:
            while True:
                started = time.monotonic()
                try:
                    self.run_once(cap)
                except (urllib.error.URLError, TimeoutError, RuntimeError) as exc:
                    self.log("failed", str(exc))
                if self.args.once:
                    return
                elapsed = time.monotonic() - started
                time.sleep(max(0.0, self.args.interval - elapsed))
        finally:
            cap.release()


def parse_args():
    pre_parser = argparse.ArgumentParser(add_help=False)
    pre_parser.add_argument("--config", default=DEFAULT_CONFIG_PATH)
    pre_args, _ = pre_parser.parse_known_args()
    config = load_edge_config(pre_args.config)

    parser = argparse.ArgumentParser(description="Capture GlobalCam frames and upload them as CCTV JPEG images.", parents=[pre_parser])
    parser.add_argument("--camera", default=str(nested_config(config, "cctv.camera", "/dev/video0")))
    parser.add_argument("--device-id", default=str(nested_config(config, "device.id", "edge-dev-001")))
    parser.add_argument("--camera-id", default=str(nested_config(config, "cctv.camera_id", "globalcam-001")))
    parser.add_argument("--server-url", default=str(nested_config(config, "cctv.server_url", "")))
    parser.add_argument("--images-path", default=str(nested_config(config, "cctv.images_path", "/api/v1/cctv/images")))
    parser.add_argument("--token", default=str(nested_config(config, "cctv.token", "")))
    parser.add_argument("--interval", type=float, default=float(nested_config(config, "cctv.interval_sec", 3.0)))
    parser.add_argument("--width", type=int, default=int(nested_config(config, "cctv.width", 640)))
    parser.add_argument("--height", type=int, default=int(nested_config(config, "cctv.height", 480)))
    parser.add_argument("--fps", type=float, default=float(nested_config(config, "cctv.fps", 10.0)))
    parser.add_argument("--jpeg-quality", type=int, default=int(nested_config(config, "cctv.jpeg_quality", 80)))
    parser.add_argument("--timeout", type=float, default=float(nested_config(config, "cctv.timeout_sec", 3.0)))
    parser.add_argument("--frame-read-retries", type=int, default=int(nested_config(config, "cctv.frame_read_retries", 10)))
    parser.add_argument("--frame-retry-sleep", type=float, default=float(nested_config(config, "cctv.frame_retry_sleep", 0.05)))
    parser.add_argument("--last-image-path", default=str(nested_config(config, "cctv.last_image_path", "/tmp/globalcam_cctv_latest.jpg")))
    parser.add_argument("--local-log-path", default=str(nested_config(config, "cctv.local_log_path", "data/cctv_sender_events.jsonl")))
    parser.add_argument("--dry-run", action="store_true", default=bool(nested_config(config, "cctv.dry_run", False)))
    parser.add_argument("--once", action="store_true", help="Capture and upload one frame, then exit")
    return parser.parse_args()


def main():
    args = parse_args()
    CctvSender(args).run()


if __name__ == "__main__":
    main()
