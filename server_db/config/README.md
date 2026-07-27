# 서버 연동 인터페이스

서버는 아래 custom ROS 2 인터페이스를 사용합니다. 원본 패키지는 팀 공용 ROS 2 워크스페이스의 `teamproject_interfaces`이며, 여기의 `.msg`·`.srv` 파일은 명세 확인용 사본입니다.

- `RobotStatus.msg`
- `ObstacleVerdict.msg`
- `DispatchToEvent.srv`
- `SetMode.srv`

도메인 브릿지 실행 구성은 실제 배포 도메인 ID와 네트워크 환경에 따라 달라질 수 있으므로, 실행용 YAML은 배포 환경별로 관리합니다.
