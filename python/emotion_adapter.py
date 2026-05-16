"""
emotion_adapter.py — cross-session emotion analysis → adaptive UI hints.
Reads sessions.json emotion counts, writes adaptive_hints_{face_id}.json.
Run on session end from gesture_server.py.
"""

import json
import os
import re

_DATA_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Data")
_SESSIONS_FILE = os.path.join(_DATA_DIR, "sessions.json")


def _sanitize_face_id(face_id: str) -> str:
    """Windows forbids ':' in filenames; map every non-alnum char to '_'."""
    return re.sub(r"[^A-Za-z0-9]+", "_", (face_id or "user")).strip("_") or "user"


def load_emotion_history(face_id, n=5):
    if not os.path.exists(_SESSIONS_FILE):
        return []
    try:
        with open(_SESSIONS_FILE, "r") as f:
            data = json.load(f)
        sessions = [s for s in data.get("sessions", []) if s.get("face_id") == face_id]
        return [s.get("emotion_counts", {}) for s in sessions[-n:]]
    except Exception as e:
        print(f"[EmotionAdapter] Could not load sessions: {e}")
        return []


def compute_emotion_profile(history):
    totals = {}
    grand_total = 0
    for counts in history:
        for label, cnt in counts.items():
            totals[label] = totals.get(label, 0) + cnt
            grand_total += cnt
    if grand_total == 0:
        return {"dominant": "happy", "happy_rate": 1.0,
                "confused_rate": 0.0, "bored_rate": 0.0}
    rates = {k: v / grand_total for k, v in totals.items()}
    dominant = max(rates, key=rates.get) if rates else "happy"
    return {
        "dominant":       dominant,
        "happy_rate":     rates.get("happy", 0.0),
        "confused_rate":  rates.get("confused", 0.0),
        "bored_rate":     rates.get("bored", 0.0),
    }


def generate_hints(face_id):
    """Write emotion-derived hints into Data/adaptive_hints_{face_id}.json."""
    history = load_emotion_history(face_id)
    profile = compute_emotion_profile(history)

    # Bias difficulty easier if child is frequently confused or bored
    confused = profile["confused_rate"]
    bored    = profile["bored_rate"]
    if confused > 0.25:
        difficulty_bias = "easier"
        start_with_audio = True
    elif bored > 0.30:
        difficulty_bias = "harder"
        start_with_audio = False
    else:
        difficulty_bias = "same"
        start_with_audio = False

    hints_path = os.path.join(_DATA_DIR, f"adaptive_hints_{_sanitize_face_id(face_id)}.json")
    existing = {}
    if os.path.exists(hints_path):
        try:
            with open(hints_path, "r") as f:
                existing = json.load(f)
        except Exception:
            pass

    existing["face_id"] = face_id
    existing.setdefault("gaze", {})
    existing["emotion"] = {
        "dominant":           profile["dominant"],
        "happy_rate":         round(profile["happy_rate"], 3),
        "confused_rate":      round(profile["confused_rate"], 3),
        "bored_rate":         round(profile["bored_rate"], 3),
        "start_with_audio_hint": start_with_audio,
        "difficulty_bias":    difficulty_bias,
        "confusing_fruits":   [],  # populated by future analysis
    }
    existing.setdefault("combined", {
        "layout_bias": "center",
        "auto_hint_threshold_sec": 4,
    })

    os.makedirs(_DATA_DIR, exist_ok=True)
    with open(hints_path, "w") as f:
        json.dump(existing, f, indent=2)
    print(f"[EmotionAdapter] Hints written for {face_id} (bias={difficulty_bias})")
