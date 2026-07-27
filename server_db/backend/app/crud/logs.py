from datetime import datetime
from typing import Optional, Dict, Any
from sqlalchemy.orm import Session
from app.db import models
from app.schemas import logs as schema_logs

# ==========================================
# 조회 관련
# ==========================================

# 1. 출퇴근 로그 전체 조회
def get_all_employee_attendance(db: Session):
    return db.query(models.EmployeeAttendanceLog).order_by(models.EmployeeAttendanceLog.timestamp.desc()).all()

def get_all_visitor_attendance(db: Session):
    return db.query(models.VisitorAttendanceLog).order_by(models.VisitorAttendanceLog.timestamp.desc()).all()

# 2. 사건/사고(위반, 응급) 로그 전체 조회
def get_all_incidents(db: Session):
    return db.query(models.IncidentLog).order_by(models.IncidentLog.timestamp.desc()).all()

# 3. 로봇 임무 및 타임라인 로그 전체 조회
def get_all_patrol_logs(db: Session):
    return db.query(models.PatrolLog).order_by(models.PatrolLog.start_time.desc()).all()

def get_all_patrol_timelines(db: Session):
    return db.query(models.PatrolTimeline).order_by(models.PatrolTimeline.changed_at.desc()).all()

# 4. 로봇 제어 명령 로그 전체 조회
def get_all_command_logs(db: Session):
    return db.query(models.RobotCommandLog).order_by(models.RobotCommandLog.requested_at.desc()).all()


# ==========================================
# 저장 관련
# ==========================================

# 새로운 위반/응급 상황 저장
def create_incident_log(
    db: Session,
    incident_type: str,          # 예: 'FIRE', 'EVENT_HELMET', 'EVENT_FALL'
    detected_by: str,            # 예: 'ROBOT' 또는 'GLOBAL_CAM'
    location_x: float,           # 발생 X 좌표
    location_y: float,           # 발생 Y 좌표
    robot_id: Optional[int] = None,       # 발견한 로봇 번호 (글로벌캠이면 None)
    employee_id: Optional[str] = None,    # 누군지 식별되었다면 사번, 아니면 None
    photo_url: Optional[str] = None,      # 저장된 사진 경로
    ai_details: Optional[Dict[str, Any]] = None  # AI 신뢰도 등 추가 정보 (JSONB)
) -> models.IncidentLog:
    """새로운 위반/응급 상황을 DB에 저장합니다."""

    # 1. 넣을 데이터 조립 (뼈대 만들기)
    new_incident = models.IncidentLog(
        incident_type=incident_type,
        detected_by=detected_by,
        robot_id=robot_id,
        employee_id=employee_id,
        location_x=location_x,
        location_y=location_y,
        photo_url=photo_url,
        ai_details=ai_details
    )

    # 2. DB에 밀어 넣고 확정(commit)하기
    db.add(new_incident)
    db.commit()

    # 3. 방금 들어간 데이터(특히 자동 생성된 log_id)를 다시 가져와서 반환
    db.refresh(new_incident)
    return new_incident

# 새로운 직원 프로필 등록
def create_employee(db: Session, employee: schema_logs.EmployeeCreate) -> models.Employee:
    """새로운 작업자를 등록합니다."""
    new_employee = models.Employee(
        employee_id=employee.employee_id,
        name=employee.name,
        position=employee.position,
        phone_number=employee.phone_number
    )
    db.add(new_employee)
    db.commit()
    db.refresh(new_employee)
    return new_employee

# 순찰 임무 시작 등록
def create_patrol_log(db: Session, patrol: schema_logs.PatrolLogCreate) -> models.PatrolLog:
    """터틀봇이 관제탑 명령을 받거나 자동 스케줄링으로 순찰 임무를 시작할 때 호출됩니다."""
    new_patrol = models.PatrolLog(
        robot_id=patrol.robot_id,
        status=patrol.status,
        start_time=datetime.now() # 서버 시간 기준으로 순찰 시작 시간 캡처
    )
    db.add(new_patrol)
    db.commit()
    db.refresh(new_patrol)
    return new_patrol

# 순찰 도중 의미 있는 상태 전리(FSM Transition) 누적 기록
def create_patrol_timeline(db: Session, timeline: schema_logs.PatrolTimelineCreate) -> models.PatrolTimeline:
    """
    로봇 자율주행팀 FSM 상태 전이(배터리 부족, 장애물 대기 등)가 넘어올 때 타임라인을 기록합니다.
    이 데이터가 꼼꼼히 쌓여야 공장 대시보드 모니터링이 원활하게 구동됩니다.
    """
    new_timeline = models.PatrolTimeline(
        log_id=timeline.log_id,
        state=timeline.state,
        pause_reason=timeline.pause_reason,
        location_x=timeline.location_x,
        location_y=timeline.location_y,
        changed_at=datetime.now()
    )
    db.add(new_timeline)
    db.commit()
    db.refresh(new_timeline)
    return new_timeline

# 순찰 임무 종료 업데이트 함수
def update_patrol_log_end(db: Session, log_id: int, final_status: str) -> Optional[models.PatrolLog]:
    """순찰이 정상 완료(SUCCESS)되거나 긴급 정지 등으로 중단(FAIL)되었을 때 종료 시간을 마킹합니다."""
    patrol_record = db.query(models.PatrolLog).filter(models.PatrolLog.log_id == log_id).first()
    if patrol_record:
        patrol_record.end_time = datetime.now()
        patrol_record.status = final_status
        db.commit()
        db.refresh(patrol_record)
    return patrol_record

# 관제 명령 실행 결과 로그 저장
def create_command_log(
    db: Session,
    robot_id: int,
    operator_id: str,
    command: str,
    result_status: str,          # 'accepted', 'rejected', 'error', 'timeout' 등
    response_message: str = None
) -> models.RobotCommandLog:
    """관제 GUI에서 로봇으로 보낸 제어 명령과 그 결과를 기록합니다."""

    new_log = models.RobotCommandLog(
        robot_id=robot_id,
        operator_id=operator_id,
        command=command,
        result_status=result_status,
        response_message=response_message,
        requested_at=datetime.now()
    )
    db.add(new_log)
    db.commit()
    db.refresh(new_log)
    return new_log

# 💡 [추가] 특정 로그 하나만 조회하는 함수
def get_incident_log(db: Session, log_id: int) -> Optional[models.IncidentLog]:
    return db.query(models.IncidentLog).filter(models.IncidentLog.log_id == log_id).first()

# 알림 조치 완료 업데이트
def clear_incident_log(db: Session, log_id: int):
    """특정 알림 로그를 '조치 완료(CLEARED)' 상태로 업데이트합니다."""
    incident = db.query(models.IncidentLog).filter(models.IncidentLog.log_id == log_id).first()
    if incident and incident.status != "CLEARED":
        incident.status = "CLEARED"
        incident.cleared_at = datetime.now()
        db.commit()
        db.refresh(incident)
    return incident