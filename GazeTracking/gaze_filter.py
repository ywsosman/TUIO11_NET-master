#!/usr/bin/env python3
"""
Kalman Filter for Gaze Smoothing
Reduces jitter and provides stable gaze tracking output
"""

import numpy as np
from typing import Tuple, Optional, List
from dataclasses import dataclass
from collections import deque
import json
from pathlib import Path


@dataclass
class GazeState:
    x: float
    y: float
    vx: float = 0.0
    vy: float = 0.0
    timestamp: float = 0.0


class KalmanGazeFilter:
    def __init__(
        self,
        process_noise: float = 0.01,
        measurement_noise: float = 0.1,
        initial_uncertainty: float = 1.0
    ):
        self.process_noise = process_noise
        self.measurement_noise = measurement_noise
        
        self.state = np.array([[0.5], [0.5], [0.0], [0.0]])
        
        self.P = np.eye(4) * initial_uncertainty
        
        self.F = np.array([
            [1.0, 0.0, 1.0, 0.0],
            [0.0, 1.0, 0.0, 1.0],
            [0.0, 0.0, 1.0, 0.0],
            [0.0, 0.0, 0.0, 1.0]
        ])
        
        self.H = np.array([
            [1.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 0.0]
        ])
        
        self.Q = np.array([
            [self.process_noise, 0.0, 0.0, 0.0],
            [0.0, self.process_noise, 0.0, 0.0],
            [0.0, 0.0, self.process_noise * 0.1, 0.0],
            [0.0, 0.0, 0.0, self.process_noise * 0.1]
        ])
        
        self.R = np.eye(2) * self.measurement_noise
        
        self.is_initialized = False
        self.measurements = deque(maxlen=10)
    
    def predict(self, dt: float = 1.0) -> Tuple[float, float]:
        self.F[0, 2] = dt
        self.F[1, 3] = dt
        
        self.state = self.F @ self.state
        self.P = self.F @ self.P @ self.F.T + self.Q
        
        return float(self.state[0, 0]), float(self.state[1, 0])
    
    def update(self, x: float, y: float) -> Tuple[float, float]:
        measurement = np.array([[x], [y]])
        
        y_k = measurement - self.H @ self.state
        
        S = self.H @ self.P @ self.H.T + self.R
        
        K = self.P @ self.H.T @ np.linalg.inv(S)
        
        self.state = self.state + K @ y_k
        
        self.P = (np.eye(4) - K @ self.H) @ self.P
        
        self.is_initialized = True
        
        return float(self.state[0, 0]), float(self.state[1, 0])
    
    def process(self, x: float, y: float, dt: float = 1.0) -> Tuple[float, float]:
        if not self.is_initialized:
            self.state = np.array([[x], [y], [0.0], [0.0]])
            self.is_initialized = True
            return x, y
        
        self.predict(dt)
        
        return self.update(x, y)
    
    def reset(self):
        self.state = np.array([[0.5], [0.5], [0.0], [0.0]])
        self.P = np.eye(4)
        self.is_initialized = False
        self.measurements.clear()
    
    def get_velocity(self) -> Tuple[float, float]:
        return float(self.state[2, 0]), float(self.state[3, 0])
    
    def get_uncertainty(self) -> float:
        return float(np.trace(self.P[:2, :2]))
    
    def save(self, filepath: str) -> bool:
        try:
            data = {
                "state": self.state.flatten().tolist(),
                "P": self.P.flatten().tolist(),
                "process_noise": self.process_noise,
                "measurement_noise": self.measurement_noise,
                "is_initialized": self.is_initialized
            }
            with open(filepath, 'w') as f:
                json.dump(data, f)
            return True
        except Exception:
            return False
    
    def load(self, filepath: str) -> bool:
        try:
            if not Path(filepath).exists():
                return False
            
            with open(filepath, 'r') as f:
                data = json.load(f)
            
            self.state = np.array(data["state"]).reshape(4, 1)
            self.P = np.array(data["P"]).reshape(4, 4)
            self.process_noise = data["process_noise"]
            self.measurement_noise = data["measurement_noise"]
            self.is_initialized = data["is_initialized"]
            
            self.Q = np.array([
                [self.process_noise, 0.0, 0.0, 0.0],
                [0.0, self.process_noise, 0.0, 0.0],
                [0.0, 0.0, self.process_noise * 0.1, 0.0],
                [0.0, 0.0, 0.0, self.process_noise * 0.1]
            ])
            self.R = np.eye(2) * self.measurement_noise
            
            return True
        except Exception:
            return False


class ExponentialMovingAverageFilter:
    def __init__(self, alpha: float = 0.3):
        self.alpha = np.clip(alpha, 0.01, 0.99)
        self.x = None
        self.y = None
    
    def process(self, x: float, y: float) -> Tuple[float, float]:
        if self.x is None or self.y is None:
            self.x = x
            self.y = y
            return x, y
        
        self.x = self.alpha * x + (1 - self.alpha) * self.x
        self.y = self.alpha * y + (1 - self.alpha) * self.y
        
        return self.x, self.y
    
    def reset(self):
        self.x = None
        self.y = None


class MovingAverageFilter:
    def __init__(self, window_size: int = 5):
        self.window_size = max(1, window_size)
        self.x_buffer = deque(maxlen=self.window_size)
        self.y_buffer = deque(maxlen=self.window_size)
    
    def process(self, x: float, y: float) -> Tuple[float, float]:
        self.x_buffer.append(x)
        self.y_buffer.append(y)
        
        avg_x = sum(self.x_buffer) / len(self.x_buffer)
        avg_y = sum(self.y_buffer) / len(self.y_buffer)
        
        return avg_x, avg_y
    
    def reset(self):
        self.x_buffer.clear()
        self.y_buffer.clear()


class OneEuroFilter:
    def __init__(
        self,
        min_cutoff: float = 1.0,
        beta: float = 0.0,
        d_cutoff: float = 1.0
    ):
        self.min_cutoff = min_cutoff
        self.beta = beta
        self.d_cutoff = d_cutoff
        
        self.x_prev = None
        self.y_prev = None
        self.dx_prev = None
        self.dy_prev = None
        self.t_prev = None
    
    def _alpha(self, cutoff: float, dt: float) -> float:
        return 1.0 / (1.0 + dt / cutoff)
    
    def process(self, x: float, y: float, t: float = None) -> Tuple[float, float]:
        if self.t_prev is None:
            self.x_prev = x
            self.y_prev = y
            self.dx_prev = 0.0
            self.dy_prev = 0.0
            self.t_prev = t if t is not None else 0.0
            return x, y
        
        dt = max(0.001, t - self.t_prev)
        
        dx = (x - self.x_prev) / dt if dt > 0 else 0.0
        dy = (y - self.y_prev) / dt if dt > 0 else 0.0
        
        edx = self._alpha(self.d_cutoff, dt) * dx + (1 - self._alpha(self.d_cutoff, dt)) * self.dx_prev
        edy = self._alpha(self.d_cutoff, dt) * dy + (1 - self._alpha(self.d_cutoff, dt)) * self.dy_prev
        
        cutoff_x = self.min_cutoff + self.beta * abs(edx)
        cutoff_y = self.min_cutoff + self.beta * abs(edy)
        
        x_filtered = self._alpha(cutoff_x, dt) * x + (1 - self._alpha(cutoff_x, dt)) * self.x_prev
        y_filtered = self._alpha(cutoff_y, dt) * y + (1 - self._alpha(cutoff_y, dt)) * self.y_prev
        
        self.x_prev = x_filtered
        self.y_prev = y_filtered
        self.dx_prev = edx
        self.dy_prev = edy
        self.t_prev = t
        
        return x_filtered, y_filtered
    
    def reset(self):
        self.x_prev = None
        self.y_prev = None
        self.dx_prev = None
        self.dy_prev = None
        self.t_prev = None


class GazeFilterChain:
    def __init__(
        self,
        filter_type: str = "kalman",
        smoothing_strength: float = 0.5
    ):
        self.filter_type = filter_type
        
        if filter_type == "kalman":
            self.filter = KalmanGazeFilter(
                process_noise=0.001 * smoothing_strength,
                measurement_noise=0.1 / smoothing_strength
            )
        elif filter_type == "ema":
            alpha = np.clip(smoothing_strength, 0.1, 0.9)
            self.filter = ExponentialMovingAverageFilter(alpha=alpha)
        elif filter_type == "moving_avg":
            window = int(3 + smoothing_strength * 7)
            self.filter = MovingAverageFilter(window_size=window)
        elif filter_type == "euro":
            self.filter = OneEuroFilter(
                min_cutoff=1.0 / smoothing_strength,
                beta=smoothing_strength * 0.1
            )
        else:
            self.filter = KalmanGazeFilter()
        
        self.frame_count = 0
        self.last_timestamp = 0.0
    
    def process(self, x: float, y: float, timestamp: float = None) -> Tuple[float, float]:
        if timestamp is not None:
            dt = max(0.001, timestamp - self.last_timestamp)
            self.last_timestamp = timestamp
        else:
            dt = 1.0 / 60.0
        
        if isinstance(self.filter, KalmanGazeFilter):
            result = self.filter.process(x, y, dt)
        elif isinstance(self.filter, OneEuroFilter):
            result = self.filter.process(x, y, timestamp)
        else:
            result = self.filter.process(x, y)
        
        self.frame_count += 1
        return result
    
    def reset(self):
        self.filter.reset()
        self.frame_count = 0
        self.last_timestamp = 0.0
    
    def get_stats(self) -> dict:
        if isinstance(self.filter, KalmanGazeFilter):
            vx, vy = self.filter.get_velocity()
            uncertainty = self.filter.get_uncertainty()
            return {
                "type": "kalman",
                "vx": vx,
                "vy": vy,
                "uncertainty": uncertainty,
                "initialized": self.filter.is_initialized
            }
        else:
            return {
                "type": self.filter_type,
                "initialized": True
            }


def create_gaze_filter(filter_type: str = "kalman", strength: float = 0.5) -> GazeFilterChain:
    return GazeFilterChain(filter_type=filter_type, smoothing_strength=strength)


if __name__ == "__main__":
    import time
    
    print("Testing Gaze Filters...")
    print("-" * 50)
    
    filter_types = ["kalman", "ema", "moving_avg", "euro"]
    
    test_data = [(0.5 + np.sin(i * 0.1) * 0.1 + np.random.randn() * 0.02,
                 0.5 + np.cos(i * 0.1) * 0.1 + np.random.randn() * 0.02)
                for i in range(100)]
    
    for ftype in filter_types:
        print(f"\nFilter: {ftype.upper()}")
        print("-" * 30)
        
        filter_chain = create_gaze_filter(ftype, strength=0.5)
        
        smoothed = []
        for i, (x, y) in enumerate(test_data):
            sx, sy = filter_chain.process(x, y, timestamp=i * 0.016)
            smoothed.append((sx, sy))
        
        errors = [np.sqrt((test_data[i][0] - smoothed[i][0])**2 + 
                          (test_data[i][1] - smoothed[i][1])**2)
                  for i in range(len(test_data))]
        
        print(f"  Mean error: {np.mean(errors):.4f}")
        print(f"  Max error:  {np.max(errors):.4f}")
        print(f"  Std dev:    {np.std(errors):.4f}")
    
    print("\n" + "=" * 50)
    print("Filter test complete")
    print("=" * 50)
    
    print("\nSimulated real-time filtering (press Ctrl+C to stop):")
    
    kf = create_gaze_filter("kalman", strength=0.3)
    
    try:
        while True:
            fake_x = 0.5 + np.random.randn() * 0.05
            fake_y = 0.5 + np.random.randn() * 0.05
            
            sx, sy = kf.process(fake_x, fake_y)
            
            print(f"Raw: ({fake_x:.3f}, {fake_y:.3f}) -> Smoothed: ({sx:.3f}, {sy:.3f})", end='\r')
            
            time.sleep(0.016)
    except KeyboardInterrupt:
        print("\n\nFilter test finished.")