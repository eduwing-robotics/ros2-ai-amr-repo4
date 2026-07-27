from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session
from typing import List

from app.db.database import get_db
from app.crud import logs as crud_logs
from app.schemas import logs as schema_logs

router = APIRouter(prefix="/api/v1/logs", tags=["All Logs (개발용 전체 조회)"])

# 1. 인적 자원 출입 로그 API
@router.get("/attendance/employee", response_model=List[schema_logs.EmployeeAttendanceLogResponse])
def read_employee_attendance(db: Session = Depends(get_db)):
    """모든 직원 출퇴근 로그를 최신순으로 가져옵니다."""
    return crud_logs.get_all_employee_attendance(db)

@router.get("/attendance/visitor", response_model=List[schema_logs.VisitorAttendanceLogResponse])
def read_visitor_attendance(db: Session = Depends(get_db)):
    """모든 방문자 입출입 로그를 최신순으로 가져옵니다."""
    return crud_logs.get_all_visitor_attendance(db)

# 2. AI 이상 상황 로그 API
@router.get("/incidents", response_model=List[schema_logs.IncidentLogResponse])
def read_incidents(db: Session = Depends(get_db)):
    """모든 위반 및 응급 상황(Incident) 로그를 최신순으로 가져옵니다."""
    return crud_logs.get_all_incidents(db)

# 3. 로봇 주행 및 관제 로그 API
@router.get("/patrol", response_model=List[schema_logs.PatrolLogResponse])
def read_patrol_logs(db: Session = Depends(get_db)):
    """모든 순찰 임무(큰 틀) 요약 로그를 가져옵니다."""
    return crud_logs.get_all_patrol_logs(db)

@router.get("/patrol/timeline", response_model=List[schema_logs.PatrolTimelineResponse])
def read_patrol_timeline(db: Session = Depends(get_db)):
    """모든 순찰 세부 타임라인(로봇 상태 변화) 로그를 가져옵니다."""
    return crud_logs.get_all_patrol_timelines(db)

@router.get("/commands", response_model=List[schema_logs.RobotCommandLogResponse])
def read_command_logs(db: Session = Depends(get_db)):
    """관제사가 로봇에 내린 모든 제어 명령 이력을 가져옵니다."""
    return crud_logs.get_all_command_logs(db)