import os
import cv2
import numpy as np
import time
import rclpy
from rclpy.qos import qos_profile_sensor_data, QoSProfile, ReliabilityPolicy, HistoryPolicy
from rclpy.node import Node
import threading
from geometry_msgs.msg import TwistStamped

# 자율주행 팀과 합의한 커스텀 인터페이스
from sensor_msgs import msg
from teamproject_interfaces.msg import RobotStatus, ObstacleVerdict
from teamproject_interfaces.srv import SetMode
from sensor_msgs.msg import CompressedImage
from sensor_msgs.msg import Image

# DB 연동을 위한 임포트
from app.db.database import SessionLocal
from app.crud import logs as crud_logs
from app.schemas import logs as schema_logs

class FactoryRosNode(Node):
    # 외부에서 콜백 함수를 주입받을 수 있도록 추가
    def __init__(self, broadcast_callback=None, video_callback=None):
        super().__init__('fastapi_main_server_node')
        self.broadcast_callback = broadcast_callback    # 웹소켓 통로
        self.video_callback = video_callback            # 영상 전달용 통로

        # 로봇 3대의 통신 객체와 최신 상태를 저장할 딕셔너리
        self.robots = {1: {}, 2: {}, 3: {}}

        # 로봇별 최신 상태 캐시 (DB 저장 시 "x,y" 좌표를 빼오기 위함)
        self.status_cache = {
            1: None, 2: None, 3: None
        }

        # 상태 전이(Transition)를 감지하기 위한 기억 장치
        self.prev_status = {1: None, 2: None, 3: None}

        # 현재 진행 중인 순찰 임무의 부모 ID(log_id)를 기억하는 장치
        self.active_patrol_log_id = {1: None, 2: None, 3: None}

        # 사진 저장을 위해 로봇별 최신 카메라 프레임을 들고 있을 캐시
        self.latest_frames = {1: None, 2: None, 3: None}
        self.latest_frame_stamps = {1: None, 2: None, 3: None}

        # 📊 관제 GUI 카메라 & AI 상태 수집용 실측 통계 지표 초기화
        self.camera_last_frame_time = {"global": 0.0, 1: 0.0, 2: 0.0, 3: 0.0}
        self.camera_frame_count = {"global": 0, 1: 0, 2: 0, 3: 0}
        self.camera_fps = {"global": 0.0, 1: 0.0, 2: 0.0, 3: 0.0}
        self.camera_latency_ms = {"global": 0.0, 1: 0.0, 2: 0.0, 3: 0.0}
        self.camera_resolution = {"global": "1280x960", 1: "640x480", 2: "640x480", 3: "640x480"}

        # ⏳ 로그 및 웹소켓 전송 주기 제한용 시간 기록 딕셔너리
        self._last_status_log_time = {1: 0.0, 2: 0.0, 3: 0.0}
        self._last_ws_send_time = {1: 0.0, 2: 0.0, 3: 0.0}

        # 각 로봇별 AI 알림 방어용 임시 자물쇠 (초기값: 모두 False)
        self.is_waiting_for_robot_ack = {1: False, 2: False, 3: False}

        current_t = time.time()
        # 로봇별 마지막 통신 시간을 기록할 딕셔너리
        self.last_seen_time = {
            1: current_t,
            2: current_t,
            3: current_t
        }

        # 로봇이 현재 OFFLINE으로 처리되었는지 기억하는 딕셔너리 (중복 전송 방지용)
        self.is_offline = {1: False, 2: False, 3: False}

        # 1초마다 실행되는 워치독(감시자) 타이머 생성
        self.watchdog_timer = self.create_timer(1.0, self.check_timeout)


        # 구독자 QoS를 Best Effort로 변경
        image_qos_profile = QoSProfile(
            reliability=ReliabilityPolicy.BEST_EFFORT,
            history=HistoryPolicy.KEEP_LAST,
            depth=1
        )

        '''self.global_camera_sub = self.create_subscription(
                CompressedImage,
                '/globalcam/image_raw/compressed',
                self.global_camera_callback,
                image_qos_profile
        )'''

        self.global_camera_sub = self.create_subscription(
                CompressedImage,
                '/globalcam/live/image',
                self.global_camera_callback,
                image_qos_profile
        )

        # 1~3호기 퍼블리셔, 서브스크라이버, 서비스 클라이언트 동적 생성
        for i in range(1, 4):
            # 1. 서브스크라이버: 상태 수신 (1Hz)
            self.robots[i]['status_sub'] = self.create_subscription(
                RobotStatus,
                f'/robot{i}/robot_status',
                lambda msg, r_id=i: self.status_callback(msg, r_id),
                10
            )

            # 2. 서브스크라이버: 카메라 영상 수신 (상시 스트림)
            self.robots[i]['camera_sub'] = self.create_subscription(
                Image,
                f'/robot{i}/live/image',
                lambda msg, r_id=i: self.camera_callback(msg, r_id),
                image_qos_profile
            )

            # 3. 퍼블리셔: 장애물/AI 판정 결과 발행 (상시 스트림)
            self.robots[i]['verdict_pub'] = self.create_publisher(
                ObstacleVerdict,
                f'/robot{i}/obstacle_event',
                10
            )

            # 3-2. 퍼블리셔: 수동 주행 조종 (cmd_vel)
            self.robots[i]['cmd_vel_pub'] = self.create_publisher(
                TwistStamped,
                f'/robot{i}/cmd_vel',
                10
            )

            # 4. 서비스 클라이언트: 관제 명령 (수동조작, 긴급정지 등)
            self.robots[i]['set_mode_client'] = self.create_client(
                SetMode,
                f'/robot{i}/set_mode'
            )

        self.get_logger().info("🏭 Factory Main Server ROS2 Node Initialized.")

    # ==========================================
    # 📡 수신 (Subscribe) 콜백 함수
    # ==========================================
    def status_callback(self, msg: RobotStatus, robot_id: int):
        """
        로봇으로부터 1Hz 단위로 상태를 수신하고, 상태가 변했을 때만 DB에 타임라인을 기록합니다.
        (이 부분에서 WebSocket으로 관제 화면에 데이터를 쏴줍니다)
        """
        self.last_seen_time[robot_id] = time.time() # 마지막 통신 시각 갱신

        if self.is_offline.get(robot_id, False):
            self.get_logger().info(f"🔌 [로봇 {robot_id}] 통신 재개! ONLINE 상태로 전환")
            self.is_offline[robot_id] = False # OFFLINE 상태 해제

        # DB 저장용으로 최신 상태 캐싱 (MOVING이 빠진 17개 상태, x, y 좌표 포함)
        self.status_cache[robot_id] = msg

        previous_status = self.prev_status[robot_id]
        current_time = time.time()

        # 로봇별 고유 이모지 및 시각적 태그 지정
        robot_tags = {
            1: "🤖 [1호기 🟦]",
            2: "🤖 [2호기 🟧]",
            3: "🤖 [3호기 🟪]"
        }
        tag = robot_tags.get(robot_id, f"🤖 [{robot_id}호기]")

        # ⏳ [콘솔 로그 스로틀링]: 1초에 1번 또는 상태 변화 시에만 콘솔 로그 출력
        if (current_time - self._last_status_log_time.get(robot_id, 0.0) >= 1.0) or (msg.status != previous_status):
            self.get_logger().info(
                f"{tag} 상태: {msg.status} | 배터리: {msg.battery:3.0f}% | "
                f"좌표: ({msg.x:6.2f}, {msg.y:6.2f}) | yaw: {msg.yaw:5.2f} | "
                f"목표 wp: {msg.current_target_wp:<3} | 사유: {msg.pause_reason}"
            )
            self._last_status_log_time[robot_id] = current_time

        # Unity 관제용 WebSocket Payload 만들기
        ws_payload = {
            "type": "ROBOT_STATUS",
            "data": {
                "robot_id": robot_id,
                "x": round(msg.x, 2),
                "y": round(msg.y, 2),
                "yaw": round(msg.yaw, 2),
                "status": msg.status,
                "battery": round(msg.battery, 1),
                "linear_vel": round(msg.linear_vel, 2),
                "angular_vel": round(msg.angular_vel, 2),
                "pause_reason": msg.pause_reason,
                "current_target_wp": msg.current_target_wp
            }
        }

        # ⏳ [웹소켓 송출 스로틀링]: 최소 0.5초 간격을 유지하여 트래픽 최적화 및 렉 방지
        if current_time - self._last_ws_send_time.get(robot_id, 0.0) >= 0.5:
            if self.broadcast_callback:
                self.broadcast_callback(ws_payload)
                self._last_ws_send_time[robot_id] = current_time

        # ============================================
        # DB 로깅 로직 (상태가 변했을 때만 작동하는 FSM 트리거)
        # ============================================
        current_status = msg.status
        previous_status = self.prev_status[robot_id]

        # 이전 상태와 현재 상태가 다르다면? (상태 전이 발생!)
        if current_status != previous_status:
            db = SessionLocal() # DB 통로 열기
            try:
                # [상황 A] 로봇이 새로 순찰을 시작함 (IDLE -> LOCALIZING -> PATROLLING)
                # (혹시 통신 지연으로 LOCALIZING을 건너뛰고 PATROLLING이 먼저 올 경우도 대비하여 in 으로 묶음)
                if current_status in ["LOCALIZING", "PATROLLING"] and previous_status in [None, "IDLE", "CHARGING", "RESUMING_AFTER_CHARGE", "UNDOCKING"]:
                    # 1) 부모 테이블(PatrolLog)에 새 순찰 임무 시작을 기록
                    new_patrol = crud_logs.create_patrol_log(
                        db=db,
                        patrol=schema_logs.PatrolLogCreate(
                            robot_id=robot_id,
                            status="IN_PROGRESS"
                        )
                    )
                    # 2) 방금 생성된 부모 ID를 기억해둠
                    self.active_patrol_log_id[robot_id] = new_patrol.log_id
                    self.get_logger().info(f"🟢 [로봇 {robot_id}] 새 순찰 임무(Log ID: {new_patrol.log_id}) 개시: {current_status}")

                # [상황 B] 순찰 중인 로봇의 의미 있는 상태 변화 기록 (타임라인 적재)
                # 현재 진행 중인 순찰 ID가 있을 때만 기록
                current_log_id = self.active_patrol_log_id[robot_id]
                if current_log_id is not None:
                    # 상황 A를 통해 LOCALIZING에서 부모 ID가 생겼으므로,
                    # 이후 LOCALIZING -> PATROLLING 으로 넘어가는 순간도 여기서 타임라인으로 자동 기록됨
                    crud_logs.create_patrol_timeline(
                        db=db,
                        timeline=schema_logs.PatrolTimelineCreate(
                            log_id=current_log_id,
                            state=current_status,
                            pause_reason=msg.pause_reason,
                            location_x=msg.x,
                            location_y=msg.y
                        )
                    )
                    self.get_logger().info(f"📝 [로봇 {robot_id}] 타임라인 기록: {previous_status} ➔ {current_status}")

                # [상황 C] 로봇이 순찰을 마치거나 충전하러 감 (종료 감지)
                if current_status in ["IDLE", "RETURNING_TO_CHARGER", "CHARGING"] and previous_status not in [None, "IDLE", "CHARGING", "RESUMING_AFTER_CHARGE"]:
                    if current_log_id is not None:
                        # 부모 테이블(PatrolLog)의 종료 시간과 최종 상태를 업데이트
                        crud_logs.update_patrol_log_end(db, current_log_id, "COMPLETED")
                        self.active_patrol_log_id[robot_id] = None # 임무 종료되었으므로 부모 ID 초기화
                        self.get_logger().info(f"🏁 [로봇 {robot_id}] 순찰 임무(Log ID: {current_log_id}) 정상 종료")

                # ★ [상황 D] 도착 판정 및 증거 사진 저장 로직 추가
                # 파견지 이동(MOVING_TO_EVENT)을 끝내고 정지(PAUSED) 상태가 되었을 때
                if previous_status == "MOVING_TO_EVENT" and current_status == "PAUSED":
                    # pause_reason이 유의미한 이상 상황일 때만 캡처 진행
                    if msg.pause_reason in ["EVENT_FIRE", "EVENT_FALL", "EVENT_HELMET"]:
                        self.get_logger().info(f"📸 [로봇 {robot_id}] 현장 도착! 사진 촬영 및 DB 기록을 시작합니다...")
                        self._capture_and_save_incident(db, robot_id, msg.pause_reason, msg.x, msg.y)

            except Exception as e:
                self.get_logger().error(f"❌ DB 타임라인 저장 중 에러 발생: {e}")
            finally:
                db.close()  # 통로 닫기
        # 처리가 끝났으니 현재 상태를 '이전 상태'로 업데이트
        self.prev_status[robot_id] = current_status


    def camera_callback(self, msg: Image, robot_id: int):
        """
        카메라 프레임을 수신하여 AI 모델(YOLO/안면인식)로 넘깁니다.
        """
        try:
            np_arr = np.frombuffer(msg.data, dtype=np.uint8)
            frame = np_arr.reshape((msg.height, msg.width, 3))
            if msg.encoding == "rgb8":
                frame = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)
            ok, encoded = cv2.imencode(".jpg", frame)
            if not ok:
                return
            raw_bytes = encoded.tobytes()
        except Exception as e:
            self.get_logger().error(f"❌ [카메라 이미지 인코딩 에러] : {e}")
            return

        # 가장 최근의 프레임을 로봇 번호별로 캐싱
        self.latest_frames[robot_id] = raw_bytes
        self.latest_frame_stamps[robot_id] = msg.header.stamp

        # 📊 [카메라 상태 모니터링] 실측 통계 지표 업데이트
        self.camera_frame_count[robot_id] += 1
        self.camera_last_frame_time[robot_id] = time.time()
        try:
            stamp_sec = msg.header.stamp.sec + msg.header.stamp.nanosec * 1e-9
            latency = time.time() - stamp_sec
            self.camera_latency_ms[robot_id] = max(0.0, round(latency * 1000.0, 1))
        except Exception:
            self.camera_latency_ms[robot_id] = 150.0  # 지연시간 기본값 폴백
        self.camera_resolution[robot_id] = f"{msg.width}x{msg.height}"

        # 수신받은 JPEG 바이트를 그대로 FastAPI 쪽으로 던져줌
        if self.video_callback:
            self.video_callback(str(robot_id), raw_bytes)

    def global_camera_callback(self, msg: CompressedImage):
        """글로벌 카메라 프레임 수신"""
        #print("📸 글로벌 캠 프레임 도착!")
        # 글로벌 캠은 "global" 이라는 방 이름표를 달아줍니다.
        raw_bytes = bytes(msg.data)

        # 프레임 캡처 시각 (AI 판정 후 신선도 검증을 위해 반드시 기억해야 함)
        frame_stamp = msg.header.stamp

        # 📊 [글로벌캠 상태 모니터링] 실측 통계 지표 업데이트
        self.camera_frame_count["global"] += 1
        self.camera_last_frame_time["global"] = time.time()
        try:
            stamp_sec = msg.header.stamp.sec + msg.header.stamp.nanosec * 1e-9
            latency = time.time() - stamp_sec
            self.camera_latency_ms["global"] = max(0.0, round(latency * 1000.0, 1))
        except Exception:
            self.camera_latency_ms["global"] = 120.0

        if self.video_callback:
            self.video_callback("global", raw_bytes)

        # TODO: 글로벌 캠 영상도 AI 비전 분석이 필요하다면 여기서 넘기면 됩니다!

    # ==========================================
    # ★ [신규 추가] 도착 시 사진 저장 및 IncidentLog 생성 헬퍼 함수
    # ==========================================
    def _capture_and_save_incident(self, db, robot_id: int, event_type: str, x: float, y: float):
        """현장에 도착했을 때 최신 프레임을 파일로 저장하고 DB에 위반/응급 로그를 남깁니다."""
        frame_bytes = self.latest_frames.get(robot_id)

        if not frame_bytes:
            self.get_logger().warning(f"⚠️ [로봇 {robot_id}] 저장할 최신 카메라 프레임이 없습니다!")
            return

        # 1. 파일 저장 경로 및 이름 설정 (예: static/alerts/event_fire_1_1623...jpg)
        save_dir = "static/alerts"
        os.makedirs(save_dir, exist_ok=True) # 폴더가 없으면 자동 생성

        filename = f"{event_type.lower()}_{robot_id}_{int(time.time())}.jpg"
        filepath = os.path.join(save_dir, filename)

        # 2. 로컬 디스크에 바이너리 파일로 저장 (JPEG 원본 그대로)
        try:
            with open(filepath, "wb") as f:
                f.write(frame_bytes)
        except Exception as e:
            self.get_logger().error(f"❌ 사진 파일 저장 실패: {e}")
            return

        photo_url = f"/{filepath}" # 클라이언트(Unity)가 접근할 수 있는 상대 경로

        # 3. 기존의 미해결(NEW) IncidentLog가 있는지 확인 후 업데이트, 없으면 신규 생성
        try:
            from app.db.models import IncidentLog
            from sqlalchemy import desc

            existing_log = db.query(IncidentLog).filter(
                IncidentLog.robot_id == robot_id,
                IncidentLog.incident_type == event_type,
                IncidentLog.status == "NEW"
            ).order_by(desc(IncidentLog.timestamp)).first()

            if existing_log:
                existing_log.photo_url = photo_url
                existing_log.location_x = x
                existing_log.location_y = y
                db.commit()
                self.get_logger().info(f"🎉 [증거 업데이트] 기존 로그 [Log ID: {existing_log.log_id}]에 로봇 현장 사진 반영 완료: {photo_url}")
            else:
                new_log = crud_logs.create_incident_log(
                    db=db,
                    incident_type=event_type,
                    detected_by="ROBOT",
                    robot_id=robot_id,
                    location_x=x,
                    location_y=y,
                    photo_url=photo_url
                )
                self.get_logger().info(f"🎉 증거 수집 완료! 신규 로그 [Log ID: {new_log.log_id}] 생성 및 사진 저장: {photo_url}")
        except Exception as e:
            self.get_logger().error(f"❌ IncidentLog DB 저장 실패: {e}")

    # ==========================================
    # 🚀 송신 (Publish / Service Call) 함수
    # ==========================================
    def publish_obstacle_verdict(self, robot_id: int, verdict: str, obj_type: str, confidence: float, original_stamp):
        """
        AI 분석 결과를 로봇으로 전송합니다. (v1.2 확정: request_id 폐기, stamp 사용)
        """
        msg = ObstacleVerdict()
        # ★ 핵심: 현재 서버 시간이 아니라, 분석 대상이 되었던 "원본 영상의 캡처 시간"을 넣어야 함
        msg.header.stamp = original_stamp
        msg.verdict = verdict        # "CLEAR" 또는 "EMERGENCY"
        msg.type = obj_type          # "person", "box", "FALL" 등
        msg.confidence = confidence  # 0.0 ~ 1.0

        self.robots[robot_id]['verdict_pub'].publish(msg)

    def call_set_mode(self, robot_id: int, mode: str):
        """
        관제 화면(웹)에서 버튼을 눌렀을 때 호출되어 로봇의 상태를 변경합니다.
        """
        client = self.robots[robot_id]['set_mode_client']
        if not client.wait_for_service(timeout_sec=1.0):
            self.get_logger().error(f"Robot {robot_id} set_mode service not available")
            return None

        req = SetMode.Request()
        req.mode = mode # 예: "EMERGENCY_STOP", "PATROL_START"

        if mode == "RESUME":
            self.is_waiting_for_robot_ack[robot_id] = False
            self.get_logger().info(f"🔓 [로봇 {robot_id}호기] AI 감지 자물쇠 해제 완료!")

        # 비동기로 서비스 호출 (FastAPI 요청이 블로킹되지 않도록)
        future = client.call_async(req)

        # 서버 로그 및 상태 확인을 위한 콜백 부착
        future.add_done_callback(
            lambda f: self._set_mode_response_callback(f, robot_id, mode)
        )
        return future

    def _set_mode_response_callback(self, future, robot_id: int, mode: str):
        """set_mode 서비스 호출 완료 후 실행되는 콜백"""
        try:
            response = future.result()
            if response.success:
                self.get_logger().info(f"✅ [로봇 {robot_id}] {mode} 명령 수락됨: {response.message}")
            else:
                self.get_logger().warning(f"⚠️ [로봇 {robot_id}] {mode} 명령 거부됨: {response.message}")
        except Exception as e:
            self.get_logger().error(f"❌ [로봇 {robot_id}] {mode} 서비스 응답 중 예외 발생: {e}")

    def publish_manual_control(self, robot_id: int, linear_x: float, angular_z: float,
                               lift: float = 0.0):
        """수동 주행 조종. lift는 3호기 리프트: +1.0 올림 / -1.0 내림 / 0.0 정지."""
        msg = TwistStamped()

        # 타임스탬프 기록 필수
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.header.frame_id = 'base_link'

        # 속도 및 회전값 세팅
        msg.twist.linear.x = float(linear_x)
        msg.twist.angular.z = float(angular_z)
        msg.twist.linear.z = float(lift)

        # 해당 로봇의 네임스페이스 cmd_vel 퍼블리셔로 발행
        pub = self.robots.get(robot_id, {}).get('cmd_vel_pub')
        if pub:
            pub.publish(msg)
        else:
            self.get_logger().error(f"❌ [cmd_vel] 로봇 {robot_id}호기의 cmd_vel_pub을 찾을 수 없습니다!")

    def check_timeout(self):
        current_time = time.time()

        # 기록된 모든 로봇의 마지막 통신 시간을 검사
        for robot_id, last_time in self.last_seen_time.items():
            # 이미 OFFLINE 처리된 로봇은 무시
            if self.is_offline.get(robot_id, False):
                continue

            # 3초 이상 데이터가 안 들어왔다면? -> OFFLINE 판정!
            if current_time - last_time > 3.0:
                self.get_logger().warning(f"🔴 [로봇 {robot_id}] 3초간 응답 없음! OFFLINE 처리합니다.")
                self.is_offline[robot_id] = True

                # 유니티 관제용 OFFLINE 가짜(Mock) Payload 만들기
                ws_payload = {
                    "type": "ROBOT_STATUS",
                    "data": {
                        "robot_id": robot_id,
                        "status": "OFFLINE", # 상태를 OFFLINE으로 덮어씌움
                        "x": 0.0, "y": 0.0, "yaw": 0.0, # 좌표는 0이거나 마지막 캐시 좌표 사용
                        "battery": 0.0,
                        "pause_reason": "DISCONNECTED",
                        "current_target_wp": -1
                    }
                }

                # WebSocket 브로드캐스트 로직 호출
                if self.broadcast_callback:
                    self.broadcast_callback(ws_payload)