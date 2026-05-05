"""
face_emotion.py  –  FaceEmotionAnalyzer
Uses hsemotion-onnx (ONNX model, no TensorFlow needed, works on Python 3.14+)
for facial expression detection.

Requirements:
    pip install hsemotion-onnx onnxruntime opencv-python

Confidence threshold : 70%
Runs every ANALYZE_EVERY frames to keep CPU usage low.
Smooths output with a rolling majority vote over the last VOTE_WINDOW predictions.
"""

import time
from collections import deque, Counter

import cv2
import numpy as np

# ── tuneable constants ────────────────────────────────────────────────────────
CONFIDENCE_THRESHOLD = 0.70   # ignore predictions below this
ANALYZE_EVERY        = 5      # run model once every N frames
VOTE_WINDOW          = 3      # majority-vote window for smoothing

# Emotion → game difficulty mapping
DIFFICULTY_MAP = {
    "sad":      "easier",
    "angry":    "easier",
    "fear":     "easier",
    "surprise": "easier",
    "disgust":  "easier",
    "neutral":  "normal",
    "happy":    "harder",
}

# Colour used in the debug window per emotion  (BGR)
EMOTION_COLORS = {
    "sad":      (200, 100,  50),
    "angry":    (  0,  50, 220),
    "fear":     (  0, 180, 200),
    "surprise": (  0, 200, 255),
    "disgust":  ( 50,   0, 180),
    "neutral":  (180, 180, 180),
    "happy":    ( 50, 220,  50),
}

# hsemotion class names (model output order matches this list)
_HSEMOTION_LABELS = [
    "angry", "disgust", "fear", "happy",
    "neutral", "sad", "surprise",
]


class FaceEmotionAnalyzer:
    """
    Detects facial expressions using hsemotion-onnx (works on Python 3.14).

    Usage
    -----
    analyzer = FaceEmotionAnalyzer()
    result   = analyzer.analyze(bgr_frame)
    # → {"label": "sad", "confidence": 0.82, "difficulty_hint": "easier"}
    # → None  if no face found or confidence below threshold
    """

    def __init__(self):
        try:
            import urllib.request  # Fix for hsemotion-onnx urllib bug
            from hsemotion_onnx.facial_emotions import HSEmotionRecognizer
        except ImportError:
            raise ImportError(
                "hsemotion-onnx not found.  Install with:\n"
                "  pip install hsemotion-onnx onnxruntime"
            )

        # HSEmotionRecognizer downloads the ONNX model on first run (~3 MB)
        self._recognizer   = HSEmotionRecognizer(model_name="enet_b0_8_va_mtl")
        self._face_cascade = cv2.CascadeClassifier(
            cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
        )

        self._frame_count  = 0
        self._vote_buffer  = deque(maxlen=VOTE_WINDOW)
        self._last_result  = None   # last accepted result
        self._last_bbox    = None   # (x, y, w, h) of detected face

    # ── public API ────────────────────────────────────────────────────────────

    def analyze(self, bgr_frame: np.ndarray):
        """
        Call once per camera frame.

        Parameters
        ----------
        bgr_frame : numpy ndarray in BGR format (from cv2.VideoCapture)

        Returns
        -------
        dict or None
            {"label": str, "confidence": float, "difficulty_hint": str}
        """
        self._frame_count += 1
        if self._frame_count % ANALYZE_EVERY != 0:
            return self._last_result   # reuse last result between analysis frames

        result = self._run_model(bgr_frame)
        if result is not None:
            self._last_result = result
        return self._last_result

    def last_bbox(self):
        """Return the (x, y, w, h) of the last detected face, or None."""
        return self._last_bbox

    # ── internals ─────────────────────────────────────────────────────────────

    def _run_model(self, bgr_frame: np.ndarray):
        gray = cv2.cvtColor(bgr_frame, cv2.COLOR_BGR2GRAY)
        faces = self._face_cascade.detectMultiScale(
            gray, scaleFactor=1.1, minNeighbors=5, minSize=(60, 60)
        )

        if len(faces) == 0:
            self._last_bbox = None
            return None

        # Use the largest face
        x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
        self._last_bbox = (x, y, w, h)

        # Crop and run ONNX inference
        face_crop = bgr_frame[y : y + h, x : x + w]
        if face_crop.size == 0:
            return None

        try:
            emotion_label, scores = self._recognizer.predict_emotions(
                face_crop, logits=False
            )
        except Exception as e:
            print(f"[FaceEmotion] Inference error: {e}", flush=True)
            return None

        # `emotion_label` is a string like "happy"
        # `scores` is a list/array of probabilities for each class
        label_lower = emotion_label.lower() if emotion_label else ""

        if hasattr(scores, "__len__") and len(scores) > 0:
            # Find confidence for the dominant class
            scores_arr = list(scores)
            max_score  = max(scores_arr)
            confidence = float(max_score)
        else:
            confidence = 1.0   # model didn't return scores – trust the label

        if confidence < CONFIDENCE_THRESHOLD:
            return None

        # Smooth with majority vote
        self._vote_buffer.append(label_lower)
        smoothed = Counter(self._vote_buffer).most_common(1)[0][0]
        difficulty = DIFFICULTY_MAP.get(smoothed, "normal")

        return {
            "label":           smoothed,
            "confidence":      round(confidence, 3),
            "difficulty_hint": difficulty,
        }


# ── debug overlay drawing ─────────────────────────────────────────────────────

def draw_debug_overlay(frame: np.ndarray, result, bbox) -> np.ndarray:
    """
    Draw face bounding box, emotion label, confidence bar and difficulty hint
    onto *frame* (in-place).  Returns the frame for convenience.

    Parameters
    ----------
    frame  : BGR frame to draw on
    result : dict from FaceEmotionAnalyzer.analyze() or None
    bbox   : (x,y,w,h) tuple from FaceEmotionAnalyzer.last_bbox() or None
    """
    h, w = frame.shape[:2]

    # --- bounding box ---------------------------------------------------------
    if bbox is not None:
        x, y, bw, bh = bbox
        label  = result["label"] if result else "???"
        color  = EMOTION_COLORS.get(label, (200, 200, 200))
        cv2.rectangle(frame, (x, y), (x + bw, y + bh), color, 2)

    # --- emotion label + confidence bar --------------------------------------
    if result:
        label  = result["label"]
        conf   = result["confidence"]
        hint   = result["difficulty_hint"]
        color  = EMOTION_COLORS.get(label, (200, 200, 200))

        cv2.putText(frame, f"Emotion: {label.upper()}",
                    (10, 35), cv2.FONT_HERSHEY_SIMPLEX, 0.9, color, 2)

        bar_max = 300
        bar_w   = int(conf * bar_max)
        cv2.rectangle(frame, (10, 48), (10 + bar_max, 68), (60, 60, 60), -1)
        cv2.rectangle(frame, (10, 48), (10 + bar_w,   68), color,        -1)
        cv2.putText(frame, f"{int(conf*100)}%",
                    (10 + bar_max + 8, 65),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (220, 220, 220), 1)

        hint_color = {
            "easier": ( 50, 220,  50),
            "harder": ( 50, 100, 220),
            "normal": (180, 180, 180),
        }.get(hint, (200, 200, 200))
        cv2.putText(frame, f"Hint: {hint}",
                    (10, 95), cv2.FONT_HERSHEY_SIMPLEX, 0.75, hint_color, 2)
    else:
        cv2.putText(frame, "No face / low confidence",
                    (10, 35), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (80, 80, 200), 2)

    # --- footer ---------------------------------------------------------------
    cv2.putText(frame, "Face Emotion Debug  |  Press Q to quit",
                (10, h - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (160, 160, 160), 1)

    return frame
