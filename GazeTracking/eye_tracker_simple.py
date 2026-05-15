#!/usr/bin/env python3
"""
Eye Tracker - Simple, Fast, Reliable
Uses MediaPipe for accurate eye tracking
"""

import cv2
import numpy as np
import time
from typing import Optional
from enum import Enum


class BlinkState(Enum):
    OPEN = "open"
    CLOSED = "closed"
    BLINKING = "blinking"


class EyeTracker:
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

        self.prev_l = (0, 0)
        self.prev_r = (0, 0)
        self.prev_open_l = 0.0
        self.prev_open_r = 0.0

        self.blink_count = 0
        self.is_blinking = False

        self.frame_w = 640
        self.frame_h = 480

        self.detector = None
        self.use_mediapipe_tasks = False
        
        self._init_detector()

        self.face_found = False
        self.frame_count = 0

    def _init_detector(self):
        try:
            import mediapipe.tasks as tasks
            
            vision = getattr(tasks, 'vision')
            BaseOptions = tasks.BaseOptions
            
            model_path = 'face_landmarker.task'
            base_options = BaseOptions(model_asset_path=model_path)
            options = vision.FaceLandmarkerOptions(base_options=base_options, num_faces=1)
            self.detector = vision.FaceLandmarker.create_from_options(options)
            
            import mediapipe
            self.mp_image = mediapipe.Image
            self.mp_format = mediapipe.ImageFormat
            self.use_mediapipe_tasks = True
            
            print("[OK] Using MediaPipe Tasks FaceLandmarker")
        except Exception as e:
            print(f"[WARN] Tasks API failed: {e}")
            self.use_mediapipe_tasks = False
            
            try:
                import mediapipe as mp
                self.face_mesh = mp.solutions.face_mesh
                self.detector = self.face_mesh.FaceMesh(
                    max_num_faces=1,
                    refine_landmarks=True,
                    min_detection_confidence=0.5,
                    min_tracking_confidence=0.3
                )
                print("[OK] Using legacy FaceMesh")
            except Exception as e2:
                print(f"[ERROR] Legacy API also failed: {e2}")
                self.detector = None

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

    def lerp(self, a, b, t):
        return t * a + (1 - t) * b

    def get_eye_center(self, lm, indices):
        cx = sum(lm[i].x for i in indices) / len(indices)
        cy = sum(lm[i].y for i in indices) / len(indices)
        return int(cx * self.frame_w), int(cy * self.frame_h)

    def get_eye_openness(self, lm, indices):
        top = lm[indices[1]].y
        bottom = lm[indices[5]].y
        left = lm[indices[0]].x
        right = lm[indices[3]].x

        h = abs(bottom - top)
        w = abs(right - left)

        if w < 0.001:
            return 0.0

        return min(1.0, (h / w) * 2.0)

    def track(self, frame) -> dict:
        result = {
            "face_detected": False,
            "left_x": 0, "left_y": 0,
            "right_x": 0, "right_y": 0,
            "left_openness": 0.0,
            "right_openness": 0.0,
            "blink_state": "open",
            "quadrant": "none",
            "gaze_x": 0.5,
            "gaze_y": 0.5,
            "timestamp": time.time()
        }

        if self.detector is None:
            return result

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

        try:
            if self.use_mediapipe_tasks:
                mp_image = self.mp_image(image_format=self.mp_format.SRGB, data=rgb)
                results = self.detector.detect(mp_image)
                
                if not results.face_landmarks:
                    return result
                
                lm = results.face_landmarks[0]
            else:
                results = self.detector.process(rgb)
                
                if not results.multi_face_landmarks:
                    return result
                
                lm = results.multi_face_landmarks[0]
        except Exception as e:
            print(f"Detection error: {e}")
            return result

        self.face_found = True

        left_idx = [362, 385, 387, 263, 373, 380]
        right_idx = [133, 159, 161, 33, 155, 153]

        lx, ly = self.get_eye_center(lm, left_idx)
        rx, ry = self.get_eye_center(lm, right_idx)

        open_l = self.get_eye_openness(lm, left_idx)
        open_r = self.get_eye_openness(lm, right_idx)

        lx = int(self.lerp(lx, self.prev_l[0], self.smoothing))
        ly = int(self.lerp(ly, self.prev_l[1], self.smoothing))
        rx = int(self.lerp(rx, self.prev_r[0], self.smoothing))
        ry = int(self.lerp(ry, self.prev_r[1], self.smoothing))

        open_l = self.lerp(open_l, self.prev_open_l, self.smoothing)
        open_r = self.lerp(open_r, self.prev_open_r, self.smoothing)

        self.prev_l = (lx, ly)
        self.prev_r = (rx, ry)
        self.prev_open_l = open_l
        self.prev_open_r = open_r

        if open_l < self.blink_threshold and open_r < self.blink_threshold:
            self.blink_count += 1
        else:
            self.blink_count = 0
            self.is_blinking = False

        if self.blink_count >= 3:
            self.is_blinking = True

        if open_l < self.blink_threshold and open_r < self.blink_threshold:
            blink = "closed"
        elif self.is_blinking:
            blink = "blinking"
        else:
            blink = "open"

        x = (lx / self.frame_w + rx / self.frame_w) / 2
        y = (ly / self.frame_h + ry / self.frame_h) / 2

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

        quad = f"{h}-{v}" if not self.is_blinking else "blink"

        result["face_detected"] = True
        result["left_x"] = lx
        result["left_y"] = ly
        result["right_x"] = rx
        result["right_y"] = ry
        result["left_openness"] = round(open_l, 3)
        result["right_openness"] = round(open_r, 3)
        result["blink_state"] = blink
        result["quadrant"] = quad
        result["gaze_x"] = round(x, 4)
        result["gaze_y"] = round(y, 4)

        self.frame_count += 1
        return result

    def read_frame(self) -> Optional[np.ndarray]:
        if self.cap is None or not self.cap.isOpened():
            return None
        ret, frame = self.cap.read()
        return cv2.flip(frame, 1) if ret else None


def main():
    import argparse

    parser = argparse.ArgumentParser(description='Eye Tracker')
    parser.add_argument('--camera', type=int, default=0, help='Camera index')
    parser.add_argument('--smoothing', type=float, default=0.4, help='Smoothing')
    parser.add_argument('--blink', type=float, default=0.15, help='Blink threshold')
    args = parser.parse_args()

    print("=" * 50)
    print("EYE TRACKER (MediaPipe)")
    print("=" * 50)

    tracker = EyeTracker(camera_index=args.camera, smoothing=args.smoothing, blink_threshold=args.blink)

    if not tracker.start():
        print(f"ERROR: Cannot open camera {args.camera}")
        return

    print(f"Camera: {args.camera}")
    print(f"Resolution: {tracker.frame_w}x{tracker.frame_h}")
    print("=" * 50)

    while True:
        frame = tracker.read_frame()
        if frame is None:
            continue

        h, w = frame.shape[:2]
        result = tracker.track(frame)

        if result["face_detected"]:
            lx, ly = result["left_x"], result["left_y"]
            rx, ry = result["right_x"], result["right_y"]

            cv2.circle(frame, (lx, ly), 8, (0, 255, 0), -1)
            cv2.circle(frame, (rx, ry), 8, (0, 255, 0), -1)

            if result["quadrant"] != "blink":
                gx = int(result["gaze_x"] * w)
                gy = int(result["gaze_y"] * h)
                cv2.drawMarker(frame, (gx, gy), (0, 0, 255), cv2.MARKER_CROSS, 40, 3)

                mid_x = (lx + rx) // 2
                mid_y = (ly + ry) // 2
                cv2.arrowedLine(frame, (mid_x, mid_y), (gx, gy), (255, 255, 0), 2, tipLength=0.3)

            qcolor = (0, 255, 0) if result["quadrant"] != "blink" else (150, 150, 0)
            cv2.putText(frame, result["quadrant"].upper(), (10, 40),
                       cv2.FONT_HERSHEY_SIMPLEX, 1.0, qcolor, 2)

            cv2.putText(frame, f"Gaze: ({result['gaze_x']:.2f}, {result['gaze_y']:.2f})", (10, 80),
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)

            l_bar = int(result["left_openness"] * 60)
            r_bar = int(result["right_openness"] * 60)
            cv2.rectangle(frame, (10, h - 50), (10 + l_bar, h - 30), (0, 255, 0), -1)
            cv2.rectangle(frame, (10, h - 25), (10 + r_bar, h - 5), (0, 255, 0), -1)

            cv2.putText(frame, "L", (15, h - 45), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)
            cv2.putText(frame, "R", (15, h - 25), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)

            if result["blink_state"] == "closed":
                cv2.putText(frame, "EYES CLOSED", (w // 2 - 70, h // 2),
                           cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 0, 255), 2)
        else:
            cv2.putText(frame, "NO FACE", (w // 2 - 60, h // 2),
                       cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 2)

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

        current_quad = result["quadrant"]
        for name, px, py in quadrant_map:
            is_current = (current_quad.replace("-", "") == name.lower().replace("-", ""))
            color = (0, 255, 255) if is_current else (80, 80, 80)
            thickness = 3 if is_current else 1
            cv2.circle(frame, (px, py), 15 if is_current else 10, color, thickness)

        cv2.imshow("Eye Tracker", frame)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q'):
            break
        elif key == ord('s'):
            args.smoothing = 0.8 if args.smoothing < 0.5 else 0.3
            tracker.smoothing = args.smoothing
            print(f"Smoothing: {args.smoothing}")
        elif key == ord('b'):
            args.blink = 0.25 if args.blink < 0.2 else 0.15
            tracker.blink_threshold = args.blink
            print(f"Blink: {args.blink}")

    tracker.stop()
    cv2.destroyAllWindows()
    print("\nDone!")


if __name__ == "__main__":
    main()