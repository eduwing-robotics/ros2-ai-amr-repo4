# ControlTower Unity Source

TB3 Smart Factory Control Tower의 Unity Runtime C# 소스입니다. 외부 데이터 수신, Runtime 상태 관리와 관제 화면 표시를 Bridge, Core, UI 영역으로 구분했습니다.

## 폴더 구성

```text
src/
├─ README.md
├─ DEPENDENCIES.md
├─ SCRIPT_INDEX.md
└─ Unity/ControlTower/
   ├─ Bridge/
   ├─ Core/
   └─ UI/
```

| 영역 | 역할 |
|:---|:---|
| `Bridge` | REST API, Control WebSocket와 JPEG Camera WebSocket 연동 |
| `Core` | Runtime 상태, View 전환, 선택 로봇과 Dashboard 연결 |
| `UI` | Factory, Map Status, Marker, Conveyor, Forklift와 Pallet 시각화 |

## Runtime 연결 흐름

```text
REST API·Control WebSocket·Camera WebSocket
→ Bridge
→ Robot·Route·Event Runtime State
→ Core
→ Dashboard·Factory·Robot·Map Status·Camera View
```

운영자 명령은 UI에서 선택 로봇과 요청을 구성해 REST Client로 전달합니다. 화면 상태는 `command_ack`와 후속 `ROBOT_STATUS`를 기준으로 갱신합니다.

## 문서

- [스크립트 목록](SCRIPT_INDEX.md)
- [의존성](DEPENDENCIES.md)
- [프로젝트 구조](../docs/07_project_structure.md)

총 Runtime C# 스크립트: **19개**

---

[문서 목차](../docs/README.md) · [프로젝트 README](../README.md)
