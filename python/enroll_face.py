"""
enroll_face.py — capture a new face and add it to the people/ enrolment.

Launched on demand by the teacher dashboard's "+" (Add Student) tile.
Opens the webcam, shows a live preview, and once a face stays still in
view for a few frames, saves a cropped snapshot to:

    python/people/Student_NN.jpg

and appends a row to python/people/roles.tsv as a student.

Detection backend: mediapipe Tasks API + face_landmarker.task. The legacy
mp.solutions.* shim was dropped in mediapipe ≥ 0.10, so we use the modern
detector with the same face_landmarker.task that gesture_server already
downloads to the project root. From the 478 landmarks we derive a simple
bounding box for the snapshot crop. No face_recognition / deepface deps.

Exit codes / stdout contract for the C# launcher:
    0   success    → prints one line "ENROLLED:<filename>:<display_name>"
    1   cancelled  → user pressed Q / closed window
    2   error      → mediapipe / camera dependency unavailable
"""

import math
import os
import pathlib
import sys
import urllib.request

import cv2
import numpy as np

_SCRIPT_DIR  = pathlib.Path(__file__).resolve().parent
_PROJECT_DIR = _SCRIPT_DIR.parent
_PEOPLE_DIR  = _SCRIPT_DIR / "people"
_ROLES_PATH  = _PEOPLE_DIR / "roles.tsv"

# Reuse the model gesture_server already downloads — same Tasks API, no
# extra file management. URL is the official mediapipe-models bucket so
# we can also auto-download if the user wiped the project root.
_FACE_MODEL_NAME = "face_landmarker.task"
_FACE_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/"
    "face_landmarker/face_landmarker/float16/1/face_landmarker.task"
)

# A face must stay still — bbox-center motion under this fraction of the
# frame between two ticks counts as "stationary".
_MAX_MOTION_NORMALISED = 0.03
# Consecutive stable frames before we commit the snapshot. ~0.7s at 30fps,
# longer than the login flow on purpose so the teacher has time to abort
# if they realise they're enrolling the wrong person.
_REQUIRED_STABLE = 20

_WINDOW_TITLE = "Enroll new student - Q to cancel"


def _resolve_face_model() -> str:
    """Find face_landmarker.task next to gesture_server's other models, or
    download it on first run."""
    for base in (_PROJECT_DIR, _SCRIPT_DIR):
        p = base / _FACE_MODEL_NAME
        if p.is_file():
            return str(p)
    dst = _PROJECT_DIR / _FACE_MODEL_NAME
    print(f"Downloading {_FACE_MODEL_NAME}...", file=sys.stderr, flush=True)
    urllib.request.urlretrieve(_FACE_MODEL_URL, str(dst))
    return str(dst)


def _next_student_number() -> int:
    n = 0
    if not _PEOPLE_DIR.exists():
        return 1
    for path in _PEOPLE_DIR.glob("Student_*.jpg"):
        try:
            n = max(n, int(path.stem.split("_", 1)[1]))
        except (IndexError, ValueError):
            pass
    return n + 1


def _open_camera():
    """MSMF first so Win11 multi-app camera sharing works while other Python
    processes (gesture_server, yolo_tuio_bridge) hold the same device."""
    if sys.platform == "win32":
        backends = [
            (cv2.CAP_MSMF, "MSMF"),
            (cv2.CAP_DSHOW, "DSHOW"),
            (cv2.CAP_ANY, "ANY"),
        ]
    else:
        backends = [(cv2.CAP_ANY, "ANY")]

    for backend, _ in backends:
        cap = cv2.VideoCapture(0, backend)
        if cap.isOpened():
            ok, _ = cap.read()
            if ok:
                return cap
            cap.release()
    return None


def main() -> int:
    try:
        import mediapipe as mp
        from mediapipe.tasks.python import BaseOptions
        from mediapipe.tasks.python import vision
    except ImportError as ex:
        print(f"mediapipe import failed: {ex}", file=sys.stderr)
        return 2

    _PEOPLE_DIR.mkdir(parents=True, exist_ok=True)

    try:
        model_path = _resolve_face_model()
    except Exception as ex:
        print(f"Could not locate/download {_FACE_MODEL_NAME}: {ex}", file=sys.stderr)
        return 2

    cap = _open_camera()
    if cap is None:
        print("Could not open webcam (camera 0)", file=sys.stderr)
        return 2

    # FaceLandmarker gives us 478 landmarks per face. We don't care about the
    # landmarks themselves — we just take their min/max to compute a bbox for
    # the snapshot crop. One face is enough; pick the first detection.
    options = vision.FaceLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.IMAGE,
        num_faces=1,
    )
    detector = vision.FaceLandmarker.create_from_options(options)

    n = _next_student_number()
    display_name = f"Student {n}"
    out_name = f"Student_{n:02d}.jpg"
    out_path = _PEOPLE_DIR / out_name

    stable_count = 0
    last_center = None
    latest_box_px = None
    latest_frame_for_save = None

    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_img = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            result = detector.detect(mp_img)

            picked_center = None
            h, w = frame.shape[:2]

            if result.face_landmarks:
                # Single-face mode (num_faces=1) — first entry is the face.
                lms = result.face_landmarks[0]
                xs = [lm.x for lm in lms]
                ys = [lm.y for lm in lms]
                xmin, xmax = max(0.0, min(xs)), min(1.0, max(xs))
                ymin, ymax = max(0.0, min(ys)), min(1.0, max(ys))
                cx = (xmin + xmax) / 2.0
                cy = (ymin + ymax) / 2.0
                picked_center = (cx, cy)

                x0 = max(0, int(xmin * w))
                y0 = max(0, int(ymin * h))
                x1 = min(w, int(xmax * w))
                y1 = min(h, int(ymax * h))
                latest_box_px = (x0, y0, x1, y1)
                latest_frame_for_save = frame.copy()
            else:
                latest_box_px = None

            stable_now = False
            if picked_center is not None:
                if last_center is not None:
                    dx = picked_center[0] - last_center[0]
                    dy = picked_center[1] - last_center[1]
                    motion = math.hypot(dx, dy)
                    stable_now = motion < _MAX_MOTION_NORMALISED
                else:
                    stable_now = True   # first sighting after no-face
                last_center = picked_center
            else:
                last_center = None

            if stable_now:
                stable_count += 1
            else:
                stable_count = 0

            # ── Preview overlay ──────────────────────────────────────────
            if latest_box_px is not None:
                x0, y0, x1, y1 = latest_box_px
                colour = (0, 255, 0) if stable_count > 0 else (0, 165, 255)
                cv2.rectangle(frame, (x0, y0), (x1, y1), colour, 3)

            if stable_count > 0:
                hud = f"Hold still: {stable_count}/{_REQUIRED_STABLE}"
                hud_col = (0, 255, 0)
            elif latest_box_px is not None:
                hud = "Detecting motion - hold still"
                hud_col = (0, 165, 255)
            else:
                hud = "Position the new student's face in view"
                hud_col = (255, 255, 255)
            cv2.putText(frame, hud, (12, 36),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.9, hud_col, 2)
            cv2.putText(frame, "Q = cancel", (12, frame.shape[0] - 14),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, (220, 220, 220), 1)

            cv2.imshow(_WINDOW_TITLE, frame)

            # ── Commit when stability target reached ─────────────────────
            if (stable_count >= _REQUIRED_STABLE
                    and latest_box_px is not None
                    and latest_frame_for_save is not None):
                x0, y0, x1, y1 = latest_box_px
                # ~33% margin so we keep some hair / chin context.
                mh = max(8, (y1 - y0) // 3)
                mw = max(8, (x1 - x0) // 3)
                cy0 = max(0, y0 - mh)
                cy1 = min(latest_frame_for_save.shape[0], y1 + mh)
                cx0 = max(0, x0 - mw)
                cx1 = min(latest_frame_for_save.shape[1], x1 + mw)
                crop = latest_frame_for_save[cy0:cy1, cx0:cx1]
                cv2.imwrite(str(out_path), crop)

                # Append to roles.tsv (create with a trailing newline if missing).
                needs_leading_nl = (
                    _ROLES_PATH.exists()
                    and _ROLES_PATH.stat().st_size > 0
                    and not _ROLES_PATH.read_bytes().endswith(b"\n")
                )
                with open(_ROLES_PATH, "a", encoding="utf-8") as f:
                    if needs_leading_nl:
                        f.write("\n")
                    f.write(f"{out_name}\tstudent\t{display_name}\n")

                print(f"ENROLLED:{out_name}:{display_name}")
                return 0

            k = cv2.waitKey(1) & 0xFF
            if k in (ord("q"), 27):
                return 1
    finally:
        cap.release()
        cv2.destroyAllWindows()
        try:
            detector.close()
        except Exception:
            pass

    return 1


if __name__ == "__main__":
    # Wrap everything so an uncaught exception exits with 2 (real failure)
    # instead of Python's default 1 (which the dashboard treats as "user
    # cancelled"). Also dump the full traceback to stderr so the launcher
    # can surface the first line in the UI banner.
    import traceback
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except BaseException as ex:
        sys.stderr.write("enroll_face crashed: " + repr(ex) + "\n")
        traceback.print_exc(file=sys.stderr)
        sys.exit(2)
