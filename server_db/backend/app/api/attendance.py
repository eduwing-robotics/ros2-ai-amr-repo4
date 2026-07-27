from fastapi import APIRouter, Depends, HTTPException, BackgroundTasks
from sqlalchemy.orm import Session
from app.db.database import get_db
from app.crud import employees as crud_employees
from app.schemas import attendance as schema_attendance
from app.crud import visitors as crud_visitors
from app.core.websocket import manager

router = APIRouter()

# ==========================================
# [3단계] 직원 출/퇴근 기록 저장 (수동 보정 포함)
# ==========================================
@router.post("/records", response_model=schema_attendance.AttendanceStateResponse)
def record_employee_attendance(
    log_data: schema_attendance.EmployeeRecordRequest,
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db)
):
    """노트북 단말기에서 직원의 출근/퇴근이 확정되었을 때 호출됩니다."""

    # 1. DB에 로그 저장
    saved_log = crud_employees.create_employee_attendance_log(db, log_data)

    employee = crud_employees.get_employee(db, saved_log.employee_id)
    employee_name = employee.name if employee else "알수없음"

    # 2. 명세서에 맞춘 응답 데이터 조립
    # 방금 "check_in"(출근)을 찍었으면 현재 상태는 "in", "OUT"(퇴근)을 찍었으면 "out"이 됩니다.
    current_state = "in" if saved_log.action_type == "check_in" else "out"
    mode_text = "check_in" if saved_log.action_type == "check_in" else "check_out"
    label_text = "입장 상태" if saved_log.action_type == "check_in" else "퇴장 상태"

    ws_payload = {
        "type": "EMPLOYEE_ATTENDANCE",
        "data": {
            "employee_id": saved_log.employee_id,
            "name": employee_name,
            "action_type": saved_log.action_type,
            "timestamp": saved_log.timestamp.isoformat()
        }
    }
    background_tasks.add_task(manager.broadcast, ws_payload)
    print(ws_payload)

    return {
        "state": current_state,
        "mode": mode_text,
        "label": label_text,
        "last_recorded_at": saved_log.timestamp
    }

# ==========================================
# [4단계] 직원 출퇴근 로그 목록 조회 (관리자 화면용)
# ==========================================
@router.get("/records", response_model=schema_attendance.EmployeeLogListResponse)
def get_employee_attendance_logs(limit: int = 100, db: Session = Depends(get_db)):
    """관리자 대시보드의 '로그' 탭에서 전체 출입 기록을 긁어갈 때 호출됩니다."""

    logs = crud_employees.get_all_employee_attendance_logs(db, limit=limit)

    # 스키마(EmployeeLogItem) 규격에 맞게 변환
    formatted_records = []
    for log in logs:
        formatted_records.append({
            "log_id": log.log_id,
            "employee_id": log.employee_id,
            "name": log.name, # JOIN으로 가져온 이름
            "action_type": log.action_type,
            "timestamp": log.timestamp,
            "admin_override": log.admin_override
        })

    return {
        "ok": True,
        "records": formatted_records
    }

# ==========================================
# 6-3. 방문자 입/퇴장 기록 저장 (수동 보정 포함)
# ==========================================
@router.post("/visitor-access/records", response_model=schema_attendance.VisitorStateResponse)
def record_visitor_access(
    log_data: schema_attendance.VisitorRecordRequest,
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db)
):
    """노트북 단말기에서 방문자의 입/퇴장이 확정되었을 때 호출됩니다."""
    saved_log = crud_visitors.create_visitor_attendance_log(db, log_data)

    current_state = "in" if saved_log.action_type == "entry" else "out"
    mode_text = "entry" if saved_log.action_type == "entry" else "exit"
    label_text = "입장 상태" if saved_log.action_type == "entry" else "퇴장 상태"

    ws_payload = {
        "type": "VISITOR_ATTENDANCE",
        "data": {
            "visitor_id": saved_log.visitor_id,
            "name": saved_log.visitor.name if saved_log.visitor else "알수없음",
            "action_type": saved_log.action_type,
            "timestamp": saved_log.timestamp.isoformat()
        }
    }
    background_tasks.add_task(manager.broadcast, ws_payload)
    print(ws_payload)

    return {
        "state": current_state,
        "mode": mode_text,
        "label": label_text,
        "last_recorded_at": saved_log.timestamp
    }

# ==========================================
# 6-4. 방문자 로그 목록 조회 (관리자 화면용)
# ==========================================
@router.get("/visitor-access/records")
def get_visitor_access_logs(limit: int = 100, db: Session = Depends(get_db)):
    """관리자 대시보드의 '방문자' 탭에서 전체 출입 기록을 가져갑니다."""
    logs = crud_visitors.get_all_visitor_logs(db, limit=limit)

    formatted_records = []
    for log in logs:
        formatted_records.append({
            "log_id": log.log_id,
            "visitor_id": log.visitor_id,
            "name": log.name,
            "action_type": log.action_type,
            "timestamp": log.timestamp,
            "admin_override": log.admin_override
        })

    return {
        "ok": True,
        "records": formatted_records
    }



'''
from fastapi import APIRouter, Depends, File, UploadFile, Form, Query, HTTPException
from sqlalchemy.orm import Session
from sqlalchemy import select, func
from typing import List, Optional
from datetime import datetime

# 기존 프로젝트 구조에서 불러올 모듈들 (환경에 맞게 경로 조절 가능)
from app.db.database import get_db
from app.db.models import Employee, EmployeeFace, EmployeeAttendanceLog, Visitor, VisitorAttendanceLog  # 필요한 모델들
from app.services.file_manager import save_upload_file
from pydantic import BaseModel

# 💡 라우터 기본 접두사를 /api/v1으로 설정합니다.
router = APIRouter(prefix="/api/v1", tags=["v1 직원 관련 API"])


# ==========================================
# 📑 [Pydantic Schemas] 요청/응답용 DTO 정의
# ==========================================

class EmployeeCreate(BaseModel):
    employee_id: str
    name: str
    position: str
    phone_number: str

class EmployeeUpdate(BaseModel):
    name: Optional[str] = None
    position: Optional[str] = None
    phone_number: Optional[str] = None


# ==========================================
# 📱 [출/퇴근 관련 API - T 파트]
# ==========================================

@router.get("/health")
async def get_health_status():
    """
    [T-01] 태블릿 서버 상태 확인
    """
    return {
        "ok": True,
        "data": {
            "server": "ok",
            "database": "ok",
            "face_model": "ready",
            "time": datetime.now().isoformat()
        }
    }


@router.post("/attendance/recognize")
async def recognize_attendance(
    image: UploadFile = File(...),
    action_type: str = Form(...), # "IN" 또는 "OUT"
    device_id: str = Form(...),
    db: Session = Depends(get_db)
):
    """
    [T-02] 사진 1장 기반 출근/퇴근 인증
    """
    # 1. 파일 세이브
    saved_url = save_upload_file(image, sub_folder="attendance")

    # 🧠 [AI 모델 연동부] 임시로 송한결(003) 사원을 인식했다고 가정
    detected_employee_id = "003"
    confidence_score = 0.92

    # 2. DB 직원 확인
    employee = db.query(Employee).filter(Employee.employee_id == detected_employee_id, Employee.is_active == True).first()
    if not employee:
        return {
            "ok": False,
            "error_code": "FACE_NOT_MATCHED",
            "message": "등록된 직원과 일치하지 않습니다."
        }

    # 3. 출퇴근 로그 기록 추가
    new_log = AttendanceLog(
        employee_id=employee.employee_id,
        action_type=action_type
    )
    db.add(new_log)
    db.commit()
    db.refresh(new_log)

    action_label = "출근" if action_type == "IN" else "퇴근"
    return {
        "ok": True,
        "data": {
            "attendance_id": new_log.log_id,
            "employee": {
                "employee_id": employee.employee_id,
                "name": employee.name,
                "position": employee.position
            },
            "action_type": new_log.action_type,
            "action_label": action_label,
            "timestamp": new_log.timestamp.isoformat(),
            "confidence": confidence_score
        },
        "message": f"{employee.name}님 {action_label} 처리되었습니다."
    }


@router.get("/attendance/recent")
async def get_recent_attendance(
    limit: int = Query(default=10, le=50),
    db: Session = Depends(get_db)
):
    """
    [T-03] 최근 태블릿 인증 결과 조회 (이름 JOIN 포함)
    """
    stmt = (
        select(
            AttendanceLog.log_id,
            Employee.name,
            AttendanceLog.action_type,
            AttendanceLog.timestamp
        )
        .join(Employee, AttendanceLog.employee_id == Employee.employee_id)
        .order_by(AttendanceLog.timestamp.desc())
        .limit(limit)
    )
    results = db.execute(stmt).all()

    attendance_list = [
        {
            "log_id": row.log_id,
            "name": row.name,
            "action_type": row.action_type,
            "timestamp": row.timestamp.isoformat()
        } for row in results
    ]
    return {"ok": True, "data": attendance_list}


# ==========================================
# 👥 [직원 및 얼굴 관리 API - EMP / FACE 파트]
# ==========================================

@router.get("/employees")
async def get_employees(
    q: Optional[str] = Query(default=""),
    page: int = Query(default=1, ge=1),
    size: int = Query(default=20, ge=1),
    db: Session = Depends(get_db)
):
    """
    [EMP-01] 직원 목록 조회 (페이징 및 검색 기능 포함)
    """
    query = db.query(Employee).filter(Employee.is_active == True)
    if q:
        query = query.filter(Employee.name.contains(q) | Employee.position.contains(q))

    total = query.count()
    offset = (page - 1) * size
    employees = query.offset(offset).limit(size).all()

    items = [
        {
            "employee_id": emp.employee_id,
            "name": emp.name,
            "position": emp.position,
            "phone_number": emp.phone_number,
            "has_face": True, # 임시 True 처리, 실제로는 얼굴 데이터 유무 확인 후 결정
            "created_at": emp.created_at.isoformat()
        } for emp in employees
    ]
    return {
        "ok": True,
        "data": {
            "items": items,
            "page": page,
            "size": size,
            "total": total
        }
    }


@router.post("/employees")
async def create_employee(employee_data: EmployeeCreate, db: Session = Depends(get_db)):
    """
    [EMP-02] 신규 직원 등록
    """
    # 중복 체크
    existing = db.query(Employee).filter(Employee.employee_id == employee_data.employee_id).first()
    if existing:
        raise HTTPException(status_code=400, detail="이미 존재하는 사번입니다.")

    new_emp = Employee(
        employee_id=employee_data.employee_id,
        name=employee_data.name,
        position=employee_data.position,
        phone_number=employee_data.phone_number,
        is_active=True
    )
    db.add(new_emp)
    db.commit()
    db.refresh(new_emp)

    return {
        "ok": True,
        "data": {
            "employee_id": new_emp.employee_id,
            "name": new_emp.name,
            "position": new_emp.position,
            "phone_number": new_emp.phone_number,
            "created_at": new_emp.created_at.isoformat()
        },
        "message": "신규 직원이 성공적으로 등록되었습니다."
    }


@router.patch("/employees/{employee_id}")
async def update_employee(employee_id: str, update_data: EmployeeUpdate, db: Session = Depends(get_db)):
    """
    [EMP-03] 직원 정보 수정 (PATCH)
    """
    emp = db.query(Employee).filter(Employee.employee_id == employee_id, Employee.is_active == True).first()
    if not emp:
        raise HTTPException(status_code=404, detail="직원을 찾을 수 없습니다.")

    # 넘어온 값만 핀셋 업데이트
    if update_data.name is not None: emp.name = update_data.name
    if update_data.position is not None: emp.position = update_data.position
    if update_data.phone_number is not None: emp.phone_number = update_data.phone_number

    db.commit()
    db.refresh(emp)

    return {
        "ok": True,
        "data": {
            "employee_id": emp.employee_id,
            "name": emp.name,
            "position": emp.position,
            "phone_number": emp.phone_number,
            "created_at": emp.created_at.isoformat()
        },
        "message": "직원 정보가 수정되었습니다."
    }


@router.delete("/employees/{employee_id}")
async def deactivate_employee(employee_id: str, db: Session = Depends(get_db)):
    """
    [EMP-04] 직원 비활성화 (Soft Delete)
    """
    emp = db.query(Employee).filter(Employee.employee_id == employee_id).first()
    if not emp:
        raise HTTPException(status_code=404, detail="직원을 찾을 수 없습니다.")

    emp.is_active = False # 실제 삭제 대신 상태값 변경
    db.commit()

    return {
        "ok": True,
        "message": "해당 직원이 성공적으로 비활성화 되었습니다."
    }


@router.post("/employees/{employee_id}/faces")
async def register_employee_faces(
    employee_id: str,
    photos: List[UploadFile] = File(...),
    capture_mode: Optional[str] = Form("webcam")
):
    """
    [FACE-01] 직원 얼굴 등록 (다중 파일 처리 및 AI 특징 추출)
    """
    saved_urls = []
    for photo in photos:
        url = save_upload_file(photo, sub_folder=f"faces/{employee_id}")
        saved_urls.append(url)

    # 🧠 [AI 연동부] 이 시점에 InsightFace 모델을 사용해 512차원 특징 벡터(Embedding)를 생성 후 DB 저장 로직 수행 가능

    return {
        "ok": True,
        "data": {
            "employee_id": employee_id,
            "photo_urls": saved_urls,
            "embedding_model": "insightface",
            "embedding_dim": 512,
            "registered_photo_count": len(saved_urls),
            "updated_at": datetime.now().isoformat()
        },
        "message": "얼굴 등록이 완료되었습니다."
    }


@router.get("/employees/{employee_id}/faces")
async def get_employee_face_status(employee_id: str):
    """
    [FACE-02] 직원 얼굴 등록 상태 조회
    """
    # 임시 Mock 데이터 반환 (실제로는 DB에서 해당 사원의 가입 이력과 사진 경로 확인 가능)
    return {
        "ok": True,
        "data": {
            "has_face": true,
            "photo_urls": [f"/static/faces/{employee_id}/20260608_153012_01.jpg"],
            "updated_at": datetime.now().isoformat(),
            "embedding_model": "insightface"
        }
    }


# ==========================================
# 📊 [관리자 기록/안전 로그 조회 API - LOG 파트]
# ==========================================

@router.get("/attendance/logs")
async def get_attendance_logs(
    from_date: Optional[str] = Query(None, alias="from"),
    to_date: Optional[str] = Query(None, alias="to"),
    employee_id: Optional[str] = Query(None),
    action_type: Optional[str] = Query(None),
    page: int = Query(default=1, ge=1),
    size: int = Query(default=20, ge=1),
    db: Session = Depends(get_db)
):
    """
    [LOG-01] 출퇴근 기록 조회 (전체 이력 관리용)
    """
    # 전체 로그와 직원의 이름을 매칭하여 가져오는 대형 쿼리 베이스 예시
    query = db.query(AttendanceLog, Employee.name).join(Employee, AttendanceLog.employee_id == Employee.employee_id)

    if employee_id: query = query.filter(AttendanceLog.employee_id == employee_id)
    if action_type: query = query.filter(AttendanceLog.action_type == action_type)

    total = query.count()
    offset = (page - 1) * size
    logs = query.offset(offset).limit(size).all()

    items = [
        {
            "log_id": log.AttendanceLog.log_id,
            "employee_id": log.AttendanceLog.employee_id,
            "name": log.name,
            "action_type": log.AttendanceLog.action_type,
            "timestamp": log.AttendanceLog.timestamp.isoformat()
        } for log in logs
    ]
    return {"ok": True, "data": {"items": items, "page": page, "size": size, "total": total}}


@router.get("/violations")
async def get_violation_logs(page: int = 1, size: int = 20):
    """
    [LOG-02] 위반 사항 로그 조회 (Mock 데이터)
    """
    return {
        "ok": True,
        "data": {
            "items": [
                {
                    "violation_id": 501,
                    "type": "NO_HELMET",
                    "employee_id": "003",
                    "name": "송한결",
                    "timestamp": datetime.now().isoformat()
                }
            ],
            "page": page,
            "size": size,
            "total": 1
        }
    }


@router.get("/emergencies")
async def get_emergency_logs(page: int = 1, size: int = 20):
    """
    [LOG-03] 응급 상황 로그 조회 (Mock 데이터)
    """
    return {
        "ok": True,
        "data": {
            "items": [
                {
                    "emergency_id": 301,
                    "type": "FALL",
                    "robot_id": "tb3-01",
                    "timestamp": datetime.now().isoformat()
                }
            ],
            "page": page,
            "size": size,
            "total": 1
        }
    }
'''