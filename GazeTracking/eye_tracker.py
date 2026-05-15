#!/usr/bin/env python3
"""
Simple Eye Tracker - Lightweight, Fast, Reliable
Tracks both eyes for quadrant position + blink state
Optimized for FPS, no calibration needed
"""

import cv2
import numpy as np
from typing import Tuple, Optional, List
from dataclasses import dataclass
from enum import Enum


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
    timestamp: float = 0.0


@dataclass
class GazeQuadrant:
    horizontal: str = "center"   # left, center, right
    vertical: str = "mid"        # up, mid, down
    quadrant_name: str = "center-mid"
    gaze_x: float = 0.5
    gaze_y: float = 0.5
    confidence: float = 0.0


class SimpleEyeTracker:
    def __init__(
        self,
        camera_index: int = 0,
        smoothing_factor: float = 0.3,
        blink_threshold: float = 0.2,
        blink_frames_hold: int = 5
    ):
        self.camera_index = camera_index
        self.cap = None
        
        self.smoothing_factor = smoothing_factor
        self.blink_threshold = blink_threshold
        self.blink_frames_hold = blink_frames_hold
        
        self.last_left_center = (0, 0)
        self.last_right_center = (0, 0)
        self.last_left_open = 0.0
        self.last_right_open = 0.0
        
        self.blink_counter = 0
        self.is_blinking = False
        
        self.frame_width = 640
        self.frame_height = 480
        
        import mediapipe as mp
        self.mp_face_mesh = mp.solutions.face_mesh
        self.face_mesh = self.mp_face_mesh.FaceMesh(
            max_num_faces=1,
            refine_landmarks=True,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.3
        )
        
        self.face_detected = False
        self.frame_count = 0
    
    def start(self) -> bool:
        if self.cap is None or not self.cap.isOpened():
            self.cap = cv2.VideoCapture(self.camera_index)
            self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            self.cap.set(cv2.CAP_PROP_FPS, 60)
        
        if self.cap.isOpened():
            ret, _ = self.cap.read()
            if ret:
                self.frame_width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                self.frame_height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        
        return self.cap.isOpened()
    
    def stop(self):
        if self.cap and self.cap.isOpened():
            self.cap.release()
    
    def _get_eye_openness(self, eye_landmarks) -> float:
        if len(eye_landmarks) < 6:
            return 0.0
        
        top_y = eye_landmarks[1].y
        bottom_y = eye_landmarks[5].y
        left_x = eye_landmarks[0].x
        right_x = eye_landmarks[3].x
        
        height = abs(bottom_y - top_y)
        width = abs(right_x - left_x)
        
        if width < 0.0001:
            return 0.0
        
        openness = height / width
        
        return min(1.0, max(0.0, openness * 2.0))
    
    def _get_eye_center(self, eye_landmarks) -> Tuple[int, int]:
        if len(eye_landmarks) < 6:
            return (0, 0)
        
        center_x = sum(lm.x for lm in eye_landmarks[:6]) / 6
        center_y = sum(lm.y for lm in eye_landmarks[:6]) / 6
        
        x = int(center_x * self.frame_width)
        y = int(center_y * self.frame_height)
        
        return (x, y)
    
    def _smooth_position(self, new_pos: Tuple[int, int], last_pos: Tuple[int, int]) -> Tuple[int, int]:
        alpha = self.smoothing_factor
        x = int(alpha * new_pos[0] + (1 - alpha) * last_pos[0])
        y = int(alpha * new_pos[1] + (1 - alpha) * last_pos[1])
        return (x, y)
    
    def _smooth_value(self, new_val: float, last_val: float) -> float:
        alpha = self.smoothing_factor
        return alpha * new_val + (1 - alpha) * last_val
    
    def detect_eyes(self, frame) -> EyeState:
        state = EyeState()
        state.timestamp = cv2.getTickCount() / cv2.getTickFrequency()
        
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = self.face_mesh.process(rgb)
        
        if not results.multi_face_landmarks:
            self.face_detected = False
            return state
        
        self.face_detected = True
        landmarks = results.multi_face_landmarks[0]
        
        left_eye_indices = [362, 385, 387, 263, 373, 380]
        right_eye_indices = [133, 159, 161, 33, 155, 153]
        
        left_eye = [landmarks[i] for i in left_eye_indices]
        right_eye = [landmarks[i] for i in right_eye_indices]
        
        raw_left_center = self._get_eye_center(left_eye)
        raw_right_center = self._get_eye_center(right_eye)
        raw_left_open = self._get_eye_openness(left_eye)
        raw_right_open = self._get_eye_openness(right_eye)
        
        if self.last_left_center == (0, 0):
            self.last_left_center = raw_left_center
            self.last_right_center = raw_right_center
            self.last_left_open = raw_left_open
            self.last_right_open = raw_right_open
        
        left_center = self._smooth_position(raw_left_center, self.last_left_center)
        right_center = self._smooth_position(raw_right_center, self.last_right_center)
        
        left_openness = self._smooth_value(raw_left_open, self.last_left_open)
        right_openness = self._smooth_value(raw_right_open, self.last_right_open)
        
        self.last_left_center = left_center
        self.last_right_center = right_center
        self.last_left_open = left_openness
        self.last_right_open = right_openness
        
        if left_openness < self.blink_threshold and right_openness < self.blink_threshold:
            self.blink_counter += 1
        else:
            self.blink_counter = 0
            self.is_blinking = False
        
        if self.blink_counter >= 2:
            self.is_blinking = True
        
        avg_openness = (left_openness + right_openness) / 2
        if avg_openness < self.blink_threshold:
            state.blink_state = BlinkState.CLOSED
        elif self.is_blinking:
            state.blink_state = BlinkState.BLINKING
        else:
            state.blink_state = BlinkState.OPEN
        
        state.left_center = left_center
        state.right_center = right_center
        state.left_openness = left_openness
        state.right_openness = right_openness
        state.face_detected = True
        
        self.frame_count += 1
        return state
    
    def get_gaze_quadrant(self, eye_state: EyeState) -> GazeQuadrant:
        if not eye_state.face_detected:
            return GazeQuadrant()
        
        if self.is_blinking:
            return GazeQuadrant(
                quadrant_name="blink",
                confidence=0.0
            )
        
        left_x = eye_state.left_center[0] / self.frame_width
        right_x = eye_state.right_center[0] / self.frame_width
        
        left_y = eye_state.left_center[1] / self.frame_height
        right_y = eye_state.right_center[1] / self.frame_height
        
        avg_x = (left_x + right_x) / 2
        avg_y = (left_y + right_y) / 2
        
        gaze_x = np.clip(avg_x, 0.0, 1.0)
        gaze_y = np.clip(avg_y, 0.0, 1.0)
        
        if gaze_x < 0.4:
            h = "left"
        elif gaze_x > 0.6:
            h = "right"
        else:
            h = "center"
        
        if gaze_y < 0.4:
            v = "up"
        elif gaze_y > 0.6:
            v = "down"
        else:
            v = "mid"
        
        confidence = (eye_state.left_openness + eye_state.right_openness) / 2
        
        return GazeQuadrant(
            horizontal=h,
            vertical=v,
            quadrant_name=f"{h}-{v}",
            gaze_x=gaze_x,
            gaze_y=gaze_y,
            confidence=confidence
        )
    
    def read_frame(self) -> Optional[np.ndarray]:
        if self.cap is None or not self.cap.isOpened():
            return None
        
        ret, frame = self.cap.read()
        if not ret:
            return None
        
        return cv2.flip(frame, 1)


def draw_eye_overlay(frame, eye_state: EyeState, quadrant: GazeQuadrant):
    h, w = frame.shape[:2]
    
    if eye_state.face_detected:
        lx, ly = eye_state.left_center
        rx, ry = eye_state.right_center
        
        cv2.circle(frame, (lx, ly), 6, (0, 255, 0), -1)
        cv2.circle(frame, (rx, ry), 6, (0, 255, 0), -1)
        
        if quadrant.quadrant_name != "blink":
            gx = int(quadrant.gaze_x * w)
            gy = int(quadrant.gaze_y * h)
            
            cv2.drawMarker(frame, (gx, gy), (0, 0, 255), cv2.MARKER_CROSS, 30, 2)
            
            center_x = (lx + rx) // 2
            center_y = (ly + ry) // 2
            cv2.line(frame, (center_x, center_y), (gx, gy), (255, 255, 0), 2)
        
        openness_text = f"L:{eye_state.left_openness:.2f} R:{eye_state.right_openness:.2f}"
        cv2.putText(frame, openness_text, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
        
        quadrant_color = (0, 255, 0) if quadrant.quadrant_name != "blink" else (150, 150, 0)
        cv2.putText(frame, quadrant.quadrant_name.upper(), (10, 60), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, quadrant_color, 2)
        
        if eye_state.blink_state == BlinkState.CLOSED:
            cv2.putText(frame, "BLINKING", (w//2 - 50, h//2), 
                       cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 2)
        
        left_open = int(eye_state.left_openness * 50)
        right_open = int(eye_state.right_openness * 50)
        
        cv2.rectangle(frame, (10, h - 40), (10 + left_open, h - 20), (0, 255, 0), -1)
        cv2.rectangle(frame, (70, h - 40), (70 + right_open, h - 20), (0, 255, 0), -1)
        cv2.putText(frame, "L", (15, h - 45), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)
        cv2.putText(frame, "R", (75, h - 45), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)
    else:
        cv2.putText(frame, "NO FACE DETECTED", (w//2 - 80, h//2), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)
    
    return frame


if __name__ == "__main__":
    print("Starting Simple Eye Tracker...")
    
    tracker = SimpleEyeTracker(camera_index=0)
    
    if not tracker.start():
        print("ERROR: Cannot open camera")
        exit(1)
    
    print("\nControls:")
    print("  [Q] - Quit")
    print("  [S] - Toggle smoothing")
    print("  [B] - Toggle blink detection")
    print("\nEye quadrants: left/center/right + up/mid/down")
    print("-" * 40)
    
    smoothing_enabled = True
    
    while True:
        frame = tracker.read_frame()
        if frame is None:
            continue
        
        eye_state = tracker.detect_eyes(frame)
        quadrant = tracker.get_gaze_quadrant(eye_state)
        
        display = draw_eye_overlay(frame, eye_state, quadrant)
        
        cv2.imshow("Eye Tracker", display)
        
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            break
        elif key == ord('s'):
            smoothing_enabled = not smoothing_enabled
            tracker.smoothing_factor = 0.3 if smoothing_enabled else 1.0
            print(f"Smoothing: {'ON' if smoothing_enabled else 'OFF'}")
        elif key == ord('b'):
            tracker.blink_threshold = 0.2 if tracker.blink_threshold > 0.1 else 0.1
            print(f"Blink threshold: {tracker.blink_threshold}")
    
    tracker.stop()
    cv2.destroyAllWindows()
    print("\nDone!")