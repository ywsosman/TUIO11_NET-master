"""
laser_tracker.py — HSV blob detection + stroke/dwell accumulator for laser pointers.
Called by gesture_server.py each frame when laser_tracker is enabled.
"""

import time
from collections import deque

import cv2
import numpy as np

_RING_SIZE        = 120    # max stroke history points
_DWELL_THRESH_S   = 0.8    # seconds of stationary blob = dwell/tap
_STATIONARY_PIX   = 0.03   # normalised radius; blob moves less than this = stationary
_STROKE_MIN_SPEED = 0.015  # stroke complete when speed drops after movement


def find_laser_blob(frame, hsv_lower, hsv_upper):
    """Return (cx, cy) normalised [0,1] of the largest HSV blob, or None."""
    h, w = frame.shape[:2]
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, hsv_lower, hsv_upper)
    # Handle red-hue wraparound (0-10 as well as 160-180)
    if hsv_lower[0] >= 160:
        lower2 = np.array([0, hsv_lower[1], hsv_lower[2]])
        upper2 = np.array([10, hsv_upper[1], hsv_upper[2]])
        mask = cv2.bitwise_or(mask, cv2.inRange(hsv, lower2, upper2))
    mask = cv2.erode(mask, None, iterations=2)
    mask = cv2.dilate(mask, None, iterations=2)
    cnts, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not cnts:
        return None
    c = max(cnts, key=cv2.contourArea)
    if cv2.contourArea(c) < 20:
        return None
    M = cv2.moments(c)
    if M["m00"] == 0:
        return None
    cx = M["m10"] / M["m00"] / w
    cy = M["m01"] / M["m00"] / h
    return cx, cy


class LaserStrokeAccumulator:
    def __init__(self):
        self._pts = deque(maxlen=_RING_SIZE)  # (cx, cy, t)
        self._dwell_start = None

    def update(self, cx, cy):
        now = time.time()
        self._pts.append((cx, cy, now))
        # Track dwell start
        if len(self._pts) >= 2:
            x1, y1, _ = self._pts[-2]
            dist = ((cx - x1) ** 2 + (cy - y1) ** 2) ** 0.5
            if dist < _STATIONARY_PIX:
                if self._dwell_start is None:
                    self._dwell_start = now
            else:
                self._dwell_start = None

    def is_dwell(self, threshold_s=_DWELL_THRESH_S):
        if self._dwell_start is None:
            return False
        return (time.time() - self._dwell_start) >= threshold_s

    def _recent_speed(self):
        if len(self._pts) < 2:
            return 0.0
        x1, y1, t1 = self._pts[-2]
        x2, y2, t2 = self._pts[-1]
        dt = max(t2 - t1, 1e-6)
        return ((x2 - x1) ** 2 + (y2 - y1) ** 2) ** 0.5 / dt

    def ready_for_recognition(self):
        if len(self._pts) < 10:
            return False
        pts = list(self._pts)
        recent = self._recent_speed()
        if len(pts) >= 6:
            dt = max(pts[-4][2] - pts[-6][2], 1e-6)
            mid = ((pts[-4][0] - pts[-6][0]) ** 2 + (pts[-4][1] - pts[-6][1]) ** 2) ** 0.5 / dt
            return mid > _STROKE_MIN_SPEED and recent < _STROKE_MIN_SPEED
        return False

    def get_stroke(self):
        return [(x, y) for x, y, _ in self._pts]

    def clear(self):
        self._pts.clear()
        self._dwell_start = None
