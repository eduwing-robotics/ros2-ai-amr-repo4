import os
import shutil
import numpy as np
from sqlalchemy.orm import Session
from app.db.database import SessionLocal
from app.db import models

# ==========================================
# 1. 경로 설정
# ==========================================
# 프론트 팀원에게 받은 폴더를 서버의 이 위치에 압축 푸세요.
SOURCE_DIR = "./registered_faces"
# 서버에서 실제로 사진을 서비스할 정적 폴더
TARGET_DIR = "./app/static/faces"

def migrate_local_data_to_db():
    db: Session = SessionLocal()
    os.makedirs(TARGET_DIR, exist_ok=True)

    print(f"🚀 [마이그레이션 시작] '{SOURCE_DIR}' 폴더를 스캔합니다...\n")

    try:
        # 2. '이름_사번' 폴더들 순회
        for folder_name in os.listdir(SOURCE_DIR):
            folder_path = os.path.join(SOURCE_DIR, folder_name)

            # 폴더가 아니면 무시 (예: .DS_Store 같은 숨김 파일)
            if not os.path.isdir(folder_path):
                continue

            # 폴더명 분리 ("백은주_001" -> 이름: 백은주, 사번: 001)
            parts = folder_name.split("_")
            if len(parts) != 2:
                print(f"⚠️ 무시됨: '{folder_name}' (이름_사번 형식이 아님)")
                continue

            emp_name = parts[0]
            emp_id = parts[1]

            print(f"🔄 처리 중: {emp_name} (사번: {emp_id})...")

            # 3. 서버 정적 폴더에 해당 사번 폴더 생성 (예: app/static/faces/001/)
            emp_target_dir = os.path.join(TARGET_DIR, emp_id)
            os.makedirs(emp_target_dir, exist_ok=True)

            embeddings_list = []
            saved_photo_urls = []

            # 4. embeddings 폴더 처리 (.npy 파일 찾기)
            embeddings_dir = os.path.join(folder_path, "embeddings")
            if os.path.exists(embeddings_dir):
                for file_name in os.listdir(embeddings_dir):
                    if file_name.endswith(".npy"):
                        npy_path = os.path.join(embeddings_dir, file_name)
                        # numpy로 읽어서 파이썬 리스트로 변환 (JSONB용)
                        np_array = np.load(npy_path, allow_pickle=True)
                        embeddings_list = np_array.tolist()
                        break # npy 파일은 1개라고 가정

            if not embeddings_list:
                print(f"   ❌ 실패: {emp_name}의 embeddings.npy 파일이 없습니다.")
                continue

            # 5. faces_224 폴더 처리 (크롭된 사진들 복사)
            faces_dir = os.path.join(folder_path, "faces_224")
            if os.path.exists(faces_dir):
                for file_name in os.listdir(faces_dir):
                    if file_name.lower().endswith(('.png', '.jpg', '.jpeg')):
                        source_file_path = os.path.join(faces_dir, file_name)
                        target_file_path = os.path.join(emp_target_dir, file_name)

                        # 파일 복사
                        shutil.copy2(source_file_path, target_file_path)

                        # DB에 넣을 웹 접근용 URL (예: /static/faces/001/face1.jpg)
                        db_url = f"/static/faces/{emp_id}/{file_name}"
                        saved_photo_urls.append(db_url)

            if not saved_photo_urls:
                 print(f"   ⚠️ 경고: {emp_name}의 사진(faces_224)이 없습니다. (빈 사진 배열로 진행)")

            # 6. DB 중복 확인 및 삽입
            existing_emp = db.query(models.Employee).filter(models.Employee.employee_id == emp_id).first()
            if existing_emp:
                print(f"   ⏩ 건너뜀: 사번 {emp_id}는 이미 DB에 존재합니다.")
                continue

            # 직원 정보 INSERT
            new_employee = models.Employee(
                employee_id=emp_id,
                name=emp_name,
            )
            db.add(new_employee)

            # 얼굴 정보 INSERT
            new_face = models.EmployeeFace(
                employee_id=emp_id,
                face_embedding=embeddings_list,
                photo_urls=saved_photo_urls
            )
            db.add(new_face)
            db.commit()

            print(f"   ✅ 완료: {emp_name} DB 등록 및 사진 {len(saved_photo_urls)}장 복사 성공!\n")

    except Exception as e:
        print(f"\n❌ 치명적 에러 발생: {e}")
        db.rollback()
    finally:
        db.close()
        print("🎉 [마이그레이션 종료] 모든 작업이 끝났습니다.")

if __name__ == "__main__":
    migrate_local_data_to_db()