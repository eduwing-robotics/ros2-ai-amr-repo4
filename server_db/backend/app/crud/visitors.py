from sqlalchemy.orm import Session
from app.db import models
from app.schemas import attendance as schema_attendance
from typing import Optional, List

# [6-1단계] 모든 방문자의 QR 토큰 목록 가져오기 (초기 동기화용)
def get_all_visitor_qr_tokens(db: Session):
    """
    노트북(Edge)이 로컬에서 QR을 빠르게 검증할 수 있도록
    모든 방문자의 ID와 QR 토큰을 반환합니다.
    """
    return db.query(models.Visitor.visitor_id, models.Visitor.qr_token).all()

# [6-2단계] 특정 방문자의 가장 최근 입퇴장 상태 가져오기
def get_visitor_current_status(db: Session, visitor_id: str) -> Optional[models.VisitorAttendanceLog]:
    """특정 방문자의 가장 최신 출입 로그를 찾아 반환합니다."""
    return db.query(models.VisitorAttendanceLog)\
             .filter(models.VisitorAttendanceLog.visitor_id == visitor_id)\
             .order_by(models.VisitorAttendanceLog.timestamp.desc())\
             .first()

# [6-3단계] 방문자 입/퇴장 기록 저장하기
def create_visitor_attendance_log(db: Session, log_data: schema_attendance.VisitorRecordRequest) -> models.VisitorAttendanceLog:
    """방문자가 QR을 찍고 게이트를 통과했을 때 로그를 생성합니다."""
    new_log = models.VisitorAttendanceLog(
        visitor_id=log_data.visitor_id,
        action_type=log_data.action_type,  # "entry" 또는 "exit"
        timestamp=log_data.timestamp,
        admin_override=log_data.admin_override
    )

    db.add(new_log)
    db.commit()
    db.refresh(new_log)
    return new_log

# [6-4단계] 방문자 전체 로그 가져오기 (관리자용)
def get_all_visitor_logs(db: Session, limit: int = 100):
    """관리자 화면에 보여줄 방문자 출입 기록을 최신순으로 가져옵니다."""
    logs = db.query(
        models.VisitorAttendanceLog.log_id,
        models.VisitorAttendanceLog.visitor_id,
        models.Visitor.name, # JOIN으로 이름 가져오기
        models.VisitorAttendanceLog.action_type,
        models.VisitorAttendanceLog.timestamp,
        models.VisitorAttendanceLog.admin_override
    ).join(
        models.Visitor,
        models.VisitorAttendanceLog.visitor_id == models.Visitor.visitor_id
    ).order_by(
        models.VisitorAttendanceLog.timestamp.desc()
    ).limit(limit).all()

    return logs

# [7단계용 미리 준비] 현재 미퇴장(체류 중)인 방문자 목록 가져오기
def get_inside_visitors(db: Session):
    """
    가장 마지막 로그가 'entry'(입장)인 방문자만 걸러서 반환합니다.
    (방식 B 채택에 맞춤)
    """
    # PostgreSQL의 DISTINCT ON을 사용하여 각 방문자의 가장 최신 로그만 뽑아낸 후 서브쿼리로 만듭니다.
    subquery = db.query(models.VisitorAttendanceLog)\
                 .distinct(models.VisitorAttendanceLog.visitor_id)\
                 .order_by(models.VisitorAttendanceLog.visitor_id, models.VisitorAttendanceLog.timestamp.desc())\
                 .subquery()

    # 최신 로그가 'entry'인 사람만 조회
    active_visitors = db.query(subquery).filter(subquery.c.action_type == "entry").all()
    return active_visitors