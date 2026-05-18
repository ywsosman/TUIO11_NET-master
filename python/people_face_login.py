"""
Enrolled face login matching python/face.ipynb:
  * Prefer face_recognition + dlib (HOG face_locations, small resize, sparse recognition)
  * Else DeepFace with OpenFace (lighter than Facenet) and half-rate neural embedding calls

Reference photos: python/people/*.jpg|png...
Roles: python/people/roles.tsv (copy from roles.tsv.example)

Stdout for TuioDemoApp.FaceLogin:
  PERSON:<name>
  PROFILE:teacher:<slug> or PROFILE:student:<slug>
  MODE:GAME|RADIAL
"""
from __future__ import annotations

import argparse
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

# When launched from TuioDemoApp ( --result-file ), OpenCV GUI bretks if stdout is
# redirected to a pipe; the host writes protocol lines here instead of parsing stdout.
_RESULT_FILE: Optional[pathlib.Path] = None


def _emit_result(lines: List[str]) -> None:
    """Print TuioDemo protocol lines to stdout and/or the optional result file."""
    text = "\n".join(lines) + "\n"
    if _RESULT_FILE is not None:
        try:
            _RESULT_FILE.parent.mkdir(parents=True, exist_ok=True)
            _RESULT_FILE.write_text(text, encoding="utf-8")
        except Exception:
            pass
    else:
        sys.stdout.write(text)
        sys.stdout.flush()

WINDOW_TITLE = "TUIO Face Login - Press Q"
_CONFIRMS = 12              # consecutive agreeing frames before commit (~0.4s @ 30fps)
_MIN_CONFIDENCE = 80.0      # face_recognition / DeepFace: (1 - dist)*100
_FR_TOLERANCE = 0.20
_DF_TOLERANCE = 0.20

# Speed knobs (face_recognition / live loop)
_FR_RESIZE = 0.2            # smaller = faster dlib work (was 0.25 in face.ipynb)
_FR_NUM_JITTERS = 3         # encoding re-samples; higher = more robust, slower
_FR_UPSAMPLE = 0            # face_locations upsampling passes (default 1)
# Run face detect+encode on 1 of every N display frames; preview still updates every frame
_FR_RECOGNIZE_EVERY_N = 2

# DeepFace fallback: lighter model + fewer neural calls per second
_DF_MODEL = "OpenFace"  # smaller/faster than Facenet for many GPUs/CPUs
_DEEPFACE_EVERY_N = 2  # run represent() on 1 of N frames; reuse last embedding between

# Capture (lower latency + less pixels to process)
_CAP_WIDTH = 640
_CAP_HEIGHT = 480


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


def _configure_capture(cap) -> None:
    """Smaller frames + buffer size 1 = less lag on many Windows webcams."""
    for prop, val in (
        (cv2.CAP_PROP_BUFFERSIZE, 1),
        (cv2.CAP_PROP_FRAME_WIDTH, _CAP_WIDTH),
        (cv2.CAP_PROP_FRAME_HEIGHT, _CAP_HEIGHT),
    ):
        try:
            cap.set(prop, val)
        except Exception:
            pass
    try:
        cap.set(cv2.CAP_PROP_FPS, 30)
    except Exception:
        pass


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
                _configure_capture(cap)
                print(f"CAMERA: backend={name}, index={idx}", file=sys.stderr)
                return cap
            cap.release()
    return None


def _open_preview_window() -> None:
    """Reserve the OpenCV window (title must stay ASCII-only for Windows OpenCV ANSI title bar)."""
    cv2.namedWindow(WINDOW_TITLE, cv2.WINDOW_NORMAL)


def _splash_loading(
    line1: str = "Loading face enrollment...",
    line2: str = "This may take a moment (first run is slower).",
) -> None:
    """Show a window immediately so the user sees something while dlib/DeepFace load."""
    try:
        _open_preview_window()
        img = np.zeros((220, 720, 3), dtype=np.uint8)
        cv2.putText(
            img,
            line1,
            (28, 98),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.72,
            (245, 245, 245),
            2,
            cv2.LINE_AA,
        )
        cv2.putText(
            img,
            line2,
            (28, 145),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.52,
            (160, 200, 230),
            1,
            cv2.LINE_AA,
        )
        cv2.imshow(WINDOW_TITLE, img)
        cv2.waitKey(1)
    except Exception:
        pass


# --- face_recognition path (face.ipynb) ---


def _load_enrollment_face_recognition(fr, imgs, roles):
    encs: List = []
    labels: List[str] = []
    rfs: List[str] = []
    for path in imgs:
        rgb = fr.load_image_file(str(path))
        fe = fr.face_encodings(rgb, num_jitters=_FR_NUM_JITTERS)
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
    """Like face.ipynb: small resize; run dlib on every Nth frame; full FPS preview."""
    confirmations = collections.deque(maxlen=_CONFIRMS)
    face_locations = []
    face_names: List[Tuple[str, str]] = []
    last_hud = "Align your face with the camera"
    last_confidence = 0.0
    frame_n = 0
    inv_scale = 1.0 / _FR_RESIZE

    print("Using face_recognition (face.ipynb style).", file=sys.stderr)

    _open_preview_window()

    while True:
        ok, frame = cap.read()
        if not ok or frame is None:
            print("Lost webcam feed.", file=sys.stderr)
            break

        frame_n += 1
        do_recognize = frame_n % _FR_RECOGNIZE_EVERY_N == 0

        if do_recognize:
            small_frame = cv2.resize(frame, (0, 0), fx=_FR_RESIZE, fy=_FR_RESIZE)
            rgb_small = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)
            face_locations = fr.face_locations(
                rgb_small,
                number_of_times_to_upsample=_FR_UPSAMPLE,
                model="hog",
            )
            encs = fr.face_encodings(
                rgb_small,
                face_locations,
                num_jitters=_FR_NUM_JITTERS,
            )
            face_names = []      # List[Tuple[str, str, float]]  (name, role, confidence)
            hud = "No face detected"
            for face_encoding in encs:
                dists = fr.face_distance(known_encodings, face_encoding)
                best = int(np.argmin(dists))
                confidence = max(0.0, (1.0 - float(dists[best])) * 100.0)
                if confidence >= _MIN_CONFIDENCE:
                    face_names.append((known_labels[best], known_roles[best], confidence))
                else:
                    face_names.append(("Unknown", "student", confidence))

            if not encs:
                confirmations.append(None)
            else:
                areas = [(b - t) * (r - l) for (t, r, b, l) in face_locations]
                pick = int(np.argmax(areas)) if areas else 0
                name, rk, conf = face_names[pick] if pick < len(face_names) else ("Unknown", "student", 0.0)
                if name != "Unknown":
                    last_confidence = conf
                    hud = f"{name} ({last_confidence:.0f}%)"
                    confirmations.append((name, rk))
                else:
                    hud = f"Low confidence ({conf:.0f}%) - move closer or improve light"
                    confirmations.append(None)
            last_hud = hud

        if len(face_names) != len(face_locations):
            face_names = [("Unknown", "student", 0.0)] * len(face_locations)

        for (top, right, bottom, left), (nm, _rk, _conf) in zip(face_locations, face_names):
            ti = int(round(top * inv_scale))
            ri = int(round(right * inv_scale))
            bi = int(round(bottom * inv_scale))
            le = int(round(left * inv_scale))
            col = (0, 0, 255) if nm == "Unknown" else (0, 200, 80)
            cv2.rectangle(frame, (le, ti), (ri, bi), col, 2)
            cv2.rectangle(frame, (le, bi - 36), (ri, bi), col, cv2.FILLED)
            cv2.putText(
                frame,
                nm,
                (le + 6, bi - 8),
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
            _emit_result(
                [
                    f"PERSON:{who}",
                    f"PROFILE:{kind}:{_slug_key(who)}",
                    f"MODE:{'RADIAL' if kind == 'teacher' else 'GAME'}",
                    f"CONFIDENCE:{last_confidence:.1f}",
                ]
            )
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
            _emit_result(["MODE:CANCEL"])
            cap.release()
            cv2.destroyAllWindows()
            return 0

    cap.release()
    cv2.destroyAllWindows()
    _emit_result(["MODE:ERROR", "ERR:Lost webcam feed before a match."])
    return 1


# --- DeepFace fallback ---


def _df_embed_file(path: pathlib.Path, DeepFace) -> Optional[np.ndarray]:
    try:
        reps = DeepFace.represent(
            img_path=str(path),
            model_name=_DF_MODEL,
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
            model_name=_DF_MODEL,
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


def _run_deepface_login(imgs, roles) -> int:
    try:
        from deepface import DeepFace  # noqa: WPS433
    except ImportError:
        print(
            "Missing deepface (and notebook path unavailable). Install: pip install deepface tensorflow tf-keras",
            file=sys.stderr,
        )
        _emit_result(
            [
                "MODE:ERROR",
                "ERR:Missing deepface. Run: pip install deepface tensorflow tf-keras",
            ]
        )
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
        _emit_result(
            ["MODE:ERROR", "ERR:Could not derive embeddings from python/people/ photos."]
        )
        return 1

    print(
        f"DeepFace model={_DF_MODEL}, {len(vecs)} embedding(s). "
        "First weights download can take a minute once.",
        file=sys.stderr,
    )

    cap = _open_camera()
    if cap is None:
        print("Could not open webcam.", file=sys.stderr)
        _emit_result(["MODE:ERROR", "ERR:Could not open webcam."])
        return 1

    _open_preview_window()

    cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    confirmations = collections.deque(maxlen=_CONFIRMS)
    frame_n = 0
    last_emb: Optional[np.ndarray] = None
    last_confidence = 0.0

    while True:
        ok, frame = cap.read()
        if not ok or frame is None:
            break

        frame_n += 1
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        h0, w0 = gray.shape[:2]
        gray_small = cv2.resize(gray, (w0 // 2, h0 // 2), interpolation=cv2.INTER_AREA)
        rects = cascade.detectMultiScale(
            gray_small,
            scaleFactor=1.2,
            minNeighbors=4,
            minSize=(40, 40),
        )
        rects = np.array([(x * 2, y * 2, w * 2, h * 2) for (x, y, w, h) in rects], dtype=np.int32)

        hud = "No face detected"

        if len(rects):
            x, y, w, h = max(rects, key=lambda r: int(r[2]) * int(r[3]))
            pad = int(0.12 * max(w, h))
            x0 = max(0, x - pad)
            y0 = max(0, y - pad)
            x1 = min(frame.shape[1], x + w + pad)
            y1 = min(frame.shape[0], y + h + pad)
            roi = frame[y0:y1, x0:x1]

            need_embed = (
                last_emb is None
                or (frame_n % _DEEPFACE_EVERY_N == 0)
            )
            if need_embed:
                emb = _df_embed_roi(roi, DeepFace) if roi is not None and roi.size else None
                last_emb = emb
            else:
                emb = last_emb

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
                confidence = max(0.0, (1.0 - best_d) * 100.0)
                if confidence >= _MIN_CONFIDENCE:
                    nm, rk = labels[best_i], rfs[best_i]
                    last_confidence = confidence
                    hud = f"{nm} ({confidence:.0f}%)"
                    confirmations.append((nm, rk))
                else:
                    confirmations.append(None)
                    hud = f"Low confidence ({confidence:.0f}%) - adjust position"
        else:
            last_emb = None
            confirmations.append(None)

        stable = (
            len(confirmations) == confirmations.maxlen
            and all(x is not None for x in confirmations)
            and len(set(confirmations)) == 1
        )
        if stable:
            who, rk = confirmations[0]
            kind = "teacher" if rk == "teacher" else "student"
            _emit_result(
                [
                    f"PERSON:{who}",
                    f"PROFILE:{kind}:{_slug_key(who)}",
                    f"MODE:{'RADIAL' if kind == 'teacher' else 'GAME'}",
                    f"CONFIDENCE:{last_confidence:.1f}",
                ]
            )
            cap.release()
            cv2.destroyAllWindows()
            return 0

        cv2.putText(frame, hud, (10, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.72, (0, 230, 100), 2, cv2.LINE_AA)
        cv2.imshow(WINDOW_TITLE, frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            _emit_result(["MODE:CANCEL"])
            cap.release()
            cv2.destroyAllWindows()
            return 0

    cap.release()
    cv2.destroyAllWindows()
    _emit_result(["MODE:ERROR", "ERR:Lost webcam feed before a match."])
    return 1


def main() -> int:
    global _RESULT_FILE

    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument(
        "--result-file",
        metavar="PATH",
        help="Write PERSON/PROFILE/MODE lines here for TuioDemo (avoids redirecting stdout).",
    )
    args, _unknown = parser.parse_known_args()
    _RESULT_FILE = pathlib.Path(args.result_file).resolve() if args.result_file else None

    imgs = _discover_images(_PEOPLE_DIR)
    if not imgs:
        print(
            "No images in python/people/ (.jpg / .png). See roles.tsv.example.",
            file=sys.stderr,
        )
        _emit_result(
            [
                "MODE:ERROR",
                "ERR:No enrollment images in python/people/. Add .jpg/.png and roles.tsv.",
            ]
        )
        return 1

    roles_map = _load_roles(_ROLES_PATH)
    _splash_loading()

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
                _emit_result(["MODE:ERROR", "ERR:Could not open webcam."])
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
