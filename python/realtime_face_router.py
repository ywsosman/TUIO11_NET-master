import collections
import os
import statistics
import sys
import time

# Reduce TensorFlow log noise and oneDNN variance spam.
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")

import cv2
from deepface import DeepFace


def enhance_face_roi(face_bgr):
    # Improve low-light face quality before age inference.
    if face_bgr is None or face_bgr.size == 0:
        return face_bgr
    ycrcb = cv2.cvtColor(face_bgr, cv2.COLOR_BGR2YCrCb)
    y, cr, cb = cv2.split(ycrcb)
    clahe = cv2.createCLAHE(clipLimit=2.5, tileGridSize=(8, 8))
    y2 = clahe.apply(y)
    merged = cv2.merge((y2, cr, cb))
    enhanced = cv2.cvtColor(merged, cv2.COLOR_YCrCb2BGR)
    return enhanced


def open_camera():
    # Windows can fail on one backend but work on another.
    # Try common backends and several camera indexes.
    backends = [
        ("DSHOW", cv2.CAP_DSHOW),
        ("MSMF", cv2.CAP_MSMF),
        ("ANY", cv2.CAP_ANY),
    ]
    for name, backend in backends:
        for idx in range(0, 8):
            cap = cv2.VideoCapture(idx, backend)
            if not cap.isOpened():
                cap.release()
                continue
            ok, frame = cap.read()
            if ok and frame is not None and frame.size > 0:
                print(f"CAMERA: backend={name}, index={idx}")
                return cap
            cap.release()
    return None


def extract_age(result):
    if isinstance(result, list) and result:
        result = result[0]
    if isinstance(result, dict):
        age = result.get("age")
        if age is None:
            return None
        try:
            return float(age)
        except Exception:
            return None
    return None


def main():
    cap = open_camera()
    if cap is None:
        print("MODE:ERROR")
        return 1

    cascade = cv2.CascadeClassifier(
        cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    )

    ages = collections.deque(maxlen=20)
    labels = collections.deque(maxlen=20)
    mode = None
    last_analyze = 0.0
    analyze_every_sec = 0.8
    last_avg_age = None
    last_raw_age = None

    print("Camera started. Press Q to cancel.")
    while True:
        ok, frame = cap.read()
        if not ok:
            print("MODE:ERROR")
            break

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, scaleFactor=1.2, minNeighbors=5, minSize=(60, 60))

        now = time.time()
        should_analyze = len(faces) > 0 and (now - last_analyze >= analyze_every_sec)
        if should_analyze:
            # Analyze only the largest detected face every few seconds
            # to prevent repeated heavy model allocations.
            try:
                x, y, w, h = max(faces, key=lambda item: item[2] * item[3])
                pad = int(0.15 * max(w, h))
                x0 = max(0, x - pad)
                y0 = max(0, y - pad)
                x1 = min(frame.shape[1], x + w + pad)
                y1 = min(frame.shape[0], y + h + pad)
                face_roi = frame[y0:y1, x0:x1]
                if face_roi.size == 0:
                    last_analyze = now
                    continue

                face_roi = enhance_face_roi(face_roi)

                result = DeepFace.analyze(
                    face_roi,
                    actions=["age"],
                    detector_backend="retinaface",
                    enforce_detection=False,
                    silent=True,
                )
                age = extract_age(result)
                if age is not None:
                    ages.append(age)
                    last_raw_age = age
                    if 3.0 <= age <= 7.0:
                        labels.append("STUDENT")
                    elif age >= 20.0:
                        labels.append("TEACHER")
                    else:
                        labels.append("UNCERTAIN")
                last_analyze = now
            except Exception:
                pass

        avg_age = None
        if len(ages) >= 8:
            avg_age = statistics.median(ages)
            last_avg_age = avg_age
            spread = max(ages) - min(ages)
            student_votes = sum(1 for x in labels if x == "STUDENT")
            teacher_votes = sum(1 for x in labels if x == "TEACHER")
            total_votes = max(1, len(labels))
            student_ratio = student_votes / float(total_votes)
            teacher_ratio = teacher_votes / float(total_votes)

            # Only route when the age signal is stable and dominant.
            if spread <= 7.0:
                if student_ratio >= 0.65 and 3.0 <= avg_age <= 7.0:
                    mode = "GAME"
                elif teacher_ratio >= 0.65 and avg_age >= 20.0:
                    mode = "RADIAL"

        for (x, y, w, h) in faces:
            cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 220, 0), 2)

        if avg_age is None:
            text = "Age: Detecting..."
        else:
            text = f"Age(avg): {avg_age:.1f}"
        cv2.putText(frame, text, (20, 35), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
        cv2.putText(frame, "Student: 3-7 | Teacher: 20+ | Q => Cancel", (20, 70), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 0), 2)
        cv2.putText(frame, "Uncertain ages won't auto-open", (20, 100), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (180, 255, 180), 2)

        cv2.imshow("Real-time Face Identification", frame)
        key = cv2.waitKey(1) & 0xFF
        if key == ord("q"):
            mode = "CANCEL"
            break
        if mode in ("GAME", "RADIAL"):
            break

    cap.release()
    cv2.destroyAllWindows()

    if mode in ("GAME", "RADIAL"):
        if last_avg_age is not None:
            print("AGE:{0:.1f}".format(last_avg_age))
        elif last_raw_age is not None:
            print("AGE:{0:.1f}".format(last_raw_age))
        else:
            print("AGE:unknown")
        print("MODE:" + mode)
        return 0
    if mode == "CANCEL":
        print("MODE:CANCEL")
        return 0

    print("MODE:ERROR")
    return 1


if __name__ == "__main__":
    sys.exit(main())
