# 05. Validation

## 최종 검증 요약

| 검증 영역 | 확인 내용 | 결과 |
|:---|:---|:---|
| 로봇 상태 | Pose·방향·배터리·속도·FSM을 선택 로봇 기준으로 표시 | 확인 |
| Route·Waypoint | 현재·다음·완료 Waypoint와 Route 진행 상태 표시 | 확인 |
| Camera Stream | Global·TB3-01·TB3-02 독립 수신과 Main Feed·Preview 전환 | 확인 |
| 안전 이벤트 | AI 이벤트 Popup·Snapshot·최근 이벤트·Timeline 반영 | 확인 |
| 운영자 명령 | 요청 결과와 `command_ack`·후속 `ROBOT_STATUS` 구분 | 확인 |
| 수동 제어 | Manual Enter/Exit, Hold Teleop, Pointer Up STOP 처리 | 확인 |
| Factory View | 로봇·인원·팔레트 위치를 2D·3D 화면에 반영 | 확인 |
| 지게차·팔레트 | TB3-03 Lift와 팔레트 부착·운반·배치 시각화 | 확인 |

아래 세부 항목과 시연 영상은 이 요약표의 기능 흐름을 기준으로 정리했습니다.

## 검증 환경

| 항목 | 내용 |
|:---|:---|
| Unity | 6000.3.10f |
| 주요 언어 | C# |
| UI | Unity uGUI, TextMesh Pro |
| 통신 | REST API, Control WebSocket, JPEG Camera WebSocket |
| 로봇 연동 | ROS2, Nav2, FastAPI |
| 검증 방식 | 팀 통합 실행과 시나리오 기반 수동 검증 |
| 검증 자료 | 팀 통합 실행 화면과 시연 영상 |

## 검증 원칙

- 화면 표시, 데이터 수신, 명령 요청과 후속 상태 반영을 기능 단위로 확인했습니다.
- 정적 UI와 Runtime 연동 결과를 구분해 검증했습니다.
- 실제 서버 수신값과 미수신 상태를 구분했습니다.
- REST 요청 결과와 후속 `command_ack`·`ROBOT_STATUS`를 함께 확인했습니다.
- View 전환과 재연결 이후에도 상태가 일관되게 유지되는지 확인했습니다.

## 기능별 확인

### 로봇 상태·위치

- 로봇 ID별 Pose·방향·배터리·속도·상태 표시
- `ROBOT_STATUS`의 부분 필드와 실제 숫자 `0` 처리
- View 재진입 시 마지막 유효 Pose 복원
- ROS 좌표와 Unity 2D·3D Marker 위치 일치

### Route·Waypoint

- 로봇별 Route Cache
- 완료·현재·다음 Waypoint 구분
- 현재 index와 전체 Waypoint 개수 표시
- 부분 패킷 이후 기존 정상 경로 유지
- Nav2, 장애물과 복구 상태 표시

### Camera

- Global·TB3-01·TB3-02 스트림별 독립 Texture 적용
- 소켓 연결과 실제 JPEG 프레임 상태 구분
- Main Feed 전환 후 이전 Texture 제거
- 하단 고정 Preview의 스트림 분리
- TB3-03 카메라 집계 제외
- 화면 전환·재연결 후 상태 일관성

### AI 이벤트

- 유형·시각·구역·감지 카메라·관련 로봇·신뢰도 표시
- 좌표–공장 구역–2D·3D Marker 일치
- 이벤트별 Snapshot 상태
- 확인·조치 결과와 목록·로그 동기화

### 제어

- 선택 로봇과 명령 대상 일치
- 순찰 시작·임무 재개·충전소 복귀
- 긴급정지와 초기화
- 수동 모드 진입·종료
- 전진·후진·좌·우와 Pointer Release 정지
- TB3-03 리프트 상승·하강·정지
- HTTP 응답, Accepted·Rejected와 후속 상태 분리

### Factory Runtime

- 2D·3D 로봇 Marker
- 직원·방문자 Marker와 출입구 차단기
- 컨베이어 Runtime 동작
- 팔레트 감지·부착·운반·Drop Slot 배치
- View 전환 후 상태 유지

## 시나리오별 검증 결과

| 시나리오 | Unity 확인 범위 | 검증 자료 |
|:---|:---|:---|
| 출퇴근 얼굴 인식 | 서버 출입 기록과 Dashboard 집계 | 출퇴근 편집 영상 |
| 물류센터 순찰 | Pose, Route·Waypoint, 장애물·복구 상태 | 지도·경로 영상 |
| 안전모 미착용 | 이벤트, 구역, 관련 로봇과 Popup | 안전 이벤트 영상 |
| 화재·쓰러짐 | Global Camera 이벤트와 현장 확인 | Global Camera 반복 영상 |
| 자동 충전 | 배터리·복귀 요청과 임무 상태 | 통합 관제 영상 |
| 수동조작·긴급정지 | 선택 로봇, 명령 결과와 후속 상태 | 관제·수동 제어 영상 |
| 지게차 운반 | 리프트 요청과 팔레트 운반 시각화 | 지게차 수동 제어 영상 |

## 대표 검증 영상

### 통합 관제 제어

https://github.com/user-attachments/assets/52d3d2e0-575b-4442-bbe4-073944d63f12

### 지도·경로 상태

https://github.com/user-attachments/assets/e13f0af1-54d8-4f9a-936a-4cf1701c2a06

### 출퇴근 통합 현황

https://github.com/user-attachments/assets/bc9a27c7-b84e-4b76-998a-1591bec9b6ea

### 지게차 수동 제어

https://github.com/user-attachments/assets/6fe87fc4-cd96-4fa3-9915-d400636cf369

전체 영상은 [시연 영상](demo/README.md)에 정리했습니다.

---

[문서 목차](README.md) · [프로젝트 README](../README.md)
