# File Inventory

## C# 소스

| 경로 | 주요 클래스 | 구분 | 역할 / 관련 기능 |
| --- | --- | --- | --- |
| `Bridge/scr_ControlTowerWebSocketClient.cs` | `scr_ControlTowerWebSocketClient` 및 WebSocket DTO | Runtime | 관제 WebSocket 연결, 상태·이벤트·ACK 파싱 |
| `Bridge/scr_ControlTowerRobotApiClient.cs` | `scr_ControlTowerRobotApiClient` | Runtime | 자동 명령과 teleop REST 요청 |
| `Bridge/scr_ControlTowerCameraStreamManager.cs` | `scr_ControlTowerCameraStreamManager` | Runtime | Global/TB3 영상 소스와 프리뷰 관리 |
| `Bridge/scr_CameraJpegWebSocketClient.cs` | `scr_CameraJpegWebSocketClient` | Runtime | JPEG WebSocket 프레임 수신과 Texture 적용 |
| `Core/scr_ControlTowerUIManager.cs` | `scr_ControlTowerUIManager` | Runtime | 전체 View, 상태 캐시, 명령, 이벤트와 팝업 조정 |
| `Core/scr_ControlTowerDashboardRuntimeBinder.cs` | `scr_ControlTowerDashboardRuntimeBinder` | Runtime | Dashboard 런타임 UI 참조 연결 |
| `UI/scr_MapStatusRouteController.cs` | `scr_MapStatusRouteController` 및 route DTO | Runtime | Waypoint, 경로 진행, 장애물·복구 표시 |
| `UI/scr_FactoryFull2DMapController.cs` | `scr_FactoryFull2DMapController` | Runtime | 전체 2D 공장맵 좌표 변환과 로봇 표시 |
| `UI/scr_FactoryMini2DMapController.cs` | `scr_FactoryMini2DMapController` | Runtime | 미니맵 로봇 위치와 방향 표시 |
| `UI/scr_Factory3DRobotMarkerController.cs` | `scr_Factory3DRobotMarkerController` | Runtime | 3D 로봇 marker pose 적용 |
| `UI/scr_Factory3DMapCameraController.cs` | `scr_Factory3DMapCameraController` | Runtime | 3D 공장 카메라 시점 제어 |
| `UI/scr_FactoryConveyorRuntimeController.cs` | `scr_FactoryConveyorRuntimeController` | Runtime | 컨베이어 런타임 동작 표시 |
| `UI/scr_Personnel3DMarkerController.cs` | `scr_Personnel3DMarkerController` | Runtime | 출입 이벤트 기반 3D 인원 marker 관리 |
| `UI/scr_Factory2DPeopleMarkerController.cs` | `scr_Factory2DPeopleMarkerController` | Runtime | 2D 공장맵 인원 marker 표시 |
| `UI/scr_StaffEntranceBarrierController.cs` | `scr_StaffEntranceBarrierController` | Runtime | 직원 출입구 차단기 동작 표시 |
| `UI/scr_Factory2DPalletMarkerController.cs` | `scr_Factory2DPalletMarkerController` | Runtime | 2D 팔레트 위치와 상태 표시 |
| `UI/scr_TB3PalletCarryController.cs` | `scr_TB3PalletCarryController`, `PalletDropSlot` | Runtime | 팔레트 픽업·운반·지정 slot 투하 |
| `UI/scr_TB3ForkliftRuntimeController.cs` | `scr_TB3ForkliftRuntimeController` | Runtime | 지게차 리프트 높이 동작 시각화 |
| `UI/scr_TB3ForkliftPalletCarryController.cs` | `scr_TB3ForkliftPalletCarryController` | Runtime | Rigidbody 기반 팔레트 부착·해제 |
| `Editor/scr_TB3ForkliftPalletCarrySetupTool.cs` | Editor setup tool | Editor | 지게차 팔레트 운반 참조 자동 설정 |
| `Editor/scr_MapMeasuredLayoutConfig.cs` | `scr_MapMeasuredLayoutConfig` | Editor | 측정 좌표와 레이아웃 설정 asset |
| `Editor/scr_MapLayoutEditorTool.cs` | Editor layout tool | Editor | 2D·3D 레이아웃 측정, 적용과 검증 |
| `Editor/scr_MapLayoutBackupAsset.cs` | `scr_MapLayoutBackupAsset` | Editor | 레이아웃 object snapshot 저장 형식 |
| `Editor/scr_DashboardFinalReferenceSetupTool.cs` | Editor setup tool | Editor | Dashboard 최종 참조 연결 |

위 상대 경로의 기준은
`unity/Assets/Project/Scripts/ControlTower`다. 각 C# 소스와 같은 위치에
Unity GUID를 보존하는 대응 `.cs.meta` 파일이 포함되어 있다.

## 복사된 전체 파일

```text
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_CameraJpegWebSocketClient.cs
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_CameraJpegWebSocketClient.cs.meta
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerCameraStreamManager.cs
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerCameraStreamManager.cs.meta
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerRobotApiClient.cs
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerRobotApiClient.cs.meta
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerWebSocketClient.cs
unity/Assets/Project/Scripts/ControlTower/Bridge/scr_ControlTowerWebSocketClient.cs.meta
unity/Assets/Project/Scripts/ControlTower/Core/scr_ControlTowerDashboardRuntimeBinder.cs
unity/Assets/Project/Scripts/ControlTower/Core/scr_ControlTowerDashboardRuntimeBinder.cs.meta
unity/Assets/Project/Scripts/ControlTower/Core/scr_ControlTowerUIManager.cs
unity/Assets/Project/Scripts/ControlTower/Core/scr_ControlTowerUIManager.cs.meta
unity/Assets/Project/Scripts/ControlTower/Editor/scr_DashboardFinalReferenceSetupTool.cs
unity/Assets/Project/Scripts/ControlTower/Editor/scr_DashboardFinalReferenceSetupTool.cs.meta
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapLayoutBackupAsset.cs
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapLayoutBackupAsset.cs.meta
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapLayoutEditorTool.cs
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapLayoutEditorTool.cs.meta
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapMeasuredLayoutConfig.cs
unity/Assets/Project/Scripts/ControlTower/Editor/scr_MapMeasuredLayoutConfig.cs.meta
unity/Assets/Project/Scripts/ControlTower/Editor/scr_TB3ForkliftPalletCarrySetupTool.cs
unity/Assets/Project/Scripts/ControlTower/Editor/scr_TB3ForkliftPalletCarrySetupTool.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory2DPeopleMarkerController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory2DPeopleMarkerController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory2DPalletMarkerController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory2DPalletMarkerController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory3DMapCameraController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory3DMapCameraController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory3DRobotMarkerController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_Factory3DRobotMarkerController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryConveyorRuntimeController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryConveyorRuntimeController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryFull2DMapController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryFull2DMapController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryMini2DMapController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_FactoryMini2DMapController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_MapStatusRouteController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_MapStatusRouteController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_Personnel3DMarkerController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_Personnel3DMarkerController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_StaffEntranceBarrierController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_StaffEntranceBarrierController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3ForkliftPalletCarryController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3ForkliftPalletCarryController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3ForkliftRuntimeController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3ForkliftRuntimeController.cs.meta
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3PalletCarryController.cs
unity/Assets/Project/Scripts/ControlTower/UI/scr_TB3PalletCarryController.cs.meta
unity/Packages/manifest.json
unity/Packages/packages-lock.json
unity/ProjectSettings/ProjectVersion.txt
```

총 51개 복사 파일로, C# 24개, 대응 `.cs.meta` 24개, Unity 구성 3개다.

## 작성된 기술 문서

```text
docs/architecture.md
docs/file-inventory.md
docs/integration.md
docs/security.md
docs/troubleshooting.md
docs/ui-views.md
```

기존 `README.md`는 원문을 유지하고 소스 구조, 기술 문서 링크와 공유본
범위 안내를 하단에 추가했다.
