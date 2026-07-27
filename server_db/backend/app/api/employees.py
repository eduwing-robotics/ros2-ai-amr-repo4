import os
import json
import shutil
from fastapi import APIRouter, Depends, HTTPException, File, Form, UploadFile
from typing import List
from sqlalchemy.orm import Session
from app.db.database import get_db
from app.crud import employees as crud_employees
# (사전에 정의한 응답용 Pydantic 스키마들을 임포트해야 합니다)
from app.schemas.attendance import AttendanceStateResponse

router = APIRouter()

# ==========================================
# [1단계] 얼굴 데이터 전체 조회
# ==========================================
@router.get("/face-registry")
def get_face_registry(db: Session = Depends(get_db)):
    """노트북(Edge) 초기 구동 시, 얼굴 인식을 위해 모든 임베딩 리스트를 가져갑니다."""
    faces = crud_employees.get_all_employee_faces(db)
    response_data = []
    for face in faces:
        response_data.append({
            "employee_id": face.employee_id,
            "name": face.name,
            # JSONB로 바꿨기 때문에 변환 없이 그냥 쏙 넣으면 됩니다!
            "face_embedding": face.face_embedding
        })
    return response_data

# ==========================================
# [2단계] 특정 직원 현재 출퇴근 상태 조회
# ==========================================
@router.get("/{employee_id}/attendance-state")
def get_attendance_state(employee_id: str, db: Session = Depends(get_db)):
    """
    얼굴 인식 성공 후, 이 사람이 출근해야 하는지 퇴근해야 하는지 판별하기 위해
    현재 상태(가장 마지막 로그)를 조회합니다.
    """
    last_log = crud_employees.get_employee_current_status(db, employee_id)

    # 💡 만약 한 번도 찍은 적 없는 쌩얼굴(신입)이라면 기본값을 "OUT"으로 간주합니다.
    if not last_log:
        return {"state": "out"}

    # 마지막으로 찍은 액션이 "check_in"(출근) 이라면 현재 상태는 "in", 아니면 "out"
    current_state = "in" if last_log.action_type == "check_in" else "out"

    return {"state": current_state}

# ==========================================
# [5단계] 신규 직원 얼굴 등록 (Multipart)
# ==========================================
@router.post("/face-registration")
async def register_employee_face(
    employee_id: str = Form(...),
    name: str = Form(...),
    position: str = Form("직원"), # 기본값 설정
    phone_number: str = Form(None),
    embedding_file: str = Form(...), # 프론트에서 배열을 JSON 문자열로 바꿔서 보냄 (예: "[0.12, 0.44, ...]")
    photo_files: List[UploadFile] = File(...),
    db: Session = Depends(get_db)
):
    """
    관리자 페이지에서 신규 직원의 인적사항과 얼굴 사진, 임베딩 벡터를 등록합니다.
    """

    # 1. 전달받은 임베딩 문자열을 파이썬 리스트(배열)로 변환
    try:
        embedding_list = json.loads(embedding_file)
    except json.JSONDecodeError:
        raise HTTPException(status_code=400, detail="임베딩 데이터 형식이 올바르지 않습니다.")

    # 2. 사진 파일을 저장할 폴더 준비 (예: app/static/faces/EMP-003/)
    save_directory = f"app/static/faces/{employee_id}"
    os.makedirs(save_directory, exist_ok=True)

    # 3. 사진 파일들을 서버 하드디스크에 저장하고, 저장된 경로들을 모음
    saved_photo_urls = []
    for file in photo_files:
        file_path = f"{save_directory}/{file.filename}"

        # 청크 단위로 안전하게 파일 쓰기
        with open(file_path, "wb") as buffer:
            shutil.copyfileobj(file.file, buffer)

        # DB에 넣을 때는 웹에서 접근 가능한 상대 경로로 변환 (예: /static/faces/EMP-003/1.jpg)
        db_url = f"/static/faces/{employee_id}/{file.filename}"
        saved_photo_urls.append(db_url)

    # 4. DB에 직원 정보와 얼굴 데이터 최종 저장 (아까 만든 CRUD 호출)
    try:
        saved_employee = crud_employees.create_employee_with_face(
            db=db,
            employee_id=employee_id,
            name=name,
            position=position,
            phone_number=phone_number,
            face_embedding=embedding_list,
            photo_urls=saved_photo_urls
        )
    except Exception as e:
        # 이미 존재하는 사번이거나 DB 에러 발생 시
        raise HTTPException(status_code=400, detail=f"직원 등록 실패: {str(e)}")

    # 5. 명세서 규격에 맞게 성공 응답 반환
    return {
        "ok": True,
        "employee": {
            "employee_id": saved_employee.employee_id,
            "name": saved_employee.name,
            "status": "active"
        }
    }