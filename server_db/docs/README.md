# 서버 / DB 기술 문서

이 디렉터리에는 FMS 관제 서버의 설계와 연동 방식을 정리한 상세 기술 문서가 있습니다.

| 문서 | 내용 |
| --- | --- |
| [01. 시스템 아키텍처](01-architecture.md) | FastAPI, ROS 2, PostgreSQL, GUI, TTS의 연결 구조 |
| [02. 데이터베이스 설계 및 ERD](02-database-design.md) | 스키마, 테이블 역할, 사건 저장 방식 |
| [03. ROS 2·Domain Bridge 연동](03-ros-integration.md) | 다중 도메인 토픽·서비스·실시간 전달 구조 |
| [04. AI 이벤트·로봇 파견 로직](04-event-dispatch.md) | 우선순위, 큐, 배차, 재검증, 임무 교대 |
| [05. API·WebSocket 인터페이스](05-api-interface.md) | REST 리소스와 실시간 메시지 분류 |

구현 소스는 [`../backend`](../backend)에, 공유 ROS 2 인터페이스는 [`../domain_bridge_ws/src/teamproject_interfaces`](../domain_bridge_ws/src/teamproject_interfaces)에 있습니다.
