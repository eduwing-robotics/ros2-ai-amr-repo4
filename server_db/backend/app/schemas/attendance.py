from pydantic import BaseModel
from typing import List, Optional
from datetime import datetime

# ==========================================
# 1. 직원 출퇴근 (Employee Attendance)
# ==========================================

# [요청] 출퇴근 기록 저장 (POST /api/v1/attendance/records)
class EmployeeRecordRequest(BaseModel):
    employee_id: str
    action_type: str         # "check_in" 또는 "check_out"
    timestamp: datetime
    admin_override: bool = False

# [응답] 현재 상태 응답 (상태 조회 및 저장 후 응답 공통 사용)
class AttendanceStateResponse(BaseModel):
    state: str               # "in" 또는 "out"
    mode: Optional[str] = None
    label: Optional[str] = None
    last_recorded_at: Optional[datetime] = None

# [응답] 출퇴근 로그 목록 조회
class EmployeeLogItem(BaseModel):
    log_id: int
    employee_id: str
    name: str
    action_type: str
    timestamp: datetime
    admin_override: bool

class EmployeeLogListResponse(BaseModel):
    ok: bool
    records: List[EmployeeLogItem]


# ==========================================
# 2. 방문자 입퇴장 (Visitor Access)
# ==========================================

# [요청] 방문자 입퇴장 기록 저장 (POST /api/v1/visitor-access/records)
class VisitorRecordRequest(BaseModel):
    visitor_id: str
    action_type: str         # "entry" 또는 "exit"
    timestamp: datetime
    admin_override: bool = False

# [응답] 방문자 상태 응답
class VisitorStateResponse(BaseModel):
    state: str               # "in" 또는 "out"
    mode: Optional[str] = None
    label: Optional[str] = None
    last_recorded_at: Optional[datetime] = None

# [응답] 방문자 목록/로그 아이템
class VisitorLogItem(BaseModel):
    log_id: int
    visitor_id: str
    name: str
    action_type: str
    timestamp: datetime
    admin_override: bool