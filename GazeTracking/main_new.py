#!/usr/bin/env python3
"""
Gaze Tracking Demo - Uses unified gaze_tracker module
Supports calibration and Kalman smoothing
"""

import cv2
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from gaze_tracker import create_tracker, GazeAPI
from gaze_filter import create_gaze_filter


def main():
    import argparse
    
    parser = argparse.ArgumentParser(description='Gaze Tracking with Calibration')
    parser.add_argument('--camera', type=int, default=0, help='Camera index (default: 0)')
    parser.add_argument('--api', type=str, default='legacy', choices=['legacy', 'new'],
                       help='MediaPipe API to use (default: legacy)')
    parser.add_argument('--calibrate', action='store_true', help='Run calibration on startup')
    parser.add_argument('--load-cal', type=str, help='Load calibration from file')
    parser.add_argument('--filter', type=str, default='kalman', 
                       choices=['kalman', 'ema', 'moving_avg', 'euro'],
                       help='Filter type (default: kalman)')
    parser.add_argument('--filter-strength', type=float, default=0.5,
                       help='Filter smoothing strength 0.1-1.0 (default: 0.5)')
    args = parser.parse_args()
    
    print("=" * 60)
    print("GAZE TRACKING SYSTEM")
    print("=" * 60)
    print(f"Camera: {args.camera}")
    print(f"API: {args.api}")
    print(f"Filter: {args.filter}")
    print(f"Filter Strength: {args.filter_strength}")
    print("=" * 60)
    
    tracker = create_tracker(camera=args.camera, api=args.api)
    
    if not tracker.start():
        print(f"ERROR: Cannot open camera {args.camera}")
        return
    
    print("Camera opened successfully")
    
    if args.load_cal and Path(args.load_cal).exists():
        if tracker.load_calibration(args.load_cal):
            print(f"Calibration loaded: {args.load_cal}")
        else:
            print("Failed to load calibration")
    
    gaze_filter = create_gaze_filter(args.filter, args.filter_strength)
    
    if args.calibrate:
        from calibrate import run_calibration_wizard
        run_calibration_wizard(tracker)
    
    print("\nControls:")
    print("  [C] - Run calibration")
    print("  [F] - Toggle filter (kalman/ema/euro)")
    print("  [S] - Save calibration")
    print("  [L] - Load calibration")
    print("  [R] - Reset filter")
    print("  [Q] - Quit")
    print("-" * 60)
    
    filter_types = ['kalman', 'ema', 'euro']
    current_filter_idx = 0
    
    while True:
        frame = tracker.read_frame()
        if frame is None:
            continue
        
        display, gaze = tracker.get_frame_with_gaze(frame)
        
        smoothed_x, smoothed_y = gaze_filter.process(gaze.x, gaze.y)
        
        h, w = display.shape[:2]
        raw_x, raw_y = int(gaze.x * w), int(gaze.y * h)
        smooth_x, smooth_y = int(smoothed_x * w), int(smoothed_y * h)
        
        cv2.circle(display, (raw_x, raw_y), 25, (255, 0, 0), 2)
        cv2.drawMarker(display, (smooth_x, smooth_y), (0, 255, 0), cv2.MARKER_CROSS, 30, 3)
        
        cv2.putText(display, f"Raw: ({raw_x}, {raw_y})", (10, 30), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 100, 100), 1)
        cv2.putText(display, f"Smooth: ({smooth_x}, {smooth_y})", (10, 50), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (100, 255, 100), 1)
        
        filter_stats = gaze_filter.get_stats()
        cv2.putText(display, f"Filter: {filter_stats['type']}", (10, 70), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 0), 1)
        
        status = "[CALIBRATED]" if tracker.is_calibrated else "[UNCALIBRATED]"
        color = (0, 255, 0) if tracker.is_calibrated else (150, 150, 0)
        cv2.putText(display, status, (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1)
        
        if gaze.face_detected:
            cv2.putText(display, f"Yaw: {gaze.yaw*180/3.14159:.1f} | Pitch: {gaze.pitch*180/3.14159:.1f}", 
                       (10, 110), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (150, 150, 255), 1)
        
        cv2.imshow('Gaze Tracking', display)
        
        key = cv2.waitKey(1) & 0xFF
        
        if key == ord('q'):
            break
        elif key == ord('c'):
            from calibrate import run_calibration_wizard
            run_calibration_wizard(tracker)
        elif key == ord('f'):
            current_filter_idx = (current_filter_idx + 1) % len(filter_types)
            new_filter = filter_types[current_filter_idx]
            gaze_filter = create_gaze_filter(new_filter, args.filter_strength)
            print(f"Switched to {new_filter} filter")
        elif key == ord('s'):
            save_path = "gaze_calibration.json"
            if tracker.save_calibration(save_path):
                print(f"Calibration saved: {save_path}")
        elif key == ord('l'):
            load_path = "gaze_calibration.json"
            if tracker.load_calibration(load_path):
                print(f"Calibration loaded: {load_path}")
        elif key == ord('r'):
            gaze_filter.reset()
            print("Filter reset")
    
    tracker.stop()
    cv2.destroyAllWindows()
    print("\nGaze tracking stopped.")


if __name__ == "__main__":
    main()