# 실시간 통신 연동 (Integration)

## 연동 원칙

관제 화면은 실제 REST 응답, WebSocket 이벤트와 카메라 프레임만 표시합니다.
응답이 없거나 필드가 누락된 경우 임의 값을 생성하지 않고 미수신 상태를 유지합니다.
실제 서버 호스트, 인증과 접근 제어는 배포 환경에서 별도로 적용했습니다.

## REST API 연동

| 목적 | 경로 | 처리 |
| --- | --- | --- |
| 로봇 자동 명령 | `POST /api/v1/robots/{robotId}/commands` | 자동 명령과 대상을 적용했습니다. |
| 수동 주행 | `POST /api/v1/robots/{robotId}/teleop` | 선속도, 각속도와 지속 시간을 적용했습니다. |
| 오늘 요약 | `GET /api/v1/dashboard/today-summary` | 출입, 사건과 운영 집계를 표시합니다. |
| 출근 기록 | `GET /api/v1/attendance/records` | 실제 출입 기록을 표시합니다. |
| 방문 기록 | `GET /api/v1/visitor-access/records` | 실제 방문 기록을 표시합니다. |
| 사건 기록 | `GET /api/v1/incidents/records` | 이벤트 목록과 상세 정보를 표시합니다. |
| 사건 조치 | `POST /api/v1/incidents/{logId}/clear` | 사건의 조치 완료 상태를 적용했습니다. |

## 관제 웹소켓 (WebSocket)

관제 WebSocket 경로 `/ws/control-tower`에서 이벤트 유형을 구분해 표시합니다.

| 이벤트 | 반영 내용 |
| --- | --- |
| `ROBOT_STATUS` | 위치, 방향, 배터리, 속도와 Nav2 상태를 표시합니다. |
| `CAMERA_AI_STATUS` | 스트림, 마지막 프레임과 AI 모델 상태를 표시합니다. |
| `NEW_ALERT` | 사건 유형, 위치, 관련 로봇과 스냅샷 상태를 표시합니다. |
| `EMPLOYEE_ATTENDANCE` | 출퇴근 상태와 Dashboard 집계를 표시합니다. |
| `VISITOR_ATTENDANCE` | 방문자 출입 상태와 Dashboard 집계를 표시합니다. |
| `command_ack` | 로봇 명령의 승인 또는 실패 결과를 표시합니다. |
| `alert_ack_result` | 이벤트 확인과 조치 결과를 표시합니다. |

전체 수신 메시지는 Console에 출력하지 않고 이벤트 유형과 연결 상태만 유지합니다.

## 카메라 스트림 (Camera Stream)

Global CCTV는 `/ws/video/global`, TB3 카메라는 `/ws/video/{robotId}` 경로를 적용했습니다.
소켓 연결 상태와 마지막 실제 프레임 적용 시각을 분리해 표시합니다.
프레임이 화면에 적용된 경우에만 영상 수신 상태를 정상으로 표시합니다.

## 경로와 웨이포인트 (Route / Waypoint)

로봇별 Route, Waypoint 배열, 현재 index와 전체 개수를 유지합니다.
부분 상태 메시지가 기존 정상 경로를 지우지 않도록 상태와 경로 갱신을 분리해 적용했습니다.

## AI 이벤트와 출입 정보

AI 이벤트의 유형, 좌표, 카메라, 관련 로봇과 스냅샷 상태를 표시합니다.
출입 이벤트는 인원 표시와 최근 출입 상태에 적용하고, 전체 집계는 서버 요약값을 유지합니다.
