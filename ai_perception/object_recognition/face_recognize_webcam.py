#!/usr/bin/env python3
import argparse
import csv
import json
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np
from insightface.app import FaceAnalysis

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    Image = None
    ImageDraw = None
    ImageFont = None


WINDOW_NAME = "Face Recognition"
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp"}
ATTENDANCE_FIELDS = [
    "timestamp",
    "mode",
    "identity_key",
    "name",
    "number",
    "score",
    "best_similarity",
    "margin",
]


@dataclass
class Identity:
    key: str
    name: str
    number: str
    embeddings: np.ndarray
    image_paths: list[str]
    source_dirs: list[str]


@dataclass
class MatchResult:
    label: str
    name: str | None
    number: str | None
    score: float | None
    best_similarity: float | None
    margin: float | None
    best_image: str | None
    is_known: bool


def load_korean_font(size: int):
    if ImageFont is None:
        return None

    candidates = [
        "/usr/share/fonts/truetype/nanum/NanumGothic.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/System/Library/Fonts/AppleSDGothicNeo.ttc",
        "C:/Windows/Fonts/malgun.ttf",
    ]
    for candidate in candidates:
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


FONT_LARGE = load_korean_font(42)
FONT_MEDIUM = load_korean_font(24)
FONT_SMALL = load_korean_font(18)


def put_text_korean(frame, text, position, font, color=(255, 255, 255)):
    if Image is None or ImageDraw is None or font is None:
        cv2.putText(
            frame,
            text.encode("ascii", "ignore").decode("ascii") or "Face",
            position,
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            color,
            2,
            cv2.LINE_AA,
        )
        return frame

    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
    image = Image.fromarray(rgb)
    draw = ImageDraw.Draw(image)
    draw.text(position, text, font=font, fill=(color[2], color[1], color[0]))
    return cv2.cvtColor(np.array(image), cv2.COLOR_RGB2BGR)


def normalize_embeddings(embeddings: np.ndarray) -> np.ndarray:
    embeddings = np.asarray(embeddings, dtype=np.float32)
    if embeddings.ndim == 1:
        embeddings = embeddings.reshape(1, -1)
    norms = np.linalg.norm(embeddings, axis=1, keepdims=True)
    return embeddings / np.maximum(norms, 1e-12)


def identity_from_dir(session_dir: Path, metadata: dict) -> tuple[str, str, str]:
    name = str(metadata.get("user_name") or "").strip()
    number = str(metadata.get("user_number") or "").strip()

    if not name or not number:
        parts = session_dir.name.split("_")
        if len(parts) >= 2:
            name = name or parts[0]
            number = number or parts[1]
        else:
            name = name or session_dir.name
            number = number or ""

    key = f"{name}_{number}" if number else name
    return key, name, number


def load_identity_session(session_dir: Path):
    embedding_dir = session_dir / "embeddings"
    embeddings_path = embedding_dir / "embeddings.npy"
    metadata_path = embedding_dir / "metadata.json"
    if not embeddings_path.exists() or not metadata_path.exists():
        return None

    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    key, name, number = identity_from_dir(session_dir, metadata)
    embeddings = normalize_embeddings(np.load(embeddings_path))
    image_paths = metadata.get("encoded_images") or []
    return key, name, number, embeddings, image_paths, str(session_dir)


def load_registered_identities(root: Path) -> list[Identity]:
    grouped: dict[str, dict] = {}

    for embeddings_path in sorted(root.glob("*/embeddings/embeddings.npy")):
        session_dir = embeddings_path.parent.parent
        loaded = load_identity_session(session_dir)
        if loaded is None:
            continue

        key, name, number, embeddings, image_paths, source_dir = loaded
        item = grouped.setdefault(
            key,
            {
                "name": name,
                "number": number,
                "embeddings": [],
                "image_paths": [],
                "source_dirs": [],
            },
        )
        item["embeddings"].append(embeddings)
        item["image_paths"].extend(image_paths)
        item["source_dirs"].append(source_dir)

    identities = []
    for key, item in grouped.items():
        embeddings = normalize_embeddings(np.vstack(item["embeddings"]))
        identities.append(
            Identity(
                key=key,
                name=item["name"],
                number=item["number"],
                embeddings=embeddings,
                image_paths=item["image_paths"],
                source_dirs=item["source_dirs"],
            )
        )

    return sorted(identities, key=lambda identity: identity.key)


def create_face_app(providers: list[str], det_size: int):
    app = FaceAnalysis(name="buffalo_l", providers=providers)
    app.prepare(ctx_id=0, det_size=(det_size, det_size))
    return app


def largest_face_only(faces):
    if not faces:
        return []
    return [max(faces, key=lambda face: (face.bbox[2] - face.bbox[0]) * (face.bbox[3] - face.bbox[1]))]


def score_identity(query_embedding: np.ndarray, identity: Identity, top_k: int):
    similarities = identity.embeddings @ query_embedding
    best_index = int(np.argmax(similarities))
    best_similarity = float(similarities[best_index])
    k = max(1, min(top_k, similarities.size))
    top_scores = np.sort(similarities)[-k:]
    top_mean = float(np.mean(top_scores))
    # Keep pose-specific matching dominant while still rewarding consistent nearby samples.
    score = 0.75 * best_similarity + 0.25 * top_mean
    best_image = identity.image_paths[best_index] if best_index < len(identity.image_paths) else None
    return score, best_similarity, best_image


def recognize_embedding(
    query_embedding: np.ndarray,
    identities: list[Identity],
    similarity_threshold: float,
    margin_threshold: float,
    top_k: int,
):
    query_embedding = normalize_embeddings(query_embedding)[0]
    candidates = []

    for identity in identities:
        score, best_similarity, best_image = score_identity(query_embedding, identity, top_k)
        candidates.append((score, best_similarity, identity, best_image))

    if not candidates:
        return MatchResult("Unknown", None, None, None, None, None, None, False)

    candidates.sort(key=lambda item: item[0], reverse=True)
    best_score, best_similarity, best_identity, best_image = candidates[0]
    second_score = candidates[1][0] if len(candidates) > 1 else None
    margin = None if second_score is None else best_score - second_score

    is_known = best_similarity >= similarity_threshold and (
        margin is None or margin >= margin_threshold
    )
    if not is_known:
        return MatchResult(
            "Unknown",
            None,
            None,
            float(best_score),
            float(best_similarity),
            None if margin is None else float(margin),
            best_image,
            False,
        )

    return MatchResult(
        best_identity.key,
        best_identity.name,
        best_identity.number,
        float(best_score),
        float(best_similarity),
        None if margin is None else float(margin),
        best_image,
        True,
    )


def recognize_frame(frame, app, identities, args):
    faces = app.get(frame)
    if args.single_face:
        faces = largest_face_only(faces)

    results = []
    for face in faces:
        embedding = np.asarray(face.normed_embedding, dtype=np.float32)
        match = recognize_embedding(
            embedding,
            identities,
            args.similarity_threshold,
            args.margin_threshold,
            args.top_k,
        )
        bbox = tuple(int(value) for value in face.bbox)
        results.append((bbox, match, face))
    return results




def display_name(match: MatchResult) -> str:
    if not match.is_known:
        return match.label
    if match.name and match.number:
        return f"{match.name} {match.number}"
    return match.name or match.label


def recognition_message(match: MatchResult) -> str:
    name = match.name or ""
    number = match.number or ""
    return f"인식되었습니다. 이름: {name} 사번: {number}"


def recognition_lines(match: MatchResult | None) -> tuple[list[str], bool]:
    if match is None:
        return ["인식중..."], False
    return ["인식되었습니다.", f"이름: {match.name or ''}    사번: {match.number or ''}"], True


def blink_status_lines(
    match: MatchResult,
    blink_count: int,
    required_blinks: int,
    pose_changed: bool,
) -> list[str]:
    name = match.name or match.label
    number = match.number or ""
    pose_text = "확인" if pose_changed else "대기"
    return [
        "본인 확인중...",
        f"이름: {name} 사번: {number} 깜빡임: {blink_count}/{required_blinks} 방향: {pose_text}",
    ]


def first_known_match(results):
    for _, match, _ in results:
        if match.is_known:
            return match
    return None


def first_known_result(results):
    for bbox, match, face in results:
        if match.is_known:
            return bbox, match, face
    return None, None, None


def eyes_are_open(frame, bbox, eye_classifier, open_eye_count: int) -> bool | None:
    left, top, right, bottom = bbox
    height, width = frame.shape[:2]
    x1 = max(0, int(left))
    y1 = max(0, int(top))
    x2 = min(width, int(right))
    y2 = min(height, int(bottom))
    if x2 <= x1 or y2 <= y1:
        return None

    face_roi = frame[y1:y2, x1:x2]
    roi_h, roi_w = face_roi.shape[:2]
    upper_face = face_roi[: max(1, int(roi_h * 0.62)), :]
    gray = cv2.cvtColor(upper_face, cv2.COLOR_BGR2GRAY)
    eyes = eye_classifier.detectMultiScale(
        gray,
        scaleFactor=1.08,
        minNeighbors=4,
        minSize=(max(12, roi_w // 12), max(8, roi_h // 18)),
    )
    return len(eyes) >= open_eye_count


def update_blink_count(eyes_open, previous_eyes_open, blink_count):
    if eyes_open is None:
        return previous_eyes_open, blink_count
    if previous_eyes_open is False and eyes_open is True:
        blink_count += 1
    return eyes_open, blink_count


def face_pose_signature(face):
    keypoints = getattr(face, "kps", None)
    if keypoints is None or len(keypoints) < 5:
        return None

    left_eye, right_eye, nose, left_mouth, right_mouth = np.asarray(keypoints[:5], dtype=np.float32)
    eye_mid = (left_eye + right_eye) / 2.0
    mouth_mid = (left_mouth + right_mouth) / 2.0
    eye_width = max(float(np.linalg.norm(right_eye - left_eye)), 1e-6)
    face_height = max(float(np.linalg.norm(mouth_mid - eye_mid)), 1e-6)

    yaw = float((nose[0] - eye_mid[0]) / eye_width)
    pitch = float((nose[1] - eye_mid[1]) / face_height)
    return np.array([yaw, pitch], dtype=np.float32)


def pose_changed_from_baseline(face, baseline_pose, threshold):
    pose = face_pose_signature(face)
    if pose is None:
        return baseline_pose, False
    if baseline_pose is None:
        return pose, False
    changed = bool(np.max(np.abs(pose - baseline_pose)) >= threshold)
    return baseline_pose, changed


def draw_status_panel(frame, lines, is_success=False, is_pending=False):
    border_color = (40, 190, 80) if is_success else (70, 150, 255)
    if is_pending:
        border_color = (50, 210, 230)
    text_color = (90, 255, 130) if is_success else (220, 235, 255)

    height, width = frame.shape[:2]
    panel_h = 88 if len(lines) > 1 else 58
    y1 = height - panel_h - 12
    y2 = height - 12

    overlay = frame.copy()
    cv2.rectangle(overlay, (16, y1), (width - 16, y2), (15, 15, 15), -1)
    cv2.addWeighted(overlay, 0.72, frame, 0.28, 0, frame)
    cv2.rectangle(frame, (16, y1), (width - 16, y2), border_color, 2)

    if len(lines) > 1:
        frame = put_text_korean(frame, lines[0], (32, y1 + 10), FONT_MEDIUM, text_color)
        frame = put_text_korean(frame, lines[1], (32, y1 + 48), FONT_SMALL, (255, 255, 255))
    else:
        frame = put_text_korean(frame, lines[0], (32, y1 + 16), FONT_MEDIUM, text_color)
    return frame


def draw_recognition_message(frame, results, forced_match=None):
    match = forced_match if forced_match is not None else first_known_match(results)
    lines, is_known = recognition_lines(match)
    return draw_status_panel(frame, lines, is_success=is_known)


def draw_blink_status(
    frame,
    match: MatchResult,
    blink_count: int,
    required_blinks: int,
    pose_changed: bool,
):
    return draw_status_panel(
        frame,
        blink_status_lines(match, blink_count, required_blinks, pose_changed),
        is_pending=True,
    )


def draw_results(frame, results):
    for (left, top, right, bottom), match, _ in results:
        color = (40, 190, 80) if match.is_known else (50, 70, 230)
        cv2.rectangle(frame, (left, top), (right, bottom), color, 2)

        label = display_name(match)
        if match.best_similarity is not None:
            label = f"{label} sim={match.best_similarity:.3f}"
        if match.margin is not None:
            label = f"{label} margin={match.margin:.3f}"

        label_y = max(0, top - 34)
        cv2.rectangle(frame, (left, label_y), (max(right, left + 260), label_y + 30), color, cv2.FILLED)
        frame = put_text_korean(frame, label, (left + 6, label_y + 4), FONT_SMALL, (255, 255, 255))


def print_gallery_summary(identities: list[Identity]):
    total_embeddings = sum(identity.embeddings.shape[0] for identity in identities)
    print(f"Loaded identities: {len(identities)}")
    print(f"Loaded embeddings: {total_embeddings}")
    for identity in identities:
        print(
            f"  - {identity.key}: {identity.embeddings.shape[0]} embedding(s), "
            f"{len(identity.source_dirs)} session(s)"
        )


def run_image_mode(args, app, identities):
    image = cv2.imread(str(args.image))
    if image is None:
        raise RuntimeError(f"이미지를 읽을 수 없습니다: {args.image}")

    results = recognize_frame(image, app, identities, args)
    draw_results(image, results)
    image = draw_recognition_message(image, results)

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        cv2.imwrite(str(args.output), image)
        print(f"Saved output: {args.output}")

    print(f"Detected faces: {len(results)}")
    for index, (_, match, _) in enumerate(results, start=1):
        print(
            f"face#{index}: {display_name(match)} "
            f"identity_key={match.label} score={match.score} "
            f"best_similarity={match.best_similarity} margin={match.margin}"
        )


def iter_image_paths(input_dir: Path, recursive: bool):
    pattern = "**/*" if recursive else "*"
    return sorted(
        path
        for path in input_dir.glob(pattern)
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def run_dir_mode(args, app, identities):
    image_paths = iter_image_paths(args.input_dir, args.recursive)
    if not image_paths:
        raise RuntimeError(f"인식할 이미지가 없습니다: {args.input_dir}")

    output_dir = args.output_dir
    if output_dir:
        output_dir.mkdir(parents=True, exist_ok=True)

    csv_path = args.csv or Path("recognition_results.csv")
    csv_path.parent.mkdir(parents=True, exist_ok=True)

    rows = []
    for image_path in image_paths:
        image = cv2.imread(str(image_path))
        if image is None:
            rows.append({
                "image_path": str(image_path),
                "face_index": "",
                "identity_key": "",
                "name": "",
                "number": "",
                "label": "read_failed",
                "is_known": False,
                "score": "",
                "best_similarity": "",
                "margin": "",
                "bbox": "",
                "best_image": "",
            })
            continue

        results = recognize_frame(image, app, identities, args)
        draw_results(image, results)
        image = draw_recognition_message(image, results)

        if output_dir:
            out_path = output_dir / image_path.name
            cv2.imwrite(str(out_path), image)

        if not results:
            rows.append({
                "image_path": str(image_path),
                "face_index": 0,
                "identity_key": "",
                "name": "",
                "number": "",
                "label": "no_face",
                "is_known": False,
                "score": "",
                "best_similarity": "",
                "margin": "",
                "bbox": "",
                "best_image": "",
            })
            continue

        for index, (bbox, match, _) in enumerate(results, start=1):
            rows.append({
                "image_path": str(image_path),
                "face_index": index,
                "identity_key": match.label,
                "name": match.name or "",
                "number": match.number or "",
                "label": display_name(match),
                "is_known": match.is_known,
                "score": "" if match.score is None else f"{match.score:.6f}",
                "best_similarity": "" if match.best_similarity is None else f"{match.best_similarity:.6f}",
                "margin": "" if match.margin is None else f"{match.margin:.6f}",
                "bbox": ",".join(str(value) for value in bbox),
                "best_image": match.best_image or "",
            })

    with csv_path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=[
                "image_path",
                "face_index",
                "identity_key",
                "name",
                "number",
                "label",
                "is_known",
                "score",
                "best_similarity",
                "margin",
                "bbox",
                "best_image",
            ],
        )
        writer.writeheader()
        writer.writerows(rows)

    print(f"Processed images: {len(image_paths)}")
    print(f"Saved CSV: {csv_path}")
    if output_dir:
        print(f"Saved annotated images: {output_dir}")


def append_attendance_log(path: Path, mode: str, match: MatchResult):
    path.parent.mkdir(parents=True, exist_ok=True)
    file_exists = path.exists()
    row = {
        "timestamp": datetime.now().isoformat(timespec="seconds"),
        "mode": mode,
        "identity_key": match.label,
        "name": match.name or "",
        "number": match.number or "",
        "score": "" if match.score is None else f"{match.score:.6f}",
        "best_similarity": "" if match.best_similarity is None else f"{match.best_similarity:.6f}",
        "margin": "" if match.margin is None else f"{match.margin:.6f}",
    }
    with path.open("a", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=ATTENDANCE_FIELDS)
        if not file_exists:
            writer.writeheader()
        writer.writerow(row)
    return row


def run_webcam_mode(args, app, identities):
    capture = cv2.VideoCapture(args.camera)
    if not capture.isOpened():
        raise RuntimeError(f"카메라를 열 수 없습니다: {args.camera}")

    eye_path = cv2.data.haarcascades + "haarcascade_eye_tree_eyeglasses.xml"
    eye_classifier = cv2.CascadeClassifier(eye_path)
    if eye_classifier.empty():
        raise RuntimeError("OpenCV 눈 검출 모델을 불러오지 못했습니다.")

    capture.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    capture.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    print("Press q to quit.")
    last_process_at = 0.0
    last_results = []
    pending_match = None
    previous_eyes_open = None
    blink_count = 0
    baseline_pose = None
    pose_changed = False
    success_match = None
    success_at = None
    attendance_recorded = False

    try:
        while True:
            ok, frame = capture.read()
            if not ok:
                print("카메라 프레임을 읽을 수 없습니다.")
                break

            now = time.monotonic()
            if success_match is None and now - last_process_at >= args.process_interval:
                last_results = recognize_frame(frame, app, identities, args)
                last_process_at = now
                bbox, known_match, known_face = first_known_result(last_results)
                if known_match is not None:
                    if pending_match is None or pending_match.label != known_match.label:
                        pending_match = known_match
                        previous_eyes_open = None
                        blink_count = 0
                        baseline_pose = None
                        pose_changed = False

                    eyes_open = eyes_are_open(frame, bbox, eye_classifier, args.open_eye_count)
                    previous_eyes_open, blink_count = update_blink_count(
                        eyes_open,
                        previous_eyes_open,
                        blink_count,
                    )
                    baseline_pose, current_pose_changed = pose_changed_from_baseline(
                        known_face,
                        baseline_pose,
                        args.pose_change_threshold,
                    )
                    pose_changed = pose_changed or current_pose_changed
                    if blink_count >= args.blink_count and pose_changed:
                        success_match = pending_match
                        success_at = now
                        if not attendance_recorded:
                            row = append_attendance_log(args.attendance_log, args.attendance_mode, success_match)
                            attendance_recorded = True
                            print(
                                "Attendance recorded: "
                                f"{row['timestamp']} {row['mode']} {row['name']} {row['number']}"
                            )
                else:
                    pending_match = None
                    previous_eyes_open = None
                    blink_count = 0
                    baseline_pose = None
                    pose_changed = False

            if success_match is not None:
                frame = draw_recognition_message(frame, last_results, forced_match=success_match)
            else:
                draw_results(frame, last_results)
                if pending_match is not None:
                    frame = draw_blink_status(
                        frame,
                        pending_match,
                        blink_count,
                        args.blink_count,
                        pose_changed,
                    )
                else:
                    frame = draw_recognition_message(frame, last_results)

            cv2.imshow(WINDOW_NAME, frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

            if success_at is not None and now - success_at >= 3.0:
                break
    finally:
        capture.release()
        cv2.destroyAllWindows()


def parse_args():
    parser = argparse.ArgumentParser(
        description="registered_faces 임베딩을 사용해 InsightFace 기반 안면인식을 실행합니다."
    )
    parser.add_argument("--registered-dir", type=Path, default=Path("registered_faces"))
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--image", type=Path, help="웹캠 대신 단일 이미지 파일을 인식")
    parser.add_argument("--output", type=Path, help="--image 결과를 저장할 경로")
    parser.add_argument("--input-dir", type=Path, help="폴더 안 이미지들을 일괄 인식")
    parser.add_argument("--recursive", action="store_true", help="--input-dir 하위 폴더까지 포함")
    parser.add_argument("--output-dir", type=Path, help="--input-dir 결과 이미지를 저장할 폴더")
    parser.add_argument("--csv", type=Path, help="--input-dir 결과 CSV 경로")
    parser.add_argument("--similarity-threshold", type=float, default=0.32)
    parser.add_argument("--margin-threshold", type=float, default=0.03)
    parser.add_argument("--top-k", type=int, default=3)
    parser.add_argument("--det-size", type=int, default=640)
    parser.add_argument("--multi-face", dest="single_face", action="store_false")
    parser.set_defaults(single_face=True)
    parser.add_argument("--process-interval", type=float, default=0.05)
    parser.add_argument("--blink-count", type=int, default=3, help="최종 통과에 필요한 눈 깜빡임 횟수")
    parser.add_argument("--open-eye-count", type=int, default=2, help="눈이 열린 상태로 볼 최소 눈 검출 개수")
    parser.add_argument("--pose-change-threshold", type=float, default=0.08, help="얼굴 방향 변화 인정 임계값")
    parser.add_argument(
        "--attendance-mode",
        choices=["check_in", "check_out"],
        default="check_in",
        help="웹캠 인증 성공 시 기록할 출퇴근 모드",
    )
    parser.add_argument(
        "--attendance-log",
        type=Path,
        default=Path("attendance_log.csv"),
        help="웹캠 인증 성공 기록 CSV 경로",
    )
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=720)
    parser.add_argument(
        "--providers",
        nargs="+",
        default=["CUDAExecutionProvider", "CPUExecutionProvider"],
        help="insightface/onnxruntime provider 목록",
    )
    return parser.parse_args()


def main():
    args = parse_args()
    identities = load_registered_identities(args.registered_dir)
    if not identities:
        raise RuntimeError(f"사용 가능한 임베딩을 찾지 못했습니다: {args.registered_dir}")

    print_gallery_summary(identities)
    app = create_face_app(args.providers, args.det_size)

    if args.image:
        run_image_mode(args, app, identities)
    elif args.input_dir:
        run_dir_mode(args, app, identities)
    else:
        run_webcam_mode(args, app, identities)


if __name__ == "__main__":
    main()
