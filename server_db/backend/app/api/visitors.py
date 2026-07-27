from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from app.db.database import get_db
from app.crud import visitors as crud_visitors
from app.schemas import attendance as schema_attendance

router = APIRouter()

# ==========================================
# 6-1. 방문자 QR 정보 전체 조회 (노트북 초기화용)
# ==========================================
@router.get("/qr-registry")
def get_qr_registry(db: Session = Depends(get_db)):
    """QR 기반 오프라인 검증을 위해 방문자 ID와 토큰 목록을 가져갑니다."""
    tokens = crud_visitors.get_all_visitor_qr_tokens(db)

    response_data = [{"visitor_id": v.visitor_id, "qr_token": v.qr_token} for v in tokens]
    return response_data

# ==========================================
# 6-2. 특정 방문자 현재 상태 조회
# ==========================================
@router.get("/{visitor_number}/access-state")
def get_visitor_access_state(visitor_number: str, db: Session = Depends(get_db)):
    """QR 인식 성공 후, 이 방문자가 입장해야 하는지 퇴장해야 하는지 판별합니다."""
    last_log = crud_visitors.get_visitor_current_status(db, visitor_number)

    if not last_log:
        return {"state": "out"}

    current_state = "in" if last_log.action_type == "entry" else "out"
    return {"state": current_state}

# ==========================================
# 7. 현재 공장 안에 있는 방문자 목록 조회 (방식 B)
# ==========================================
@router.get("/inside")
def get_visitors_inside(db: Session = Depends(get_db)):
    """관리자 대시보드에 표시할 '아직 안 나간(체류 중)' 방문자 목록을 반환합니다."""
    inside_visitors = crud_visitors.get_inside_visitors(db)
    # 필요한 정보만 가공해서 전달
    return [
        {
            "visitor_id": v.visitor_id,
            "last_entry_time": v.timestamp
        } for v in inside_visitors
    ]