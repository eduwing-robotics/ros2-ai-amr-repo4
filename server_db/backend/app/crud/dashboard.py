from sqlalchemy.orm import Session
from sqlalchemy import func
from datetime import date
from app.db.models import EmployeeAttendanceLog, VisitorAttendanceLog
from app.db.models import IncidentLog # (실제 모델 경로에 맞게 수정)

def get_today_employee_attendance_count(db: Session, target_date: date) -> int:
    """오늘 출근한 현재 인원 계산 (최신 상태 기준)"""
    from sqlalchemy import and_

    # 1. 각 직원별 최신 로그의 timestamp 구하기
    subquery = db.query(
        EmployeeAttendanceLog.employee_id,
        func.max(EmployeeAttendanceLog.timestamp).label("max_ts")
    ).group_by(EmployeeAttendanceLog.employee_id).subquery()

    # 2. 최신 로그의 action_type이 'check_in'이고, 그 날짜가 target_date인 인원 계산
    current_in = db.query(func.count(EmployeeAttendanceLog.log_id)).join(
        subquery,
        and_(
            EmployeeAttendanceLog.employee_id == subquery.c.employee_id,
            EmployeeAttendanceLog.timestamp == subquery.c.max_ts
        )
    ).filter(
        EmployeeAttendanceLog.action_type == "check_in",
        func.date(EmployeeAttendanceLog.timestamp) == target_date
    ).scalar() or 0

    return current_in

def get_today_employee_leave_count(db: Session, target_date: date) -> int:
    """오늘 퇴근한 현재 인원 계산 (최신 상태 기준)"""
    from sqlalchemy import and_

    # 1. 각 직원별 최신 로그의 timestamp 구하기
    subquery = db.query(
        EmployeeAttendanceLog.employee_id,
        func.max(EmployeeAttendanceLog.timestamp).label("max_ts")
    ).group_by(EmployeeAttendanceLog.employee_id).subquery()

    # 2. 최신 로그의 action_type이 'check_out'이고, 그 날짜가 target_date인 인원 계산
    current_out = db.query(func.count(EmployeeAttendanceLog.log_id)).join(
        subquery,
        and_(
            EmployeeAttendanceLog.employee_id == subquery.c.employee_id,
            EmployeeAttendanceLog.timestamp == subquery.c.max_ts
        )
    ).filter(
        EmployeeAttendanceLog.action_type == "check_out",
        func.date(EmployeeAttendanceLog.timestamp) == target_date
    ).scalar() or 0

    return current_out

def get_today_visitor_count(db: Session, target_date: date) -> int:
    """오늘 누적 방문자 수 계산 (중복 제외)"""
    return db.query(func.count(func.distinct(VisitorAttendanceLog.visitor_id))).filter(
        func.date(VisitorAttendanceLog.timestamp) == target_date,
        VisitorAttendanceLog.action_type == "entry"
    ).scalar() or 0

def get_today_incident_counts(db: Session, target_date: date) -> list:
    """오늘 발생한 이상상황 건수를 타입별로 그룹화하여 반환"""
    return db.query(
        IncidentLog.incident_type,
        func.count(IncidentLog.log_id)
    ).filter(
        func.date(IncidentLog.timestamp) == target_date
    ).group_by(
        IncidentLog.incident_type      # 타입으로만 묶음
    ).all()