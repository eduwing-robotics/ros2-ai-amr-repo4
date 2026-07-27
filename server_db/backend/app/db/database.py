import os

from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, declarative_base

# DATABASE_URL is supplied through the local .env file.
SQLALCHEMY_DATABASE_URL = os.environ.get("DATABASE_URL")
if not SQLALCHEMY_DATABASE_URL:
    raise RuntimeError("DATABASE_URL must be set (see .env.example).")

# DB 엔진(모터) 생성
engine = create_engine(SQLALCHEMY_DATABASE_URL)

# DB에 접속해서 작업할 '세션(연결 통로)' 공장 만들기
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

# 모든 테이블의 뼈대가 될 부모 클래스
Base = declarative_base()

# 웨이터(API)들이 DB에 접근할 때 쓸 수 있게 통로를 열어주고 닫아주는 함수
def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()