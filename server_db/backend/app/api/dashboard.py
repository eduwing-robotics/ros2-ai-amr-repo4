from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session
from datetime import date
from app.db.database import get_db
from app.crud import dashboard as crud_dashboard # CRUD 함수 가져오기
from app.schemas.dashboard import DashboardSummaryResponse # 스키마 가져오기

router = APIRouter(prefix="/api/v1/dashboard", tags=["v1 오늘의 요약 통계 API"])

@router.get("/today-summary", response_model=DashboardSummaryResponse)
def get_today_summary(db: Session = Depends(get_db)):
    """관제 GUI 대시보드 상단의 '오늘의 요약 통계' 숫자를 한 번에 조회합니다."""

    today = date.today()

    # 1. 주방(CRUD)에 데이터 요청
    current_in_employees = crud_dashboard.get_today_employee_attendance_count(db, today)
    current_out_employees = crud_dashboard.get_today_employee_leave_count(db, today)
    today_visitors = crud_dashboard.get_today_visitor_count(db, today)
    incident_data = crud_dashboard.get_today_incident_counts(db, today)

    # 2. 결과물(Raw Data)을 프론트엔드 포맷에 맞게 가공 (비즈니스 로직)
    violation_summary = {"NO_HELMET": 0}
    emergency_summary = {"FALL": 0, "FIRE": 0}

    for i_type, count in incident_data:
        if i_type in violation_summary:
            violation_summary[i_type] = count
        elif i_type in emergency_summary:
            emergency_summary[i_type] = count

    # 3. 완성된 JSON(Schema) 반환
    return {
        "ok": True,
        "today_summary": {
            "attendance": {
                "current_in": current_in_employees,
                "current_out": current_out_employees
            },
            "visitor": {"today_total": today_visitors},
            "violation": violation_summary,
            "emergency": emergency_summary
        }
    }