"""Play fixed safety announcements received through ROS 2 topics."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from queue import Empty, Full, Queue
from subprocess import DEVNULL, PIPE, run
from threading import Event, Thread

import rclpy
from rclpy.node import Node
from std_msgs.msg import String


@dataclass(frozen=True)
class Announcement:
    text: str
    voice: str = "ko-KR-HyunsuNeural"
    rate: str = "+0%"
    pitch: str = "+0Hz"
    volume: str = "+0%"
    alert: str = "none"


class TtsNode(Node):
    """Serially play scenario-specific Korean announcements."""

    SCENARIO_ANNOUNCEMENTS = {
        "/fire": Announcement("화재 발생! 화재 발생! 즉시 대피하십시오!", rate="+45%", pitch="+25Hz", volume="+35%", alert="fire"),
        "/worker_down": Announcement("응급상황입니다. 작업자가 쓰러졌습니다. 즉시 확인해 주십시오!", rate="+30%", pitch="+12Hz", volume="+20%", alert="medical"),
    }
    HELMET_NAMES = {"001": "백은주", "002": "유예린", "003": "송한결", "004": "김성엽"}

    def __init__(self) -> None:
        super().__init__("tb3_tts")
        self.declare_parameter("audio_device", "default")
        venv_python = Path.home() / "turtlebot_tts" / ".venv" / "bin" / "python"
        self.declare_parameter(
            "python_executable",
            str(venv_python) if venv_python.exists() else "/usr/bin/python3",
        )
        self.declare_parameter("speaker_script", str(Path.home() / "turtlebot_tts" / "speak.py"))
        self._audio_device = self.get_parameter("audio_device").value
        self._python = self.get_parameter("python_executable").value
        self._speaker_script = self.get_parameter("speaker_script").value
        self._queue: Queue[Announcement] = Queue(maxsize=10)
        self._stop_event = Event()
        self._worker = Thread(target=self._speak_worker, daemon=True)
        self._worker.start()
        self._subscriptions = [
            self.create_subscription(String, topic, lambda _msg, topic=topic, announcement=announcement: self._queue_announcement(topic, announcement), 10)
            for topic, announcement in self.SCENARIO_ANNOUNCEMENTS.items()
        ]
        self._subscriptions.append(self.create_subscription(String, "/helmet_missing", self._on_helmet_missing, 10))
        self.get_logger().info("Listening on /fire, /worker_down, /helmet_missing; audio device: %s" % self._audio_device)

    def _on_helmet_missing(self, message: String) -> None:
        name = self.HELMET_NAMES.get(message.data.strip())
        if name is None:
            self.get_logger().warning("Ignored unknown helmet-missing ID: %r" % message.data)
            return
        self._queue_announcement("/helmet_missing", Announcement(f"{name}님 안전모를 착용해주십시오.", rate="-5%", pitch="-5Hz"))

    def _queue_announcement(self, topic: str, announcement: Announcement) -> None:
        try:
            self._queue.put_nowait(announcement)
            self.get_logger().info(f"Queued {topic}: {announcement.text}")
        except Full:
            self.get_logger().warning("TTS queue is full; ignored the newest message")

    def _speak_worker(self) -> None:
        while not self._stop_event.is_set():
            try:
                announcement = self._queue.get(timeout=0.2)
            except Empty:
                continue
            result = run([self._python, self._speaker_script, "--device", self._audio_device, f"--voice={announcement.voice}", f"--rate={announcement.rate}", f"--pitch={announcement.pitch}", f"--volume={announcement.volume}", f"--alert={announcement.alert}", announcement.text], stdin=DEVNULL, stdout=PIPE, stderr=PIPE, text=True, check=False)
            if result.returncode:
                self.get_logger().error("TTS playback failed: %s" % (result.stderr.strip() or result.stdout.strip()))
            self._queue.task_done()

    def destroy_node(self) -> bool:
        self._stop_event.set()
        self._worker.join(timeout=1.0)
        return super().destroy_node()


def main() -> None:
    rclpy.init()
    node = TtsNode()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()
