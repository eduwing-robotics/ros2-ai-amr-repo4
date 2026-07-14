# 안전 안내방송 TTS

서버가 안전 이벤트 토픽을 발행하면 이 노드가 한국어 음성을 생성해 USB 스피커로 재생합니다. 음성 생성은 Microsoft Edge TTS를 사용하므로 **인터넷 연결이 필요**합니다.

## 토픽 계약

| 토픽 | 형식 | 재생 내용 |
| --- | --- | --- |
| `/fire` | `std_msgs/msg/String` | 메시지 내용과 무관하게 화재 대피 안내 |
| `/worker_down` | `std_msgs/msg/String` | 메시지 내용과 무관하게 응급상황 안내 |
| `/helmet_missing` | `std_msgs/msg/String` | `001`~`004` 작업자 ID에 맞춘 안전모 착용 안내 |

알 수 없는 작업자 ID는 음성을 내지 않고 경고 로그만 남깁니다. 메시지는 최대 10개까지 FIFO 순서로 재생됩니다.

## 노트북 설치

아래 경로는 이 저장소를 `~/ros2-ai-amr-repo4`에 clone한 경우입니다. 다른 위치라면 경로만 바꾸면 됩니다.

```bash
sudo apt update
sudo apt install -y python3-venv python3-pygame

mkdir -p ~/turtlebot_tts
cp ~/ros2-ai-amr-repo4/ai_perception/face_recognition/edge_face_system/tts/turtlebot_tts/{speak.py,requirements.txt} ~/turtlebot_tts/
python3 -m venv ~/turtlebot_tts/.venv
~/turtlebot_tts/.venv/bin/python -m pip install --upgrade pip
~/turtlebot_tts/.venv/bin/python -m pip install -r ~/turtlebot_tts/requirements.txt

mkdir -p ~/turtlebot3_ws/src
cp -r ~/ros2-ai-amr-repo4/ai_perception/face_recognition/edge_face_system/tts/ros2_ws/src/tb3_tts ~/turtlebot3_ws/src/
source /opt/ros/jazzy/setup.bash
cd ~/turtlebot3_ws
colcon build --packages-select tb3_tts --symlink-install
```

USB 스피커 장치는 `aplay -l`로 확인합니다. 예를 들어 카드 1, 장치 0이면 `hw:1,0`입니다.

```bash
source /opt/ros/jazzy/setup.bash
source ~/turtlebot3_ws/install/setup.bash
ros2 run tb3_tts tts_node --ros-args -p audio_device:=hw:1,0
```

시험 발행:

```bash
ros2 topic pub --once /fire std_msgs/msg/String "{data: 'detected'}"
ros2 topic pub --once /helmet_missing std_msgs/msg/String "{data: '001'}"
```

서버와 노트북은 같은 네트워크 및 같은 `ROS_DOMAIN_ID`를 사용해야 합니다.

## 자동 실행

`systemd/tb3-tts.service`와 `scripts/run_tb3_tts`는 운영 장비가 `/home/gyul/robot_face_system`, `~/turtlebot3_ws`, USB 장치 `hw:1,0`을 사용하는 것을 전제로 합니다. 다른 장비는 사용자·경로·`TTS_AUDIO_DEVICE` 값을 맞춘 뒤 systemd user service로 설치합니다.
