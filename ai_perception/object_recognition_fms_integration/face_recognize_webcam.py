import os
from pathlib import Path
import numpy as np

class MatchResult:
    def __init__(self, label="unknown", name=None, number=None, score=0.0, best_similarity=0.0):
        self.label = label
        self.name = name
        self.number = number
        self.score = score
        self.best_similarity = best_similarity
        self.margin = 0.0

    @property
    def is_known(self) -> bool:
        return self.label != "unknown"

class RegisteredIdentity:
    def __init__(self, label, name, number, mean_embedding):
        self.label = label
        self.name = name
        self.number = number
        self.mean_embedding = mean_embedding

def load_registered_identities(directory_path):
    identities = []
    dir_path = Path(directory_path)
    if not dir_path.exists():
        return identities

    for sub_dir in dir_path.iterdir():
        if not sub_dir.is_dir():
            continue

        folder_name = sub_dir.name
        parts = folder_name.split("_")
        if len(parts) < 2:
            continue
        name = parts[0]
        number = parts[1]

        mean_embedding_file = sub_dir / "embeddings" / "mean_embedding.npy"
        if not mean_embedding_file.exists():
            mean_embedding_file = sub_dir / "mean_embedding.npy"

        if mean_embedding_file.exists():
            try:
                mean_embedding = np.load(mean_embedding_file)
                identities.append(RegisteredIdentity(
                    label=folder_name,
                    name=name,
                    number=number,
                    mean_embedding=mean_embedding
                ))
            except Exception as e:
                pass

    return identities

def cosine_similarity(v1, v2):
    dot_product = np.dot(v1, v2)
    norm_v1 = np.linalg.norm(v1)
    norm_v2 = np.linalg.norm(v2)
    if norm_v1 == 0 or norm_v2 == 0:
        return 0.0
    return float(dot_product / (norm_v1 * norm_v2))

def recognize_frame(roi, face_app, identities, face_args):
    faces = face_app.get(roi)
    if not faces:
        return []

    results = []
    if getattr(face_args, "single_face", True):
        faces = sorted(faces, key=lambda f: (f.bbox[2]-f.bbox[0]) * (f.bbox[3]-f.bbox[1]), reverse=True)[:1]

    similarity_threshold = getattr(face_args, "similarity_threshold", 0.34)

    for face in faces:
        face_bbox = [int(x) for x in face.bbox]
        embedding = face.embedding
        if embedding is None:
            continue

        best_score = -1.0
        best_id = None

        for identity in identities:
            sim = cosine_similarity(embedding, identity.mean_embedding)
            if sim > best_score:
                best_score = sim
                best_id = identity

        if best_score >= similarity_threshold and best_id is not None:
            match = MatchResult(
                label=best_id.label,
                name=best_id.name,
                number=best_id.number,
                score=best_score,
                best_similarity=best_score
            )
        else:
            match = MatchResult(
                label="unknown",
                name=None,
                number=None,
                score=best_score if best_id else 0.0,
                best_similarity=best_score if best_id else 0.0
            )

        results.append((face_bbox, match, best_score))

    return results
