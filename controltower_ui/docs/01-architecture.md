# 시스템 구조 (Architecture)

## 전체 연결 구조

```text
TurtleBot3 / Nav2 / Robot Sensors
                ↓
             ROS2 Nodes
                ↓
        FastAPI Control Server
          ↓ REST / WebSocket
       Unity Control Tower UI
          ↓
Dashboard / Factory / Robot / Map / Camera·AI

Camera / AI Pipeline
          ↓ Frame / Event
FastAPI Control Server
```

ROS2와 Nav2가 로봇 주행, 위치 추정과 임무 실행을 담당하도록 구현했습니다.
FastAPI 서버가 ROS2 데이터와 Unity 사이의 REST 및 WebSocket 경계를 유지합니다.
카메라 프레임과 AI 안전 이벤트는 서버를 거쳐 Unity 관제 화면에 표시합니다.

## 데이터 흐름

1. ROS2가 로봇 위치, 방향, 배터리와 임무 상태를 서버에 전달하도록 구현했습니다.
2. 서버가 로봇 상태와 경로를 WebSocket 이벤트로 전달하도록 구현했습니다.
3. Unity가 로봇별 최신 상태를 보관하고 현재 View에 표시합니다.
4. 카메라와 AI 파이프라인이 영상 상태와 안전 이벤트를 서버에 전달하도록 구현했습니다.
5. Unity가 이벤트 위치, 스냅샷 상태와 처리 결과를 일관되게 유지합니다.

## 제어 명령 흐름

```text
Unity Control Input
        ↓
FastAPI REST API
        ↓
ROS2 Command / Nav2 Task
        ↓
TurtleBot3
        ↓
Command ACK / Robot Status
        ↓
Unity Control Tower UI
```

자동 명령과 수동 속도 명령을 서버 API를 통해 적용했습니다.
Unity는 HTTP 요청만으로 상태를 확정하지 않고 후속 명령 결과와 로봇 상태를 표시합니다.

## 디지털 트윈 범위

실제 로봇의 위치·방향·배터리·주행 상태, 서버 경로, 카메라 프레임과
AI 안전 이벤트를 Unity 공장 화면에 표시합니다. 물리 공장의 모든 센서와
동역학을 복제하지 않으며, TB3-03 리프트와 팔레트는 서버가 승인한 명령을
기준으로 Unity 동작을 표시합니다.
