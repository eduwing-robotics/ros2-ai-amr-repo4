# Control Tower UI Architecture

## 전체 연결 구조

```text
TurtleBot3 / Nav2 / robot sensors
                |
                v
             ROS2 nodes
                |
                v
        FastAPI control server <------ Camera / AI pipeline
          |          |                         |
          | REST     | Control WebSocket       | camera frames / AI events
          v          v                         v
                    Unity Control Tower UI
       Dashboard / Factory / Robot / Map Status / Camera·AI
```

- ROS2와 Nav2는 로봇 주행, 위치, 상태와 임무 실행을 담당한다.
- FastAPI 서버는 Unity가 직접 ROS2 토픽에 의존하지 않도록 REST와 WebSocket 경계를 제공한다.
- 카메라 스트림은 영상용 WebSocket으로 수신하고, AI 상태와 안전 이벤트는 관제 WebSocket 이벤트로 수신한다.
- Unity는 수신 데이터를 2D·3D 표시, 운영 상태, 경로, 이벤트 팝업에 반영한다.

## Unity 내부 구성

| 영역 | 책임 |
| --- | --- |
| `Bridge` | REST 명령, 관제 WebSocket, JPEG 카메라 스트림 연결 |
| `Core` | View 전환, 상태 저장, 서버 데이터 적용, 명령 및 이벤트 UI 조정 |
| `UI` | 2D·3D 로봇/인원/팔레트 표시, 경로, 카메라, 설비 동작 |
| `Editor` | 레이아웃 측정·검증, 참조 연결, 팔레트 운반 설정을 위한 Unity Editor 도구 |

`scr_ControlTowerWebSocketClient`가 메시지를 유형별 데이터로 변환하고
`scr_ControlTowerUIManager`에 전달한다. UI Manager는 로봇별 최신 상태와 경로를
보관한 뒤 각 View 컨트롤러를 갱신한다. 영상은
`scr_ControlTowerCameraStreamManager`와 `scr_CameraJpegWebSocketClient`가
수신 프레임의 실제 적용 여부까지 추적한다.

## 제어 명령 흐름

```text
Unity button / control input
        |
        v
scr_ControlTowerUIManager
        |
        v
scr_ControlTowerRobotApiClient
        |
        v
FastAPI REST endpoint
        |
        v
ROS2 command / Nav2 task
        |
        v
TurtleBot3
        |
        v
command_ack / ROBOT_STATUS -> WebSocket -> Unity
```

자동 명령은 로봇 command API, 수동 속도 명령은 teleop API로 전달한다.
Unity는 HTTP 요청 성공만으로 로봇 상태를 확정하지 않고, 이후 수신되는
`command_ack`와 `ROBOT_STATUS`를 화면 상태에 반영한다.

## 디지털 트윈 범위

이 구현은 실제 로봇의 위치·방향·배터리·주행 상태, 서버 경로와 Waypoint,
카메라 프레임, AI 안전 이벤트를 Unity 공장 화면에 반영하는 운영 시각화다.
관제 명령은 서버와 ROS2를 통해 실제 로봇으로 전달된다.

물리 공장의 모든 센서와 동역학을 복제하는 시뮬레이터는 아니다. 특히
TB3-03 지게차 리프트와 팔레트 운반은 서버가 승인한 명령을 기준으로
Unity 오브젝트의 위치와 회전을 시각화하며, 실제 리프트 높이 센서의
연속 측정값을 재현하는 기능으로 보지 않는다.
