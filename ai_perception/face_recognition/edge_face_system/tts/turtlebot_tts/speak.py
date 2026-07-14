#!/usr/bin/env python3
"""Create Korean neural speech and play it through an ALSA device."""

from __future__ import annotations

import argparse
import asyncio
import math
import os
import struct
import sys
import tempfile
import time
import wave
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Speak Korean text through a speaker")
    parser.add_argument("text", nargs="+", help="Text to speak")
    parser.add_argument("--device", default=os.environ.get("TTS_AUDIO_DEVICE", "default"))
    parser.add_argument("--voice", default="ko-KR-HyunsuNeural")
    parser.add_argument("--rate", default="+0%")
    parser.add_argument("--pitch", default="+0Hz")
    parser.add_argument("--volume", default="+0%")
    parser.add_argument("--alert", choices=("none", "fire", "medical"), default="none")
    return parser.parse_args()


async def create_speech(text: str, path: Path, args: argparse.Namespace) -> None:
    import edge_tts

    await edge_tts.Communicate(
        text, voice=args.voice, rate=args.rate, pitch=args.pitch, volume=args.volume
    ).save(str(path))


def create_alert_tone(alert: str, path: Path) -> bool:
    patterns = {
        "fire": ((1100, 0.18), (0, 0.08), (1100, 0.18), (0, 0.08), (1100, 0.18)),
        "medical": ((700, 0.18), (0, 0.10), (880, 0.18)),
    }
    pattern = patterns.get(alert)
    if pattern is None:
        return False

    sample_rate = 44_100
    with wave.open(str(path), "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(sample_rate)
        for frequency, duration in pattern:
            for index in range(int(sample_rate * duration)):
                sample = 0 if frequency == 0 else int(
                    16_000 * math.sin(2 * math.pi * frequency * index / sample_rate)
                )
                wav_file.writeframesraw(struct.pack("<h", sample))
    return True


def play_sound(path: Path) -> None:
    import pygame

    pygame.mixer.music.load(str(path))
    pygame.mixer.music.play()
    while pygame.mixer.music.get_busy():
        time.sleep(0.05)


def main() -> int:
    args = parse_args()
    text = " ".join(args.text).strip()
    if not text:
        print("말할 텍스트를 입력하세요.", file=sys.stderr)
        return 2

    os.environ.setdefault("SDL_AUDIODRIVER", "alsa")
    if args.device != "default":
        os.environ["AUDIODEV"] = args.device

    with tempfile.TemporaryDirectory(prefix="turtlebot_tts_") as temp_dir:
        audio_path = Path(temp_dir) / "speech.mp3"
        alert_path = Path(temp_dir) / "alert.wav"
        try:
            asyncio.run(create_speech(text, audio_path, args))
        except Exception as exc:
            print(f"음성 생성에 실패했습니다: {exc}", file=sys.stderr)
            return 1

        try:
            import pygame

            pygame.mixer.init()
            if create_alert_tone(args.alert, alert_path):
                play_sound(alert_path)
            play_sound(audio_path)
            pygame.mixer.quit()
        except Exception as exc:
            print(f"재생에 실패했습니다 ({args.device}): {exc}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
