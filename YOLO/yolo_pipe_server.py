#!/usr/bin/env python3
"""
YOLO Detection Named Pipe Server
Provides real-time object detection to C# applications via Windows Named Pipes
"""

import cv2
import numpy as np
import json
import base64
import time
import sys
import os
from pathlib import Path
from typing import List, Dict, Optional, Tuple
from dataclasses import dataclass, asdict
from ultralytics import YOLO

from camera_utils import print_camera_list, resolve_camera

import win32pipe
import win32file
import pywintypes


@dataclass
class Detection:
    track_id: int = -1
    class_name: str = ""
    class_id: int = -1
    confidence: float = 0.0
    x1: float = 0.0
    y1: float = 0.0
    x2: float = 0.0
    y2: float = 0.0
    source: str = "coco"


@dataclass
class DetectionResponse:
    frame_id: int
    timestamp: float
    detections: List[Detection]
    count: int
    processing_time_ms: float
    tracking_enabled: bool


class YoloPipeServer:
    def __init__(
        self,
        pipe_name: str = "YoloDetectorPipe",
        model_path: str = "yolo26m.pt",
        conf_threshold: float = 0.35,
        iou_threshold: float = 0.45,
        max_fps: int = 15,
        tracking_enabled: bool = True
    ):
        self.pipe_name = pipe_name
        self.model_path = model_path
        self.conf_threshold = conf_threshold
        self.iou_threshold = iou_threshold
        self.max_fps = max_fps
        self.tracking_enabled = tracking_enabled
        
        self.frame_id = 0
        self.running = False
        
        self.model = None
        self.tracker = None
        
        self.stats = {
            "frames_processed": 0,
            "total_detections": 0,
            "avg_processing_ms": 0,
            "uptime_seconds": 0
        }
        self.start_time = time.time()
        
        self._init_model()
        if tracking_enabled:
            self._init_tracker()
    
    def _init_model(self):
        print(f"[YOLO] Loading model: {self.model_path}")
        try:
            if not Path(self.model_path).exists():
                print(f"[YOLO] Model not found at {self.model_path}, downloading...")
            
            self.model = YOLO(self.model_path)
            print(f"[YOLO] Model loaded successfully")
            print(f"[YOLO] Classes: {len(self.model.names)}")
        except Exception as e:
            print(f"[YOLO] Failed to load model: {e}")
            raise
    
    def _init_tracker(self):
        try:
            from sort_tracker import SORTTracker
            self.tracker = SORTTracker(max_age=30, min_hits=3, iou_threshold=0.3)
            print("[YOLO] SORT tracker initialized")
        except ImportError:
            print("[YOLO] SORT tracker not available")
            self.tracking_enabled = False
    
    def detect(self, frame: np.ndarray) -> List[Detection]:
        start_time = time.perf_counter()
        
        try:
            results = self.model(
                frame,
                conf=self.conf_threshold,
                iou=self.iou_threshold,
                verbose=False
            )
            
            detections = []
            
            if results and len(results) > 0:
                result = results[0]
                boxes = result.boxes
                
                dets_for_tracker = []
                
                for i in range(len(boxes)):
                    box = boxes[i]
                    xyxy = box.xyxy[0].cpu().numpy()
                    conf = float(box.conf[0])
                    cls = int(box.cls[0])
                    class_name = result.names[cls]
                    
                    det = Detection(
                        class_name=class_name,
                        class_id=cls,
                        confidence=round(conf, 3),
                        x1=float(xyxy[0]),
                        y1=float(xyxy[1]),
                        x2=float(xyxy[2]),
                        y2=float(xyxy[3]),
                        source="coco"
                    )
                    detections.append(det)
                    
                    dets_for_tracker.append([
                        xyxy[0], xyxy[1], xyxy[2], xyxy[3], conf
                    ])
            
            if self.tracking_enabled and self.tracker and dets_for_tracker:
                tracked = self.tracker.update(np.array(dets_for_tracker))
                
                track_id_map = {}
                for track in tracked:
                    track_id = int(track[4])
                    track_id_map[(track[0], track[1], track[2], track[3])] = track_id
                
                for det in detections:
                    for tid, (x1, y1, x2, y2) in track_id_map.items():
                        if (abs(det.x1 - x1) < 5 and abs(det.y1 - y1) < 5):
                            det.track_id = tid
                            break
            
            processing_time = (time.perf_counter() - start_time) * 1000
            
            return detections, processing_time
            
        except Exception as e:
            print(f"[YOLO] Detection error: {e}")
            return [], (time.perf_counter() - start_time) * 1000
    
    def process_frame(self, frame_data: bytes) -> Tuple[Optional[np.ndarray], Optional[str]]:
        try:
            nparr = np.frombuffer(frame_data, np.uint8)
            frame = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
            return frame, None
        except Exception as e:
            return None, str(e)
    
    def create_response(self, detections: List[Detection], processing_time_ms: float) -> str:
        response = DetectionResponse(
            frame_id=self.frame_id,
            timestamp=time.time(),
            detections=detections,
            count=len(detections),
            processing_time_ms=round(processing_time_ms, 2),
            tracking_enabled=self.tracking_enabled
        )
        
        response_dict = {
            "frame_id": response.frame_id,
            "timestamp": response.timestamp,
            "count": response.count,
            "processing_ms": response.processing_time_ms,
            "tracking": response.tracking_enabled,
            "detections": [
                {
                    "track_id": d.track_id,
                    "class": d.class_name,
                    "class_id": d.class_id,
                    "confidence": d.confidence,
                    "bbox": {
                        "x1": round(d.x1, 1),
                        "y1": round(d.y1, 1),
                        "x2": round(d.x2, 1),
                        "y2": round(d.y2, 1)
                    },
                    "source": d.source
                }
                for d in response.detections
            ]
        }
        
        return json.dumps(response_dict)
    
    def handle_client(self, pipe):
        print(f"[PIPE] Client connected")
        
        buffer_size = 1024 * 1024 * 5
        
        while self.running:
            try:
                hr, data = win32file.ReadFile(pipe, buffer_size, None)
                
                if not data:
                    break
                
                try:
                    message = data.decode('utf-8').strip()
                    
                    if message == "PING":
                        response = json.dumps({"status": "ok", "frame_id": self.frame_id})
                        win32file.WriteFile(pipe, response.encode('utf-8'))
                        continue
                    
                    if message == "QUIT":
                        print("[PIPE] Quit command received")
                        response = json.dumps({"status": "quit"})
                        win32file.WriteFile(pipe, response.encode('utf-8'))
                        break
                    
                    request = json.loads(message)
                    
                    if request.get("command") == "detect":
                        frame_b64 = request.get("image", "")
                        
                        if frame_b64:
                            img_data = base64.b64decode(frame_b64)
                            frame, err = self.process_frame(img_data)
                            
                            if frame is not None:
                                detections, proc_time = self.detect(frame)
                                response = self.create_response(detections, proc_time)
                                
                                self.frame_id += 1
                                self.stats["frames_processed"] += 1
                                self.stats["total_detections"] += len(detections)
                            else:
                                response = json.dumps({
                                    "error": f"Frame decode failed: {err}",
                                    "frame_id": self.frame_id
                                })
                        else:
                            response = json.dumps({
                                "error": "No image data",
                                "frame_id": self.frame_id
                            })
                        
                        win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                except json.JSONDecodeError:
                    response = json.dumps({"error": "Invalid JSON"})
                    win32file.WriteFile(pipe, response.encode('utf-8'))
                except Exception as e:
                    response = json.dumps({"error": str(e)})
                    win32file.WriteFile(pipe, response.encode('utf-8'))
                    
            except pywintypes.error as e:
                if e.winerror == 232:
                    print("[PIPE] Client disconnected")
                    break
                raise
        
        win32file.CloseHandle(pipe)
        print("[PIPE] Client handler finished")
    
    def run(self):
        print(f"[PIPE] Starting server on pipe: {self.pipe_name}")
        print("[PIPE] Waiting for connections...")
        
        self.running = True
        
        while self.running:
            try:
                pipe = win32pipe.CreateNamedPipe(
                    f"\\\\.\\pipe\\{self.pipe_name}",
                    win32pipe.PIPE_ACCESS_DUPLEX,
                    win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_READMODE_MESSAGE | win32pipe.PIPE_WAIT,
                    1,
                    65536,
                    65536,
                    0,
                    None
                )
                
                win32pipe.ConnectNamedPipe(pipe, None)
                
                self.handle_client(pipe)
                
            except pywintypes.error as e:
                if self.running:
                    print(f"[PIPE] Pipe error: {e}")
            except Exception as e:
                print(f"[PIPE] Server error: {e}")
        
        print("[PIPE] Server stopped")
    
    def stop(self):
        self.running = False
    
    def get_stats(self) -> Dict:
        self.stats["uptime_seconds"] = time.time() - self.start_time
        return self.stats


def main():
    import argparse

    parser = argparse.ArgumentParser(description="YOLO Named Pipe Server")
    parser.add_argument("--pipe", type=str, default="YoloDetectorPipe", help="Pipe name")
    parser.add_argument("--model", type=str, default="yolo26m.pt", help="YOLO model path")
    parser.add_argument("--conf", type=float, default=0.35, help="Confidence threshold")
    parser.add_argument("--iou", type=float, default=0.45, help="IoU threshold")
    parser.add_argument("--fps", type=int, default=15, help="Max FPS")
    parser.add_argument("--no-tracking", action="store_true", help="Disable SORT tracking")
    parser.add_argument(
        "--list-cameras", action="store_true",
        help="List all detected cameras and exit.",
    )
    args = parser.parse_args()

    if args.list_cameras:
        print_camera_list()
        return

    print("=" * 60)
    print("YOLO Named Pipe Server")
    print("=" * 60)
    print(f"Pipe: {args.pipe}")
    print(f"Model: {args.model}")
    print(f"Confidence: {args.conf}")
    print(f"IoU: {args.iou}")
    print(f"Max FPS: {args.fps}")
    print(f"Tracking: {not args.no_tracking}")
    print("=" * 60)

    server = YoloPipeServer(
        pipe_name=args.pipe,
        model_path=args.model,
        conf_threshold=args.conf,
        iou_threshold=args.iou,
        max_fps=args.fps,
        tracking_enabled=not args.no_tracking,
    )

    try:
        server.run()
    except KeyboardInterrupt:
        print("\n[PIPE] Shutting down...")
        server.stop()


if __name__ == "__main__":
    main()