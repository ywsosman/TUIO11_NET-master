#!/usr/bin/env python3
"""
Gaze Tracking Pipe Server
Provides gaze data to C# applications via Windows Named Pipes
Works with the unified gaze_tracker module
"""

import cv2
import numpy as np
import json
import time
import sys
from pathlib import Path

try:
    import win32pipe
    import win32file
    import pywintypes
    PYWIN32_AVAILABLE = True
except ImportError:
    PYWIN32_AVAILABLE = False
    print("[WARN] pywin32 not available, pipe server disabled")
    print("[INFO] Install pywin32: pip install pywin32")

sys.path.insert(0, str(Path(__file__).parent))

from gaze_tracker import create_tracker, GazeAPI
from gaze_filter import create_gaze_filter
from calibrate import run_calibration_wizard


class GazePipeServer:
    def __init__(self, pipe_name: str = "GazeTrackerPipe", camera: int = 0):
        self.pipe_name = pipe_name
        self.camera = camera
        self.running = False
        
        self.tracker = create_tracker(camera=camera, api="legacy")
        self.gaze_filter = create_gaze_filter("kalman", strength=0.5)
        
        self.current_gaze = {"x": 0.5, "y": 0.5, "raw_x": 0.5, "raw_y": 0.5,
                           "yaw": 0, "pitch": 0, "face_detected": False,
                           "timestamp": time.time()}
        
        self.stats = {
            "frames_processed": 0,
            "uptime_seconds": 0,
            "calibrated": False
        }
        self.start_time = time.time()
    
    def start(self):
        if not self.tracker.start():
            print(f"[GAZE] Failed to open camera {self.camera}")
            return False
        
        print(f"[GAZE] Camera {self.camera} opened")
        self.running = True
        return True
    
    def stop(self):
        self.running = False
        self.tracker.stop()
    
    def process_frame(self):
        frame = self.tracker.read_frame()
        if frame is None:
            return None
        
        display, gaze = self.tracker.get_frame_with_gaze(frame)
        
        smoothed_x, smoothed_y = self.gaze_filter.process(gaze.x, gaze.y)
        
        self.current_gaze = {
            "x": round(smoothed_x, 4),
            "y": round(smoothed_y, 4),
            "raw_x": round(gaze.x, 4),
            "raw_y": round(gaze.y, 4),
            "yaw": round(gaze.yaw, 4),
            "pitch": round(gaze.pitch, 4),
            "face_detected": gaze.face_detected,
            "confidence": round(gaze.confidence, 2),
            "timestamp": time.time()
        }
        
        self.stats["frames_processed"] += 1
        self.stats["uptime_seconds"] = time.time() - self.start_time
        self.stats["calibrated"] = self.tracker.is_calibrated
        
        return self.current_gaze
    
    def run_pipe_loop(self):
        if not PYWIN32_AVAILABLE:
            print("[GAZE] Pipe server not available")
            return
        
        print(f"[GAZE] Starting pipe server on: {self.pipe_name}")
        
        while self.running:
            try:
                pipe = win32pipe.CreateNamedPipe(
                    f"\\\\.\\pipe\\{self.pipe_name}",
                    win32pipe.PIPE_ACCESS_DUPLEX,
                    win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_READMODE_MESSAGE | win32pipe.PIPE_WAIT,
                    1, 65536, 65536, 0, None
                )
                
                win32pipe.ConnectNamedPipe(pipe, None)
                print("[GAZE] Client connected")
                
                while self.running:
                    try:
                        hr, data = win32file.ReadFile(pipe, 4096, None)
                        
                        if not data:
                            break
                        
                        message = data.decode('utf-8').strip()
                        
                        if message == "PING":
                            response = json.dumps({"status": "ok", "running": self.running})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "GET_GAZE":
                            gaze_data = self.process_frame()
                            if gaze_data:
                                response = json.dumps(gaze_data)
                            else:
                                response = json.dumps({"error": "No frame"})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "GET_STATS":
                            response = json.dumps(self.stats)
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "CALIBRATE":
                            success = run_calibration_wizard(self.tracker)
                            response = json.dumps({"calibration_success": success})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "RESET_CALIBRATION":
                            self.tracker.reset_calibration()
                            response = json.dumps({"reset": True})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "SAVE_CALIBRATION":
                            success = self.tracker.save_calibration("gaze_calibration.json")
                            response = json.dumps({"saved": success})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "LOAD_CALIBRATION":
                            success = self.tracker.load_calibration("gaze_calibration.json")
                            response = json.dumps({"loaded": success})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                        
                        elif message == "QUIT":
                            response = json.dumps({"status": "quit"})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                            break
                        
                        else:
                            response = json.dumps({"error": "Unknown command"})
                            win32file.WriteFile(pipe, response.encode('utf-8'))
                    
                    except pywintypes.error as e:
                        if e.winerror == 232:
                            print("[GAZE] Client disconnected")
                            break
                        raise
                
                win32file.CloseHandle(pipe)
            
            except Exception as e:
                print(f"[GAZE] Pipe error: {e}")
                time.sleep(0.5)
        
        print("[GAZE] Pipe server stopped")
    
    def run_visual_loop(self):
        print("[GAZE] Running visual mode (no pipe)")
        
        while self.running:
            frame = self.tracker.read_frame()
            if frame is None:
                continue
            
            display, gaze = self.tracker.get_frame_with_gaze(frame)
            
            smoothed_x, smoothed_y = self.gaze_filter.process(gaze.x, gaze.y)
            
            h, w = display.shape[:2]
            raw_x, raw_y = int(gaze.x * w), int(gaze.y * h)
            smooth_x, smooth_y = int(smoothed_x * w), int(smoothed_y * h)
            
            cv2.circle(display, (raw_x, raw_y), 25, (255, 0, 0), 2)
            cv2.drawMarker(display, (smooth_x, smooth_y), (0, 255, 0), cv2.MARKER_CROSS, 30, 3)
            
            status = "CALIBRATED" if self.tracker.is_calibrated else "UNCALIBRATED"
            color = (0, 255, 0) if self.tracker.is_calibrated else (150, 150, 0)
            cv2.putText(display, f"[{status}]", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1)
            cv2.putText(display, f"Gaze: ({smoothed_x:.3f}, {smoothed_y:.3f})", (10, 50), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, (100, 255, 100), 1)
            
            cv2.imshow('Gaze Pipe Server', display)
            
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q'):
                self.running = False
            elif key == ord('c'):
                run_calibration_wizard(self.tracker)
        
        cv2.destroyAllWindows()
    
    def run(self, visual_mode: bool = True):
        if not self.start():
            return
        
        if visual_mode and PYWIN32_AVAILABLE:
            from threading import Thread
            pipe_thread = Thread(target=self.run_pipe_loop, daemon=True)
            pipe_thread.start()
            self.run_visual_loop()
            self.running = False
        elif visual_mode:
            self.run_visual_loop()
        else:
            self.run_pipe_loop()


def main():
    import argparse
    
    parser = argparse.ArgumentParser(description='Gaze Tracking Pipe Server')
    parser.add_argument('--pipe', type=str, default='GazeTrackerPipe', help='Pipe name')
    parser.add_argument('--camera', type=int, default=0, help='Camera index')
    parser.add_argument('--no-visual', action='store_true', help='Pipe only mode')
    parser.add_argument('--load-cal', type=str, help='Load calibration file')
    args = parser.parse_args()
    
    print("=" * 50)
    print("GAZE TRACKING PIPE SERVER")
    print("=" * 50)
    print(f"Pipe: {args.pipe}")
    print(f"Camera: {args.camera}")
    print("=" * 50)
    
    server = GazePipeServer(pipe_name=args.pipe, camera=args.camera)
    
    if args.load_cal:
        server.tracker.load_calibration(args.load_cal)
        print(f"Loaded calibration: {args.load_cal}")
    
    try:
        server.run(visual_mode=not args.no_visual)
    except KeyboardInterrupt:
        print("\n[GAZE] Shutting down...")
        server.stop()


if __name__ == "__main__":
    main()