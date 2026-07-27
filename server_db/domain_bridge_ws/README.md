# Domain Bridge 워크스페이스

서버 도메인(73)과 로봇 도메인(1호기 97, 2호기 88, 3호기 4) 사이의 ROS2 Topic/Service 중계를 위한 소스 워크스페이스입니다.

## 포함 패키지

- `src/domain_bridge`: Domain Bridge C++ 구현
- `src/teamproject_interfaces`: `RobotStatus`, `ObstacleVerdict`, `SetMode`, `DispatchToEvent` 공용 인터페이스

## 빌드

```bash
cd server_db/domain_bridge_ws
source /opt/ros/jazzy/setup.bash
colcon build --symlink-install
source install/setup.bash
```

실행 환경의 Domain ID·네트워크·필요 토픽은 `bridge_config.yaml`에서 확인합니다. build/install/log 디렉터리는 생성 산출물이므로 저장소에 포함하지 않습니다.
