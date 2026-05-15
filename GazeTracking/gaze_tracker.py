#!/usr/bin/env python3
"""
Unified Gaze Tracking Module
Supports both legacy mp.solutions.face_mesh and new FaceLandmarker API
Provides accurate eye gaze estimation with head pose compensation
"""

import cv2
import numpy as np
from typing import Tuple, Optional, List, Dict
from dataclasses import dataclass
from enum import Enum
import json
from pathlib import Path


class GazeAPI(Enum):
    LEGACY = "legacy"       # mp.solutions.face_mesh (old API)
    NEW = "new"             # mp.tasks.vision.FaceLandmarker (new API)


@dataclass
class GazeResult:
    x: float
    y: float
    yaw: float          # Horizontal gaze angle (radians)
    pitch: float        # Vertical gaze angle (radians)
    head_yaw: float     # Head rotation yaw
    head_pitch: float   # Head rotation pitch
    confidence: float
    left_pupil: Tuple[int, int]
    right_pupil: Tuple[int, int]
    face_detected: bool


@dataclass
class CalibrationPoint:
    screen_x: float     # Target screen position (normalized 0-1)
    screen_y: float
    gaze_x: float       # Observed gaze position
    gaze_y: float
    valid: bool = False


class GazeTracker:
    def __init__(
        self,
        camera_index: int = 0,
        api: GazeAPI = GazeAPI.LEGACY,
        model_path: str = "face_landmarker.task",
        head_pose_enabled: bool = True
    ):
        self.camera_index = camera_index
        self.api = api
        self.model_path = model_path
        self.head_pose_enabled = head_pose_enabled
        self.cap = None
        
        self.calibration_matrix = None
        self.calibration_data: List[CalibrationPoint] = []
        self.is_calibrated = False
        
        self._3d_model_points = np.array([
            (0.0, 0.0, 0.0),           # Nose tip
            (0, -63.6, -12.5),         # Chin
            (-43.3, 32.7, -26),        # Left eye, left corner
            (43.3, 32.7, -26),         # Right eye, right corner
            (-28.9, -28.9, -24.1),     # Left mouth corner
            (28.9, -28.9, -24.1)       # Right mouth corner
        ], dtype="double")
        
        self.eye_ball_center_right = np.array([[-29.05], [32.7], [-39.5]])
        self.eye_ball_center_left = np.array([[29.05], [32.7], [-39.5]])
        
        self._setup_detector()
    
    def _setup_detector(self):
        if self.api == GazeAPI.NEW:
            from mediapipe.tasks import python
            from mediapipe.tasks.vision import FaceLandmarker, FaceLandmarkerOptions
            
            base_options = python.BaseOptions(model_asset_path=self.model_path)
            options = FaceLandmarkerOptions(base_options=base_options, num_faces=1)
            self.detector = FaceLandmarker.create_from_options(options)
        else:
            import mediapipe as mp
            self.mp_face_mesh = mp.solutions.face_mesh
            self.detector = self.mp_face_mesh.FaceMesh(
                max_num_faces=1,
                refine_landmarks=True,
                min_detection_confidence=0.5,
                min_tracking_confidence=0.5
            )
    
    def start(self):
        if self.cap is None or not self.cap.isOpened():
            self.cap = cv2.VideoCapture(self.camera_index)
            self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
            self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
            self.cap.set(cv2.CAP_PROP_FPS, 60)
        return self.cap.isOpened()
    
    def stop(self):
        if self.cap and self.cap.isOpened():
            self.cap.release()
    
    def _get_image_points_legacy(self, landmarks, frame_shape):
        h, w = frame_shape[:2]
        indices = [4, 152, 263, 33, 287, 57]
        return np.array([
            (int(landmarks[idx].x * w), int(landmarks[idx].y * h))
            for idx in indices
        ], dtype="double")
    
    def _get_image_points_new(self, landmarks, frame_shape):
        indices = [4, 152, 263, 33, 287, 57]
        return np.array([
            (int(landmarks[idx].x * frame_shape[1]), int(landmarks[idx].y * frame_shape[0]))
            for idx in indices
        ], dtype="double")
    
    def _get_iris_landmarks(self, landmarks, api: GazeAPI):
        if api == GazeAPI.LEGACY:
            left_iris = landmarks[468]
            right_iris = landmarks[473]
            return (int(left_iris.x * 1280), int(left_iris.y * 720)), \
                   (int(right_iris.x * 1280), int(right_iris.y * 720))
        else:
            left_iris = landmarks[468]
            right_iris = landmarks[473]
            return (int(left_iris.x * 1280), int(left_iris.y * 720)), \
                   (int(right_iris.x * 1280), int(right_iris.y * 720))
    
    def estimate_gaze(self, frame) -> GazeResult:
        h, w = frame.shape[:2]
        
        if self.api == GazeAPI.NEW:
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            import mediapipe as mp
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            results = self.detector.detect(mp_image)
            
            if not results.face_landmarks:
                return GazeResult(0.5, 0.5, 0, 0, 0, 0, 0, (0, 0), (0, 0), False)
            
            landmarks = results.face_landmarks[0]
            image_points = self._get_image_points_new(landmarks, (h, w))
        else:
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.detector.process(rgb)
            
            if not results.multi_face_landmarks:
                return GazeResult(0.5, 0.5, 0, 0, 0, 0, 0, (0, 0), (0, 0), False)
            
            landmarks = results.multi_face_landmarks[0]
            image_points = self._get_image_points_legacy(landmarks, (h, w))
        
        left_pupil, right_pupil = self._get_iris_landmarks(landmarks, self.api)
        
        focal_length = w
        center = (w / 2, h / 2)
        camera_matrix = np.array([
            [focal_length, 0, center[0]],
            [0, focal_length, center[1]],
            [0, 0, 1]
        ], dtype="double")
        
        dist_coeffs = np.zeros((4, 1))
        success, rotation_vector, translation_vector = cv2.solvePnP(
            self._3d_model_points,
            image_points,
            camera_matrix,
            dist_coeffs,
            flags=cv2.SOLVEPNP_ITERATIVE
        )
        
        if not success:
            return GazeResult(0.5, 0.5, 0, 0, 0, 0, 0, left_pupil, right_pupil, True)
        
        rmat, _ = cv2.Rodrigues(rotation_vector)
        angles, _, _, _, _, _ = cv2.RQDecomp3x3(rmat)
        head_yaw = angles[1] if len(angles) > 1 else 0
        head_pitch = angles[0] if len(angles) > 0 else 0
        
        image_points_3d = np.array([
            (int(image_points[i][0]), int(image_points[i][1]), 0)
            for i in range(len(image_points))
        ], dtype="double")
        
        transformation, _ = cv2.estimateAffine3D(
            np.array([(p[0], p[1], 0) for p in image_points], dtype="double"),
            self._3d_model_points
        )
        
        if transformation is None:
            return GazeResult(0.5, 0.5, 0, 0, head_yaw, head_pitch, 0.5, left_pupil, right_pupil, True)
        
        pupil_world_cord = transformation @ np.array([[left_pupil[0], left_pupil[1], 0, 1]]).T
        
        S = self.eye_ball_center_left + (pupil_world_cord - self.eye_ball_center_left) * 10
        
        (eye_pupil2D, _) = cv2.projectPoints(
            (int(S[0]), int(S[1]), int(S[2])),
            rotation_vector,
            translation_vector,
            camera_matrix,
            dist_coeffs
        )
        
        (head_pose, _) = cv2.projectPoints(
            (int(pupil_world_cord[0]), int(pupil_world_cord[1]), 40),
            rotation_vector,
            translation_vector,
            camera_matrix,
            dist_coeffs
        )
        
        gaze_2d = left_pupil + (eye_pupil2D[0][0] - left_pupil) - (head_pose[0][0] - left_pupil)
        
        norm_x = np.clip(gaze_2d[0] / w, 0, 1)
        norm_y = np.clip(gaze_2d[1] / h, 0, 1)
        
        if self.is_calibrated and self.calibration_matrix is not None:
            norm_x, norm_y = self._apply_calibration(norm_x, norm_y)
        
        avg_iris_x = (left_pupil[0] + right_pupil[0]) / 2
        avg_iris_y = (left_pupil[1] + right_pupil[1]) / 2
        eye_center_x = (image_points[2][0] + image_points[3][0]) / 2
        eye_center_y = (image_points[2][1] + image_points[3][1]) / 2
        
        dx = avg_iris_x - eye_center_x
        dy = avg_iris_y - eye_center_y
        yaw = np.arctan2(dx, 100)
        pitch = np.arctan2(dy, 100)
        
        return GazeResult(
            x=norm_x,
            y=norm_y,
            yaw=yaw,
            pitch=pitch,
            head_yaw=head_yaw,
            head_pitch=head_pitch,
            confidence=0.85,
            left_pupil=left_pupil,
            right_pupil=right_pupil,
            face_detected=True
        )
    
    def _apply_calibration(self, x, y) -> Tuple[float, float]:
        x_mapped = self.calibration_matrix[0][0] * x + self.calibration_matrix[0][1] * y + self.calibration_matrix[0][2]
        y_mapped = self.calibration_matrix[1][0] * x + self.calibration_matrix[1][1] * y + self.calibration_matrix[1][2]
        return np.clip(x_mapped, 0, 1), np.clip(y_mapped, 0, 1)
    
    def add_calibration_sample(self, screen_x: float, screen_y: float, gaze_x: float, gaze_y: float):
        self.calibration_data.append(CalibrationPoint(screen_x, screen_y, gaze_x, gaze_y, True))
    
    def compute_calibration(self):
        if len(self.calibration_data) < 4:
            return False
        
        valid_points = [p for p in self.calibration_data if p.valid]
        if len(valid_points) < 4:
            return False
        
        src_points = np.array([[p.gaze_x, p.gaze_y] for p in valid_points], dtype=np.float32)
        dst_points = np.array([[p.screen_x, p.screen_y] for p in valid_points], dtype=np.float32)
        
        self.calibration_matrix, _ = cv2.findHomography(src_points, dst_points, cv2.RANSAC, 5.0)
        
        if self.calibration_matrix is not None:
            self.is_calibrated = True
            return True
        return False
    
    def save_calibration(self, filepath: str):
        if not self.is_calibrated:
            return False
        
        data = {
            "calibration_matrix": self.calibration_matrix.tolist() if self.calibration_matrix is not None else None,
            "points": [
                {
                    "screen_x": p.screen_x,
                    "screen_y": p.screen_y,
                    "gaze_x": p.gaze_x,
                    "gaze_y": p.gaze_y,
                    "valid": p.valid
                }
                for p in self.calibration_data
            ]
        }
        
        with open(filepath, 'w') as f:
            json.dump(data, f)
        return True
    
    def load_calibration(self, filepath: str) -> bool:
        if not Path(filepath).exists():
            return False
        
        with open(filepath, 'r') as f:
            data = json.load(f)
        
        if data.get("calibration_matrix"):
            self.calibration_matrix = np.array(data["calibration_matrix"])
            self.is_calibrated = True
        
        self.calibration_data = [
            CalibrationPoint(
                p["screen_x"], p["screen_y"],
                p["gaze_x"], p["gaze_y"],
                p["valid"]
            )
            for p in data.get("points", [])
        ]
        
        return self.is_calibrated
    
    def reset_calibration(self):
        self.calibration_matrix = None
        self.calibration_data = []
        self.is_calibrated = False
    
    def read_frame(self) -> Optional[np.ndarray]:
        if self.cap is None or not self.cap.isOpened():
            return None
        
        ret, frame = self.cap.read()
        if not ret:
            return None
        
        return cv2.flip(frame, 1)
    
    def get_frame_with_gaze(self, frame) -> Tuple[np.ndarray, GazeResult]:
        result = self.estimate_gaze(frame)
        
        display = frame.copy()
        
        if result.face_detected:
            cv2.circle(display, result.left_pupil, 8, (0, 255, 0), -1)
            cv2.circle(display, result.right_pupil, 8, (0, 255, 0), -1)
            
            gaze_x = int(result.x * display.shape[1])
            gaze_y = int(result.y * display.shape[0])
            cv2.drawMarker(display, (gaze_x, gaze_y), (0, 0, 255), cv2.MARKER_CROSS, 40, 2)
            
            info = f"Yaw:{result.yaw*180/np.pi:.1f} Pitch:{result.pitch*180/np.pi:.1f}"
            cv2.putText(display, info, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            
            status = "[CALIBRATED]" if self.is_calibrated else "[UNCALIBRATED]"
            cv2.putText(display, status, (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
        
        return display, result


def create_tracker(
    camera: int = 0,
    api: str = "legacy",
    model_path: str = "face_landmarker.task"
) -> GazeTracker:
    api_enum = GazeAPI.LEGACY if api.lower() == "legacy" else GazeAPI.NEW
    return GazeTracker(camera, api_enum, model_path)


if __name__ == "__main__":
    tracker = create_tracker(camera=0, api="legacy")
    tracker.start()
    
    print("Gaze Tracker - Press 'q' to quit, 'c' to clear calibration, 's' to save calibration")
    
    while True:
        frame = tracker.read_frame()
        if frame is None:
            continue
        
        display, result = tracker.get_frame_with_gaze(frame)
        
        cv2.imshow("Gaze Tracker", display)
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            break
        elif key == ord('c'):
            tracker.reset_calibration()
            print("Calibration cleared")
        elif key == ord('s'):
            if tracker.is_calibrated:
                tracker.save_calibration("gaze_calibration.json")
                print("Calibration saved")
    
    tracker.stop()
    cv2.destroyAllWindows()