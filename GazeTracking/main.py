#!/usr/bin/env python3
"""
Eye Tracker - Main Demo
Simple, fast, reliable eye tracking with MediaPipe
"""

import cv2
import numpy as np
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from eye_tracker_simple import EyeTracker


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