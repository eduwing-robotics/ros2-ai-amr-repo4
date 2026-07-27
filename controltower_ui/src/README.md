# Unity ControlTower Scripts

이 폴더는 Unity ControlTower UI의 핵심 C# 스크립트를 공유하기 위한 코드 정리본입니다.

전체 Unity 실행 프로젝트가 아니며 Scene, Prefab, 모델, 폰트와 UI 자산은
포함하지 않습니다.

## 폴더별 역할

- `Bridge`: REST API, WebSocket, Camera Stream 통신
- `Core`: ControlTower 전체 상태와 Runtime UI 연결
- `UI`: Factory, Map, Marker, Forklift와 Pallet 표시
- `Editor`: Unity Editor 배치와 설정 도구

스크립트는 원본 Unity 프로젝트의 Scene, Prefab, TextMeshPro와 UI 컴포넌트
참조를 필요로 할 수 있습니다. 이 폴더만으로는 독립 실행되지 않습니다.

## 문서

- [스크립트 목록](SCRIPT_INDEX.md)
- [의존성 안내](DEPENDENCIES.md)
- [C# 소스 폴더](Unity/ControlTower/)
