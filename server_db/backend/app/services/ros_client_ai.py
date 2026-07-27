import os
import threading
import time
import json
import uuid
import cv2
import numpy as np
from datetime import datetime, timezone
from typing import Optional, Dict, Any, List

from rclpy.node import Node
from std_msgs.msg import String, Int32
from sensor_msgs.msg import CompressedImage
from geometry_msgs.msg import PoseStamped

# 기존 파일(ros_client.py)을 건드리지 않고 상속받아 확장
from app.services.ros_client import FactoryRosNode
from app.db.database import SessionLocal
from app.db.models import IncidentLog, Waypoint
from app.crud import logs as crud_logs
from app.schemas import logs as schema_logs
from app.core.websocket import manager  # 웹소켓 통로
from app.services.file_manager import save_alert_frame

# teamproject_interfaces 에서 서비스 가져오기
from teamproject_interfaces.srv import SetMode, DispatchToEvent

# AI 클래스명을 DB용 대문자로 매핑
EVENT_TYPE_MAP = {
    "fire": "FIRE",
    "fall": "FALL",
    "fallen_worker": "FALL",
    "fall_detected": "FALL",
    "no_helmet": "NO_HELMET",
    "head": "NO_HELMET"
}

# 2.1 이벤트 우선순위 등급 (Priority)
PRIORITY_MAP = {
    "FIRE": 3,
    "FALL": 2,
    "NO_HELMET": 1,
    "CLEAR": 0
}

PAUSE_REASON_PRIORITY = {
    "EVENT_FIRE": 3,
    "EVENT_FALL": 2,
    "EVENT_HELMET": 1
}

class FactoryRosAiNode(FactoryRosNode):
    def __init__(self, broadcast_callback=None, video_callback=None):
        # 1. 부모 클래스(FactoryRosNode)의 초기화 실행
        super().__init__(broadcast_callback=broadcast_callback, video_callback=video_callback)

        # Section 3: 스마트 배차 큐 (Queue)
        self.event_queue: List[Dict[str, Any]] = []

        # Section 4: 센서 퓨전 중복 제거를 위한 활성 이벤트 캐시
        self.active_events: List[Dict[str, Any]] = []

        # Section 5: AI 쿨타임 (무적 시간)을 저장하기 위한 딕셔너리
        self.robot_resume_time = {1: 0.0, 2: 0.0, 3: 0.0}

        # 글로벌캠 실시간 이미지 캐시 (이상 상황 저장용)
        self.latest_global_frame_bytes = None

        # 오탐 자동 해제를 위한 시간 추적 딕셔너리
        self.clear_started_at = {}

        # 이상 상황 최초 1회 기록 완료 플래그를 관리하는 딕셔너리
        self.event_logged = {1: False, 2: False}
        self.active_no_helmet_log_id = {1: None, 2: None, 3: None}
        # 안전모 미착용 정지 뒤 얼굴인식 결과를 기다릴 최대 시간(초)
        self.no_helmet_face_wait_sec = 15.0
        self.no_helmet_resume_timers = {}
        self.no_helmet_pause_generation = {1: 0, 2: 0, 3: 0}
        # The first confirmed head detection in each EVENT_NO_HELMET pause.
        self.no_helmet_pending_detection = {1: None, 2: None, 3: None}
        self.no_helmet_db_retry_count = {1: 0, 2: 0, 3: 0}
        self.no_helmet_db_max_retries = 2

        # 임무 교대(Handover) 로직 활성화 여부 플래그
        self.enable_handover = True

        # 각 로봇별 최초 감지 소스 추적 (기본값 "ROBOT", 글로벌캠 배차 시 "GLOBAL_CAM"으로 전환)
        self.dispatch_source = {1: "ROBOT", 2: "ROBOT", 3: "ROBOT"}

        # 글로벌캠 배차 시 실제 파견(안전 접근) 좌표를 기억하기 위한 딕셔너리
        self.dispatch_target_coords = {1: None, 2: None, 3: None}
        # 글로벌캠 배차 시 최초 사건 감지(정좌표)를 기억하기 위한 딕셔너리
        self.dispatch_detected_coords = {1: None, 2: None, 3: None}
        # 글로벌캠 파견의 사건 유형. 배터리 복귀 시 같은 임무를 다른 로봇에게 넘길 때 보존한다.
        self.dispatch_event_type = {1: None, 2: None, 3: None}
        # 같은 글로벌캠 임무를 상태 메시지마다 중복으로 큐에 넣지 않기 위한 보호 장치
        self.globalcam_takeover_queued = {1: False, 2: False, 3: False}
        # 글로벌캠 목표 지점에 실제 도착(PAUSED)했는지. 도착 뒤 충전 복귀는 임무 인계 대상이 아니다.
        self.globalcam_task_arrived = {1: False, 2: False, 3: False}
        # 글로벌캠이 퍼블리시하는 가장 최신의 안전 접근 목표 좌표들을 저장하기 위한 캐시
        self.latest_goal_coordinates = {}

        # 2. 글로벌캠 AI 이벤트 구독 설정
        self.globalcam_goal_sub = self.create_subscription(
            String,
            '/globalcam/turtlebot_goal/coordinates',
            self.globalcam_goal_callback,
            10
        )
        self.globalcam_event_sub = self.create_subscription(
            String,
            '/globalcam/server/object_events',
            self.globalcam_event_callback,
            10
        )

        # 2.5. 대기 큐 주기적 자동 배차 타이머 활성화 (1.0초 주기)
        self.queue_timer = self.create_timer(1.0, self.check_and_process_queue)

        # 3. 로봇 1, 2호기 전용 AI 이벤트 및 검출 결과 구독 설정 (3호기는 배차 및 파견에서 영구 제외)
        for i in [1, 2]:
            # 로봇 카메라 안전 이벤트 구독
            self.robots[i]['server_safety_event_sub'] = self.create_subscription(
                String,
                f'/robot{i}/server/safety_events',
                lambda msg, r_id=i: self.robot_safety_event_callback(msg, r_id),
                10
            )

            # 로봇 카메라 정밀 검출 결과 구독
            self.robots[i]['safety_detections_sub'] = self.create_subscription(
                String,
                f'/robot{i}/safety/detections',
                lambda msg, r_id=i: self.robot_safety_detections_callback(msg, r_id),
                10
            )

            # DB 저장 직후 실제 detector 추론을 잠시 멈추는 제어 토픽
            self.robots[i]['safety_control_pub'] = self.create_publisher(
                String, f'/robot{i}/safety/control', 10
            )

            # 로봇 파견용 Service Client 생성
            self.robots[i]['dispatch_client'] = self.create_client(
                DispatchToEvent,
                f'/robot{i}/dispatch_to_event'
            )

            # 로봇 임무 교대(Handover) 요청 구독 설정
            self.robots[i]['handover_sub'] = self.create_subscription(
                Int32,
                f'/robot{i}/handover_request',
                lambda msg, r_id=i: self.handover_request_callback(msg, r_id),
                10
            )

            # 로봇 자율주행 상세 네비게이션 리포트 구독 (Map/Nav, Route, Obstacle/Recovery 동기화용)
            self.robots[i]['nav_report_sub'] = self.create_subscription(
                String,
                f'/robot{i}/nav_report',
                lambda msg, r_id=i: self.robot_nav_report_callback(msg, r_id),
                10
            )

        # 🔊 실시간 TTS 스피커 방송용 퍼블리셔 초기화
        self.tts_fire_pub = self.create_publisher(String, '/fire', 10)
        self.tts_worker_down_pub = self.create_publisher(String, '/worker_down', 10)
        self.tts_helmet_missing_pub = self.create_publisher(String, '/helmet_missing', 10)

        # 📊 [AI 상태 모니터링용] 마지막 실제 감지 시각 기억 캐시
        self.last_detection_timestamp_str = None

        # 🕒 CAMERA_AI_STATUS 실시간 상태 웹소켓 전송 타이머 (1.0초 주기)
        self.status_broadcast_timer = self.create_timer(1.0, self.broadcast_camera_ai_status)

        self.get_logger().info("🤖 [FactoryRosAiNode] Initialized with Triage, MRTA, Deduplication, Cooldown, TTS, and Camera/AI Metrics Broadcast Policies.")

    # ==========================================
    def _cancel_no_helmet_resume(self, robot_id: int):
        timer = self.no_helmet_resume_timers.pop(robot_id, None)
        if timer is not None:
            timer.cancel()

    def _start_safety_inference_cooldown(self, robot_id: int, reason: str, duration_sec: float = 5.0):
        publisher = self.robots.get(robot_id, {}).get("safety_control_pub")
        if publisher is None:
            self.get_logger().warning(f"Safety cooldown publisher unavailable robot_id={robot_id}")
            return
        publisher.publish(String(data=json.dumps({
            "command": "COOLDOWN",
            "duration_sec": float(duration_sec),
            "reason": str(reason),
        }, ensure_ascii=False)))
        self.get_logger().info(
            f"⏸️ [객체인식 쿨다운] 로봇 {robot_id}호기 detector 추론 {duration_sec:.0f}초 중지 "
            f"reason={reason}"
        )

    def _schedule_no_helmet_resume(self, robot_id: int):
        # A NULL employee fallback is allowed only after /safety/detections
        # confirmed a head during this exact event pause.
        if not self.no_helmet_pending_detection.get(robot_id):
            return
        existing = self.no_helmet_resume_timers.get(robot_id)
        if existing is not None and existing.is_alive():
            return
        pause_generation = self.no_helmet_pause_generation.get(robot_id, 0)

        def persist_then_resume():
            self.no_helmet_resume_timers.pop(robot_id, None)
            if pause_generation != self.no_helmet_pause_generation.get(robot_id, 0):
                return
            status = self.status_cache.get(robot_id)
            if not (status and status.status == "PAUSED" and status.pause_reason in ("EVENT_HELMET", "EVENT_NO_HELMET")):
                return

            pending = self.no_helmet_pending_detection.get(robot_id)
            if not pending:
                # No confirmed candidate means there is nothing safe to persist or resume.
                self.get_logger().error(
                    f"NO_HELMET resume blocked: missing persistence candidate robot_id={robot_id}"
                )
                return

            event_to_persist = dict(pending)
            if not event_to_persist.get("employee_id"):
                event_to_persist["employee_id"] = None
                event_to_persist["face_recognition"] = {
                    "status": "unrecognized",
                    "reason": "server_safety_event_timeout",
                }

            if not self.persist_recognized_no_helmet_event(event_to_persist, robot_id):
                retries = self.no_helmet_db_retry_count.get(robot_id, 0) + 1
                self.no_helmet_db_retry_count[robot_id] = retries
                if retries <= self.no_helmet_db_max_retries:
                    self.get_logger().error(
                        f"NO_HELMET DB save failed; retry {retries}/{self.no_helmet_db_max_retries} "
                        f"in 1s while keeping PAUSED robot_id={robot_id}"
                    )
                    retry_timer = threading.Timer(1.0, persist_then_resume)
                    retry_timer.daemon = True
                    self.no_helmet_resume_timers[robot_id] = retry_timer
                    retry_timer.start()
                else:
                    self.get_logger().error(
                        f"NO_HELMET DB save failed after {retries} attempts; "
                        f"keeping robot {robot_id} PAUSED for operator action"
                    )
                return

            self.no_helmet_db_retry_count[robot_id] = 0
            self.no_helmet_pending_detection[robot_id] = None
            saved_employee_id = event_to_persist.get("employee_id")
            if saved_employee_id:
                self.get_logger().info(
                    f"DB NO_HELMET SAVED WITH EMPLOYEE robot_id={robot_id} employee_id={saved_employee_id}"
                )
            else:
                self.get_logger().warning(
                    f"DB NO_HELMET SAVED WITHOUT EMPLOYEE robot_id={robot_id} "
                    "reason=server_safety_event_timeout"
                )
            self.get_logger().warning(
                f"⏸️ [안전모 DB 저장 완료] 로봇 {robot_id}호기 PAUSED 유지 - 관제 RESUME 명령 대기"
            )

        timer = threading.Timer(self.no_helmet_face_wait_sec, persist_then_resume)
        timer.daemon = True
        self.no_helmet_resume_timers[robot_id] = timer
        timer.start()
    # 📡 로봇 상태 수신 콜백 오버라이드 (Handover 감지)
    # ==========================================
    def _queue_interrupted_globalcam_task(self, robot_id: int, current_status: str):
        """배터리 복귀로 중단된 글로벌캠 출동을 정확히 한 번 대기 큐로 되돌린다."""
        if self.dispatch_source.get(robot_id) != "GLOBAL_CAM" or self.globalcam_takeover_queued.get(robot_id, False):
            return
        if self.globalcam_task_arrived.get(robot_id, False):
            self.get_logger().info(
                f"✅ [글로벌캠 임무 완료] 로봇 {robot_id}호기는 현장 도착 후 충전 복귀({current_status}) 중이므로 재배차하지 않습니다."
            )
            return

        target = self.dispatch_target_coords.get(robot_id)
        event_type = self.dispatch_event_type.get(robot_id)
        if target is None or not event_type:
            self.get_logger().warning(
                f"⚠️ [글로벌캠 임무 인계 불가] 로봇 {robot_id}호기 {current_status}: 목표/유형 정보가 없습니다."
            )
            return

        detected = self.dispatch_detected_coords.get(robot_id) or target
        event = {
            "event_type": event_type,
            "x": float(target[0]), "y": float(target[1]),
            "priority": {"FIRE": 3, "FALL": 2, "NO_HELMET": 1}.get(event_type, 0),
            "target_wp_index": -1, "timestamp": time.time(),
            "detected_x": float(detected[0]), "detected_y": float(detected[1]),
        }
        self.globalcam_takeover_queued[robot_id] = True
        if self._enqueue_event_if_not_duplicate(event):
            self.get_logger().warning(
                f"🔁 [글로벌캠 임무 인계 대기] 로봇 {robot_id}호기 배터리 복귀({current_status}) → "
                f"{event_type} 목표 ({target[0]:.2f}, {target[1]:.2f})를 다른 가용 로봇에 재배정합니다."
            )
        else:
            self.get_logger().info(f"⏭️ [글로벌캠 임무 인계] 로봇 {robot_id}호기의 동일 사건이 이미 대기 큐에 있습니다.")

    def status_callback(self, msg, robot_id: int):
        previous = self.status_cache.get(robot_id)
        previous_status = str(previous.status).strip().upper() if previous else ""
        previous_reason = str(previous.pause_reason).strip().upper() if previous else ""
        super().status_callback(msg, robot_id)

        current_status = str(msg.status).strip().upper()
        current_reason = str(msg.pause_reason).strip().upper()
        # 글로벌캠 출동이 PAUSED까지 도달했으면 사건 현장 도착으로 간주한다.
        if current_status == "PAUSED" and self.dispatch_source.get(robot_id) == "GLOBAL_CAM":
            self.globalcam_task_arrived[robot_id] = True
        # 현장 도착 후 귀환이 아니라, 출동(MOVING_TO_EVENT) 도중 배터리로 끊긴 경우만 인계한다.
        if (current_status in ("LOW_BATTERY", "RETURNING_TO_CHARGER", "CHARGING")
                and previous_status == "MOVING_TO_EVENT"):
            self._queue_interrupted_globalcam_task(robot_id, current_status)
        is_event_pause = current_status == "PAUSED" and current_reason in ("EVENT_HELMET", "EVENT_NO_HELMET")
        if is_event_pause and (previous_status != "PAUSED" or previous_reason != current_reason):
            self.no_helmet_pause_generation[robot_id] = self.no_helmet_pause_generation.get(robot_id, 0) + 1
            self._cancel_no_helmet_resume(robot_id)
            self.no_helmet_pending_detection[robot_id] = None
            self.no_helmet_db_retry_count[robot_id] = 0
        elif current_status != "PAUSED":
            self.no_helmet_pause_generation[robot_id] = self.no_helmet_pause_generation.get(robot_id, 0) + 1
            self._cancel_no_helmet_resume(robot_id)
            self.no_helmet_pending_detection[robot_id] = None
            self.no_helmet_db_retry_count[robot_id] = 0
            if robot_id in self.event_logged:
                self.event_logged[robot_id] = False
            if robot_id in self.active_no_helmet_log_id:
                self.active_no_helmet_log_id[robot_id] = None
            if robot_id in self.clear_started_at:
                self.clear_started_at.pop(robot_id, None)
            if current_status in ["IDLE", "PATROLLING", "RETURNING_TO_CHARGER", "CHARGING"]:
                self.dispatch_source[robot_id] = "ROBOT"
                self.dispatch_target_coords[robot_id] = None
                self.dispatch_detected_coords[robot_id] = None
                self.dispatch_event_type[robot_id] = None

    def trigger_handover(self, robot_id: int, target_wp_index: int):
        # 1. 교대해 줄 대기 로봇 선정 (1호기 ↔ 2호기 교대)
        candidate_id = 2 if robot_id == 1 else 1

        # 2. 대기 로봇 상태만 확인한다. 배터리 잔량은 배차 제한에 사용하지 않는다.
        candidate_status = self.status_cache.get(candidate_id)
        if candidate_status and candidate_status.status == "IDLE":
            self.get_logger().info(
                f"🎯 [Handover] 대기 로봇 {candidate_id}호기 가용 상태 확인 (상태: IDLE, 배터리 무관). "
                f"임무 교대(PATROL_START)를 즉시 지시합니다."
            )
            # 2호기 순찰 시작 서비스 호출
            self.call_set_mode(candidate_id, "PATROL_START")
            # FMS 상에서 교대 로봇은 대기(락) 상태로 두지 않음
            self.is_waiting_for_robot_ack[candidate_id] = False
        elif candidate_status and str(candidate_status.status).strip().upper() == "CHARGING":
            # 로봇 정책상 CHARGING에서는 PATROL_START를 거부한다. 배터리 수치와
            # 무관하게 서버가 출동을 결정하는 현재 정책에 맞춰 RESET 성공 후 출발시킨다.
            self.get_logger().info(
                f"🎯 [Handover] 대기 로봇 {candidate_id}호기 CHARGING 감지. "
                "RESET 완료를 확인한 뒤 PATROL_START를 지시합니다."
            )
            reset_future = self.call_set_mode(candidate_id, "RESET")
            if reset_future is not None:
                reset_future.add_done_callback(
                    lambda f, r_id=candidate_id: self._handover_reset_then_start(f, r_id)
                )
        else:
            # 가용 로봇이 없거나 배터리가 부족한 경우 큐에 적재
            status_str = candidate_status.status if candidate_status else "OFFLINE"
            battery_str = f"{candidate_status.battery:.1f}%" if candidate_status else "N/A"
            self.get_logger().warning(
                f"📥 [Handover 큐 적재] 대기 로봇 {candidate_id}호기가 가용하지 않음 (상태: {status_str}, 배터리: {battery_str}). "
                f"임무 교대 요청을 대기 큐에 추가합니다."
            )
            self.enqueue_event({
                "event_type": "HANDOVER",
                "x": 0.0,
                "y": 0.0,
                "priority": 2, # Handover 우선순위는 낙상(2)과 동일
                "target_wp_index": target_wp_index,
                "timestamp": time.time()
            })
            # 큐 정렬
            self.event_queue.sort(key=lambda item: (-item["priority"], item["timestamp"]))

    def _handover_reset_then_start(self, future, robot_id: int):
        """교대 대상의 CHARGING 해제(RESET)가 성공했을 때만 순찰을 시작한다."""
        try:
            response = future.result()
        except Exception as exc:
            self.get_logger().error(
                f"❌ [Handover] 로봇 {robot_id}호기 RESET 응답 예외: {exc}. PATROL_START를 보내지 않습니다."
            )
            return

        if not response.success:
            self.get_logger().warning(
                f"⚠️ [Handover] 로봇 {robot_id}호기 RESET 거부: {response.message}. "
                "PATROL_START를 보내지 않습니다."
            )
            return

        self.get_logger().info(
            f"🚀 [Handover] 로봇 {robot_id}호기 RESET 완료 → PATROL_START 전송"
        )
        self.call_set_mode(robot_id, "PATROL_START")
        self.is_waiting_for_robot_ack[robot_id] = False

    def handover_request_callback(self, msg: Int32, robot_id: int):
        """로봇의 배터리가 부족하여 2호기 투입(교대)을 요청할 때 처리"""
        target_wp_index = int(msg.data)
        self.get_logger().info(f"🔄 [FMS Handover 수신] 로봇 {robot_id}호기가 임무 교대 요청 (시작 wp: {target_wp_index})")
        self.trigger_handover(robot_id, target_wp_index)

    def globalcam_goal_callback(self, msg: String):
        """글로벌 카메라 안전 접근 목표 좌표 토픽 수신 콜백"""
        try:
            payload = json.loads(msg.data)
            goals = payload.get("goals", [])
            for goal in goals:
                class_name = goal.get("class", "").lower()
                mapped_type = EVENT_TYPE_MAP.get(class_name, class_name.upper())
                goal_coord = goal.get("goal_coordinate") # [x, y]
                if isinstance(goal_coord, dict):
                    goal_coord = goal_coord.get("map_xy") or [
                        goal_coord.get("x"),
                        goal_coord.get("y"),
                    ]
                if isinstance(goal_coord, list) and len(goal_coord) >= 2:
                    self.latest_goal_coordinates[mapped_type] = (float(goal_coord[0]), float(goal_coord[1]))
        except Exception as e:
            self.get_logger().error(f"❌ [글로벌캠 안전 목표 좌표 처리 에러] : {e}")

    # ==========================================
    # 🎥 글로벌 카메라 영상 캐싱 오버라이드
    # ==========================================
    def global_camera_callback(self, msg: CompressedImage):
        super().global_camera_callback(msg)
        self.latest_global_frame_bytes = bytes(msg.data)

    # ==========================================
    # 📡 글로벌캠 AI 이벤트 콜백
    # ==========================================
    def globalcam_event_callback(self, msg: String):
        try:
            payload = json.loads(msg.data)
            event_type = payload.get("event_type", "").lower()
            mapped_type = EVENT_TYPE_MAP.get(event_type, event_type.upper())

            coord = payload.get("coordinate", {})
            loc_x = float(coord.get("x", 0.0))
            goal_coordinate = payload.get("goal_coordinate")
            if isinstance(goal_coordinate, dict):
                goal_coordinate = goal_coordinate.get("map_xy") or [
                    goal_coordinate.get("x"),
                    goal_coordinate.get("y"),
                ]
            if not (isinstance(goal_coordinate, (list, tuple)) and len(goal_coordinate) >= 2):
                goal_coordinate = None
            else:
                goal_coordinate = (float(goal_coordinate[0]), float(goal_coordinate[1]))
            loc_y = float(coord.get("y", 0.0))

            last_det = payload.get("last_detection", {})
            confidence = float(last_det.get("confidence", 0.0))
            bbox = last_det.get("bbox", [])

            # 통합 처리기(process_triage_events)에 단일 이벤트 전달
            event = {
                "event_type": mapped_type,
                "x": loc_x,
                "y": loc_y,
                "confidence": confidence,
                "bbox": bbox,
                "employee_id": None,
                "goal_coordinate": goal_coordinate,
            }
            self.process_triage_events([event], robot_id=None, detected_by="GLOBAL_CAM")

        except Exception as e:
            self.get_logger().error(f"❌ [글로벌캠 AI 이벤트 처리 에러] : {e}")

    def persist_recognized_no_helmet_event(self, event: Dict[str, Any], robot_id: int) -> bool:
        status = self.status_cache.get(robot_id)
        if not (status and status.status == "PAUSED" and str(status.pause_reason).startswith("EVENT_")):
            self.get_logger().info(f"NO_HELMET face result deferred: robot={robot_id} is not in EVENT pause state")
            return False

        db = SessionLocal()
        try:
            created_new_incident = False
            log_id = self.active_no_helmet_log_id.get(robot_id)
            incident = db.get(IncidentLog, log_id) if log_id is not None else None
            if incident is not None and incident.employee_id:
                self.event_logged[robot_id] = True
                return True

            if incident is None:
                incident = crud_logs.create_incident_log(
                    db=db, incident_type="NO_HELMET",
                    detected_by=self.dispatch_source.get(robot_id, "ROBOT"),
                    location_x=event["x"], location_y=event["y"], robot_id=robot_id,
                    employee_id=event["employee_id"], photo_url=self.save_event_image(event, robot_id),
                    ai_details={"confidence": round(float(event["confidence"]), 2),
                                "bbox": event.get("bbox", []),
                                "face_recognition": event.get("face_recognition")},
                )
                created_new_incident = True
                self.active_no_helmet_log_id[robot_id] = incident.log_id
                self.get_logger().info(
                    f"DB NO_HELMET SAVED log_id={incident.log_id} employee_id={incident.employee_id} "
                    f"robot_id={robot_id}"
                )
            else:
                details = dict(incident.ai_details or {})
                details.update({"confidence": round(float(event["confidence"]), 2),
                                "bbox": event.get("bbox", []),
                                "face_recognition": event.get("face_recognition")})
                incident.employee_id = event["employee_id"]
                incident.ai_details = details
                db.commit()
                db.refresh(incident)
                self.get_logger().info(
                    f"DB NO_HELMET FACE ENRICHED log_id={incident.log_id} employee_id={incident.employee_id}"
                )

            # 화재·낙상과 동일하게 최초 DB 저장 직후 관제 GUI에 경보를 보낸다.
            # 사번 보강(enrich)은 기존 사고 건의 갱신이므로 NEW_ALERT를 중복 전송하지 않는다.
            if created_new_incident:
                message = f"[로봇 {robot_id}호기 현장 확인] NO_HELMET 감지 확정!"
                if incident.employee_id:
                    message += f" (작업자: {incident.employee_id})"
                self.broadcast_new_alert(incident, event["confidence"], message)

            # 🔊 실시간 TTS 방송 토픽 발행 (안전모 미착용)
            emp_id = event.get("employee_id")
            if emp_id:
                self.tts_helmet_missing_pub.publish(String(data=str(emp_id)))
                self.get_logger().info(f"🔊 [TTS 방송] 안전모 미착용 경보 발행 -> /helmet_missing (사번: {emp_id})")

            self.event_logged[robot_id] = True
            return True
        except Exception as exc:
            db.rollback()
            self.get_logger().error(f"NO_HELMET face DB persistence failed: {exc}")
            return False
        finally:
            db.close()

    # ==========================================
    # 📡 로봇 카메라 AI 안전 이벤트 콜백
    # ==========================================
    def robot_safety_event_callback(self, msg: String, robot_id: int):
        try:
            # Section 5: AI 쿨타임 (무적 시간) 적용 - 순찰 재개 후 5초간 무시
            now = time.time()
            if now - self.robot_resume_time.get(robot_id, 0.0) < 5.0:
                self.get_logger().info(f"⏳ [AI 쿨타임] 로봇 {robot_id}호기 재개 5초 이내이므로 판정 유예.")
                return

            payload = json.loads(msg.data)
            event_type = payload.get("event_type", "").lower()
            mapped_type = EVENT_TYPE_MAP.get(event_type, event_type.upper())

            # 로봇의 현재 실시간 위치 참조
            loc_x, loc_y = 0.0, 0.0
            status_cached = self.status_cache.get(robot_id)
            if status_cached:
                loc_x = status_cached.x
                loc_y = status_cached.y

            face_recognition = payload.get("face_recognition")
            employee_id = None
            if isinstance(face_recognition, dict):
                face_status = face_recognition.get("status") or face_recognition.get("identity_status")
                recognized_id = face_recognition.get("employee_id")
                if face_status == "recognized" and recognized_id:
                    employee_id = str(recognized_id)

            event = {
                "event_type": mapped_type,
                "x": loc_x,
                "y": loc_y,
                "confidence": float(payload.get("confidence", 0.0)),
                "bbox": payload.get("bbox_xyxy", []),
                "employee_id": employee_id,
                "face_recognition": face_recognition if isinstance(face_recognition, dict) else None,
            }
            if mapped_type == "NO_HELMET":
                # Face recognition is valid only after the robot has paused for this event.
                # A skipped event emitted while moving must not arm a later pause resume timer.
                is_event_pause = (
                    status_cached
                    and str(status_cached.status).strip().upper() == "PAUSED"
                    and str(status_cached.pause_reason).strip().upper()
                    in ("EVENT_HELMET", "EVENT_NO_HELMET")
                )
                face_status = ""
                if isinstance(face_recognition, dict):
                    face_status = str(
                        face_recognition.get("status")
                        or face_recognition.get("identity_status")
                        or ""
                    ).strip().lower()

                if not is_event_pause or face_status in ("", "skipped"):
                    self.get_logger().info(
                        f"NO_HELMET face event ignored: robot={robot_id} "
                        f"paused={bool(is_event_pause)} face_status={face_status or 'missing'}"
                    )
                    return

                # Once this PAUSED incident has been saved, keep waiting for the
                # operator RESUME command instead of re-arming another timeout.
                if self.event_logged.get(robot_id, False) and not employee_id:
                    self.get_logger().info(
                        f"NO_HELMET already saved; waiting for operator RESUME robot_id={robot_id}"
                    )
                    return

                # server/safety_events is derived from a head box that already passed
                # the 75x75 server-event criterion.  Keep it as a fallback candidate
                # when the parallel /safety/detections message was not received.
                if not self.no_helmet_pending_detection.get(robot_id):
                    fallback_candidate = dict(event)
                    fallback_candidate["employee_id"] = None
                    self.no_helmet_pending_detection[robot_id] = fallback_candidate
                    self.get_logger().info(
                        f"NO_HELMET server event confirmed; waiting {self.no_helmet_face_wait_sec:.0f}s "
                        f"for face result robot_id={robot_id}"
                    )

                if employee_id:
                    if self.persist_recognized_no_helmet_event(event, robot_id):
                        self._cancel_no_helmet_resume(robot_id)
                        self.no_helmet_pending_detection[robot_id] = None
                        self.get_logger().warning(
                            f"⏸️ [안전모 DB 저장 완료] 로봇 {robot_id}호기 PAUSED 유지 - 관제 RESUME 명령 대기"
                        )
                    else:
                        # Preserve the recognized employee for DB retry; never downgrade it
                        # to the NULL fallback because of a transient DB failure.
                        self.no_helmet_pending_detection[robot_id] = dict(event)
                        self._schedule_no_helmet_resume(robot_id)
                else:
                    self._schedule_no_helmet_resume(robot_id)
                    self.get_logger().info(
                        "NO_HELMET event awaiting recognized face; DB save deferred"
                    )
                return

            self.process_triage_events([event], robot_id=robot_id, detected_by="ROBOT")

        except Exception as e:
            self.get_logger().error(f"❌ [로봇 {robot_id}호기 AI 안전 이벤트 처리 에러] : {e}")

    # ==========================================
    # 📡 로봇 카메라 정밀 검출(안전모/안면 인식) 콜백
    # ==========================================
    def robot_safety_detections_callback(self, msg: String, robot_id: int):
        try:
            # Section 5: AI 쿨타임 (무적 시간) 적용 - 순찰 재개 후 5초간 무시
            now = time.time()
            if now - self.robot_resume_time.get(robot_id, 0.0) < 5.0:
                return

            payload = json.loads(msg.data)
            detections = payload.get("detections", [])

            loc_x, loc_y = 0.0, 0.0
            status_cached = self.status_cache.get(robot_id)
            if status_cached:
                loc_x = status_cached.x
                loc_y = status_cached.y

            events_to_process = []
            deferred_no_helmet = False

            for det in detections:
                det_class = det.get("class", "").lower()

                # 'head' 검출은 안전모 미착용(NO_HELMET)을 의미함
                # NO_HELMET face recognition is completed only in server_safety_events.
                # This earlier detections stream must not create a NULL employee DB row
                # or arm the resume timer.  That occurs only after a face-result event.
                if det_class == "head":
                    deferred_no_helmet = True
                    is_event_pause = (
                        status_cached
                        and str(status_cached.status).strip().upper() == "PAUSED"
                        and str(status_cached.pause_reason).strip().upper()
                        in ("EVENT_HELMET", "EVENT_NO_HELMET")
                    )
                    if is_event_pause and not self.event_logged.get(robot_id, False):
                        # Preserve only the first detection for this pause generation, so
                        # continuous detector frames cannot create duplicate DB records.
                        if not self.no_helmet_pending_detection.get(robot_id):
                            self.no_helmet_pending_detection[robot_id] = {
                                "event_type": "NO_HELMET",
                                "x": loc_x,
                                "y": loc_y,
                                "confidence": float(det.get("confidence", 0.0)),
                                "bbox": det.get("bbox_xyxy", []),
                                "employee_id": None,
                                "face_recognition": None,
                            }
                            self.get_logger().info(
                                f"NO_HELMET head confirmed; waiting {self.no_helmet_face_wait_sec:.0f}s "
                                f"for face result robot_id={robot_id}"
                            )
                        self._schedule_no_helmet_resume(robot_id)
                    continue

                elif det_class in ["fire", "fall", "fallen_worker", "fall_detected"]:
                    events_to_process.append({
                        "event_type": EVENT_TYPE_MAP.get(det_class, det_class.upper()),
                        "x": loc_x,
                        "y": loc_y,
                        "confidence": float(det.get("confidence", 0.0)),
                        "bbox": det.get("bbox_xyxy", []),
                        "employee_id": None
                    })

            if events_to_process:
                self.process_triage_events(events_to_process, robot_id=robot_id, detected_by="ROBOT")
            elif deferred_no_helmet:
                # Wait for /robotN/server/safety_events, which includes face_recognition.
                return
            else:
                # EVENT_NO_HELMET 정지 중에는 head 검출이 잠깐 끊겨도 일반 오탐
                # 자동 해제(10초)나 CLEAR 판정을 내리지 않는다. 이 구간은 얼굴
                # 인식/NULL DB 저장 타이머가 끝난 뒤 관제 RESUME만 기다린다.
                is_no_helmet_pause = (
                    status_cached
                    and str(status_cached.status).strip().upper() == "PAUSED"
                    and str(status_cached.pause_reason).strip().upper() == "EVENT_NO_HELMET"
                )
                if is_no_helmet_pause:
                    self.clear_started_at.pop(robot_id, None)
                    return
                # 감지 결과가 없으므로 정밀 검증용으로 process_triage_events에 빈 리스트 전달 (오탐 자동 해제 검사용)
                self.process_triage_events([], robot_id=robot_id, detected_by="ROBOT")
                # 안전 위반 객체가 감지되지 않았으므로 CLEAR 상태 발행
                self.publish_obstacle_verdict(
                    robot_id=robot_id,
                    verdict="CLEAR",
                    obj_type="NORMAL",
                    confidence=0.0,
                    original_stamp=self.latest_frame_stamps.get(robot_id) or self.get_clock().now().to_msg()
                )


        except Exception as e:
            self.get_logger().error(f"❌ [로봇 {robot_id}호기 정밀 검출 처리 에러] : {e}")

    # ==========================================
    # 🛡️ 복합 감지 분류(Triage) 및 센서 퓨전 중복 제거 통합 처리기
    # ==========================================
    def enqueue_event(self, event: Dict[str, Any], merge_distance: float = 1.0) -> bool:
        """동일 유형·동일 위치의 대기 이벤트는 하나만 유지한다."""
        event_type = event["event_type"]
        event_x = float(event.get("x", 0.0))
        event_y = float(event.get("y", 0.0))

        for queued in self.event_queue:
            if queued.get("event_type") != event_type:
                continue
            # HANDOVER는 위치와 무관하게 하나만 유지하고, 나머지는 1m 이내만 병합한다.
            distance = ((float(queued.get("x", 0.0)) - event_x) ** 2 +
                        (float(queued.get("y", 0.0)) - event_y) ** 2) ** 0.5
            if event_type == "HANDOVER" or distance <= merge_distance:
                queued.update(event)
                self.get_logger().info(
                    f"⏭️ [이벤트 큐 중복 병합] {event_type} "
                    f"좌표=({event_x:.2f}, {event_y:.2f})"
                )
                return False

        self.event_queue.append(event)
        self.event_queue.sort(key=lambda item: (-item.get("priority", 0), item.get("timestamp", 0.0)))
        return True

    def process_triage_events(self, events_list: List[Dict[str, Any]], robot_id: Optional[int], detected_by: str):
        now = time.time()

        # --- 1단계: 정밀 검증(Double-Verification) 상태 체크 ---
        # 로봇이 현장에 도착하여 대기 중(PAUSED + 사유가 EVENT_로 시작)인지 확인
        is_arrived_and_verifying = False
        if robot_id is not None:
            status_cached = self.status_cache.get(robot_id)
            if status_cached:
                if status_cached.status == "PAUSED" and status_cached.pause_reason.startswith("EVENT_"):
                    is_arrived_and_verifying = True

        # 밀착 검증에서는 로봇을 멈추게 한 이벤트 유형만 확정/DB 저장한다.
        # 예: EVENT_NO_HELMET으로 접근했다면 fall/fire가 같은 프레임에 보여도 무시한다.
        if is_arrived_and_verifying and robot_id is not None:
            expected_event_type = {
                "EVENT_HELMET": "NO_HELMET",
                "EVENT_NO_HELMET": "NO_HELMET",
                "EVENT_FALL": "FALL",
                "EVENT_FIRE": "FIRE",
            }.get(str(status_cached.pause_reason).strip().upper())
            if expected_event_type:
                matching_events = [
                    event for event in events_list
                    if str(event.get("event_type", "")).strip().upper() == expected_event_type
                ]
                if not matching_events:
                    self.get_logger().info(
                        f"Verification event ignored: robot={robot_id} "
                        f"pause_reason={status_cached.pause_reason} expected={expected_event_type}"
                    )
                    return
                events_list = matching_events


        # --- 2단계: 상태별 분기 처리 ---
        if not is_arrived_and_verifying:
            # [A] 예비 감지 단계 (순찰 또는 이동 중)
            # DB 저장 및 GUI 알림을 발행하지 않고, 로봇을 해당 위치로 파견시키는 역할만 수행
            if detected_by == "GLOBAL_CAM" and events_list:
                # 글로벌캠에서 감지된 경우: 가장 높은 우선순위의 이벤트를 골라 최단거리 로봇 파견
                highest_event = max(events_list, key=lambda e: PRIORITY_MAP.get(e["event_type"], 0))
                highest_priority = PRIORITY_MAP.get(highest_event["event_type"], 0)

                event_type = highest_event["event_type"]
                detected_x = highest_event["x"]
                detected_y = highest_event["y"]

                # 글로벌캠이 계산한 안전 접근 목표 좌표를 우선 사용한다.
                # 해당 이벤트 타입의 goal_coordinate가 아직 수신되지 않은 경우에만
                # 감지 정좌표로 대체해 파견이 멈추지 않도록 한다.
                goal_coordinate = highest_event.get("goal_coordinate") or self.latest_goal_coordinates.get(event_type)
                if goal_coordinate is not None:
                    dispatch_x, dispatch_y = goal_coordinate
                    self.get_logger().info(
                        f"🎯 [글로벌캠 안전 접근 좌표 파견] {event_type}: "
                        f"감지=({detected_x:.2f}, {detected_y:.2f}) -> "
                        f"목표=({dispatch_x:.2f}, {dispatch_y:.2f})"
                    )
                else:
                    dispatch_x, dispatch_y = detected_x, detected_y
                    self.get_logger().warning(
                        f"⚠️ [글로벌캠 안전 접근 좌표 없음] {event_type}: "
                        "감지 정좌표로 대체 파견합니다."
                    )

                dispatched_robot = self.find_and_dispatch_closest_robot(
                    event_type, dispatch_x, dispatch_y, highest_priority,
                    detected_x=detected_x, detected_y=detected_y
                )
                if dispatched_robot is None:
                    # 🚨 중복 제거: 대기 큐 내에 이미 동일한 유형 및 1.0m 이내의 중복 이벤트가 존재한다면 적재 생략
                    is_duplicate_in_queue = False
                    for q_event in self.event_queue:
                        if q_event["event_type"] == highest_event["event_type"]:
                            dist = ((q_event["x"] - dispatch_x)**2 + (q_event["y"] - dispatch_y)**2)**0.5
                            if dist < 1.0:
                                is_duplicate_in_queue = True
                                q_event["timestamp"] = now  # 타임스탬프만 최신 갱신하여 신선도 유지
                                break

                    if not is_duplicate_in_queue:
                        # 대기 큐 적재
                        self.get_logger().info(f"📥 [이벤트 큐 적재] {highest_event['event_type']} 최고 우선순위 이벤트 가용 로봇 없음.")
                        self.enqueue_event({
                            "event_type": highest_event["event_type"],
                            "x": dispatch_x,
                            "y": dispatch_y,
                            "priority": highest_priority,
                            "timestamp": now,
                            "detected_x": detected_x,
                            "detected_y": detected_y
                        })
                        self.event_queue.sort(key=lambda item: (-item["priority"], item["timestamp"]))

            # 로봇캠 예비 감지인 경우: 로봇 PC 자체의 로컬 좌표 융합 및 파견 노드에서 알아서 처리하므로
            # FMS 서버에서는 DB 저장이나 사이렌 전송 없이 아무 일도 하지 않고 종료합니다.
            return

        # [B] 밀착 검증 단계 (로봇 도착 정지 상태: PAUSED + EVENT_*)
        # 💡 중요: 이미 이 이벤트에 대한 DB 기록 및 관제 알림이 1회 완료된 경우, 중복 처리를 차단합니다.
        if robot_id is not None and self.event_logged.get(robot_id, False):
            return

        # 1단계: 중복 클래스 병합 (Merge)
        grouped_events = {}
        for ev in events_list:
            ev_type = ev["event_type"]
            existing = grouped_events.get(ev_type)
            if existing is None or ev["confidence"] > existing["confidence"]:
                grouped_events[ev_type] = ev

        merged_events = list(grouped_events.values())

        # 만약 감지된 이벤트가 없다면? (오탐 가능성)
        if not merged_events:

            # 오탐 자동 해제를 위한 시간 추적
            if robot_id not in self.clear_started_at:
                self.clear_started_at[robot_id] = now

            # 10.0초 동안 이상 상황이 연속으로 감지되지 않으면 오탐으로 판단하여 자동 복귀
            elapsed = now - self.clear_started_at[robot_id]
            if elapsed >= 10.0:
                self.get_logger().warning(f"🟢 [오탐 자동 해제] 로봇 {robot_id}호기 현장 확인 결과 이상 없음 (10초 경과) -> 순찰 복귀 지시(RESUME)")
                self.call_set_mode(robot_id, "RESUME")
                # 타이머 초기화
                try:
                    del self.clear_started_at[robot_id]
                except KeyError:
                    pass
            return

        # 감지된 이벤트가 있으면 오탐 해제 타이머 초기화
        if robot_id in self.clear_started_at:
            try:
                del self.clear_started_at[robot_id]
            except KeyError:
                pass

        # 2단계: 센서 퓨전 공간/시간적 중복 제거 (Deduplication)
        # 활성 이벤트 캐시 정리 (최근 10초 내 자료만 유지)
        self.active_events = [ev for ev in self.active_events if now - ev["timestamp"] <= 10.0]

        filtered_events = []
        for ev in merged_events:
            mapped_type = ev["event_type"]
            loc_x = ev["x"]
            loc_y = ev["y"]

            is_duplicate = False
            for active_ev in self.active_events:
                if active_ev["incident_type"] == mapped_type:
                    time_diff = abs(now - active_ev["timestamp"])
                    space_diff = ((active_ev["x"] - loc_x)**2 + (active_ev["y"] - loc_y)**2)**0.5
                    # 조건: 클래스 일치 & 3.0초 이내 & 2D 거리 0.2m 이내
                    if time_diff <= 3.0 and space_diff <= 0.2:
                        is_duplicate = True
                        break

            if is_duplicate:
                self.get_logger().info(f"⏭️ [센서 퓨전 중복 필터링] {mapped_type} 중복 감지 무시 ({loc_x:.2f}, {loc_y:.2f})")
                continue

            # 중복 검사 통과 시 활성 캐시에 추가
            self.active_events.append({
                "incident_type": mapped_type,
                "x": loc_x,
                "y": loc_y,
                "timestamp": now
            })
            filtered_events.append(ev)

        if not filtered_events:
            return

        # 3단계: 복합 이벤트 저장 및 알림 (Broadcast All)
        # 밀착 검증에 성공했으므로 이 시점에 DB 저장 및 웹소켓 경보 전송
        for ev in filtered_events:
            photo_url = self.save_event_image(ev, robot_id)

            db = SessionLocal()
            try:
                db_detected_by = detected_by
                db_x = ev["x"]
                db_y = ev["y"]
                if robot_id is not None:
                    db_detected_by = self.dispatch_source.get(robot_id, "ROBOT")
                    # 만약 글로벌캠이 먼저 발견해서 로봇을 파견한 경우, DB에는 글로벌캠이 추출했던 최초 감지 정좌표로 저장
                    if db_detected_by == "GLOBAL_CAM":
                        coords = self.dispatch_detected_coords.get(robot_id)
                        if coords is not None:
                            db_x, db_y = coords

                saved_log = crud_logs.create_incident_log(
                    db=db,
                    incident_type=ev["event_type"],
                    detected_by=db_detected_by,
                    location_x=db_x,
                    location_y=db_y,
                    robot_id=robot_id,
                    employee_id=ev["employee_id"],
                    photo_url=photo_url,
                    ai_details={
                        "confidence": round(float(ev["confidence"]), 2),
                        "bbox": ev.get("bbox", []),
                        "face_recognition": ev.get("face_recognition"),
                    }
                )
                self.get_logger().info(
                    f"💾 [DB 저장 완료] 확정된 이상 상황이 DB에 등록되었습니다! "
                    f"(로그 ID: {saved_log.log_id}, 타입: {saved_log.incident_type}, "
                    f"발견주체: {saved_log.detected_by}, 로봇: {saved_log.robot_id or 'N/A'}호기, "
                    f"위치: ({saved_log.location_x:.2f}, {saved_log.location_y:.2f}), "
                    f"사진: {photo_url})"
                )
                ev["saved_log"] = saved_log

                # 관제 UI 웹소켓 알림
                message = f"[{'글로벌캠' if detected_by == 'GLOBAL_CAM' else f'로봇 {robot_id}호기'} 현장 확인] {ev['event_type']} 감지 확정!"
                if ev["employee_id"]:
                    message += f" (작업자: {ev['employee_id']})"

                self.broadcast_new_alert(saved_log, ev["confidence"], message)

                # 🔊 실시간 TTS 스피커 방송 토픽 발행 (화재 / 낙상)
                event_type_str = ev["event_type"]
                if event_type_str == "FIRE":
                    self.tts_fire_pub.publish(String(data="detected"))
                    self.get_logger().info("🔊 [TTS 방송] 화재 경보 발행 -> /fire")
                elif event_type_str == "FALL":
                    self.tts_worker_down_pub.publish(String(data="detected"))
                    self.get_logger().info("🔊 [TTS 방송] 작업자 쓰러짐 경보 발행 -> /worker_down")

                # 🚨 최초 1회 기록 완료 플래그 설정
                if robot_id is not None:
                    self.event_logged[robot_id] = True

            except Exception as e:
                self.get_logger().error(f"❌ DB 감지 기록 생성 실패: {e}")
            finally:
                db.close()

        # 4단계: 로봇 제어 상태 전이 (확정 시 정지 상태 고정)
        # 이미 로봇이 현장에서 멈춰섰으므로, FMS 상에서 파견 로봇을 락상태로 전이시키고 로봇에는 EMERGENCY 판정(즉시 정지 고정) 발행
        highest_event = max(filtered_events, key=lambda e: PRIORITY_MAP.get(e["event_type"], 0))
        self.is_waiting_for_robot_ack[robot_id] = True
        self.publish_obstacle_verdict(
            robot_id=robot_id,
            verdict="EMERGENCY",
            obj_type=highest_event["event_type"],
            confidence=highest_event["confidence"],
            original_stamp=self.latest_frame_stamps.get(robot_id) or self.get_clock().now().to_msg()
        )

        # 남겨진 하위 우선순위 이벤트들 대기 큐(Queue)로 강제 이관
        for ev in filtered_events:
            if ev == highest_event:
                continue
            self.get_logger().info(f"📥 [하위 순위 큐 이관] {ev['event_type']} 상황을 이벤트 대기 큐에 보냅니다. (최고 순위: {highest_event['event_type']})")
            self.enqueue_event({
                "event_type": ev["event_type"],
                "x": ev["x"],
                "y": ev["y"],
                "priority": PRIORITY_MAP.get(ev["event_type"], 0),
                "timestamp": now
            })

        # 큐 정렬 (1순위: 우선순위 높은 순서, 2순위: 먼저 발생한 순서)
        self.event_queue.sort(key=lambda item: (-item["priority"], item["timestamp"]))

    # ==========================================
    # ==========================================
    # 🥇 FMS 스마트 배차 알고리즘 (MRTA)
    # ==========================================
    def find_and_dispatch_closest_robot(self, event_type: str, x: float, y: float, priority: int, detected_x: float = None, detected_y: float = None) -> Optional[int]:
        available_robots = []

        for r_id in [1, 2]:  # 1호기, 2호기 한정 (3호기는 영구 제외)
            status_cached = self.status_cache.get(r_id)
            if not status_cached:
                continue

            # 순찰 중(PATROLLING)이거나 대기 중(IDLE)인 가용 로봇을 파견 대상에 포함
            if status_cached.status in ["PATROLLING", "IDLE"]:
                dist = ((status_cached.x - x)**2 + (status_cached.y - y)**2)**0.5
                available_robots.append((r_id, dist, status_cached.status))

        # 순찰 중인 로봇(우선) 및 가장 가까운 로봇 선택
        if available_robots:
            # 1순위: PATROLLING 로봇 우선 (status != "PATROLLING"이 False(0)이 되므로 앞자리 차지), 2순위: 거리
            available_robots.sort(key=lambda item: (item[2] != "PATROLLING", item[1]))
            chosen_id = available_robots[0][0]
            self.get_logger().info(
                f"🎯 [FMS 배차] 글로벌캠 감지 -> 로봇 {chosen_id}호기 배정 | "
                f"인식유형: {event_type} | 타겟 좌표: ({x:.2f}, {y:.2f}) | 거리: {available_robots[0][1]:.2f}m"
            )
            self.call_dispatch_to_event(chosen_id, event_type, x, y, detected_x=detected_x, detected_y=detected_y)
            return chosen_id

        # 가용 로봇(순찰 중인 로봇)이 없으면 선점하지 않고 무조건 대기 큐(Queue)로 보냄
        return None

    # ==========================================
    # 📡 로봇 파견 명령 호출 및 전송
    # ==========================================
    def call_dispatch_to_event(self, robot_id: int, event_type: str, x: float, y: float, target_wp_index: int = -1, detected_x: float = None, detected_y: float = None):
        client = self.robots[robot_id].get('dispatch_client')
        if not client:
            self.get_logger().error(f"Robot {robot_id} has no dispatch_client")
            return None

        # FMS 서버가 배차 명령을 직접 호출했으므로 감지 소스를 "GLOBAL_CAM"으로 전환 (임무교대는 ROBOT)
        if event_type == "HANDOVER":
            self.dispatch_source[robot_id] = "ROBOT"
            self.dispatch_target_coords[robot_id] = None
            self.dispatch_detected_coords[robot_id] = None
            self.dispatch_event_type[robot_id] = None
        else:
            self.dispatch_source[robot_id] = "GLOBAL_CAM"
            self.dispatch_event_type[robot_id] = event_type
            self.globalcam_takeover_queued[robot_id] = False
            self.globalcam_task_arrived[robot_id] = False
            self.dispatch_target_coords[robot_id] = (x, y)
            # 감지 정좌표 설정 (전달되지 않은 경우 파견 좌표와 동일하게 백업)
            det_x = x if detected_x is None else detected_x
            det_y = y if detected_y is None else detected_y
            self.dispatch_detected_coords[robot_id] = (det_x, det_y)

        if not client.wait_for_service(timeout_sec=1.0):
            self.get_logger().error(f"Robot {robot_id} dispatch_to_event service not available")
            return None

        req = DispatchToEvent.Request()

        # geometry_msgs/PoseStamped 조립
        pose = PoseStamped()
        pose.header.stamp = self.get_clock().now().to_msg()
        pose.header.frame_id = 'map'
        pose.pose.position.x = float(x)
        pose.pose.position.y = float(y)
        pose.pose.position.z = 0.0
        pose.pose.orientation.w = 1.0

        req.target = pose
        req.event_type = event_type
        req.target_wp_index = target_wp_index

        self.get_logger().info(
            f"🚀 [배차 서비스 호출] 로봇 {robot_id}호기 파견 명령 전달 시작 | "
            f"인식유형: {event_type} | 타겟 좌표: ({x:.2f}, {y:.2f})"
        )
        future = client.call_async(req)
        future.add_done_callback(
            lambda f: self._dispatch_response_callback(f, robot_id, event_type, x, y, target_wp_index, detected_x, detected_y)
        )
        return future

    def _dispatch_response_callback(self, future, robot_id: int, event_type: str, x: float, y: float, target_wp_index: int, detected_x: float = None, detected_y: float = None):
        try:
            response = future.result()
            if response.accepted:
                self.get_logger().info(f"✅ [로봇 {robot_id}] 파견 명령 접수 완료: {response.message}")
            else:
                self.get_logger().warning(f"⚠️ [로봇 {robot_id}] 파견 명령 거절됨: {response.message}. 대기 큐로 재이관합니다.")
                self.is_waiting_for_robot_ack[robot_id] = False

                priority = 0
                if event_type == "FIRE":
                    priority = 3
                elif event_type in ["FALL", "HANDOVER"]:
                    priority = 2
                elif event_type == "NO_HELMET":
                    priority = 1

                import time
                self.event_queue.insert(0, {
                    "event_type": event_type,
                    "x": x,
                    "y": y,
                    "priority": priority,
                    "target_wp_index": target_wp_index,
                    "timestamp": time.time(),
                    "detected_x": x if detected_x is None else detected_x,
                    "detected_y": y if detected_y is None else detected_y
                })
        except Exception as e:
            self.get_logger().error(f"❌ [로봇 {robot_id}] 파견 명령 콜백 수행 중 예외 발생: {e}")

    # ==========================================
    # 🥉 순찰 재개 시 대기 큐 확인 및 배차 처리
    # ==========================================
    def process_event_queue(self, robot_id: int):
        if not self.event_queue:
            return

        # 대기 큐에서 가장 높은 순위의 미출동 이벤트 pop
        event = self.event_queue.pop(0)
        self.get_logger().info(
            f"📤 [대기 큐 배차 수행] 로봇 {robot_id}호기 순찰 재개에 따른 대기 작업 즉시 파견 "
            f"(타입: {event['event_type']}, 위치: {event['x']:.2f}, {event['y']:.2f})"
        )

        target_wp_idx = event.get("target_wp_index", -1)
        if event["event_type"] == "HANDOVER":
            self.get_logger().info(f"🔄 [대기 큐 배차 수행] 로봇 {robot_id}호기 임무 교대(PATROL_START) 서비스 호출")
            self.call_set_mode(robot_id, "PATROL_START")
            self.is_waiting_for_robot_ack[robot_id] = False
        else:
            # 즉시 파견 서비스 호출
            self.call_dispatch_to_event(robot_id, event["event_type"], event["x"], event["y"], target_wp_index=target_wp_idx, detected_x=event.get("detected_x"), detected_y=event.get("detected_y"))
            # 다시 락 상태로 전이
            self.is_waiting_for_robot_ack[robot_id] = True

    # ==========================================
    # 🎮 관제사 명령 오버라이드 (쿨타임 & 대기큐 연동)
    # ==========================================
    def call_set_mode(self, robot_id: int, mode: str):
        # Section 5: AI 쿨타임 (무적 시간) 적용 - RESUME 전송 즉시 5초간 해당 로봇 감지 일시 정지
        if mode == "RESUME":
            self.robot_resume_time[robot_id] = time.time()
            self._start_safety_inference_cooldown(robot_id, "ROBOT_RESUME")
            self.is_waiting_for_robot_ack[robot_id] = False
            self.get_logger().info(f"🔓 [FMS] 로봇 {robot_id}호기 순찰 재개 명령 전송 - 5초간 AI 감지 쿨타임 시작")

        future = super().call_set_mode(robot_id, mode)

        # Section 3: 순찰 재개 버튼을 누를 때 큐에 쌓여있던 작업이 있다면 즉시 목적지로 파견
        if mode == "RESUME":
            self.process_event_queue(robot_id)

        return future

    # ==========================================
    # 🖼️ 이미지 디코딩 헬퍼 함수
    # ==========================================
    def save_event_image(self, ev: Dict[str, Any], robot_id: Optional[int]) -> Optional[str]:
        # 1. 원본 이미지 bytes 선택
        if robot_id:
            frame_bytes = self.latest_frames.get(robot_id)
        else:
            frame_bytes = self.latest_global_frame_bytes

        if not frame_bytes:
            return None

        # 2. bytes에서 frame 디코딩 후 static 폴더에 파일 저장
        try:
            np_arr = np.frombuffer(frame_bytes, np.uint8)
            frame = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)
            if frame is not None:
                # 바운딩 박스가 존재하면 이미지 위에 사각형 및 라벨을 그립니다.
                bbox = ev.get("bbox")
                if bbox and len(bbox) == 4:
                    try:
                        xmin, ymin, xmax, ymax = map(int, bbox)
                        # 빨간색 사각형 그리기 (선 두께: 3)
                        cv2.rectangle(frame, (xmin, ymin), (xmax, ymax), (0, 0, 255), 3)

                        # 텍스트 라벨 내용 구성 (이벤트 종류 + 신뢰도)
                        label = f"{ev['event_type']} ({ev.get('confidence', 0.0):.2f})"
                        (label_width, label_height), baseline = cv2.getTextSize(
                            label, cv2.FONT_HERSHEY_SIMPLEX, 0.6, 2
                        )
                        # 텍스트 배경을 위한 빨간색 채워진 사각형
                        cv2.rectangle(
                            frame,
                            (xmin, ymin - label_height - 10),
                            (xmin + label_width, ymin),
                            (0, 0, 255),
                            cv2.FILLED
                        )
                        # 흰색 글씨로 라벨 텍스트 그리기
                        cv2.putText(
                            frame,
                            label,
                            (xmin, ymin - 5),
                            cv2.FONT_HERSHEY_SIMPLEX,
                            0.6,
                            (255, 255, 255),
                            2
                        )
                    except Exception as draw_err:
                        self.get_logger().error(f"⚠️ [바운딩 박스 그리기 실패] : {draw_err}")

                # globalcam은 robot_id = 0 으로 저장
                return save_alert_frame(frame, ev["event_type"], robot_id=robot_id or 0)
        except Exception as e:
            self.get_logger().error(f"❌ 증거 이미지 저장 예외 발생: {e}")
        return None

    # ==========================================
    # 📡 웹소켓 실시간 이벤트 전달 헬퍼
    # ==========================================
    def broadcast_new_alert(self, saved_log, confidence: float, message: str):
        # 📊 [AI 상태용] 마지막 실제 감지 시각 기억 갱신
        self.last_detection_timestamp_str = saved_log.timestamp.isoformat()

        # 🚨 [카메라 ID 및 발견자 식별용 매핑]
        det_by = saved_log.detected_by
        if det_by == "GLOBAL_CAM":
            camera_id = "GLOBAL-CCTV-01"
            detected_by_str = "GLOBAL_CCTV"
        elif det_by == "ROBOT" and saved_log.robot_id is not None:
            camera_id = f"TB3-CAM-{saved_log.robot_id:02d}"
            detected_by_str = "TB3_CAMERA"
        else:
            camera_id = "AI_SERVER"
            detected_by_str = "AI_SERVER"

        alert_payload = {
            "type": "NEW_ALERT",
            "data": {
                "log_id": saved_log.log_id,
                "timestamp": saved_log.timestamp.isoformat(),
                "incident_type": saved_log.incident_type,
                "detected_by": saved_log.detected_by,
                "robot_id": saved_log.robot_id,
                "employee_id": saved_log.employee_id,
                "location_x": saved_log.location_x,
                "location_y": saved_log.location_y,
                "photo_url": saved_log.photo_url,
                "status": saved_log.status,
                "cleared_at": saved_log.cleared_at.isoformat() if saved_log.cleared_at else None,
                "ai_details": saved_log.ai_details or {"confidence": round(float(confidence), 2)},
                "message": message,

                # 📡 [관제 GUI 팀원 추가 요청 필드 연동]
                "alert_id": saved_log.log_id,
                "detected_at": saved_log.timestamp.isoformat(),
                "camera_id": camera_id,
                "detected_by": detected_by_str,
                "status": saved_log.status
            }
        }
        if self.broadcast_callback:
            self.broadcast_callback(alert_payload)
            self.get_logger().info(
                f"📡 [웹소켓 경보 전송] Type: NEW_ALERT, Log ID: {saved_log.log_id}, "
                f"유형: {saved_log.incident_type}, 발견자: {saved_log.detected_by}, "
                f"메시지: '{message}'"
            )
        else:
            self.get_logger().warning("⚠️ [웹소켓 경보 실패] broadcast_callback이 등록되지 않았습니다.")

    # ==========================================
    # 📸 도착 시점 1차 캡처 생략 오버라이드
    # ==========================================
    def _capture_and_save_incident(self, db, robot_id: int, event_type: str, x: float, y: float):
        """
        AI 노드 오버라이드: 현장 도착(PAUSED) 시점의 1차 임시 저장을 생략합니다.
        대신 도착 후 2차 정밀 AI 검증 결과가 확정되었을 때만 단 1회 저장합니다.
        """
        self.get_logger().info(f"📸 [로봇 {robot_id}호기] 현장 도착 완료. 정밀 검증(AI)을 대기합니다. (예비 DB 저장 생략)")

    # ==========================================
    # 🕒 대기 큐 주기적 자동 배차 스케줄러
    # ==========================================
    def check_and_process_queue(self):
        """1초마다 실행되며, 대기 큐에 쌓인 이벤트가 있고 가용한 로봇이 있으면 자동 매칭 및 배차를 수행합니다."""
        try:
            if not self.event_queue:
                return

            # 대기 큐의 첫 번째 이벤트 (가장 높은 우선순위)
            event = self.event_queue[0]
            event_type = event["event_type"]
            event_x = event["x"]
            event_y = event["y"]

            # 가용 로봇 후보 물색
            available_robots = []
            for r_id in [1, 2]:
                status_cached = self.status_cache.get(r_id)
                if not status_cached:
                    continue

                # 조건: 상태가 PATROLLING 또는 IDLE 이고, 서버 측 소프트웨어 락(is_waiting_for_robot_ack)이 풀려 있는 상태
                if status_cached.status in ["PATROLLING", "IDLE"] and not self.is_waiting_for_robot_ack.get(r_id, False):
                    # 거리 계산
                    dist = ((status_cached.x - event_x)**2 + (status_cached.y - event_y)**2)**0.5
                    available_robots.append((r_id, dist))

            # 가용한 로봇이 있다면 가장 가까운 로봇에게 배정 및 큐에서 제거
            if available_robots:
                # 거리 순 정렬
                available_robots.sort(key=lambda item: item[1])
                chosen_id = available_robots[0][0]

                # 대기 큐에서 이벤트 소모
                event = self.event_queue.pop(0)

                self.get_logger().info(
                    f"📤 [대기 큐 자동 배차] 가용 로봇 {chosen_id}호기 감지 -> 대기 중이던 {event_type} 작업 자동 배정! "
                    f"목적지: ({event_x:.2f}, {event_y:.2f}), 거리: {available_robots[0][1]:.2f}m"
                )

                target_wp_idx = event.get("target_wp_index", -1)

                if event_type == "HANDOVER":
                    self.get_logger().info(f"🔄 [대기 큐 자동 배차] 로봇 {chosen_id}호기 임무 교대(PATROL_START) 서비스 호출")
                    self.call_set_mode(chosen_id, "PATROL_START")
                    self.is_waiting_for_robot_ack[chosen_id] = False
                else:
                    # 파견 명령 송신 (안전 접근 좌표 및 최초 감지 정좌표 전달)
                    det_x = event.get("detected_x")
                    det_y = event.get("detected_y")

                    self.call_dispatch_to_event(
                        chosen_id,
                        event_type,
                        event_x,
                        event_y,
                        target_wp_index=target_wp_idx,
                        detected_x=det_x,
                        detected_y=det_y
                    )
        except Exception as e:
            self.get_logger().error(f"❌ [대기 큐 자동 배정 스케줄러 에러] : {e}")

    # ==========================================
    # 🕒 CAMERA_AI_STATUS 실시간 상태 취합 및 웹소켓 전송
    # ==========================================
    def broadcast_camera_ai_status(self):
        """관제 GUI(Unity)팀 요청 규격에 맞추어 카메라 및 AI 상태를 취합해 1초 주기로 실시간 브로드캐스트합니다."""
        try:
            import rclpy
            from datetime import datetime, timezone, timedelta
            # KST (+09:00) 타임존 객체 생성
            kst = timezone(timedelta(hours=9))
            now = datetime.now(kst)
            now_iso = now.isoformat()

            now_ts = time.time()
            streams = []

            # (1) 글로벌 CCTV 상태 구성
            g_last_time = self.camera_last_frame_time.get("global", 0.0)
            g_connected = (now_ts - g_last_time < 3.0) if g_last_time > 0 else False
            g_fps = float(self.camera_frame_count.get("global", 0))
            self.camera_frame_count["global"] = 0
            self.camera_fps["global"] = g_fps

            g_last_frame_iso = datetime.fromtimestamp(g_last_time, kst).isoformat() if g_last_time > 0 else None

            streams.append({
                "camera_id": "GLOBAL-CCTV-01",
                "source_type": "GLOBAL",
                "robot_id": None,
                "channel": "GLOBAL",
                "connected": g_connected,
                "stream_status": "STREAMING" if g_connected else "DISCONNECTED",
                "frame_received": g_connected,
                "fps": g_fps if g_connected else None,
                "stream_latency_ms": int(self.camera_latency_ms.get("global", 0.0)) if g_connected else None,
                "resolution": self.camera_resolution.get("global") if g_connected else None,
                "last_frame_at": g_last_frame_iso,
                "error_message": None if g_connected else "No stream received"
            })

            # (2) 각 로봇 카메라 상태 구성
            robot_channels = {1: "TB3-01", 2: "TB3-02", 3: "TB3-03"}
            for r_id in [1, 2, 3]:
                r_last_time = self.camera_last_frame_time.get(r_id, 0.0)
                is_connected = not self.is_offline.get(r_id, False)
                has_video = (now_ts - r_last_time < 3.0) if r_last_time > 0 else False

                r_fps = float(self.camera_frame_count.get(r_id, 0))
                self.camera_frame_count[r_id] = 0
                self.camera_fps[r_id] = r_fps

                r_last_frame_iso = datetime.fromtimestamp(r_last_time, kst).isoformat() if r_last_time > 0 else None

                # 3호기는 물리 연결이 없는 상태일 수 있으므로 stream_status 제어
                if r_id == 3 and not has_video:
                    stream_status = "NO_STREAM"
                    err_msg = "Camera stream is not configured"
                else:
                    stream_status = "STREAMING" if (is_connected and has_video) else ("NO_STREAM" if is_connected else "DISCONNECTED")
                    err_msg = None if has_video else ("Camera stream not active" if is_connected else "Robot offline")

                streams.append({
                    "camera_id": f"TB3-CAM-{r_id:02d}",
                    "source_type": "ROBOT",
                    "robot_id": f"tb3-{r_id:02d}",
                    "channel": robot_channels[r_id],
                    "connected": is_connected,
                    "stream_status": stream_status,
                    "frame_received": has_video,
                    "fps": r_fps if has_video else None,
                    "stream_latency_ms": int(self.camera_latency_ms.get(r_id, 0.0)) if has_video else None,
                    "resolution": self.camera_resolution.get(r_id) if has_video else None,
                    "last_frame_at": r_last_frame_iso,
                    "error_message": err_msg
                })

            # 2. AI 상태 구성
            ai_inference_fps = 0.0
            active_fps_list = [self.camera_fps[k] for k in ["global", 1, 2] if self.camera_last_frame_time.get(k, 0.0) > 0]
            if active_fps_list:
                ai_inference_fps = round(sum(active_fps_list) / len(active_fps_list), 1)

            ai_payload = {
                "model_status": "RUNNING" if rclpy.ok() else "ERROR",
                "model_name": "safety_fire_fall_best.pt / yolo11n.pt",
                "model_version": "1.0.0",
                "inference_status": "RUNNING" if ai_inference_fps > 0 else "IDLE",
                "inference_fps": ai_inference_fps if ai_inference_fps > 0 else None,
                "inference_latency_ms": 40 if ai_inference_fps > 0 else None,
                "detection_enabled": True,
                "last_inference_at": now_iso,
                "last_detection_at": self.last_detection_timestamp_str or now_iso,
                "error_message": None
            }

            # 3. 통합 JSON 송출
            status_payload = {
                "event_type": "CAMERA_AI_STATUS",
                "updated_at": now_iso,
                "streams": streams,
                "ai": ai_payload
            }

            if self.broadcast_callback:
                self.broadcast_callback(status_payload)

        except Exception as e:
            self.get_logger().error(f"❌ [CAMERA_AI_STATUS 브로드캐스트 에러] : {e}")

    # ==========================================
    # 🗺️ 로봇 자율주행 상세 네비게이션 리포트 콜백 (Map/Nav, Route, Obstacle/Recovery 동기화용)
    # ==========================================
    def robot_nav_report_callback(self, msg: String, robot_id: int):
        try:
            payload = json.loads(msg.data)

            # FMS 서버 내부의 last_seen_time 갱신하여 OFFLINE 방지!
            self.last_seen_time[robot_id] = time.time()
            if self.is_offline.get(robot_id, False):
                self.get_logger().info(f"🔌 [로봇 {robot_id}] nav_report 통신 감지! ONLINE 상태로 전환")
                self.is_offline[robot_id] = False

            # 🚨 [NoneType 우주방어]: JSON에 키는 있지만 명시적으로 null이 넘어올 경우 get() 에러 방지
            pose_info = payload.get("pose") or {}
            route_info = payload.get("route") or {}
            loc_info = payload.get("localization") or {}
            nav_info = payload.get("nav2") or {}
            obs_info = payload.get("obstacle") or {}
            rec_info = payload.get("recovery") or {}

            # 기존 status_callback과 호환되도록 status_cache에 기본값 적재 및 FSM 로직 동작
            from teamproject_interfaces.msg import RobotStatus
            mock_status_msg = RobotStatus()
            mock_status_msg.x = float(pose_info.get("x", 0.0))
            mock_status_msg.y = float(pose_info.get("y", 0.0))
            mock_status_msg.yaw = float(pose_info.get("yaw", 0.0))
            mock_status_msg.status = str(payload.get("fsm_state", "IDLE"))
            mock_status_msg.battery = float(payload.get("battery", 0.0))
            mock_status_msg.pause_reason = str(payload.get("pause_reason", ""))

            # route 객체에서 wp_index 정보 추출
            mock_status_msg.current_target_wp = int(route_info.get("current_target_wp", -1))

            # 부모 클래스의 status_callback 직접 찔러서 DB Timeline 축적 및 도착 판정 자동 수행!
            self.status_callback(mock_status_msg, robot_id)

            # 📡 Unity 관제탑 웹소켓용 확장 ROBOT_STATUS 패킷 생성
            extended_data = {
                "robot_id": robot_id,
                "x": round(mock_status_msg.x, 2),
                "y": round(mock_status_msg.y, 2),
                "yaw": round(mock_status_msg.yaw, 2),
                "status": mock_status_msg.status,
                "battery": round(mock_status_msg.battery, 1),
                "linear_vel": 0.0,
                "angular_vel": 0.0,
                "pause_reason": mock_status_msg.pause_reason,
                "current_target_wp": mock_status_msg.current_target_wp,

                # 🗺️ [1. Map/Nav 상태 데이터 확장]
                "map_id": payload.get("map_id", "factory_map_52x52"),
                "localization_state": loc_info.get("localization_state"),
                "amcl_state": loc_info.get("amcl_state"),
                "initial_pose_set": loc_info.get("initial_pose_set"),
                "localization_quality": loc_info.get("localization_quality"),
                "scan_match_state": loc_info.get("scan_match_state"),
                "nav2_state": nav_info.get("nav2_state"),
                "planner_state": nav_info.get("planner_state"),
                "controller_state": nav_info.get("controller_state"),
                "goal_result": nav_info.get("goal_result"),
                "replan_count": nav_info.get("replan_count"),
                "current_wp_index": route_info.get("current_wp_index"),
                "total_waypoints": route_info.get("total_waypoints"),
                "route_state": route_info.get("route_state"),

                # 📍 [2. Waypoint Route 데이터 확장]
                "route_id": route_info.get("route_id"),
                "route_name": route_info.get("route_name"),
                "waypoints": route_info.get("waypoints", []),

                # 🛑 [3. 장애물 및 Recovery 상태 데이터 확장]
                "obstacle_state": obs_info.get("obstacle_state"),
                "obstacle_type": obs_info.get("obstacle_type"),
                "obstacle_distance": obs_info.get("obstacle_distance"),
                "obstacle_x": obs_info.get("obstacle_x"),
                "obstacle_y": obs_info.get("obstacle_y"),
                "recovery_state": rec_info.get("recovery_state"),
                "recovery_behavior": rec_info.get("recovery_behavior"),
                "recovery_retry_count": rec_info.get("recovery_retry_count"),
                "detected_at": obs_info.get("detected_at"),
                "message": payload.get("message"),
                "updated_at": payload.get("updated_at")
            }

            ws_payload = {
                "type": "ROBOT_STATUS",
                "data": extended_data
            }

            # ⏳ [웹소켓 송출 스로틀링]: 최소 0.5초 간격을 유지하여 트래픽 최적화 및 렉 방지
            current_time = time.time()
            if current_time - self._last_ws_send_time.get(robot_id, 0.0) >= 0.5:
                if self.broadcast_callback:
                    self.broadcast_callback(ws_payload)
                    self._last_ws_send_time[robot_id] = current_time

        except Exception as e:
            self.get_logger().error(f"❌ [nav_report JSON 파싱 및 브로드캐스트 에러] : {e}")
