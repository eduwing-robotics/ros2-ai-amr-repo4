import asyncio
import datetime
from fastapi import APIRouter, Request, HTTPException, status, Depends
from pydantic import BaseModel
from typing import Optional
from sqlalchemy.orm import Session

from app.db.database import get_db
from app.crud.logs import create_command_log

router = APIRouter(prefix="/api/v1", tags=["v1 로봇 관련 API"])

# --- 📦 Pydantic 데이터 모델 정의 ---
class TargetLocation(BaseModel):
    map_id: str
    x: float
    y: float
    theta: float

'''class CommandRequest(BaseModel):
    command: str        # Unity가 보냄 (예: "PATROL_START", "RESUME" 등)
    operator_id: str
    timestamp: str
    # GO_TO 명령일 때만 들어오는 데이터
    target: Optional[TargetLocation] = None
    # RESUME_MISSION 명령일 때만 들어오는 데이터
    reason: Optional[str] = None
    related_alert_id: Optional[int] = None'''

class CommandRequest(BaseModel):
    command: str    # 예: "START_PATROL", "EMERGENCY_STOP", "RESUME" 등
    operator_id: str
    timestamp: str

class AckRequest(BaseModel):
    ack_by: str
    memo: str

class TeleopRequest(BaseModel):
    linear_x: float  # 전후진 속도 (예: 0.1 이면 전진, -0.1 이면 후진, 0.0 이면 정지)
    angular_z: float # 좌우 회전 속도 (예: 0.5 이면 좌회전, -0.5 이면 우회전)
    lift: float = 0.0  # 3호기 리프트: +1.0 올림, -1.0 내림, 0.0 정지

# ------------------------------------

# 1. 로봇 제어 명령 전송 API
@router.post("/robots/{robot_id}/commands")
async def send_robot_command(
    robot_id: str,
    payload: CommandRequest,
    request: Request,
    db: Session = Depends(get_db)
):
    """
    유니티 관제 화면에서 보낸 명령을 받아, ROS2 서비스를 통해 로봇을 제어하고
    로봇이 응답한 수락/거부 결과를 유니티에게 최종 반환합니다.
    """

    # 유니티가 보낸 "tb3-01" 형태를 정수 1로 변환합니다.
    try:
        r_id = int(robot_id.split("-")[1])
    except Exception:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"❌ 잘못된 로봇 ID 형식입니다: {robot_id} (예: tb3-01)"
        )

    # app.state에서 ROS2 노드 참조
    node = request.app.state.ros_node

    ros_mode = payload.command
    if not ros_mode:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"❌ 지원하지 않는 명령입니다: {payload.command}"
        )

    # 비동기 스레드 브릿지 구성
    # FastAPI(asyncio) 스레드에서 ROS2(rclpy)의 결과를 동기적으로 기다리기 위한 객체 생성
    loop = asyncio.get_running_loop()
    asyncio_future = loop.create_future()

    # ROS2 서비스 호출 (Future 반환)
    ros_future = node.call_set_mode(r_id, payload.command)

    if ros_future is None:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail=f"🚫 로봇 {r_id}호기 제어 서비스가 오프라인 상태입니다. (로봇 실행 상태 확인 필요)"
        )

    # ROS2 서비스 완료 시 실행될 스레드 안전 콜백 함수 정의
    def ros_service_done_callback(f):
        try:
            result = f.result() #SetMode_Response 획득
            # FastAPI 컨텍스트 스레드로 안전하게 결과 주입 및 대기 해제
            loop.call_soon_threadsafe(asyncio_future.set_result, result)
        except Exception as e:
            loop.call_soon_threadsafe(asyncio_future.set_exception, e)

    # ROS2 작업 완료 콜백 리스너 부착
    ros_future.add_done_callback(ros_service_done_callback)

    try:
        # 로봇의 내부 상태 체킹 및 연산이 끝날 때까지 최대 3.0초간 비동기 대기(Await)
        robot_response = await asyncio.wait_for(asyncio_future, timeout=3.0)

        # 고유 커맨드 ID 생성
        command_id_str = f"cmd_{datetime.datetime.now().strftime('%Y%m%d%H%M%S')}_{r_id}"

        # 로봇이 승인했는지, FSM 전이제한으로 거부했는지 판단하여 유니티 응답 포맷 구성
        # 응답 분석
        if robot_response.success:
            res_status = "accepted"
            res_msg = f"수락됨: {robot_response.message}"
        else:
            res_status = "rejected"
            res_msg = f"거부됨: {robot_response.message}"

        # 🚨 [DB 저장] 로봇이 정상적으로 대답(수락 or 거부)했을 때의 기록
        # DB에서 자동 생성된 PK(log_id)를 유니티 응답의 command_id로 써도 좋습니다.
        saved_log = create_command_log(
            db=db,
            robot_id=r_id,
            operator_id=payload.operator_id,
            command=payload.command,
            result_status=res_status,
            response_message=res_msg
        )

        # 유니티 응답 포맷
        return {
            "ok": robot_response.success,
            "data": {
                "command_id": saved_log.command_id, # DB에 저장된 실제 PK 값 사용
                "robot_id": robot_id,
                "command": payload.command,
                "status": res_status,
                "message": res_msg
            }
        }
        '''
        if robot_response.success:
            return {
                "ok": True,
                "data": {
                    "command_id": command_id,
                    "robot_id": robot_id,
                    "command": payload.command,
                    "status": "accepted",
                    "message": f"로봇이 명령을 수락했습니다: {robot_response.message}"
                }
            }
        else:
            # 명령은 전달되었으나 로봇 FSM 조건상 실행이 거부된 경우 (예: 주행 중 PATROL_START 호출)
            return {
                "ok": False,
                "data": {
                    "command_id": command_id,
                    "robot_id": robot_id,
                    "command": payload.command,
                    "status": "rejected",
                    "message": f"로봇이 명령을 거부했습니다: {robot_response.message}"
                }
            }
        '''
    except asyncio.TimeoutError:
        raise HTTPException(
            status_code=status.HTTP_504_GATEWAY_TIMEOUT,
            detail=f"❌ 로봇 {r_id}호기로부터 응답 전송 시간이 초과되었습니다. (물리 네트워크 확인 필요)"
        )
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"❌ 서버 내부 스레드 브릿지 연동 오류: {str(e)}"
        )

# 2. 로봇 수동 이동 명령 API
@router.post("/robots/{robot_id}/teleop")
async def manual_drive_robot(robot_id: str, req: TeleopRequest, request: Request):
    '''
    # app.state 등에 저장해둔 ROS2 노드 가져오기
    ros_node = request.app.state.ros_node

    # ⚠️ 로봇이 현재 MANUAL_CONTROL 상태인지 체크하는 로직이 서버에 있다면 더 좋습니다.
    # if current_status != "MANUAL_CONTROL":
    #     return {"ok": False, "message": "먼저 수동 모드로 진입(MANUAL_ENTER) 해주세요."}

    # 토픽 발행
    ros_node.publish_manual_control(req.linear_x, req.angular_z)

    return {"ok": True, "message": "수동 조작 명령 전송 완료"}
    '''
    """
    유니티(관제)에서 수동 조작 방향키 신호를 보낼 때 처리합니다.
    """
    # 1. 로봇 ID 파싱
    try:
        r_id = int(robot_id.split("-")[1])
    except Exception:
        raise HTTPException(status_code=400, detail="잘못된 로봇 ID 형식")

    ros_node = request.app.state.ros_node

    # ==============================================================
    # 🚨 [핵심 안전 로직] 현재 로봇 상태를 캐시에서 꺼내어 검사합니다.
    # ==============================================================
    current_status_msg = ros_node.status_cache.get(r_id)

    # 상태를 아직 한 번도 못 받았거나, 상태가 MANUAL_CONTROL이 아니면 무조건 튕겨냅니다.
    # (단, 정지 명령(0.0, 0.0)은 혹시 모를 폭주를 대비해 언제든 먹히게 예외처리 해주는 것도 좋습니다)
    is_stop_command = (req.linear_x == 0.0 and req.angular_z == 0.0 and req.lift == 0.0)

    if not is_stop_command:
        if not current_status_msg or current_status_msg.status != "MANUAL_CONTROL":
            current_state_name = current_status_msg.status if current_status_msg else "알 수 없음(통신 불가)"

            # 클라이언트에게 403 Forbidden(금지됨) 에러 대신,
            # 팝업창을 띄우기 좋게 JSON 형태로 거부 사유를 예쁘게 포장해서 돌려줍니다.
            return {
                "ok": False,
                "message": f"🚫 수동 조작 거부됨: 로봇이 수동 모드가 아닙니다. (현재: {current_state_name})"
            }
    # ==============================================================

    # 2. 안전 검사를 통과했거나, 긴급 정지(0,0) 명령인 경우에만 실제 로봇으로 전송
    ros_node.publish_manual_control(r_id, req.linear_x, req.angular_z, req.lift)

    return {"ok": True, "message": "수동 조작 전송 완료"}