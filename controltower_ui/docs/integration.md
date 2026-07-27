# Server Integration

## 원칙

관제 화면은 실제 REST 응답, WebSocket 이벤트와 영상 프레임만 표시한다.
운영 View를 채우기 위한 Mock 데이터는 사용하지 않는다. 응답이 없거나
필드가 누락된 경우 임의 값을 생성하지 않고 미수신 또는 알 수 없음 상태로 남긴다.

이 문서의 주소는 공유본 기본값인 `127.0.0.1:8000` 기준이다. 실제 환경의
호스트, TLS, 인증과 접근 제어는 배포 설정에서 별도로 구성해야 한다.

## REST API

| 목적 | 메서드와 경로 | 처리 |
| --- | --- | --- |
| 로봇 자동 명령 | `POST /api/v1/robots/{robotId}/commands` | 명령과 대상 정보를 서버에 전달 |
| 수동 주행 | `POST /api/v1/robots/{robotId}/teleop` | 선속도, 각속도와 지속 시간 전달 |
| Dashboard 오늘 요약 | `GET /api/v1/dashboard/today-summary` | 출입, 사건과 운영 집계 반영 |
| 출근 기록 | `GET /api/v1/attendance/records?limit=100` | 실제 출입 기록 조회 |
| 방문 기록 | `GET /api/v1/visitor-access/records?limit=100` | 실제 방문자 기록 조회 |
| 사건 기록 | `GET /api/v1/incidents/records?limit=100` | 이벤트 목록과 상세 조회 |
| 사건 조치 완료 | `POST /api/v1/incidents/{logId}/clear` | 서버의 사건 상태 변경 요청 |

로봇 명령 클라이언트는 성공 여부, HTTP 상태와 서버 메시지를 UI Manager로
돌려준다. 최종 로봇 동작 상태는 후속 WebSocket 상태와 ACK를 함께 사용한다.

## Control WebSocket

기본 연결은 `ws://127.0.0.1:8000/ws/control-tower`다. 수신 envelope의
`type`, `event_type`, `event_name` 또는 `event` 필드에서 이벤트 유형을
판별하고 Unity 메인 스레드 큐를 통해 UI에 적용한다.

| 이벤트 | 주요 데이터와 반영 |
| --- | --- |
| `ROBOT_STATUS` | 로봇 좌표, 방향, 배터리, 속도, FSM/Nav2 상태 |
| `CAMERA_AI_STATUS` | 스트림별 연결 상태, 마지막 프레임, AI 모델 상태 |
| `NEW_ALERT` | 사건 유형, 위치, 로봇/카메라, 신뢰도, 스냅샷 참조 |
| `EMPLOYEE_ATTENDANCE` | 출근·퇴근 상태와 Dashboard 집계 갱신 |
| `VISITOR_ATTENDANCE` | 방문·퇴장 상태와 Dashboard 집계 갱신 |
| `robot_state_update` | 호환 로봇 상태 메시지 |
| `violation_alert` / `emergency_alert` | 안전 위반 및 긴급 이벤트 |
| `patrol_timeline_event` | 순찰 상태 변경 타임라인 |
| `patrol_log_update` | 순찰 로그의 시작·종료·상태 |
| `system_status` | 서버, WebSocket, ROS2와 AI 상태 |
| `command_ack` | 로봇 명령의 승인 또는 실패 결과 |
| `alert_ack_result` | 이벤트 확인·조치 요청 결과 |

공유본은 전체 수신 JSON을 Console에 출력하지 않는다. 파싱 오류, 연결 상태와
이벤트 유형처럼 운영에 필요한 비식별 로그만 유지한다.

## Camera Stream

| 소스 | 기본 URI |
| --- | --- |
| Global CCTV | `ws://127.0.0.1:8000/ws/video/global` |
| TB3-01 | `ws://127.0.0.1:8000/ws/video/1` |
| TB3-02 | `ws://127.0.0.1:8000/ws/video/2` |

`scr_CameraJpegWebSocketClient`가 WebSocket binary/text payload에서 JPEG
프레임을 받아 Texture로 적용한다. `scr_ControlTowerCameraStreamManager`는
소스 선택, 고정 미리보기와 연결 생명주기를 관리한다. 소켓 연결 여부와
별개로 프레임이 실제 RawImage에 적용된 시각을 영상 정상 상태의 기준으로 쓴다.

## Route / Waypoint

`ROBOT_STATUS`에 경로 데이터가 있으면 route ID/이름, Waypoint 배열,
현재 index와 전체 개수를 `ControlTowerWaypointRouteData`로 변환한다.
UI Manager는 로봇별 마지막 정상 경로를 저장하고 부분 상태 수신이 기존
정상 geometry를 지우지 않도록 상태와 경로 갱신을 분리한다.

## AI Event와 스냅샷

AI 이벤트는 유형, 좌표, 감지 카메라/로봇, 신뢰도와 스냅샷 상대 경로를
전달한다. 상대 경로는 HTTP 기본 주소와 결합해 이미지를 요청한다.
이미지 미수신 시 임의 대체 이미지를 사용하지 않고 실패 상태를 표시한다.

## Attendance / Visitor

실시간 출입 이벤트는 3D 인원 마커, 2D 인원 표시와 최근 출입 상태에
반영한다. Dashboard 집계의 기준값은 서버의 오늘 요약 REST 응답이며,
클라이언트 이벤트만으로 전체 집계를 임의 확정하지 않는다.

## Command ACK

REST 응답은 요청 전달 결과이며 `command_ack`는 서버/ROS2 처리 결과다.
UI는 대상 로봇, 명령, 결과 상태와 메시지를 선택 로봇의 명령 상태와
운영 로그에 반영한다. 이벤트 조치는 별도의 `alert_ack_result`로 확인한다.
