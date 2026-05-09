#!/usr/bin/env python3
"""
Fruit Detection using Image Classification
Detects fruits based on template matching and color analysis
Works without training - matches known fruit images
"""

import cv2
import numpy as np
import os
from pathlib import Path

class FruitDetector:
    def __init__(self):
        # Load fruit reference images
        self.fruit_images = {}
        fruit_dir = Path("bin/Debug")
        
        fruit_mapping = {
            'apple': ['apple.jpeg'],
            'banana': ['banana.jpeg'],
            'strawberry': ['straw.jpeg'],
            'watermelon': ['watermelon.jpeg'],
            'mango': ['mango.jpeg'],
            'orange': ['Orange.jpeg'],
            'kiwi': ['kiwi.jpeg']
        }
        
        print("Loading fruit reference images...")
        for fruit, filenames in fruit_mapping.items():
            for filename in filenames:
                path = fruit_dir / filename
                if path.exists():
                    img = cv2.imread(str(path))
                    if img is not None:
                        self.fruit_images[fruit] = img
                        print(f"  [OK] Loaded: {fruit} ({filename})")
        
        # Fruit colors (HSV ranges for detection)
        self.fruit_colors = {
            'apple': [(0, 50, 50), (10, 255, 255)],        # Red
            'banana': [(20, 50, 50), (30, 255, 255)],    # Yellow
            'orange': [(5, 50, 50), (15, 255, 255)],      # Orange
            'kiwi': [(30, 20, 30), (70, 255, 150)],       # Green
            'mango': [(15, 30, 50), (25, 255, 255)],     # Orange-Yellow
            'strawberry': [(0, 50, 50), (10, 255, 255)], # Red
            'watermelon': [(0, 50, 50), (10, 255, 255)]   # Red/Green
        }
        
    def detect_by_color(self, frame):
        """Detect fruits based on color analysis"""
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        detections = []
        
        for fruit, (lower, upper) in self.fruit_colors.items():
            mask = cv2.inRange(hsv, np.array(lower), np.array(upper))
            mask = cv2.GaussianBlur(mask, (5, 5), 0)
            
            # Find contours
            contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            for cnt in contours:
                area = cv2.contourArea(cnt)
                if area > 500:  # Minimum area threshold
                    x, y, w, h = cv2.boundingRect(cnt)
                    detections.append({
                        'class': fruit,
                        'confidence': min(area / 10000, 0.95),
                        'bbox': {'x1': x, 'y1': y, 'x2': x+w, 'y2': y+h}
                    })
        
        return detections
    
    def detect_by_template(self, frame):
        """Detect fruits by template matching"""
        detections = []
        
        for fruit, template in self.fruit_images.items():
            # Resize template to match frame
            template_resized = cv2.resize(template, (frame.shape[1]//4, frame.shape[0]//4))
            
            # Convert to grayscale
            gray_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            gray_template = cv2.cvtColor(template_resized, cv2.COLOR_BGR2GRAY)
            
            # Template matching
            result = cv2.matchTemplate(gray_frame, gray_template, cv2.TM_CCOEFF_NORMED)
            locations = np.where(result > 0.6)
            
            for pt in zip(*locations[::-1]):
                h, w = gray_template.shape
                detections.append({
                    'class': fruit,
                    'confidence': float(result[pt[1], pt[0]]),
                    'bbox': {'x1': pt[0], 'y1': pt[1], 'x2': pt[0]+w, 'y2': pt[1]+h}
                })
        
        return detections
    
    def detect(self, frame):
        """Combined detection using both methods"""
        # Method 1: Color-based detection
        color_detections = self.detect_by_color(frame)
        
        # Method 2: Template matching
        template_detections = self.detect_by_template(frame)
        
        # Combine and deduplicate
        all_detections = color_detections + template_detections
        
        # Remove duplicates based on overlapping bboxes
        filtered = []
        for det in all_detections:
            is_duplicate = False
            for existing in filtered:
                if self.bbox_overlap(det['bbox'], existing['bbox']):
                    if det['confidence'] > existing['confidence']:
                        existing['confidence'] = det['confidence']
                        existing['class'] = det['class']
                    is_duplicate = True
                    break
            if not is_duplicate:
                filtered.append(det)
        
        return filtered
    
    def bbox_overlap(self, bbox1, bbox2):
        """Check if two bounding boxes overlap"""
        x1 = max(bbox1['x1'], bbox2['x1'])
        y1 = max(bbox1['y1'], bbox2['y1'])
        x2 = min(bbox1['x2'], bbox2['x2'])
        y2 = min(bbox1['y2'], bbox2['y2'])
        return x1 < x2 and y1 < y2


def main():
    import argparse
    
    parser = argparse.ArgumentParser(description='Fruit Detection')
    parser.add_argument('--source', type=str, default='0', help='Video source (0 for webcam)')
    parser.add_argument('--image', type=str, help='Single image file')
    args = parser.parse_args()
    
    detector = FruitDetector()
    
    if args.image:
        # Single image mode
        frame = cv2.imread(args.image)
        if frame is None:
            print(f"Cannot read image: {args.image}")
            return
        
        detections = detector.detect(frame)
        
        print(f"\n[IMAGE] Detection Results for {args.image}")
        print("-" * 40)
        if detections:
            for det in detections:
                print(f"  [{det['class']}] {det['confidence']:.1%}")
        else:
            print("  No fruits detected")
        
        # Draw detections
        for det in detections:
            bbox = det['bbox']
            cv2.rectangle(frame, (bbox['x1'], bbox['y1']), (bbox['x2'], bbox['y2']), (0, 255, 0), 2)
            label = f"{det['class']} {det['confidence']:.0%}"
            cv2.putText(frame, label, (bbox['x1'], bbox['y1']-10), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
        
        cv2.imwrite(f"fruit_detected_{args.image.split('/')[-1]}", frame)
        print(f"\n[Saved] Output: fruit_detected_{args.image.split('/')[-1]}")
    
    else:
        # Video mode
        source = int(args.source) if args.source.isdigit() else args.source
        cap = cv2.VideoCapture(source)
        
        if not cap.isOpened():
            print("Cannot open video source")
            return
        
        print("\n[Video] Starting fruit detection...")
        print("Press 'q' to quit")
        
        while True:
            ret, frame = cap.read()
            if not ret:
                break
            
            detections = detector.detect(frame)
            
            # Draw detections
            for det in detections:
                bbox = det['bbox']
                color = (0, 255, 0)  # Green
                cv2.rectangle(frame, (bbox['x1'], bbox['y1']), (bbox['x2'], bbox['y2']), color, 2)
                label = f"{det['class']} {det['confidence']:.0%}"
                cv2.putText(frame, label, (bbox['x1'], max(bbox['y1']-10, 20)), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.6, color, 1)
            
            # Show info
            cv2.putText(frame, f"Fruits: {len(detections)}", (10, 30), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            
            cv2.imshow("Fruit Detection", frame)
            
            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
        
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()