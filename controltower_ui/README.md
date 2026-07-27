# TB3 Control Tower UI

## 프로젝트 개요

Unity 기반 스마트 공장 순찰 로봇 통합 관제 UI입니다.

실제 TurtleBot3의 상태, 위치, 순찰 경로, 카메라 영상과 AI 안전 이벤트를
Unity 2D·3D 공장 화면에 표시하고,
Unity 관제 화면에서 실제 로봇 제어 명령을 전달하도록 구현했습니다.

이 문서는 팀 전체 프로젝트가 아닌 김성엽이 담당한 Unity Control Tower UI의 구현 범위를 소개합니다.

## 담당자 및 담당 범위

담당자: 김성엽

- Unity 관제 UI 구조 설계 및 구현
- Dashboard / Factory / Robot / Map / Camera View 개발
- REST API / WebSocket / Camera Stream 연동
- 로봇 상태와 위치 시각화
- Waypoint 및 순찰 경로 시각화
- AI 이벤트 Popup / Snapshot / 운영 로그 연동
- 수동 주행, 긴급정지, 충전소 복귀
- TB3-03 지게차 리프트 및 팔레트 운반 시각화
- 화면 전환, 경로, 카메라 상태 유지 안정화

## 주요 기능

### Dashboard View

- 공장 운영 현황 요약
- 선택 로봇 상태와 배터리
- 출근자·퇴근자·방문자 현황
- 카메라·AI·시스템 상태
- 최근 운영 로그

### Factory View

- 2D 공장맵
- 3D 상단·정면·측면 시점
- 로봇과 설비 위치 표시
- 미니맵과 Global CCTV
- 이벤트 위치와 팝업 표시

### Robot View

- 선택 로봇의 현재 동작 상태
- 배터리·속도·위치
- 상태 변경 이력과 명령 응답
- 수동 주행 및 긴급정지
- 충전소 복귀
- TB3-03 리프트 제어 명령 및 Unity 동작 시각화

### Map Status View

- 로봇 위치와 방향
- 현재·다음·완료 Waypoint
- 현재 주행 구간
- Nav2 임무 상태
- 장애물 및 복구 상태

### Camera·AI View

- Global CCTV
- TB3-01 및 TB3-02 카메라
- 안전모 미착용 감지
- 화재 감지
- 쓰러짐 감지
- 이벤트 정보와 현장 스냅샷
- 실제 영상 수신 상태 표시

## 시스템 연동 구조

```text
TurtleBot3 / Camera / AI
        ↓
ROS2 / FastAPI Server
        ↓
REST API / WebSocket / Camera Stream
        ↓
Unity Control Tower UI
        ↓
Dashboard / Factory / Robot / Map / Camera / Event

제어 흐름:

Unity Control Command
        ↓
Server / ROS2
        ↓
TurtleBot3
```

## 디지털 트윈 구현 범위

- 실제 로봇 위치와 방향을 Unity 2D·3D 공장에 반영
- 실제 로봇의 상태, 배터리와 속도 표시
- 서버 순찰 경로와 Waypoint 표시
- Global CCTV 및 TB3 카메라 영상 표시
- AI 안전 이벤트와 발생 위치 표시
- Unity 관제 명령을 서버와 ROS2를 통해 실제 로봇에 전달
- TB3-03 리프트는 실제 높이 센서값을 실시간으로 수신해 반영한 것이 아니라, 서버가 승인한 제어 명령을 기준으로 Unity 3D 동작을 시각화

## 이벤트 처리 흐름

카메라 위험 감지 → 서버 이벤트 수신 → 실시간 팝업 표시 → 상세 정보와 현장 이미지 확인 → 확인 또는 조치 완료 → 알림 목록과 운영 로그 반영

## 구현 중 해결한 문제

### 화면 전환 후 로봇 위치 초기화

**문제:**

View 재진입 시 로봇이 이전 위치 또는 초기 위치에 표시됨

**해결:**

로봇별 마지막 정상 위치와 방향을 저장하고 화면 활성화 시 즉시 적용

**결과:**

화면 전환 첫 프레임부터 최신 로봇 위치 표시

### 일부 경로 정보 누락

**문제:**

불완전한 새 경로 정보가 기존 정상 경로를 덮어씀

**해결:**

로봇별 마지막 정상 경로와 Waypoint 상태를 유지하고 정상 데이터만 갱신

**결과:**

현재·다음·완료 Waypoint와 주행 구간이 안정적으로 유지됨

### 카메라 연결 상태 불일치

**문제:**

통신은 연결됐지만 실제 영상이 표시되지 않는 경우 발생

**해결:**

통신 연결 여부가 아니라 실제 영상이 Unity 화면에 들어왔는지를 정상 기준으로 사용

**결과:**

카메라 연결 표시와 실제 영상 상태의 일치도 개선

## 사용 기술

- Unity 6
- C#
- TextMeshPro
- REST API
- WebSocket
- Camera Stream
- ROS2
- FastAPI
- TurtleBot3
- Nav2
- SLAM

## 대표 화면

대표 화면 이미지는 최종 캡처 후
controltower_ui/docs/images 경로에 추가할 예정입니다.

예정 이미지:

- controltower-overview.png
- dashboard-view.png
- factory-view.png
- robot-view.png
- map-status-view.png
- camera-ai-view.png
- safety-event.png

## 시연 영상

최종 시연 영상은 추후 추가할 예정입니다.

## 팀 프로젝트 안내

이 문서는 팀 통합 프로젝트 중 Unity Control Tower UI 담당 파트를 정리한 문서입니다.

AI 인식, 서버·데이터베이스, 하드웨어와 SLAM·내비게이션 구현은
저장소의 `ai_perception`, `server_db`, `hardware`, `slam_navigation` 폴더에서 각 담당 내용을 확인할 수 있습니다.
