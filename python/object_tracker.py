"""
object_tracker.py — per-track centroid trajectory + DollarPy stroke provider.
Called by gesture_server.py after each SORT tick.
"""

import time
from collections import deque

_RING_SIZE        = 60     # max history points per track
_SMOOTH_WIN       = 5      # moving-average window for stroke smoothing
_IDLE_THRESH      = 0.02   # normalised-speed threshold; below = stationary
_STROKE_MIN_SPEED = 0.015  # stroke complete when speed drops below this after moving


class _TrackState:
    def __init__(self):
        self.pts = deque(maxlen=_RING_SIZE)  # (x, y, t)
        self.cls = "object"
        self.recognized = False


class ObjectTracker:
    def __init__(self):
        self._tracks = {}

    def update(self, track_id, cx, cy, class_name="object"):
        if track_id not in self._tracks:
            self._tracks[track_id] = _TrackState()
        s = self._tracks[track_id]
        s.pts.append((cx, cy, time.time()))
        s.cls = class_name
        s.recognized = False

    def _speed(self, a, b):
        dt = max(b[2] - a[2], 1e-6)
        return ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2) ** 0.5 / dt

    def get_velocity(self, track_id):
        s = self._tracks.get(track_id)
        if s is None or len(s.pts) < 2:
            return 0.0, 0.0, 0.0
        x1, y1, t1 = s.pts[-2]
        x2, y2, t2 = s.pts[-1]
        dt = max(t2 - t1, 1e-6)
        vx, vy = (x2 - x1) / dt, (y2 - y1) / dt
        return vx, vy, (vx ** 2 + vy ** 2) ** 0.5

    def is_stationary(self, track_id, threshold=_IDLE_THRESH):
        _, _, speed = self.get_velocity(track_id)
        return speed < threshold

    def get_stroke(self, track_id):
        s = self._tracks.get(track_id)
        if s is None:
            return []
        raw = [(x, y) for x, y, _ in s.pts]
        if len(raw) < _SMOOTH_WIN:
            return raw
        smoothed = []
        half = _SMOOTH_WIN // 2
        for i in range(len(raw)):
            lo, hi = max(0, i - half), min(len(raw), i + half + 1)
            chunk = raw[lo:hi]
            smoothed.append((sum(p[0] for p in chunk) / len(chunk),
                              sum(p[1] for p in chunk) / len(chunk)))
        return smoothed

    def ready_for_recognition(self, track_id):
        s = self._tracks.get(track_id)
        if s is None or len(s.pts) < 10 or s.recognized:
            return False
        pts = list(s.pts)
        recent = self._speed(pts[-3], pts[-1])
        if len(pts) >= 8:
            mid = self._speed(pts[-8], pts[-5])
            return mid > _STROKE_MIN_SPEED and recent < _STROKE_MIN_SPEED
        return False

    def mark_recognized(self, track_id):
        s = self._tracks.get(track_id)
        if s:
            s.recognized = True
            s.pts.clear()

    def cleanup_stale(self, max_age_sec=3.0):
        now = time.time()
        stale = [tid for tid, s in self._tracks.items()
                 if s.pts and (now - s.pts[-1][2]) > max_age_sec]
        for tid in stale:
            del self._tracks[tid]
