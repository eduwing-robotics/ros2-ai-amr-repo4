import cv2
from fastapi import APIRouter, Depends, HTTPException, status, Request
from sqlalchemy.orm import Session
from datetime import datetime
import random
import numpy as np
from app.db.database import get_db
#from app.models.logs import IncidentLog # 아까 작성해주신 IncidentLog DB 모델
from app.crud.logs import get_incident_log, clear_incident_log
from app.crud.logs import create_incident_log
from app.crud.logs import get_all_incidents
from app.core.websocket import manager
from app.services.file_manager import save_alert_frame

router = APIRouter(prefix="/api/v1/incidents", tags=["v1 이상 상황 관련 API"])

# ---------------------------------------------------------
# [이전에 만든 가짜 이미지 생성 로직 복사]
# ---------------------------------------------------------
def create_dummy_frame(verdict, robot_id):
    frame = np.zeros((320, 240, 3), dtype=np.uint8)
    if verdict == "FIRE": frame[:] = (0, 0, 200)
    elif verdict == "NO_HELMET": frame[:] = (200, 0, 0)
    elif verdict == "FALL": frame[:] = (0, 200, 200)
    else: frame[:] = (50, 50, 50)
    cv2.putText(frame, f"[{robot_id}] {verdict}", (10, 100), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
    return frame

# ---------------------------------------------------------
# 💡 핵심: 테스트용 가짜 알림 발사 API (치트키)
# ---------------------------------------------------------
@router.post("/trigger-test-alert")
async def trigger_test_alert(
    request: Request,
    incident_type: str = "FIRE", # 기본값 FIRE (Swagger에서 변경 가능)
    robot_id: int = 1,           # 기본값 1호기
    db: Session = Depends(get_db)
):
    """
    [테스트 전용] API를 호출하면 즉시 지정된 위반 상황을 DB에 저장하고 Unity로 웹소켓 알림을 브로드캐스트합니다.
    - incident_type: 'FIRE', 'NO_HELMET', 'FALL' 중 하나 입력
    """
    ros_node = request.app.state.ros_node
    confidence = round(random.uniform(0.7, 0.99), 2)

    print(f"\n🚨 [수동 트리거] {incident_type} 강제 감지! (로봇: {robot_id}호기)")

    # 1. 🔒 로봇 자물쇠 채우기 및 로봇에 정지 명령(선택)
    # (실제로는 로봇이 먼저 멈추겠지만, 테스트를 위해 서버에서 자물쇠를 걸어줌)
    ros_node.is_waiting_for_robot_ack[robot_id] = True

    # 2. 가짜 이미지 생성 및 저장
    dummy_frame = create_dummy_frame(incident_type, robot_id)
    saved_photo_url = save_alert_frame(dummy_frame, incident_type, robot_id)
    saved_photo_url = f"/static/alerts/test_{incident_type}.jpg" # 임시 URL

    # 3. DB에 강제 저장
    saved_log = create_incident_log(
        db=db,
        incident_type=incident_type,
        detected_by="ROBOT",
        location_x=round(random.uniform(0.0, 5.0), 2),
        location_y=round(random.uniform(-5.0, 5.0), 2),
        robot_id=robot_id,
        photo_url=saved_photo_url,
        ai_details={"confidence": confidence, "test_trigger": True}
    )

    # 4. Unity로 웹소켓 쏘기
    alert_payload = {
        "type": "NEW_ALERT",
        "data": {
            "log_id": saved_log.log_id,
            "timestamp": saved_log.timestamp.isoformat(),
            "incident_type": saved_log.incident_type,
            "detected_by": saved_log.detected_by,
            "robot_id": saved_log.robot_id,
            "employee_id": saved_log.employee_id,
            "location_x": saved_log.location_x,
            "location_y": saved_log.location_y,
            "photo_url": saved_log.photo_url,
            "status": saved_log.status,
            "cleared_at": saved_log.cleared_at.isoformat() if saved_log.cleared_at else None,
            "ai_details": saved_log.ai_details or {"confidence": confidence},
            "message": f"[수동 테스트] 로봇 {robot_id}호기에서 {incident_type} 감지!"
        }
    }

    await manager.broadcast(alert_payload)

    return {"ok": True, "message": "가짜 알림 생성 및 전송 완료", "payload": alert_payload}

@router.get("/records")
async def get_incident_records(db: Session = Depends(get_db)):
    """
    모든 사건/사고(위반, 응급) 로그를 조회합니다.
    """
    try:
        records = get_all_incidents(db)
        return {
            "ok": True,
            "records": records
        }
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"DB 조회 중 오류가 발생했습니다: {str(e)}"
        )

@router.post("/{log_id}/clear")
async def clear_incident_and_resume_robot(
    log_id: int,
    request: Request,
    db: Session = Depends(get_db)
):
    # 1. DB에서 해당 이상 상황 로그를 "조회"만 먼저 합니다.
    incident = get_incident_log(db, log_id)

    if not incident:
        raise HTTPException(status_code=404, detail="해당 알림 로그를 찾을 수 없습니다.")

    # 이미 처리된 거라면 여기서 바로 튕겨냅니다.
    if incident.status == "CLEARED":
        return {"ok": True, "message": "이미 조치 완료된 알림입니다.", "data": {"log_id": log_id}}

    # 2. DB 업데이트 진행 (여기서 "CLEARED"로 바뀌고 commit 됩니다)
    incident = clear_incident_log(db, log_id)

    # 3. 로봇 파견/멈춤 복귀 분기 로직
    if incident.robot_id:
        ros_node = request.app.state.ros_node
        try:
            # ROS2 서비스 호출
            ros_node.call_set_mode(robot_id=incident.robot_id, mode="RESUME")
            print(f"🛑 [ROS2] /robot{incident.robot_id}/set_mode 서비스에 'RESUME' 송신 성공")

        except Exception as ros_err:
            print(f"❌ [ROS2 에러] 로봇 제어 실패: {ros_err}")
            raise HTTPException(
                status_code=500,
                detail=f"DB는 업데이트되었으나 로봇 {incident.robot_id}호기 서비스 호출에 실패했습니다."
            )

    return {
        "ok": True,
        "message": f"로그 {log_id}번 조치 완료 및 로봇 {incident.robot_id}호기 순찰 재개 명령 전송 완료",
        "data": {
          "log_id": incident.log_id,
          "status": incident.status,
          "cleared_at": incident.cleared_at.strftime("%Y-%m-%d %H:%M:%S"),
          "robot_id": incident.robot_id
        }
    }