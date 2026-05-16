#!/usr/bin/env python3
"""
SORT (Simple Online and Realtime Tracking) Object Tracker
Provides stable object tracking across frames with unique IDs
"""

import numpy as np
from typing import List, Tuple, Optional, Dict
from dataclasses import dataclass
from collections import deque
import time


@dataclass
class Track:
    track_id: int
    bbox: np.ndarray
    confidence: float
    class_id: int
    class_name: str
    hits: int
    age: int
    time_since_update: int
    velocity: np.ndarray
    history: deque


class SORTTracker:
    def __init__(
        self,
        max_age: int = 30,
        min_hits: int = 3,
        iou_threshold: float = 0.3,
        track_buffer_size: int = 100
    ):
        self.max_age = max_age
        self.min_hits = min_hits
        self.iou_threshold = iou_threshold
        
        self.tracks: List[Track] = []
        self.track_id_count = 0
        
        self.frame_count = 0
        
        self.kalman_filters: Dict[int, 'KalmanBoxTracker'] = {}
        
        self.detection_history: deque = deque(maxlen=track_buffer_size)
        
        self.stats = {
            "total_tracks": 0,
            "active_tracks": 0,
            "frames_processed": 0,
            "avg_detections": 0
        }
    
    def _iou(self, box1: np.ndarray, box2: np.ndarray) -> float:
        x1_min, y1_min, x1_max, y1_max = box1
        x2_min, y2_min, x2_max, y2_max = box2
        
        inter_x_min = max(x1_min, x2_min)
        inter_y_min = max(y1_min, y2_min)
        inter_x_max = min(x1_max, x2_max)
        inter_y_max = min(y1_max, y2_max)
        
        if inter_x_max < inter_x_min or inter_y_max < inter_y_min:
            return 0.0
        
        inter_area = (inter_x_max - inter_x_min) * (inter_y_max - inter_y_min)
        
        box1_area = (x1_max - x1_min) * (y1_max - y1_min)
        box2_area = (x2_max - x2_min) * (y2_max - y2_min)
        
        union_area = box1_area + box2_area - inter_area
        
        return inter_area / union_area if union_area > 0 else 0.0
    
    def _associate_detections_with_tracks(
        self,
        detections: np.ndarray
    ) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        if len(self.tracks) == 0:
            return np.arange(len(detections)), np.array([]), np.arange(len(detections))
        
        iou_matrix = np.zeros((len(detections), len(self.tracks)), dtype=np.float32)
        
        for d, det in enumerate(detections):
            for t, track in enumerate(self.tracks):
                iou_matrix[d, t] = self._iou(det[:4], track.bbox)
        
        matched_indices = []
        unmatched_detections = []
        unmatched_tracks = []
        
        used_det = set()
        used_trk = set()
        
        for d in np.argsort(-iou_matrix.max(axis=1)):
            if d in used_det:
                continue
            
            matches = np.where(iou_matrix[d] >= self.iou_threshold)[0]
            
            if len(matches) > 0:
                best_t = matches[np.argmax(iou_matrix[d, matches])]
                
                if best_t not in used_trk:
                    matched_indices.append((d, best_t))
                    used_det.add(d)
                    used_trk.add(best_t)
        
        for d in range(len(detections)):
            if d not in used_det:
                unmatched_detections.append(d)
        
        for t in range(len(self.tracks)):
            if t not in used_trk:
                unmatched_tracks.append(t)
        
        if matched_indices:
            matched = np.array(matched_indices)
            return matched[:, 0], matched[:, 1], unmatched_detections
        else:
            return np.array([]), np.array([]), np.arange(len(detections))
    
    def update(self, detections: np.ndarray) -> np.ndarray:
        self.frame_count += 1
        
        if len(detections) == 0:
            for track in self.tracks:
                track.time_since_update += 1
            
            self.tracks = [t for t in self.tracks if t.time_since_update < self.max_age]
            
            return np.array([])
        
        matched_dets, matched_trks, unmatched_dets = self._associate_detections_with_tracks(detections)
        matched_trk_set = set(int(t) for t in matched_trks)
        unmatched_tracks = [i for i in range(len(self.tracks)) if i not in matched_trk_set]

        for d_idx, t_idx in zip(matched_dets, matched_trks):
            det = detections[d_idx]
            track = self.tracks[t_idx]
            
            if len(det) >= 5:
                new_confidence = det[4]
            else:
                new_confidence = track.confidence
            
            track.bbox = det[:4]
            track.confidence = new_confidence
            track.hits += 1
            track.age += 1
            track.time_since_update = 0
            
            track.history.append(det[:4])
            if len(track.history) > 10:
                track.history.popleft()
        
        for d_idx in unmatched_dets:
            det = detections[d_idx]
            new_track = Track(
                track_id=self.track_id_count,
                bbox=det[:4],
                confidence=det[4] if len(det) >= 5 else 1.0,
                class_id=-1,
                class_name="",
                hits=1,
                age=1,
                time_since_update=0,
                velocity=np.zeros(4),
                history=deque(maxlen=10)
            )
            new_track.history.append(det[:4])
            
            self.tracks.append(new_track)
            self.track_id_count += 1
            self.stats["total_tracks"] += 1
        
        for t_idx in unmatched_tracks:
            self.tracks[t_idx].time_since_update += 1
        
        self.tracks = [t for t in self.tracks if t.time_since_update < self.max_age]
        
        self.stats["active_tracks"] = len(self.tracks)
        self.stats["frames_processed"] = self.frame_count
        
        confirmed_tracks = []
        for track in self.tracks:
            if track.hits >= self.min_hits:
                x1, y1, x2, y2 = track.bbox
                confirmed_tracks.append([x1, y1, x2, y2, track.track_id])
        
        return np.array(confirmed_tracks) if confirmed_tracks else np.array([])
    
    def get_active_tracks(self) -> List[Dict]:
        return [
            {
                "track_id": t.track_id,
                "bbox": t.bbox.tolist(),
                "confidence": t.confidence,
                "age": t.age,
                "hits": t.hits
            }
            for t in self.tracks if t.hits >= self.min_hits
        ]
    
    def get_track_history(self, track_id: int) -> List[np.ndarray]:
        for track in self.tracks:
            if track.track_id == track_id:
                return list(track.history)
        return []
    
    def reset(self):
        self.tracks = []
        self.track_id_count = 0
        self.frame_count = 0
        self.kalman_filters.clear()
        self.detection_history.clear()
        self.stats = {
            "total_tracks": 0,
            "active_tracks": 0,
            "frames_processed": 0,
            "avg_detections": 0
        }
    
    def get_stats(self) -> Dict:
        return {
            **self.stats,
            "total_tracks": self.track_id_count,
            "current_active": len(self.tracks)
        }


class KalmanBoxTracker:
    def __init__(self, bbox: np.ndarray):
        self.kalman = np.array([
            [bbox[0], 0, 0, 0, bbox[2] - bbox[0], 0],
            [0, bbox[1], 0, 0, 0, bbox[3] - bbox[1]],
            [0, 0, 1, 0, 0, 0],
            [0, 0, 0, 1, 0, 0],
            [0, 0, 0, 0, 1, 0],
            [0, 0, 0, 0, 0, 1]
        ], dtype=np.float32)
        
        self.time_since_update = 0
        self.id = -1
        self.hits = 0
        self.hit_streak = 0
        self.age = 0
    
    def update(self, bbox: np.ndarray):
        self.time_since_update = 0
        self.hits += 1
        self.hit_streak += 1
    
    def predict(self):
        self.age += 1
        self.time_since_update += 1
        return self.kalman
    
    def get_state(self) -> np.ndarray:
        return self.kalman[:4]


def test_tracker():
    print("Testing SORT Tracker...")
    print("-" * 40)
    
    tracker = SORTTracker(max_age=30, min_hits=3, iou_threshold=0.3)
    
    frame1_dets = np.array([
        [100, 100, 200, 200, 0.9],
        [300, 300, 400, 400, 0.85],
        [500, 500, 600, 600, 0.8]
    ])
    
    print("Frame 1 detections:", len(frame1_dets))
    tracks = tracker.update(frame1_dets)
    print(f"Tracks after frame 1: {len(tracks)} (need {tracker.min_hits} hits to confirm)")
    
    for i in range(5):
        shift = i * 5
        frame_dets = np.array([
            [100 + shift, 100 + shift, 200 + shift, 200 + shift, 0.9],
            [300 + shift, 300 + shift, 400 + shift, 400 + shift, 0.85]
        ])
        tracks = tracker.update(frame_dets)
        print(f"Frame {i+2}: {len(tracks)} confirmed tracks")
    
    print("\nActive tracks:", tracker.get_active_tracks())
    print("\nTracker stats:", tracker.get_stats())
    
    print("\n" + "=" * 40)
    print("SORT Tracker test complete!")


if __name__ == "__main__":
    test_tracker()