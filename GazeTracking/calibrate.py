#!/usr/bin/env python3
"""
9-Point Gaze Calibration System
Displays calibration targets and collects gaze samples
"""

import cv2
import numpy as np
import time
from typing import List, Callable, Optional, Tuple
from dataclasses import dataclass

from gaze_tracker import GazeTracker, GazeResult


@dataclass
class CalibrationSample:
    target_x: float
    target_y: float
    gaze_samples: List[Tuple[float, float]]
    timestamp: float
    completed: bool = False


class GazeCalibrator:
    def __init__(
        self,
        tracker: GazeTracker,
        dot_size: int = 30,
        dot_color: Tuple[int, int, int] = (0, 255, 255),
        background_color: Tuple[int, int, int] = (30, 30, 30),
        sample_duration: float = 1.5,
        samples_per_point: int = 10,
        cooldown_duration: float = 0.3
    ):
        self.tracker = tracker
        self.dot_size = dot_size
        self.dot_color = dot_color
        self.bg_color = background_color
        self.sample_duration = sample_duration
        self.samples_per_point = samples_per_point
        self.cooldown_duration = cooldown_duration
        
        self.calibration_points = self._generate_9_points()
        self.current_point_index = 0
        self.samples: List[CalibrationSample] = []
        
        self.is_calibrating = False
        self.calibration_window_name = "Gaze Calibration"
        
        self._current_gaze_samples: List[Tuple[float, float]] = []
        self._point_start_time = 0
        self._last_sample_time = 0
    
    def _generate_9_points(self) -> List[Tuple[float, float]]:
        points = []
        for row in range(3):
            for col in range(3):
                x = (col + 1) / 4.0
                y = (row + 1) / 4.0
                points.append((x, y))
        return points
    
    def _create_calibration_window(self, screen_width: int = 800, screen_height: int = 600) -> np.ndarray:
        frame = np.full((screen_height, screen_width, 3), self.bg_color, dtype=np.uint8)
        
        cv2.putText(
            frame,
            "Look at the dot and keep your head still",
            (screen_width // 2 - 200, 50),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            (200, 200, 200),
            2
        )
        
        return frame
    
    def _draw_calibration_dot(
        self,
        frame: np.ndarray,
        x: int,
        y: int,
        progress: float,
        alpha: float = 1.0
    ):
        h, w = frame.shape[:2]
        cx, cy = int(x * w), int(y * h)
        
        outer_radius = int(self.dot_size * 1.5)
        inner_radius = self.dot_size
        
        color = self.dot_color
        color_progress = int(255 * progress)
        
        cv2.circle(frame, (cx, cy), outer_radius, (color[0], color[1], color[2] - color_progress), -1)
        
        cv2.circle(frame, (cx, cy), inner_radius, color, -1)
        
        cv2.circle(frame, (cx, cy), inner_radius, (255, 255, 255), 1)
        
        progress_text = f"{int(progress * 100)}%"
        cv2.putText(frame, progress_text, (cx - 25, cy - 40), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
    
    def _draw_instruction(self, frame: np.ndarray, text: str):
        h, w = frame.shape[:2]
        cv2.rectangle(frame, (0, h - 50), (w, h), self.bg_color, -1)
        cv2.putText(frame, text, (w // 2 - 150, h - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)
    
    def _draw_progress_indicator(self, frame: np.ndarray, current: int, total: int):
        h, w = frame.shape[:2]
        bar_width = 300
        bar_height = 20
        bar_x = (w - bar_width) // 2
        bar_y = h - 80
        
        cv2.rectangle(frame, (bar_x, bar_y), (bar_x + bar_width, bar_y + bar_height), (60, 60, 60), -1)
        
        progress = current / total
        cv2.rectangle(frame, (bar_x, bar_y), (bar_x + int(bar_width * progress), bar_y + bar_height), (0, 200, 150), -1)
        
        for i, (px, py) in enumerate(self.calibration_points):
            dot_x = int(bar_x + px * bar_width)
            dot_y = bar_y + bar_height // 2
            
            if i < current:
                color = (0, 255, 0)
            elif i == current:
                color = self.dot_color
            else:
                color = (80, 80, 80)
            
            cv2.circle(frame, (dot_x, dot_y), 6, color, -1)
    
    def _collect_sample(self, gaze: GazeResult):
        if not gaze.face_detected:
            return
        
        current_time = time.time()
        if current_time - self._last_sample_time < 0.05:
            return
        
        self._current_gaze_samples.append((gaze.x, gaze.y))
        self._last_sample_time = current_time
    
    def _compute_point_calibration(self, target_x: float, target_y: float) -> Tuple[float, float]:
        if len(self._current_gaze_samples) < 3:
            return target_x, target_y
        
        median_x = np.median([s[0] for s in self._current_gaze_samples])
        median_y = np.median([s[1] for s in self._current_gaze_samples])
        
        return median_x, median_y
    
    def calibrate(
        self,
        screen_width: int = 800,
        screen_height: int = 600,
        on_progress: Optional[Callable[[int, int], None]] = None,
        on_complete: Optional[Callable[[bool], None]] = None
    ) -> bool:
        cv2.namedWindow(self.calibration_window_name)
        cv2.moveWindow(self.calibration_window_name, 100, 100)
        
        self.is_calibrating = True
        self.current_point_index = 0
        self.samples = []
        
        for i, point in enumerate(self.calibration_points):
            self._current_gaze_samples = []
            self._point_start_time = time.time()
            
            while True:
                frame = self.tracker.read_frame()
                if frame is None:
                    continue
                
                display, gaze = self.tracker.get_frame_with_gaze(frame)
                
                frame_canvas = self._create_calibration_window(screen_width, screen_height)
                
                elapsed = time.time() - self._point_start_time
                progress = min(elapsed / self.sample_duration, 1.0)
                
                self._draw_calibration_dot(frame_canvas, point[0], point[1], progress, 0.9)
                
                self._collect_sample(gaze)
                
                progress_text = f"Point {self.current_point_index + 1}/9 - Look at the center"
                self._draw_instruction(frame_canvas, progress_text)
                self._draw_progress_indicator(frame_canvas, self.current_point_index, 9)
                
                combined = cv2.addWeighted(display, 0.6, frame_canvas, 0.4, 0)
                
                cv2.imshow(self.calibration_window_name, combined)
                
                if on_progress:
                    on_progress(self.current_point_index, 9)
                
                if cv2.waitKey(1) & 0xFF == 27:
                    self.is_calibrating = False
                    cv2.destroyWindow(self.calibration_window_name)
                    if on_complete:
                        on_complete(False)
                    return False
                
                if elapsed >= self.sample_duration:
                    break
            
            calibrated_x, calibrated_y = self._compute_point_calibration(point[0], point[1])
            
            self.tracker.add_calibration_sample(point[0], point[1], calibrated_x, calibrated_y)
            
            cooldown_start = time.time()
            while time.time() - cooldown_start < self.cooldown_duration:
                frame = self.tracker.read_frame()
                if frame is None:
                    continue
                
                display, gaze = self.tracker.get_frame_with_gaze(frame)
                
                frame_canvas = self._create_calibration_window(screen_width, screen_height)
                cv2.putText(frame_canvas, "Point captured! Next...", (screen_width // 2 - 100, screen_height // 2),
                           cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
                
                self._draw_progress_indicator(frame_canvas, self.current_point_index + 1, 9)
                
                combined = cv2.addWeighted(display, 0.6, frame_canvas, 0.4, 0)
                
                cv2.imshow(self.calibration_window_name, combined)
                cv2.waitKey(1)
            
            self.current_point_index += 1
        
        success = self.tracker.compute_calibration()
        
        cv2.destroyWindow(self.calibration_window_name)
        self.is_calibrating = False
        
        if on_complete:
            on_complete(success)
        
        return success
    
    def quick_calibrate(self) -> bool:
        for point in self.calibration_points:
            self.tracker.add_calibration_sample(point[0], point[1], point[0], point[1])
        
        return self.tracker.compute_calibration()
    
    def get_accuracy_report(self) -> dict:
        if not self.tracker.is_calibrated:
            return {"error": "Not calibrated"}
        
        points = self.tracker.calibration_data
        if not points:
            return {"error": "No calibration data"}
        
        errors = []
        for point in points:
            if not point.valid:
                continue
            
            dx = abs(point.screen_x - point.gaze_x)
            dy = abs(point.screen_y - point.gaze_y)
            error = np.sqrt(dx**2 + dy**2)
            errors.append(error)
        
        if not errors:
            return {"error": "No valid samples"}
        
        return {
            "mean_error": np.mean(errors),
            "std_error": np.std(errors),
            "max_error": np.max(errors),
            "min_error": np.min(errors),
            "num_points": len(errors)
        }


def run_calibration_wizard(
    tracker: GazeTracker,
    screen_size: Tuple[int, int] = (800, 600)
) -> bool:
    calibrator = GazeCalibrator(tracker)
    
    print("\n" + "=" * 50)
    print("GAZE CALIBRATION")
    print("=" * 50)
    print("\nInstructions:")
    print("1. Sit at a comfortable distance from the camera")
    print("2. Keep your head still during calibration")
    print("3. Look at each dot as it appears")
    print("4. Press ESC to cancel")
    print("\nStarting calibration...")
    input("Press Enter to continue...")
    
    def on_progress(current, total):
        pass
    
    def on_complete(success):
        if success:
            print("\nCalibration completed successfully!")
            report = calibrator.get_accuracy_report()
            if "error" not in report:
                print(f"Mean error: {report['mean_error']:.3f}")
                print(f"Max error: {report['max_error']:.3f}")
        else:
            print("\nCalibration failed. Please try again.")
    
    success = calibrator.calibrate(
        screen_width=screen_size[0],
        screen_height=screen_size[1],
        on_progress=on_progress,
        on_complete=on_complete
    )
    
    if success:
        save_path = "gaze_calibration.json"
        tracker.save_calibration(save_path)
        print(f"\nCalibration saved to: {save_path}")
    
    return success


if __name__ == "__main__":
    from gaze_tracker import create_tracker
    
    print("Starting Gaze Calibration...")
    
    tracker = create_tracker(camera=0, api="legacy")
    tracker.start()
    
    success = run_calibration_wizard(tracker)
    
    if success:
        print("\nTesting calibrated gaze...")
        while True:
            frame = tracker.read_frame()
            if frame is None:
                continue
            
            display, result = tracker.get_frame_with_gaze(frame)
            
            cv2.imshow("Calibrated Gaze", display)
            
            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
    
    tracker.stop()
    cv2.destroyAllWindows()