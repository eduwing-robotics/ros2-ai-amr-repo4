# main.py
import os
from contextlib import asynccontextmanager
import threading
import rclpy
from dotenv import load_dotenv
import asyncio
from fastapi import FastAPI, Request, WebSocket, WebSocketDisconnect
from app.core.websocket import manager, video_manager
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles

from app.db.database import engine, Base
from app.db import models

from app.api import employees, attendance, visitors
from app.api.attendance import router as attendance_router
from app.api.robots import router as robots_router
from app.api.logs import router as logs_router
from app.api.incidents import router as incidents_router
from app.api.dashboard import router as dashboard_router
from app.core.exceptions import APIException

#from app.services.ros_client import FactoryRosNode
from app.services.ros_client_ai import FactoryRosAiNode as FactoryRosNode

load_dotenv()
if "ROS_DOMAIN_ID" not in os.environ:
    os.environ["ROS_DOMAIN_ID"] = "73"
if "RMW_IMPLEMENTATION" not in os.environ:
    os.environ["RMW_IMPLEMENTATION"] = "rmw_cyclonedds_cpp"

# 1. 서버의 시작(Startup)과 종료(Shutdown)를 관리하는 Lifespan 정의
@asynccontextmanager
async def lifespan(app: FastAPI):
    # [-- Startup 구역: 서버가 켜질 때 자동 가동 --]

    # 🚨 [여기에 추가] 서버 켜질 때 수정된 models.py 구조대로 테이블 자동 생성!
    print("🗄️ 데이터베이스 테이블 동기화를 시작합니다...")
    Base.metadata.create_all(bind=engine)
    print("✅ 데이터베이스 테이블 동기화 완료!")

    # rclpy 초기화
    rclpy.init()

    # FastAPI가 돌아가고 있는 현재의 비동기 루프를 가져옴
    loop = asyncio.get_running_loop()

    # ROS2 스레드에서 안전하게 실행된 콜백 함수를 만듦
    def sync_broadcast(payload: dict):
        #print(f"🔄 [CCTV 1] ROS -> FastAPI 터널 통과 성공! ({payload['robot_id']}호기)")
        try:
            # Uvicorn 환경에서 가장 안전하게 비동기 작업을 예약하는 방법
            loop.call_soon_threadsafe(
                lambda: asyncio.create_task(manager.broadcast(payload))
            )
        except Exception as e:
            print(f"❌ 웹소켓 큐 등록 실패: {e}")

    # 카메라 영상 브로드캐스트
    def sync_video_broadcast(robot_id: int, frame_bytes: bytes):
        # 보고 있는 사람이 있을 때만 전송 (최적화)
        if robot_id in video_manager.active_connections and video_manager.active_connections[robot_id]:
            loop.call_soon_threadsafe(
                lambda: asyncio.create_task(video_manager.broadcast_video(robot_id, frame_bytes))
            )


    # 로봇 통신 브릿지 노드 생성. 콜백 함수를 Node에 주입하여 생성함
    ros_node = FactoryRosNode(
        broadcast_callback=sync_broadcast,
        video_callback=sync_video_broadcast)

    # 나중에 API 라우터에서 이 노드를 꺼내 쓸 수 있도록 app.state에 보관해 둠
    app.state.ros_node = ros_node

    # rclpy.spin은 블로킹 함수이므로, FastAPI event loop가 멈추지 않게 별도의 백그라운드 스레드에서 돌림
    ros_thread = threading.Thread(
        target=rclpy.spin, args=(ros_node,), daemon=True
    )
    ros_thread.start()
    print("🚀 ROS2 Domain Bridge 노드가 백그라운드 스레드에서 성공적으로 가동되었습니다!")
    print("🚀 ROS2 노드와 WebSocket 연결이 완료되었습니다!")

    # FastAPI 스레드에 제어권을 넘겨주어 웹 서버가 정상 작동하게 함
    yield

    # [-- Shutdown 구역: Ctrl+C 등으로 서버가 꺼질 때 안전하게 정지 --]
    print("🛑 FastAPI 서버 종료 감지: ROS2 연동 노드를 안전하게 해제합니다...")
    # 1. rclpy가 아직 켜져 있을 때만 노드 파괴 및 종료 시도
    if rclpy.ok():
        try:
            ros_node.destroy_node()
            rclpy.shutdown()
        except Exception as e:
            # 강제 리로드 등으로 인해 발생하는 에러는 부드럽게 무시
            pass

    # 2. ROS2 스레드가 아직 돌고 있다면 안전하게 합류(종료 대기)
    if ros_thread.is_alive():
        ros_thread.join()

    print("👋 ROS2 시스템이 완전히 안전하게 종료되었습니다.")

# 2. FastAPI 앱(서버) 생성 시 lifespan 매니저를 등록합니다.
app = FastAPI(
    title="AI Factory Backend",
    description="AI 공장 관리 자율주행 로봇 프로젝트 서버 API 명세서",
    version="1.0.0",
    lifespan=lifespan,
)

# 3. 프론트엔드가 사진을 볼 수 있게 static 폴더 개방
app.mount("/static", StaticFiles(directory="app/static"), name="static")

# 4. 서버가 잘 켜졌는지 확인하기 위한 기본 주소 (Hello World)
@app.get("/")
async def root():
    return {"message": "AI 팩토리 백엔드 서버가 정상적으로 실행되었습니다! 🚀"}

# Unity 관제용 WebSocket 엔드포인트 개방
@app.websocket("/ws/control-tower")
async def websocket_endpoint(websocket: WebSocket):
    await manager.connect(websocket)
    try:
        while True:
            # Unity에서 서버로 보내는 웹소켓 메시지 대기
            data = await websocket.receive_text()
            print(f"Unity로부터 메시지 수신: {data}")

    except WebSocketDisconnect:
        # 브라우저 창을 닫거나 유니티가 꺼지면 이쪽으로 안전하게 빠집니다.
        print(f"🔌 클라이언트가 떠났습니다. (연결 종료)")
        manager.disconnect(websocket)

    except Exception as e:
        # 그 외의 알 수 없는 에러가 터져도 서버가 죽지 않게 방어!
        print(f"⚠️ 웹소켓 통신 중 예외 발생: {e}")
        manager.disconnect(websocket)

@app.websocket("/ws/video/{camera_id}")
async def video_websocket_endpoint(websocket: WebSocket, camera_id: str):
    # camera_id 에는 "1", "2", "3" 또는 "global" 이 들어옵니다.
    await video_manager.connect(websocket, camera_id)
    try:
        while True:
            await websocket.receive_text() # 클라이언트 종료 감지용 대기
    except WebSocketDisconnect:
        video_manager.disconnect(websocket, camera_id)
    except Exception:
        video_manager.disconnect(websocket, camera_id)

# [테스트] 로봇 제어 임시 API 엔드포인트
@app.get("/test-command/{robot_id}/{mode}")
async def test_robot_command(request: Request, robot_id: int, mode: str):
    # app.state에 보관해둔 ROS2 노드를 꺼내옵니다.
    node: FactoryRosNode = request.app.state.ros_node

    # 노드의 명령 전송 함수 실행
    future = node.call_set_mode(robot_id, mode)

    # 로봇이 꺼져있거나 서비스가 없으면 future가 None으로 반환되도록 우리가 코드를 짰었죠!
    if future is None:
        return {
            "ok": False,
            "message": f"🚫 로봇 {robot_id}호기가 응답하지 않습니다. (로봇이 꺼져있거나 브릿지 연결 확인 필요)"
        }

    return {
        "ok": True,
        "message": f"✅ 로봇 {robot_id}호기에게 [{mode}] 명령 전송을 시도했습니다!"
    }

app.include_router(employees.router, prefix="/api/v1/employees", tags=["v1 직원 관련 API"])
app.include_router(attendance.router, prefix="/api/v1/attendance", tags=["v1 출퇴근 관련 API"])
app.include_router(visitors.router, prefix="/api/v1/visitors", tags=["v1 방문자 관련 API"])
app.include_router(incidents_router)
#app.include_router(attendance_router)
app.include_router(robots_router)
app.include_router(dashboard_router)
app.include_router(logs_router)

# 커스텀 에러가 터지면 이 함수가 자동으로 실행됨??
@app.exception_handler(APIException)
async def api_exception_handler(request: Request, exc: APIException):
    return JSONResponse(
        status_code=exc.status_code,
        content={
            "ok": False,
            "error_code": exc.error_code,
            "message": exc.message
        }
    )