# Object Recognition — FMS Integration Final

> 스마트 팩토리 최종 시연에서 사용한 객체인식·글로벌캠·터틀봇 안전 인식 통합본

이 디렉터리는 `object_recognition/`의 초기 AI 팀 원본을 덮어쓰지 않고, FMS 서버와의 연동을 위해 검증한 최종 운영 소스를 별도로 보관합니다.

## Integrated Features

- 글로벌캠 화재·쓰러짐 검출과 TurtleBot 검출·근접 경보
- `goal_coordinate`를 포함한 글로벌캠 이벤트 발행 및 FMS 안전 접근 좌표 배차
- `map_image_corners` 고정 꼭짓점 기반 맵 좌표 보정
- 클릭 기반 맵 캘리브레이션과 화면상 목표 좌표·근접 경보 표시
- 로봇별 `/robotN/safety/detections`, `/robotN/server/safety_events` 발행
- PAUSED 안전모 미착용 상황에서 외부 얼굴인식 결과 토픽 연동
- 서버 재개 후 안전 인식 쿨다운 제어

## Important Entry Points

| Purpose | Path |
| --- | --- |
| GlobalCam launch | `launch/globalcam_object_map.launch.py` |
| TurtleBot safety launch | `launch/turtlebot_safety.launch.py` |
| GlobalCam detector | `pc_side/globalcam_yolo_result_node.py` |
| TurtleBot safety event / face link | `pc_side/turtlebot_safety_result_node.py` |
| Fixed map calibration | `config/globalcam_perspective_calibration.json` |
| GlobalCam calibration helper | `scripts/globalcam_perspective_calibrate` |

## Local Assets Not Included

모델 가중치, 등록 얼굴 데이터·임베딩, 로그, 캡처 이미지, 캐시와 백업 파일은 GitHub에 포함하지 않습니다. 로컬 실행 전 해당 자산과 ROS 2 Jazzy·Python 의존성을 별도로 준비해야 합니다.

## Relationship to FMS Server

이 모듈은 [`server_db/`](../../server_db) 백엔드와 ROS 2 Topic/Service로 연결됩니다. FMS는 객체인식 결과를 받아 이벤트 중복 억제, 로봇 배차, DB 사고 기록, 관제 WebSocket 알림을 처리합니다.
