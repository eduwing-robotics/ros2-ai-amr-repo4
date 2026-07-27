from sqlalchemy import Column, Integer, String, DateTime, ForeignKey, Float, Boolean, Text
from sqlalchemy.dialects.postgresql import JSONB, ARRAY
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from pgvector.sqlalchemy import Vector

# 데이터베이스 연결 설정 파일에서 Base를 가져옵니다.
from app.db.database import Base

# ==========================================
# 1. 인적 자원 및 출입 관리 그룹
# ==========================================

class Employee(Base):
    '''1. 직원 정보 테이블'''
    __tablename__ = "employees"

    employee_id = Column(String(20), primary_key=True, index=True)
    name = Column(String(50), nullable=False)
    position = Column(String(50), nullable=True)
    phone_number = Column(String(20), nullable=True)

    created_at = Column(DateTime, server_default=func.now())

    # 다른 테이블에서 직원을 쉽게 참조하기 위한 ORM 관계 설정 (DB 컬럼이 아님)
    face_data = relationship("EmployeeFace", back_populates="employee", uselist=False)
    attendance_logs = relationship("EmployeeAttendanceLog", back_populates="employee")
    incident_logs = relationship("IncidentLog", back_populates="employee")


class EmployeeFace(Base):
    '''2. 안면인식 데이터 테이블'''
    __tablename__ = "employee_faces"

    face_id = Column(Integer, primary_key=True, autoincrement=True)
    employee_id = Column(String(20), ForeignKey("employees.employee_id", ondelete="CASCADE"), nullable=False, unique=True)

    # InsightFace의 512차원 임베딩 데이터를 저장하는 벡터 공간
    #face_embedding = Column(Vector(512), nullable=False)
    face_embedding = Column(JSONB, nullable=False)

    # 여러 장의 사진 경로를 저장하는 PostgreSQL 배열 타입
    photo_urls = Column(ARRAY(String), nullable=False)

    employee = relationship("Employee", back_populates="face_data")


class EmployeeAttendanceLog(Base):
    '''3. 직원 출퇴근 로그 테이블'''
    __tablename__ = "employee_attendance_logs"

    log_id = Column(Integer, primary_key=True, autoincrement=True)
    timestamp = Column(DateTime, server_default=func.now(), index=True)
    employee_id = Column(String(20), ForeignKey("employees.employee_id"), nullable=False)
    action_type = Column(String(20), nullable=False) # 'check_in', 'check_out'
    admin_override = Column(Boolean, server_default="false", default=False) # 관리자 수동 처리 여부

    employee = relationship("Employee", back_populates="attendance_logs")

class Visitor(Base):
    '''4. 방문자 정보 테이블'''
    __tablename__ = "visitors"

    visitor_id = Column(String(20), primary_key=True, index=True)
    name = Column(String(50), nullable=False)
    qr_token = Column(String(100), nullable=False, unique=True) # 중복 방지를 위한 UNIQUE 설정

class VisitorAttendanceLog(Base):
    '''5. 방문자 입출입 로그 테이블'''
    __tablename__ = "visitor_attendance_logs"

    log_id = Column(Integer, primary_key=True, autoincrement=True)
    timestamp = Column(DateTime, server_default=func.now(), index=True)
    visitor_id = Column(String(20), ForeignKey("visitors.visitor_id"), nullable=False)
    action_type = Column(String(10), nullable=False) # 'entry', 'exit'
    admin_override = Column(Boolean, server_default="false", default=False)

    # ORM Relationships
    visitor = relationship("Visitor")


# ==========================================
# 2. AI 이상 상황 감지 및 알림 그룹
# ==========================================

class IncidentLog(Base):
    __tablename__ = "incident_logs"

    log_id = Column(Integer, primary_key=True, autoincrement=True)
    timestamp = Column(DateTime, server_default=func.now(), index=True)
    incident_type = Column(String(20), nullable=False)  # 'NO_HELMET', 'FIRE', 'FALL' 등

    # 안전모 미착용 등 특정 직원 식별 시에만 저장하므로 nullable=True
    employee_id = Column(String(20), ForeignKey("employees.employee_id"), nullable=True)
    detected_by = Column(String(20), nullable=False)    # 'GLOBAL_CAM' 또는 'ROBOT'
    robot_id = Column(Integer, ForeignKey("robots.robot_id"), nullable=True)    # 감지/증거 채택 로봇 번호

    location_x = Column(Float, nullable=False)
    location_y = Column(Float, nullable=False)
    photo_url = Column(String(255), nullable=True)  # static 폴더 내 증거 사진 경로
    ai_details = Column(JSONB, nullable=True)       # AI 바운딩 박스 등 가변 데이터를 유연하게 저장하는 JSONB

    status = Column(String(20), server_default="NEW", default="NEW")    # 'NEW', 'CLEARED'
    cleared_at = Column(DateTime, nullable=True)    # 관제사 조치 완료 시각

    employee = relationship("Employee", back_populates="incident_logs")
    robot = relationship("Robot")


# ==========================================
# 3. 로봇 운용 및 순찰 관제 그룹
# ==========================================

class Robot(Base):
    '''7. 로봇 정보 테이블'''
    __tablename__ = "robots"

    robot_id = Column(Integer, primary_key=True)
    robot_name = Column(String(50), nullable=False)
    ip_address = Column(String(20), nullable=True)

class Waypoint(Base):
    '''8. 순찰 웨이포인트 테이블'''
    __tablename__ = "waypoints"

    waypoint_id = Column(Integer, primary_key=True, autoincrement=True)
    point_name = Column(String(50), nullable=False)
    sequence = Column(Integer, nullable=False) # 로봇이 이동할 순서
    x = Column(Float, nullable=False) # SLAM 프레임 기준 X 좌표
    y = Column(Float, nullable=False) # SLAM 프레임 기준 Y 좌표

class PatrolLog(Base):
    '''9. 순찰 임무 요약 테이블'''
    __tablename__ = "patrol_logs"

    log_id = Column(Integer, primary_key=True, autoincrement=True)
    robot_id = Column(Integer, ForeignKey("robots.robot_id"), nullable=False)
    start_time = Column(DateTime, server_default=func.now())
    end_time = Column(DateTime, nullable=True)
    status = Column(String(20), server_default="IN_PROGRESS", default="IN_PROGRESS") # 'IN_PROGRESS', 'COMPLETE', 'FAILED'

    # ORM Relationships
    robot = relationship("Robot")
    timelines = relationship("PatrolTimeline", back_populates="patrol_log")

class PatrolTimeline(Base):
    """10. 순찰 세부 타임라인 테이블 (Detail)"""
    __tablename__ = "patrol_timelines"

    timeline_id = Column(Integer, primary_key=True, autoincrement=True)
    log_id = Column(Integer, ForeignKey("patrol_logs.log_id", ondelete="CASCADE"), nullable=False)
    state = Column(String(30), nullable=False) # FSM 상태 (PAUSED, STUCK, LOW_BATTERY 등)
    pause_reason = Column(String(50), nullable=True) # 상태가 PAUSED일 때의 구체적 사유
    location_x = Column(Float, nullable=False) # 상태 변경 당시 X 좌표
    location_y = Column(Float, nullable=False) # 상태 변경 당시 Y 좌표
    changed_at = Column(DateTime, server_default=func.now(), index=True)

    # ORM Relationships
    patrol_log = relationship("PatrolLog", back_populates="timelines")


class RobotCommandLog(Base):
    """11. 로봇 제어 명령 이력 테이블 (control_mode 제거 최적화 버전)"""
    __tablename__ = "robot_command_logs"

    command_id = Column(Integer, primary_key=True, autoincrement=True)
    robot_id = Column(Integer, ForeignKey("robots.robot_id"), nullable=False)
    operator_id = Column(String(50), nullable=False) # 명령을 내린 사번 혹은 'SYSTEM'
    command = Column(String(50), nullable=False) # START_PATROL, EMERGENCY_STOP 등 핵심 명령
    payload = Column(JSONB, nullable=True) # 명령 가변 파라미터 (예: {"target_waypoint_id": 3})
    result_status = Column(String(20), server_default="PENDING", default="PENDING") # 'PENDING', 'SENT', 'ACK', 'FAILED'
    response_message = Column(Text, nullable=True) # 로봇으로부터 반환된 실패 사유 등 상세 메시지
    requested_at = Column(DateTime, server_default=func.now(), index=True)

    # ORM Relationships
    robot = relationship("Robot")