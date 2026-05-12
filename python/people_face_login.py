"""
Enrolled face login matching python/face.ipynb:
  * Prefer face_recognition + dlib (same flow as the notebook: 1/4 resize, every other frame).
  * If face_recognition is not installed, fall back to DeepFace Facenet embeddings.

Reference photos: python/people/*.jpg|png...
Roles: python/people/roles.tsv (copy from roles.tsv.example)

Stdout for TuioDemoApp.FaceLogin:
  PERSON:<name>
  PROFILE:teacher:<slug> or PROFILE:student:<slug>
  MODE:GAME|RADIAL
"""
from __future__ import annotations

import collections
import os
import pathlib
import re
import sys
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np

os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

_SCRIPT_DIR = pathlib.Path(__file__).resolve().parent
_PEOPLE_DIR = _SCRIPT_DIR / "people"
_ROLES_PATH = _PEOPLE_DIR / "roles.tsv"
_IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}

WINDOW_TITLE = "TUIO Face Login - Press Q"
_CONFIRMS = 9
# face_recognition notebook default distance threshold is 0.6; slightly stricter here
_FR_TOLERANCE = 0.55
# DeepFace cosine-ish distance cap (only used in fallback)
_DF_TOLERANCE = 0.52


def _slug_key(text: str) -> str:
    s = text.strip().lower().replace("&", " and ")
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-") or "user"


def _normalize_role(s: str) -> str:
    t = (s or "").strip().lower()
    if t in {"t", "teacher", "radial", "instructor", "staff"}:
        return "teacher"
    return "student"


def _load_roles(filename: pathlib.Path) -> Dict[str, Tuple[str, str]]:
    out: Dict[str, Tuple[str, str]] = {}
    if not filename.exists():
        return out
    for raw in filename.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("\t")]
        if len(parts) < 2:
            parts = [p.strip() for p in line.split(",")]
        if len(parts) < 2:
            continue
        file_part, role_col = parts[0], parts[1]
        display = parts[2] if len(parts) > 2 else pathlib.Path(file_part).stem
        base = pathlib.Path(file_part).name.lower()
        out[base] = (_normalize_role(role_col), display or base)
    return out


def _discover_images(folder: pathlib.Path) -> List[pathlib.Path]:
    if not folder.is_dir():
        return []
    return sorted(
        p for p in folder.iterdir()
        if p.is_file() and p.suffix.lower() in _IMAGE_EXTS
    )


def _open_camera():
    for name, backend in [
        ("DSHOW", cv2.CAP_DSHOW),
        ("MSMF", cv2.CAP_MSMF),
        ("ANY", cv2.CAP_ANY),
    ]:
        for idx in range(0, 8):
            cap = cv2.VideoCapture(idx, backend)
            if not cap.isOpened():
                cap.release()
                continue
            ok, frame = cap.read()
            if ok and frame is not None and frame.size > 0:
                print(f"CAMERA: backend={name}, index={idx}", file=sys.stderr)
                return cap
            cap.release()
    return None


def _open_preview_window() -> None:
    """Reserve the OpenCV window (title must stay ASCII-only for Windows OpenCV ANSI title bar)."""
    cv2.namedWindow(WINDOW_TITLE, cv2.WINDOW_NORMAL)


# --- face_recognition path (face.ipynb) ---


def _load_enrollment_face_recognition(fr, imgs, roles):
    encs: List = []
    labels: List[str] = []
    rfs: List[str] = []
    for path in imgs:
        rgb = fr.load_image_file(str(path))
        fe = fr.face_encodings(rgb)
        if not fe:
            print(f"No face in enrolment image: {path.name}", file=sys.stderr)
            continue
        basename = path.name.lower()
        role, display = roles.get(basename, ("student", path.stem))
        role = role if role == "teacher" else "student"
        encs.append(fe[0])
        labels.append(display)
        rfs.append(role)
    return encs, labels, rfs


def _run_face_recognition_loop(fr, cap, known_encodings, known_labels, known_roles) -> int:
    """Mirror face.ipynb: quarter scale, process every other frame, best face_distance match."""
    confirmations = collections.deque(maxlen=_CONFIRMS)
    process_this_frame = True
    face_locations = []
    face_names: List[Tuple[str, str]] = []
    last_hud = "Align your face with the camera"

    print("Using face_recognition (face.ipynb style).", file=sys.stderr)

    _open_preview_window()

    while True:
        ok, frame = cap.read()
        if not ok or frame is None:
            print("Lost webcam feed.", file=sys.stderr)
            break

        if process_this_frame:
            small_frame = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
            rgb_small = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)
            face_locations = fr.face_locations(rgb_small)
            encs = fr.face_encodings(rgb_small, face_locations)
            face_names = []
            hud = "No face detected"
            for face_encoding in encs:
                matches = fr.compare_faces(
                    known_encodings,
                    face_encoding,
                    tolerance=_FR_TOLERANCE,
                )
                dists = fr.face_distance(known_encodings, face_encoding)
                best = int(np.argmin(dists))
                if matches[best]:
                    name = known_labels[best]
                    role = known_roles[best]
                    face_names.append((name, role))
                else:
                    face_names.append(("Unknown", "student"))

            if not encs:
                confirmations.append(None)
            else:
                areas = [(b - t) * (r - l) for (t, r, b, l) in face_locations]
                pick = int(np.argmax(areas)) if areas else 0
                name, rk = face_names[pick] if pick < len(face_names) else ("Unknown", "student")
                hud = name if name != "Unknown" else "Unknown - move closer or improve light"
                confirmations.append((name, rk) if name != "Unknown" else None)
            last_hud = hud

        process_this_frame = not process_this_frame

        if len(face_names) != len(face_locations):
            face_names = [("Unknown", "student")] * len(face_locations)

        for (top, right, bottom, left), (nm, _) in zip(face_locations, face_names):
            top *= 4
            right *= 4
            bottom *= 4
            left *= 4
            col = (0, 0, 255) if nm == "Unknown" else (0, 200, 80)
            cv2.rectangle(frame, (left, top), (right, bottom), col, 2)
            cv2.rectangle(frame, (left, bottom - 36), (right, bottom), col, cv2.FILLED)
            cv2.putText(
                frame,
                nm,
                (left + 6, bottom - 8),
                cv2.FONT_HERSHEY_DUPLEX,
                0.85,
                (255, 255, 255),
                1,
                cv2.LINE_AA,
            )

        stable = (
            len(confirmations) == confirmations.maxlen
            and all(x is not None for x in confirmations)
            and len({(x[0], x[1]) for x in confirmations}) == 1
        )
        if stable:
            who, rk = confirmations[0]
            kind = "teacher" if rk == "teacher" else "student"
            print(f"PERSON:{who}")
            print(f"PROFILE:{kind}:{_slug_key(who)}")
            print(f"MODE:{'RADIAL' if kind == 'teacher' else 'GAME'}")
            cap.release()
            cv2.destroyAllWindows()
            return 0

        cv2.putText(
            frame,
            last_hud,
            (10, 32),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (0, 220, 255),
            2,
            cv2.LINE_AA,
        )
        cv2.imshow(WINDOW_TITLE, frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            print("MODE:CANCEL")
            cap.release()
            cv2.destroyAllWindows()
            return 0

    cap.release()
    cv2.destroyAllWindows()
    print("MODE:ERROR")
    return 1


# --- DeepFace fallback ---


def _df_embed_file(path: pathlib.Path, DeepFace) -> Optional[np.ndarray]:
    try:
        reps = DeepFace.represent(
            img_path=str(path),
            model_name="Facenet",
            detector_backend="opencv",
            enforce_detection=False,
        )
    except Exception as ex:
        print(f"No face in enrolment image {path.name}: {ex}", file=sys.stderr)
        return None
    if isinstance(reps, list) and reps:
        return np.asarray(reps[0]["embedding"], dtype=np.float64)
    return None


def _df_embed_roi(bgr_roi: np.ndarray, DeepFace) -> Optional[np.ndarray]:
    if bgr_roi is None or bgr_roi.size < 3200:
        return None
    rgb = cv2.cvtColor(bgr_roi, cv2.COLOR_BGR2RGB)
    try:
        reps = DeepFace.represent(
            img_path=rgb,
            model_name="Facenet",
            detector_backend="skip",
            enforce_detection=False,
        )
    except Exception:
        return None
    if isinstance(reps, list) and reps:
        return np.asarray(reps[0]["embedding"], dtype=np.float64)
    return None


def _cos_dist(a: np.ndarray, b: np.ndarray) -> float:
    af = np.ravel(a).astype(np.float64)
    bf = np.ravel(b).astype(np.float64)
    d = np.linalg.norm(af) * np.linalg.norm(bf)
    if d < 1e-9:
        return 1.0
    return 1.0 - float(np.dot(af, bf) / d)


def _enhance_roi(face_bgr: np.ndarray) -> np.ndarray:
    if face_bgr is None or face_bgr.size == 0:
        return face_bgr
    ycrcb = cv2.cvtColor(face_bgr, cv2.COLOR_BGR2YCrCb)
    y, cr, cb = cv2.split(ycrcb)
    clahe = cv2.createCLAHE(clipLimit=2.2, tileGridSize=(8, 8))
    y2 = clahe.apply(y)
    return cv2.cvtColor(cv2.merge((y2, cr, cb)), cv2.COLOR_YCrCb2BGR)


def _run_deepface_login(imgs, roles) -> int:
    try:
        from deepface import DeepFace  # noqa: WPS433
    except ImportError:
        print(
            "Missing deepface (and notebook path unavailable). Install: pip install deepface tensorflow tf-keras",
            file=sys.stderr,
        )
        print("MODE:ERROR")
        return 1

    vecs: List[np.ndarray] = []
    labels: List[str] = []
    rfs: List[str] = []
    for path in imgs:
        emb = _df_embed_file(path, DeepFace)
        if emb is None:
            continue
        basename = path.name.lower()
        role, display = roles.get(basename, ("student", path.stem))
        role = role if role == "teacher" else "student"
        vecs.append(emb)
        labels.append(display)
        rfs.append(role)

    if not vecs:
        print("Could not derive face embeddings from python/people/.", file=sys.stderr)
        print("MODE:ERROR")
        return 1

    print(f"DeepFace: {len(vecs)} embedding(s) loaded (first model load may take a minute once).", file=sys.stderr)

    cap = _open_camera()
    if cap is None:
        print("Could not open webcam.", file=sys.stderr)
        print("MODE:ERROR")
        return 1

    _open_preview_window()

    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    confirmations = collections.deque(maxlen=_CONFIRMS)

    while True:
        ok, frame = cap.read()
        if not ok or frame is None:
            break

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        rects = cascade.detectMultiScale(gray, scaleFactor=1.15, minNeighbors=5, minSize=(96, 96))
        hud = "No face detected"

        if len(rects):
            x, y, w, h = max(rects, key=lambda r: r[2] * r[3])
            pad = int(0.12 * max(w, h))
            x0 = max(0, x - pad)
            y0 = max(0, y - pad)
            x1 = min(frame.shape[1], x + w + pad)
            y1 = min(frame.shape[0], y + h + pad)
            roi = _enhance_roi(frame[y0:y1, x0:x1])
            emb = _df_embed_roi(roi, DeepFace) if roi is not None and roi.size else None

            cv2.rectangle(frame, (x0, y0), (x1, y1), (0, 165, 255), 2)

            if emb is None:
                confirmations.append(None)
                hud = "Analysing..."
            else:
                best_i, best_d = 0, 1e9
                for i, k in enumerate(vecs):
                    d = _cos_dist(emb, k)
                    if d < best_d:
                        best_d, best_i = d, i
                if best_d <= _DF_TOLERANCE:
                    nm, rk = labels[best_i], rfs[best_i]
                    hud = f"{nm} ({best_d:.2f})"
                    confirmations.append((nm, rk))
                else:
                    confirmations.append(None)
                    hud = f"? ({best_d:.2f})"
        else:
            confirmations.append(None)

        stable = (
            len(confirmations) == confirmations.maxlen
            and all(x is not None for x in confirmations)
            and len(set(confirmations)) == 1
        )
        if stable:
            who, rk = confirmations[0]
            kind = "teacher" if rk == "teacher" else "student"
            print(f"PERSON:{who}")
            print(f"PROFILE:{kind}:{_slug_key(who)}")
            print(f"MODE:{'RADIAL' if kind == 'teacher' else 'GAME'}")
            cap.release()
            cv2.destroyAllWindows()
            return 0

        cv2.putText(frame, hud, (10, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.72, (0, 230, 100), 2, cv2.LINE_AA)
        cv2.imshow(WINDOW_TITLE, frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            print("MODE:CANCEL")
            cap.release()
            cv2.destroyAllWindows()
            return 0

    cap.release()
    cv2.destroyAllWindows()
    print("MODE:ERROR")
    return 1


def main() -> int:
    imgs = _discover_images(_PEOPLE_DIR)
    if not imgs:
        print(
            "No images in python/people/ (.jpg / .png). See roles.tsv.example.",
            file=sys.stderr,
        )
        print("MODE:ERROR")
        return 1

    roles_map = _load_roles(_ROLES_PATH)

    # Prefer notebook stack
    try:
        import face_recognition as fr
    except ImportError:
        fr = None  # type: ignore

    if fr is not None:
        known_encodings, known_labels, known_roles = _load_enrollment_face_recognition(
            fr, imgs, roles_map
        )
        if known_encodings:
            cap = _open_camera()
            if cap is None:
                print("Could not open webcam.", file=sys.stderr)
                print("MODE:ERROR")
                return 1
            return _run_face_recognition_loop(
                fr, cap, known_encodings, known_labels, known_roles
            )
        print(
            "face_recognition installed but no usable encodings; trying DeepFace.",
            file=sys.stderr,
        )

    return _run_deepface_login(imgs, roles_map)


if __name__ == "__main__":
    raise SystemExit(main())
