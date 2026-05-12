#!/usr/bin/env python3
"""
Enhanced Real-time YOLO Detection Interface
Modern UI with stats, class legend, progress bar, and smooth overlays
"""

import cv2
import numpy as np
from ultralytics import YOLO
import argparse
import os
import time
from collections import deque
import json
from datetime import datetime

class EnhancedYoloInterface:
    def __init__(self, model_path="yolo26m.pt", fruit_model_path=None, conf=0.35, iou=0.45):
        print(f"Loading model: {model_path}")
        self.model = YOLO(model_path)
        self.fruit_model = None

        if fruit_model_path and os.path.exists(fruit_model_path):
            print(f"Loading fruit model: {fruit_model_path}")
            self.fruit_model = YOLO(fruit_model_path)

        self.conf = conf
        self.iou = iou

        # Color palette for classes (modern, vibrant colors)
        self.color_palette = {
            'person': (255, 100, 100),
            'car': (100, 200, 255),
            'truck': (255, 200, 100),
            'bus': (255, 150, 50),
            'motorcycle': (150, 255, 150),
            'bicycle': (200, 100, 255),
            'dog': (100, 255, 200),
            'cat': (255, 100, 255),
            'cup': (200, 200, 100),
            'bowl': (100, 200, 200),
            'bottle': (255, 200, 200),
            'chair': (200, 150, 100),
            'tv': (150, 150, 200),
            'laptop': (200, 200, 200),
            'phone': (100, 150, 200),
            'book': (255, 220, 180),
            'plant': (100, 200, 100),
            'table': (180, 140, 100),
            'clock': (255, 255, 150),
            'potted plant': (100, 200, 100),
        }

        # Fallback colors for unknown classes
        self.colors = {}
        self.color_index = 0

        # Stats tracking
        self.fps_history = deque(maxlen=30)
        self.frame_count = 0
        self.detection_history = []
        self.start_time = time.time()

        # Video info
        self.video_duration = 0
        self.current_frame = 0

        # Settings
        self.show_stats = True
        self.show_legend = True
        self.show_progress = True

    def get_color(self, class_name):
        if class_name not in self.colors:
            if class_name in self.color_palette:
                self.colors[class_name] = self.color_palette[class_name]
            else:
                # Generate consistent fallback colors
                np.random.seed(self.color_index)
                self.colors[class_name] = tuple(np.random.randint(50, 255, 3).tolist())
                self.color_index += 1
        return self.colors[class_name]

    def draw_rounded_rect(self, img, pt1, pt2, color, thickness, radius=10):
        """Draw rectangle with rounded corners"""
        x1, y1 = pt1
        x2, y2 = pt2

        # Draw lines
        cv2.line(img, (x1 + radius, y1), (x2 - radius, y1), color, thickness)
        cv2.line(img, (x1 + radius, y2), (x2 - radius, y2), color, thickness)
        cv2.line(img, (x1, y1 + radius), (x1, y2 - radius), color, thickness)
        cv2.line(img, (x2, y1 + radius), (x2, y2 - radius), color, thickness)

        # Draw arcs
        cv2.ellipse(img, (x1 + radius, y1 + radius), (radius, radius), 180, 0, 90, color, thickness)
        cv2.ellipse(img, (x2 - radius, y1 + radius), (radius, radius), 270, 0, 90, color, thickness)
        cv2.ellipse(img, (x1 + radius, y2 - radius), (radius, radius), 90, 0, 90, color, thickness)
        cv2.ellipse(img, (x2 - radius, y2 - radius), (radius, radius), 0, 0, 90, color, thickness)

    def draw_stats_panel(self, frame, fps, detections):
        """Draw stats overlay in top-left corner"""
        h, w = frame.shape[:2]

        # Panel background
        panel_w, panel_h = 220, 130
        panel_x, panel_y = 15, 15

        # Semi-transparent background
        overlay = frame.copy()
        cv2.rectangle(overlay, (panel_x, panel_y), (panel_x + panel_w, panel_y + panel_h), (0, 0, 0), -1)
        cv2.addWeighted(overlay, 0.7, frame, 0.3, 0, frame)

        # Border
        cv2.rectangle(frame, (panel_x, panel_y), (panel_x + panel_w, panel_y + panel_h), (60, 60, 70), 1)

        # Stats text
        font_scale = 0.55
        font = cv2.FONT_HERSHEY_SIMPLEX

        # Title
        cv2.putText(frame, "YOLO26m DETECTION", (panel_x + 10, panel_y + 22), font, 0.6, (0, 212, 255), 2)

        # Stats lines
        y_pos = panel_y + 50
        stats = [
            f"FPS: {fps:.1f}",
            f"Frame: {self.frame_count}",
            f"Objects: {len(detections)}",
            f"Runtime: {int(time.time() - self.start_time)}s"
        ]

        for stat in stats:
            cv2.putText(frame, stat, (panel_x + 10, y_pos), font, font_scale, (200, 200, 200), 1)
            y_pos += 20

        # Active model indicator
        model_text = "Model: YOLO26m"
        if self.fruit_model:
            model_text = "Model: YOLO26m + Fruit"
        cv2.putText(frame, model_text, (panel_x + 10, y_pos), font, font_scale - 0.05, (100, 255, 100), 1)

    def draw_class_legend(self, frame, detections):
        """Draw class legend panel in top-right"""
        h, w = frame.shape[:2]

        # Group by class
        class_counts = {}
        for det in detections:
            cls = det['class']
            class_counts[cls] = class_counts.get(cls, 0) + 1

        if not class_counts:
            return

        # Panel dimensions
        panel_w = 180
        panel_h = 35 + (len(class_counts) * 28)
        panel_x = w - panel_w - 15
        panel_y = 15

        # Background
        overlay = frame.copy()
        cv2.rectangle(overlay, (panel_x, panel_y), (panel_x + panel_w, panel_y + panel_h), (0, 0, 0), -1)
        cv2.addWeighted(overlay, 0.7, frame, 0.3, 0, frame)

        # Border
        cv2.rectangle(frame, (panel_x, panel_y), (panel_x + panel_w, panel_y + panel_h), (60, 60, 70), 1)

        # Title
        font = cv2.FONT_HERSHEY_SIMPLEX
        cv2.putText(frame, "DETECTED CLASSES", (panel_x + 10, panel_y + 22), font, 0.5, (0, 212, 255), 2)

        # Class items
        y_pos = panel_y + 45
        for cls, count in sorted(class_counts.items(), key=lambda x: x[1], reverse=True):
            color = self.get_color(cls)

            # Color dot
            cv2.circle(frame, (panel_x + 20, y_pos - 5), 6, color, -1)

            # Class name and count
            text = f"{cls}: {count}"
            cv2.putText(frame, text, (panel_x + 35, y_pos), font, 0.45, (220, 220, 220), 1)

            y_pos += 28

    def draw_bounding_box(self, frame, x1, y1, x2, y2, class_name, conf):
        """Draw enhanced bounding box with modern styling"""
        color = self.get_color(class_name)

        # Outer glow effect (simulated with multiple strokes)
        for i in range(3, 0, -1):
            cv2.rectangle(frame, (x1 - i, y1 - i), (x2 + i, y2 + i), color, 1)

        # Main box
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)

        # Label background (rounded)
        label = f"{class_name} {conf:.0%}"
        font = cv2.FONT_HERSHEY_SIMPLEX
        (label_w, label_h), _ = cv2.getTextSize(label, font, 0.5, 1)

        # Semi-transparent label background
        label_bg_x1 = x1
        label_bg_y1 = max(y1 - label_h - 12, 0)
        label_bg_x2 = x1 + label_w + 15
        label_bg_y2 = y1

        if label_bg_y1 >= 0:
            # Draw label background
            overlay = frame.copy()
            cv2.rectangle(overlay, (label_bg_x1, label_bg_y1), (label_bg_x2, label_bg_y2), color, -1)
            cv2.addWeighted(overlay, 0.8, frame, 0.2, 0, frame)

            # Label text
            cv2.putText(frame, label, (x1 + 8, y1 - 5), font, 0.5, (255, 255, 255), 1)

            # Confidence badge
            conf_text = f"{conf:.0%}"
            badge_x = x2 - 40
            badge_y = y2 - 25

            # Badge background
            cv2.rectangle(frame, (badge_x, badge_y), (badge_x + 38, badge_y + 18), (0, 0, 0), -1)
            cv2.rectangle(frame, (badge_x, badge_y), (badge_x + 38, badge_y + 18), color, 1)

            cv2.putText(frame, conf_text, (badge_x + 3, badge_y + 13), font, 0.4, (255, 255, 255), 1)

    def draw_progress_bar(self, frame, current, total):
        """Draw video progress bar at bottom"""
        if total <= 0:
            return

        h, w = frame.shape[:2]
        bar_height = 6
        bar_y = h - bar_height - 10

        # Background bar
        cv2.rectangle(frame, (10, bar_y), (w - 10, bar_y + bar_height), (40, 40, 50), -1)

        # Progress
        progress = min(current / total, 1.0)
        progress_w = int((w - 20) * progress)
        cv2.rectangle(frame, (10, bar_y), (10 + progress_w, bar_y + bar_height), (0, 212, 255), -1)

        # Time display
        font = cv2.FONT_HERSHEY_SIMPLEX
        time_text = f"{current}/{total} frames"
        cv2.putText(frame, time_text, (10, bar_y - 8), font, 0.45, (180, 180, 180), 1)

    def draw_status_bar(self, frame, fps, detection_count):
        """Draw status bar at bottom"""
        h, w = frame.shape[:2]

        # Background
        bar_y = h - 30
        overlay = frame.copy()
        cv2.rectangle(overlay, (0, bar_y), (w, h), (25, 25, 30), -1)
        cv2.addWeighted(overlay, 0.9, frame, 0.1, 0, frame)

        # Status text
        font = cv2.FONT_HERSHEY_SIMPLEX
        status_left = "Press: [F] Fullscreen  [S] Screenshot  [P] Pause  [Q] Quit"
        status_right = f"FPS: {fps:.1f} | Objects: {detection_count} | YOLO26m Active"

        cv2.putText(frame, status_left, (15, bar_y + 20), font, 0.4, (140, 140, 140), 1)

        text_len = len(status_right)
        cv2.putText(frame, status_right, (w - 15 - text_len * 8, bar_y + 20), font, 0.4, (0, 212, 255), 1)

    def detect_and_draw(self, frame):
        """Detect and draw all overlays"""
        # Main YOLO detection
        start_time = time.time()
        results = self.model(frame, conf=self.conf, iou=self.iou, verbose=False)
        yolo_time = time.time() - start_time

        all_detections = []

        # Draw YOLO detections
        if results and len(results) > 0:
            result = results[0]
            boxes = result.boxes

            for i in range(len(boxes)):
                box = boxes[i]
                xyxy = box.xyxy[0].cpu().numpy()
                conf = float(box.conf[0])
                cls = int(box.cls[0])
                class_name = result.names[cls]

                x1, y1, x2, y2 = map(int, xyxy)

                # Draw enhanced bounding box
                self.draw_bounding_box(frame, x1, y1, x2, y2, class_name, conf)

                all_detections.append({"class": class_name, "confidence": conf})

        # Fruit model detection
        if self.fruit_model:
            fruit_results = self.fruit_model(frame, conf=self.conf, iou=self.iou, verbose=False)
            if fruit_results and len(fruit_results) > 0:
                result = fruit_results[0]
                boxes = result.boxes
                for i in range(len(boxes)):
                    box = boxes[i]
                    xyxy = box.xyxy[0].cpu().numpy()
                    conf = float(box.conf[0])
                    cls = int(box.cls[0])
                    class_name = f"fruit: {result.names[cls]}"

                    x1, y1, x2, y2 = map(int, xyxy)
                    self.draw_bounding_box(frame, x1, y1, x2, y2, class_name, conf)
                    all_detections.append({"class": class_name, "confidence": conf, "source": "fruit"})

        # Calculate FPS
        total_time = time.time() - start_time
        fps = 1 / total_time if total_time > 0 else 0
        self.fps_history.append(fps)
        avg_fps = sum(self.fps_history) / len(self.fps_history) if self.fps_history else 0

        # Draw overlays
        if self.show_stats:
            self.draw_stats_panel(frame, avg_fps, all_detections)

        if self.show_legend:
            self.draw_class_legend(frame, all_detections)

        if self.show_progress and self.video_duration > 0:
            self.draw_progress_bar(frame, self.current_frame, self.video_duration)

        self.draw_status_bar(frame, avg_fps, len(all_detections))

        return frame, all_detections, avg_fps


def main():
    parser = argparse.ArgumentParser(description='Enhanced Real-time YOLO Detection')
    parser.add_argument('--model', type=str, default='yolo26m.pt', help='YOLO model path')
    parser.add_argument('--fruit-model', type=str, default=None, help='Custom fruit model path')
    parser.add_argument('--source', type=str, default='0', help='Video source (0 for webcam, or video file path)')
    parser.add_argument('--conf', type=float, default=0.35, help='Confidence threshold')
    parser.add_argument('--iou', type=float, default=0.45, help='IoU threshold')
    parser.add_argument('--output', type=str, default=None, help='Output video path')
    parser.add_argument('--no-stats', action='store_true', help='Hide stats panel')
    parser.add_argument('--no-legend', action='store_true', help='Hide class legend')
    parser.add_argument('--no-progress', action='store_true', help='Hide progress bar')
    args = parser.parse_args()

    detector = EnhancedYoloInterface(args.model, args.fruit_model, args.conf, args.iou)

    # Toggle options
    detector.show_stats = not args.no_stats
    detector.show_legend = not args.no_legend
    detector.show_progress = not args.no_progress

    # Open video source
    source = args.source
    if source.isdigit():
        source = int(source)

    cap = cv2.VideoCapture(source)

    if not cap.isOpened():
        print(f"Error: Cannot open video source: {source}")
        return

    # Get video properties
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    detector.video_duration = total_frames

    print(f"Video: {width}x{height} @ {fps:.1f} fps | Total frames: {total_frames}")
    print("Controls: [F] Fullscreen | [S] Screenshot | [P] Pause | [Q] Quit")

    # Output writer
    writer = None
    if args.output:
        fourcc = cv2.VideoWriter_fourcc(*'mp4v')
        writer = cv2.VideoWriter(args.output, fourcc, 30.0, (width, height))

    # Window setup
    window_name = "YOLO26m Enhanced Interface"
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)

    # Fullscreen toggle
    is_fullscreen = False
    paused = False

    frame_count = 0
    detector.start_time = time.time()

    while True:
        if not paused:
            ret, frame = cap.read()
            if not ret:
                print("End of video or error reading frame")
                break

            detector.current_frame = frame_count
            frame_count += 1

        # Detect and draw
        output_frame, detections, current_fps = detector.detect_and_draw(frame)

        # Show frame
        cv2.imshow(window_name, output_frame)

        # Write output
        if writer and not paused:
            writer.write(output_frame)

        # Keyboard controls
        key = cv2.waitKey(1 if not paused else 0) & 0xFF

        if key == ord('q') or key == ord('Q'):
            break
        elif key == ord('f') or key == ord('F'):
            # Toggle fullscreen
            is_fullscreen = not is_fullscreen
            if is_fullscreen:
                cv2.setWindowProperty(window_name, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_FULLSCREEN)
            else:
                cv2.setWindowProperty(window_name, cv2.WND_PROP_FULLSCREEN, cv2.WINDOW_NORMAL)
        elif key == ord('s') or key == ord('S'):
            # Screenshot
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            screenshot_path = f"screenshot_{timestamp}.jpg"
            cv2.imwrite(screenshot_path, output_frame)
            print(f"Screenshot saved: {screenshot_path}")
        elif key == ord('p') or key == ord('P'):
            # Toggle pause
            paused = not paused
            print(f"Paused: {paused}")

        # Print detection summary every 30 frames
        if frame_count % 30 == 0 and detections:
            print(f"Frame {frame_count}: {len(detections)} objects detected")

    cap.release()
    if writer:
        writer.release()
    cv2.destroyAllWindows()

    print(f"\n=== Session Summary ===")
    print(f"Total frames processed: {frame_count}")
    print(f"Runtime: {int(time.time() - detector.start_time)} seconds")
    print(f"Average FPS: {sum(detector.fps_history) / len(detector.fps_history):.1f}")


if __name__ == "__main__":
    main()