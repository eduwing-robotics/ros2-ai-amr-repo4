from fastapi import WebSocket
import asyncio

# --- 관제탑 매니저 ---
class ConnectionManager:
    def __init__(self):
        # 현재 접속 중인 클라이언트(Unity 등) 목록
        self.active_connections: list[WebSocket] = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)
        print(f"🔗 웹소켓 클라이언트 연결됨! (현재 연결 수: {len(self.active_connections)})")

    def disconnect(self, websocket: WebSocket):
        if websocket in self.active_connections:
            self.active_connections.remove(websocket)
            print(f"🔌 웹소켓 클라이언트 연결 해제 (현재 연결 수: {len(self.active_connections)})")

    async def broadcast(self, message: dict):
        # [CCTV 2번] 웹소켓 매니저가 브로드캐스트를 시작하는지 확인!
        #print(f"📢 [CCTV 2] 웹소켓 발사 준비! (현재 대기 중인 접속자 수: {len(self.active_connections)}명)")
        # 모든 접속자에게 JSON 데이터를 쏩니다.
        for connection in self.active_connections:
            try:
                await connection.send_json(message)
            except Exception as e:
                print(f"웹소켓 전송 에러: {e}")

# 전역 매니저 객체 생성
manager = ConnectionManager()

# --- 비디오 스트리밍 전용 매니저 ---
class VideoConnectionManager:
    def __init__(self):
        # 어떤 로봇(robot_id)의 영상을 어떤 유저들이 보고 있는지 딕셔너리로 관리
        # str을 키로 사용 (예: "1", "2", "3", "global")
        self.active_connections: dict[str, list[WebSocket]] = {}

    async def connect(self, websocket: WebSocket, camera_id: str):
        await websocket.accept()
        if camera_id not in self.active_connections:
            self.active_connections[camera_id] = []
        self.active_connections[camera_id].append(websocket)
        print(f"🎥 카메라 [{camera_id}] 영상 채널 연결됨! (현재 시청자: {len(self.active_connections[camera_id])}명)")

    def disconnect(self, websocket: WebSocket, camera_id: str):
        if camera_id in self.active_connections:
            if websocket in self.active_connections[camera_id]:
                self.active_connections[camera_id].remove(websocket)
            print(f"🔌 카메라 [{camera_id}] 영상 채널 해제")

    async def broadcast_video(self, camera_id: str, frame_byte: bytes):
        if camera_id in self.active_connections:
            # --- 🔍 유니티 팀원의 질문을 확인하기 위한 진단 코드 ---
            # 1. 첫 2바이트 (JPEG 시작 헤더, 정상이라면 FFD8 이어야 함)
            #start_bytes = frame_byte[:2].hex().upper()
            # 2. 마지막 2바이트 (JPEG 끝 헤더, 정상이라면 FFD9 이어야 함)
            #end_bytes = frame_byte[-2:].hex().upper()

            # 3. 로그 출력
            #print(f"📡 [{camera_id}] 웹소켓 전송 시도! "
            #      f"크기: {len(frame_byte)}bytes | "
            #      f"시작: {start_bytes} | 끝: {end_bytes}")

            # JSON이 아니라 send_bytes를 사용
            for connection in self.active_connections[camera_id]:
                try:
                    await connection.send_bytes(frame_byte)
                except Exception as e:
                    pass

video_manager = VideoConnectionManager()