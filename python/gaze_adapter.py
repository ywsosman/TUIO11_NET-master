"""
gaze_adapter.py — cross-session gaze analysis → adaptive UI hints.
Reads sessions.json, aggregates gaze zone data, writes adaptive_hints_{face_id}.json.
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

ZONES = ["Top-Left", "Top-Center", "Top-Right",
         "Mid-Left", "Center",     "Mid-Right",
         "Bot-Left", "Bot-Center", "Bot-Right"]


def load_user_sessions(face_id, n=5):
    if not os.path.exists(_SESSIONS_FILE):
        return []
    try:
        with open(_SESSIONS_FILE, "r") as f:
            data = json.load(f)
        sessions = [s for s in data.get("sessions", []) if s.get("face_id") == face_id]
        return sessions[-n:]
    except Exception as e:
        print(f"[GazeAdapter] Could not load sessions: {e}")
        return []


def compute_attention_map(sessions):
    """Average gaze zone hit-counts across sessions into a 3x3 matrix (row-major)."""
    matrix = [0.0] * 9
    count = 0
    for s in sessions:
        zone_hits = s.get("gaze_zones", {})
        if not zone_hits:
            continue
        total = max(sum(zone_hits.values()), 1)
        for i, z in enumerate(ZONES):
            matrix[i] += zone_hits.get(z, 0) / total
        count += 1
    if count:
        matrix = [v / count for v in matrix]
    return matrix


def generate_hints(face_id):
    """Write gaze-derived hints into Data/adaptive_hints_{face_id}.json."""
    sessions = load_user_sessions(face_id)
    matrix   = compute_attention_map(sessions)

    # Recommended CTA zone: highest average attention
    best_idx = matrix.index(max(matrix)) if matrix else 4  # default Center
    recommended_zone = ZONES[best_idx]

    # Zones with below-average attention → enlarge targets there
    avg = sum(matrix) / max(len(matrix), 1)
    enlarge = [ZONES[i] for i, v in enumerate(matrix) if v < avg * 0.6]

    hints_path = os.path.join(_DATA_DIR, f"adaptive_hints_{_sanitize_face_id(face_id)}.json")
    existing = {}
    if os.path.exists(hints_path):
        try:
            with open(hints_path, "r") as f:
                existing = json.load(f)
        except Exception:
            pass

    existing["face_id"] = face_id
    existing.setdefault("emotion", {})
    existing["gaze"] = {
        "recommended_cta_zone": recommended_zone,
        "enlarge_zones": enlarge,
    }
    existing["combined"] = {
        "layout_bias": recommended_zone.lower().replace(" ", "-"),
        "auto_hint_threshold_sec": 4,
    }

    os.makedirs(_DATA_DIR, exist_ok=True)
    with open(hints_path, "w") as f:
        json.dump(existing, f, indent=2)
    print(f"[GazeAdapter] Hints written for {face_id} → {recommended_zone}")
