from pydantic import BaseModel, ConfigDict
from datetime import datetime
from typing import Optional, Any

# ==========================================
# 1. 인적 자원 및 출입 관리 스키마
# ==========================================
class EmployeeAttendanceLogResponse(BaseModel):
    log_id: int
    employee_id: str
    action_type: str
    timestamp: datetime
    admin_override: bool
    model_config = ConfigDict(from_attributes=True)

class VisitorAttendanceLogResponse(BaseModel):
    log_id: int
    visitor_id: str
    action_type: str
    timestamp: datetime
    admin_override: bool
    model_config = ConfigDict(from_attributes=True)

# ==========================================
# 2. AI 이상 상황(위반/응급) 스키마
# ==========================================
class IncidentLogResponse(BaseModel):
    log_id: int
    incident_type: str
    employee_id: Optional[str] = None
    detected_by: str
    robot_id: Optional[int] = None
    location_x: float
    location_y: float
    photo_url: Optional[str] = None
    ai_details: Optional[Any] = None
    status: str
    timestamp: datetime
    cleared_at: Optional[datetime] = None
    model_config = ConfigDict(from_attributes=True)

# ==========================================
# 3. 로봇 운용 및 관제 스키마
# ==========================================
class PatrolLogResponse(BaseModel):
    log_id: int
    robot_id: int
    status: str
    start_time: datetime
    end_time: Optional[datetime] = None
    model_config = ConfigDict(from_attributes=True)

class PatrolTimelineResponse(BaseModel):
    timeline_id: int
    log_id: int
    state: str
    pause_reason: Optional[str] = None
    location_x: float
    location_y: float
    changed_at: datetime
    model_config = ConfigDict(from_attributes=True)

class RobotCommandLogResponse(BaseModel):
    command_id: int
    robot_id: int
    operator_id: str
    command: str
    payload: Optional[Any] = None
    result_status: str
    response_message: Optional[str] = None
    requested_at: datetime
    model_config = ConfigDict(from_attributes=True)

# ==========================================
# 4. 데이터 생성(INSERT) 요청용 스키마
# ==========================================

# --- 직원 추가 요청 ---
class EmployeeCreate(BaseModel):
    employee_id: str
    name: str
    position: Optional[str] = None
    phone_number: Optional[str] = None

# --- 순찰 임무 시작 요청 ---
class PatrolLogCreate(BaseModel):
    robot_id: int
    status: str = "START" # 최초 등록 시 상태 기본값???? 이게 맞나?

# --- 순찰 도중 의미 있는 상태 전이(Timeline) 기록 요청 ---
class PatrolTimelineCreate(BaseModel):
    log_id: int
    state: str                          # PAUSED, STUCK, MANUAL_CONTROL, LOW_BATTERY, EMERGENCY_STOP, MOVING_TO_EVENT, PATROLLING(순찰 시작/재개 시), RESUMING_AFTER_CHARGE(충전 후 복귀), ARRIVED(목표 도착/완료)
    pause_reason:Optional[str] = None   # EVENT_HElMET, EVENT_FALL, EVENT_FIRE, MANUAL_DONE
    location_x: float   # 상태가 변한 지도상의 진짜 SLAM X 좌표
    location_y: float   # 상태가 변한 지도상의 진짜 SLAM Y 좌표