# Face Recognition

노트북/엣지 PC에서 운영하는 안면인식 시스템 영역이다.

## 포함 범위

- `edge_face_system/`: ROS2 기반 출입 안면인식, RealSense PAD, 로컬 UI/API 연동, 업로더, CCTV/GlobalCam 보조 스크립트
- `edge_face_system/config/edge_local.example.yaml`: 운영 설정 예시
- `edge_face_system/docs/`: API 계약, DB 초안, 운영 개선 문서

## 제외 범위

다음 파일은 개인정보 또는 대용량 런타임 자산이므로 Git에 올리지 않는다.

- 등록 얼굴 DB: `registered_faces/`
- 로컬 이벤트/출입 기록: `data/*.jsonl`, `data/*.sqlite`
- 로그: `logs/`, `*.log`
- 모델 가중치/캐시: `*.pt`, `*.onnx`, `.insightface/`
- 실제 운영 설정: `edge_local.yaml`

운영 환경에서는 `edge_local.example.yaml`을 복사해 `edge_local.yaml`로 만든 뒤 서버 URL, 토큰, 장치 ID, 로컬 경로를 현장에 맞게 설정한다.
