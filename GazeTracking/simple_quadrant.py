#!/usr/bin/env python3
"""
Simple Eye Quadrant Tracker with TUIO Integration
Eye tracking -> TUIO cursor + Visual feedback
No complex calibration needed
"""

import cv2
import numpy as np
from typing import Tuple, Optional
from dataclasses import dataclass
from enum import Enum
import math

try:
    from TUIO import TuioClient, TuioListener
    TUIO_AVAILABLE = True
except ImportError:
    TUIO_AVAILABLE = False
    print("[WARN] TUIO not available - running without TUIO")


class BlinkState(Enum):
    OPEN = "open"
    CLOSED = "closed"
    BLINKING = "blinking"


@dataclass
class EyeState:
    left_center: Tuple[int, int] = (0, 0)
    right_center: Tuple[int, int] = (0, 0)
    left_openness: float = 0.0
    right_openness: float = 0.0
    blink_state: BlinkState = BlinkState.OPEN
    face_detected: bool = False
    gaze_x: float = 0.5
    gaze_y: float = 0.5


@dataclass
class GazeQuadrant:
    horizontal: str = "center"
    vertical: str = "mid"
    name: str = "center-mid"
    x: float = 0.5
    y: float = 0.5
    confidence: float = 0.0


class EyeQuadrantTracker:
    def __init__(
        self,
        camera_index: int = 0,
        smoothing: float = 0.4,
        blink_threshold: float = 0.15
    ):
        self.camera_index = camera_index
        self.cap = None
        
        self.smoothing = smoothing
        self.blink_threshold = blink_threshold
        
        self.prev_left = (0, 0)
        self.prev_right = (0, 0)
        self.prev_open_l = 0.0
        self.prev_open_r = 0.0
        
        self.blink_count = 0
        self.is_blinking = False
        self.blink_hold_frames = 3
        
        self.frame_w = 640
        self.frame_h = 480
        
        import mediapipe as mp
        self.mp_fm = mp.solutions.face_mesh
        self.face_mesh = self.mp_fm.FaceMesh(
            max_num_faces=1,
            refine_landmarks=True,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.3
        )
        
        self.face_found = False
    
    def start(self) -> bool:
        if self.cap is None or not self.cap.isOpened():
            self.cap = cv2.VideoCapture(self.camera_index)
            self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            self.cap.set(cv2.CAP_PROP_FPS, 60)
        
        if self.cap.isOpened():
            self.frame_w = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            self.frame_h = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        
        return self.cap.isOpened()
    
    def stop(self):
        if self.cap and self.cap.isOpened():
            self.cap.release()
    
    def _lerp(self, a, b, t):
        return t * a + (1 - t) * b
    
    def _get_eye_center(self, landmarks, indices):
        cx = sum(landmarks[i].x for i in indices) / len(indices)
        cy = sum(landmarks[i].y for i in indices) / len(indices)
        return int(cx * self.frame_w), int(cy * self.frame_h)
    
    def _get_eye_openness(self, landmarks, indices):
        top = landmarks[indices[1]].y
        bottom = landmarks[indices[5]].y
        left = landmarks[indices[0]].x
        right = landmarks[indices[3]].x
        
        height = abs(bottom - top)
        width = abs(right - left)
        
        if width < 0.001:
            return 0.0
        
        return min(1.0, (height / width) * 2.0)
    
    def process(self, frame) -> EyeState:
        state = EyeState()
        
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = self.face_mesh.process(rgb)
        
        if not results.multi_face_landmarks:
            self.face_found = False
            return state
        
        self.face_found = True
        lm = results.multi_face_landmarks[0]
        
        left_idx = [362, 385, 387, 263, 373, 380]
        right_idx = [133, 159, 161, 33, 155, 153]
        
        lx, ly = self._get_eye_center(lm, left_idx)
        rx, ry = self._get_eye_center(lm, right_idx)
        
        open_l = self._get_eye_openness(lm, left_idx)
        open_r = self._get_eye_openness(lm, right_idx)
        
        lx = int(self._lerp(lx, self.prev_left[0], self.smoothing))
        ly = int(self._lerp(ly, self.prev_left[1], self.smoothing))
        rx = int(self._lerp(rx, self.prev_right[0], self.smoothing))
        ry = int(self._lerp(ry, self.prev_right[1], self.smoothing))
        
        open_l = self._lerp(open_l, self.prev_open_l, self.smoothing)
        open_r = self._lerp(open_r, self.prev_open_r, self.smoothing)
        
        self.prev_left = (lx, ly)
        self.prev_right = (rx, ry)
        self.prev_open_l = open_l
        self.prev_open_r = open_r
        
        if open_l < self.blink_threshold and open_r < self.blink_threshold:
            self.blink_count += 1
        else:
            self.blink_count = 0
            self.is_blinking = False
        
        if self.blink_count >= self.blink_hold_frames:
            self.is_blinking = True
        
        avg_open = (open_l + open_r) / 2
        if avg_open < self.blink_threshold:
            state.blink_state = BlinkState.CLOSED
        elif self.is_blinking:
            state.blink_state = BlinkState.BLINKING
        else:
            state.blink_state = BlinkState.OPEN
        
        state.left_center = (lx, ly)
        state.right_center = (rx, ry)
        state.left_openness = open_l
        state.right_openness = open_r
        state.face_detected = True
        
        state.gaze_x = (lx / self.frame_w + rx / self.frame_w) / 2
        state.gaze_y = (ly / self.frame_h + ry / self.frame_h) / 2
        
        return state
    
    def get_quadrant(self, eye: EyeState) -> GazeQuadrant:
        if not eye.face_detected or self.is_blinking:
            return GazeQuadrant(name="blink", confidence=0.0)
        
        x = np.clip(eye.gaze_x, 0, 1)
        y = np.clip(eye.gaze_y, 0, 1)
        
        if x < 0.4:
            h = "left"
        elif x > 0.6:
            h = "right"
        else:
            h = "center"
        
        if y < 0.4:
            v = "up"
        elif y > 0.6:
            v = "down"
        else:
            v = "mid"
        
        conf = (eye.left_openness + eye.right_openness) / 2
        
        return GazeQuadrant(
            horizontal=h,
            vertical=v,
            name=f"{h}-{v}",
            x=x,
            y=y,
            confidence=conf
        )
    
    def read_frame(self) -> Optional[np.ndarray]:
        if self.cap is None or not self.cap.isOpened():
            return None
        ret, frame = self.cap.read()
        return cv2.flip(frame, 1) if ret else None


class SimpleTuioListener:
    def __init__(self):
        self.x = 0.5
        self.y = 0.5
        self.active = False
    
    def addTuioCursor(self, cursor):
        self.x = cursor.getX()
        self.y = cursor.getY()
        self.active = True
    
    def updateTuioCursor(self, cursor):
        self.x = cursor.getX()
        self.y = cursor.getY()
    
    def removeTuioCursor(self, cursor):
        self.active = False
    
    def refresh(self, t):
        pass


class GazeToTuioBridge:
    def __init__(self, port=3333):
        self.port = port
        self.client = None
        self.listener = None
        self.session_id = 0
        
        if TUIO_AVAILABLE:
            try:
                self.client = TuioClient(port)
                self.listener = SimpleTuioListener()
                self.client.addTuioListener(self.listener)
                self.client.connect()
                print(f"[TUIO] Connected on port {port}")
            except Exception as e:
                print(f"[TUIO] Failed to connect: {e}")
                self.client = None
    
    def send_gaze(self, x: float, y: float):
        if self.client is None:
            return
        
        try:
            pass
        except Exception as e:
            print(f"[TUIO] Send error: {e}")
    
    def close(self):
        if self.client:
            self.client.disconnect()


def draw_visual_feedback(frame, eye: EyeState, quad: GazeQuadrant, w: int, h: int):
    display = frame.copy()
    
    if not eye.face_detected:
        cv2.putText(display, "NO FACE", (w//2 - 60, h//2), 
                   cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 2)
        return display
    
    lx, ly = eye.left_center
    rx, ry = eye.right_center
    
    cv2.circle(display, (lx, ly), 8, (0, 255, 0), -1)
    cv2.circle(display, (rx, ry), 8, (0, 255, 0), -1)
    
    if quad.name != "blink":
        gx = int(quad.x * w)
        gy = int(quad.y * h)
        
        cv2.drawMarker(display, (gx, gy), (0, 0, 255), cv2.MARKER_CROSS, 40, 3)
        
        mid_x = (lx + rx) // 2
        mid_y = (ly + ry) // 2
        cv2.arrowedLine(display, (mid_x, mid_y), (gx, gy), (255, 255, 0), 2, tipLength=0.3)
        
        cv2.circle(display, (gx, gy), 5, (0, 0, 255), -1)
    
    qcolor = (0, 255, 0) if quad.name != "blink" else (150, 150, 0)
    cv2.putText(display, quad.name.upper(), (10, 40), 
               cv2.FONT_HERSHEY_SIMPLEX, 1.0, qcolor, 2)
    
    cv2.putText(display, f"X:{quad.x:.2f} Y:{quad.y:.2f}", (10, 80), 
               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    
    cv2.putText(display, f"L:{eye.left_openness:.2f} R:{eye.right_openness:.2f}", (10, 110), 
               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    
    if eye.blink_state == BlinkState.CLOSED:
        cv2.putText(display, "EYES CLOSED", (w//2 - 70, h//2), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 0, 255), 2)
    
    bar_y = h - 40
    l_bar = int(eye.left_openness * 60)
    r_bar = int(eye.right_openness * 60)
    
    cv2.rectangle(display, (10, bar_y), (10 + l_bar, bar_y + 15), (0, 255, 0), -1)
    cv2.rectangle(display, (10, bar_y + 20), (10 + r_bar, bar_y + 35), (0, 255, 0), -1)
    
    cv2.putText(display, "L", (15, bar_y + 12), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)
    cv2.putText(display, "R", (15, bar_y + 32), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)
    
    quadrant_map = [
        ("UP-LEFT", int(w * 0.2), int(h * 0.2)),
        ("UP-MID", int(w * 0.5), int(h * 0.2)),
        ("UP-RIGHT", int(w * 0.8), int(h * 0.2)),
        ("MID-LEFT", int(w * 0.2), int(h * 0.5)),
        ("CENTER", int(w * 0.5), int(h * 0.5)),
        ("MID-RIGHT", int(w * 0.8), int(h * 0.5)),
        ("DOWN-LEFT", int(w * 0.2), int(h * 0.8)),
        ("DOWN-MID", int(w * 0.5), int(h * 0.8)),
        ("DOWN-RIGHT", int(w * 0.8), int(h * 0.8)),
    ]
    
    is_current = False
    for name, px, py in quadrant_map:
        if quad.name == name.lower().replace("-", ""):
            is_current = True
            color = (0, 255, 255)
            thickness = 3
        else:
            is_current = False
            color = (80, 80, 80)
            thickness = 1
        
        if is_current:
            cv2.circle(display, (px, py), 15, color, -1)
            cv2.putText(display, name, (px - 40, py + 35), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.4, color, 1)
    
    return display


def main():
    import argparse
    
    parser = argparse.ArgumentParser(description='Eye Quadrant Tracker with TUIO')
    parser.add_argument('--camera', type=int, default=0, help='Camera index')
    parser.add_argument('--tuio-port', type=int, default=3333, help='TUIO port')
    parser.add_argument('--smoothing', type=float, default=0.4, help='Smoothing factor')
    args = parser.parse_args()
    
    print("=" * 50)
    print("EYE QUADRANT TRACKER + TUIO")
    print("=" * 50)
    
    tracker = EyeQuadrantTracker(camera_index=args.camera)
    
    if not tracker.start():
        print("ERROR: Cannot open camera")
        return
    
    print(f"Camera: {args.camera} ({tracker.frame_w}x{tracker.frame_h})")
    print(f"TUIO Port: {args.tuio_port}")
    print(f"Smoothing: {args.smoothing}")
    print("=" * 50)
    
    tuio = GazeToTuioBridge(port=args.tuio_port)
    
    print("\nControls:")
    print("  [Q] - Quit")
    print("  [S] - Toggle smoothing")
    print("  [B] - Adjust blink threshold")
    print("-" * 50)
    
    while True:
        frame = tracker.read_frame()
        if frame is None:
            continue
        
        h, w = frame.shape[:2]
        
        eye = tracker.process(frame)
        quad = tracker.get_quadrant(eye)
        
        display = draw_visual_feedback(frame, eye, quad, w, h)
        
        if eye.face_detected and quad.name != "blink":
            tuio.send_gaze(quad.x, quad.y)
        
        cv2.imshow("Eye Quadrant Tracker", display)
        
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            break
        elif key == ord('s'):
            args.smoothing = 0.9 if args.smoothing < 0.5 else 0.4
            tracker.smoothing = args.smoothing
            print(f"Smoothing: {args.smoothing}")
        elif key == ord('b'):
            tracker.blink_threshold = 0.25 if tracker.blink_threshold < 0.2 else 0.15
            print(f"Blink threshold: {tracker.blink_threshold}")
    
    tuio.close()
    tracker.stop()
    cv2.destroyAllWindows()
    
    print("\nSession complete!")


if __name__ == "__main__":
    main()