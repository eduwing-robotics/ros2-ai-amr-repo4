from sqlalchemy.orm import Session
from app.db import models
from typing import List, Optional
import app.schemas.attendance as schema_attendance
import json

# [1단계] 모든 직원의 얼굴 임베딩 목록 가져오기
def get_all_employee_faces(db: Session):
    """모든 직원의 사번, 이름, 얼굴 임베딩 벡터를 반환합니다."""
    # Employee와 Employee_faces 테이블을 JOIN해서 한 번에 가져옴
    result = db.query(
        models.Employee.employee_id,
        models.Employee.name,
        models.EmployeeFace.face_embedding
    ).join(
        models.EmployeeFace,
        models.Employee.employee_id == models.EmployeeFace.employee_id
    ).all()

    return result

# [2단계] 특정 직원의 현재(가장 최근) 출퇴근 상태 가져오기
def get_employee_current_status(db: Session, employee_id: str) -> Optional[models.EmployeeAttendanceLog]:
    """특정 직원의 가장 최신 출입 로그를 찾아 반환합니다."""
    return db.query(models.EmployeeAttendanceLog)\
             .filter(models.EmployeeAttendanceLog.employee_id == employee_id)\
             .order_by(models.EmployeeAttendanceLog.timestamp.desc())\
             .first()

# [3단계] 직원 출입(출퇴근) 기록 저장하기
def create_employee_attendance_log(db: Session, log_data: schema_attendance.EmployeeRecordRequest) -> models.EmployeeAttendanceLog:
    """노트북에서 출퇴근 버튼을 누르면 새로운 로그를 생성합니다."""

    # DB 모델에 맞게 데이터 조립
    new_log = models.EmployeeAttendanceLog(
        employee_id=log_data.employee_id,
        action_type=log_data.action_type,  # "check_in" 또는 "check_out"
        timestamp=log_data.timestamp,
        admin_override=log_data.admin_override
    )

    # DB에 넣고 저장(commit)
    db.add(new_log)
    db.commit()
    db.refresh(new_log) # 새로 발급된 log_id 등을 가져오기 위해 새로고침

    return new_log

# [4단계] 전체 직원 출퇴근 로그 목록 가져오기 (관리자용)
def get_all_employee_attendance_logs(db: Session, limit: int = 100):
    """관리자 화면에 보여줄 출퇴근 기록을 최신순으로 가져옵니다. (직원 이름 포함)"""

    # Employee 테이블과 JOIN해서 직원의 '이름'도 같이 가져옵니다.
    logs = db.query(
        models.EmployeeAttendanceLog.log_id,
        models.EmployeeAttendanceLog.employee_id,
        models.Employee.name, # 이름 가져오기!
        models.EmployeeAttendanceLog.action_type,
        models.EmployeeAttendanceLog.timestamp,
        models.EmployeeAttendanceLog.admin_override
    ).join(
        models.Employee,
        models.EmployeeAttendanceLog.employee_id == models.Employee.employee_id
    ).order_by(
        models.EmployeeAttendanceLog.timestamp.desc() # 최신순 정렬
    ).limit(limit).all()

    return logs

def create_employee_with_face(
    db: Session,
    employee_id: str,
    name: str,
    position: str,
    phone_number: str,
    face_embedding: list,
    photo_urls: list
) -> models.Employee:
    """새로운 직원 정보와 얼굴 데이터를 DB에 함께 등록합니다."""

    # 1. 직원 마스터 정보 생성
    new_employee = models.Employee(
        employee_id=employee_id,
        name=name,
        position=position,
        phone_number=phone_number
    )
    db.add(new_employee)

    # 2. 얼굴 임베딩 및 사진 경로 정보 생성
    new_face = models.EmployeeFace(
        employee_id=employee_id,
        face_embedding=face_embedding, # pgvector 플러그인이 실수 배열을 벡터로 자동 변환
        photo_urls=photo_urls
    )
    db.add(new_face)

    # 3. 두 테이블의 작업을 한 번에 확정 (성공 시 커밋, 실패 시 자동 롤백됨)
    db.commit()
    db.refresh(new_employee)

    return new_employee

def get_employee(db: Session, employee_id: str) -> Optional[models.Employee]:
    """특정 사번을 가진 직원의 모든 정보(마스터 데이터)를 반환합니다."""
    return db.query(models.Employee).filter(models.Employee.employee_id == employee_id).first()