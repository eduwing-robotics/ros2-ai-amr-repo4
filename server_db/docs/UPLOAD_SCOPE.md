# GitHub 업로드 범위 메모

## 이번에 추가한 파일

- `server_db/backend/`: FastAPI, PostgreSQL 모델·마이그레이션, ROS2 연동, 이벤트 큐·배차·교대·WebSocket 코드
- `server_db/config/globalcam_perspective_calibration.json`: 현재 운영 중인 고정 맵 꼭짓점 보정값
- `server_db/config/*.msg`, `*.srv`: 서버 연동에 사용한 ROS2 인터페이스 명세 사본

## 의도적으로 제외한 파일

- `.env`, DB 접속 문자열, DB 덤프
- 얼굴 등록 이미지·임베딩과 사고 캡처 이미지
- Python 가상환경, ROS2 build/install/log, 백업·원본 파일

## 객체인식 폴더 처리 원칙

`ai_perception/object_recognition`은 GitHub에 이미 AI 팀 코드가 존재하며, 운영본과 123개 파일 차이가 확인됐다. 소유자 확인 없이 자동 덮어쓰지 않는다. 서버 연동에 영향을 주는 변경 후보는 아래와 같으며, AI 팀 최신본을 기준으로 별도 병합한다.

- `launch/globalcam_object_map.launch.py`
- `launch/turtlebot_safety.launch.py`
- `map_line_reference.py`
- `pc_side/globalcam_udp_display_node.py`
- `pc_side/globalcam_yolo_result_node.py`
- `pc_side/turtlebot_safety_result_node.py`

이 문서의 목적은 서버 변경을 먼저 안전하게 기록하고, AI 인식 모델·추론 코드의 소유권 충돌을 방지하는 것이다.
