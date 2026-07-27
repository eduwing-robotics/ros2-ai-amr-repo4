import cv2
import os
import shutil
from datetime import datetime
from fastapi import UploadFile, HTTPException

# 파일을 저장할 최상위 창고 경로 (우리 프로젝트의 app/static 폴더)
BASE_STATIC_DIR = "app/static"

def save_upload_file(upload_file: UploadFile, sub_folder: str) -> str:
    """
    프론트엔드에서 받은 파일을 안전하게 폴더에 저장하고,
    DB에 저장할 수 있는 '웹 접근용 URL 주소'를 반환하는 함수입니다.
    """
    try:
        # 1. 저장할 폴더 경로 만들기 (예: app/static/alerts)
        target_directory = os.path.join(BASE_STATIC_DIR, sub_folder)

        # 폴더가 없으면 에러가 나므로, 없으면 알아서 만들도록 지시 (마법의 명령어)
        os.makedirs(target_directory, exist_ok=True)

        # 2. 중복 방지를 위한 절대 겹치지 않는 파일명 만들기
        # 예: 20260605_180302_원래파일명.jpg
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        safe_filename = f"{timestamp}_{upload_file.filename}"

        # 3. 최종 저장될 컴퓨터 안의 진짜 경로 (예: app/static/alerts/20260605_180302_fall.jpg)
        file_path = os.path.join(target_directory, safe_filename)

        # 4. 파일 쓰기 (shutil을 쓰면 서버 메모리가 터지지 않고 큰 파일도 안전하게 쪼개서 저장됩니다)
        with open(file_path, "wb") as buffer:
            shutil.copyfileobj(upload_file.file, buffer)

        # 5. 프론트엔드와 DB가 알기 쉬운 깔끔한 웹 URL 주소로 변환해서 돌려주기
        # 이 주소를 프론트가 <img src="..."> 에 넣으면 바로 사진이 뜹니다!
        web_url = f"/static/{sub_folder}/{safe_filename}"

        return web_url

    except Exception as e:
        # 파일 저장 중 문제가 생기면 서버 안 죽게 친절하게 에러 던져주기
        raise HTTPException(status_code=500, detail=f"파일 저장 중 오류가 발생했습니다: {str(e)}")


ALERTS_DIR = os.path.join("app", "static", "alerts")

def save_alert_frame(frame, incident_type: str, robot_id: int) -> str:
    """
    감지된 프레임을 JPG 파일로 저장하고 웹 접근 경로를 반환합니다.
    """
    # 1. 폴더가 없으면 자동으로 생성 (에러 방지)
    os.makedirs(ALERTS_DIR, exist_ok=True)

    # 2. 파일명 생성 (예: FALL_robot1_20260617_153022_123456.jpg)
    # 밀리초(%f)까지 넣어서 찰나의 순간에 이름이 겹치는 것을 방지합니다.
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
    filename = f"{incident_type}_robot{robot_id}_{timestamp}.jpg"

    # 3. 실제 하드디스크에 저장할 경로
    file_path = os.path.join(ALERTS_DIR, filename)

    # 4. OpenCV를 이용해 이미지 저장
    # (선택) 만약 프레임에 YOLO 바운딩 박스를 그리고 싶다면, imwrite 전에 cv2.rectangle 등을 그리면 됩니다!
    success = cv2.imwrite(file_path, frame)

    if not success:
        print(f"❌ 사진 저장 실패: {file_path}")
        return None

    # 5. DB에 저장하고 프론트엔드가 사용할 '웹 접근용 URL 경로' 반환
    web_url = f"/static/alerts/{filename}"
    return web_url