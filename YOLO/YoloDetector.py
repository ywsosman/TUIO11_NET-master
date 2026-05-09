#!/usr/bin/env python3
"""
YOLO Detection Backend for TUIO Demo
Uses YOLO26m model for real-time object detection
Communicates with C# via JSON
"""

import sys
import json
import cv2
import numpy as np
from ultralytics import YOLO
import os

class YoloDetector:
    def __init__(self, model_path="yolo26m.pt", conf_threshold=0.35, iou_threshold=0.45):
        print(f"[YOLO] Loading model: {model_path}", flush=True)
        self.model = YOLO(model_path)
        self.conf_threshold = conf_threshold
        self.iou_threshold = iou_threshold
        print("[YOLO] Model loaded successfully", flush=True)

    def detect(self, frame):
        """
        Detect objects in a frame
        Returns list of detections with bounding boxes, classes, and confidence
        """
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

                for i in range(len(boxes)):
                    box = boxes[i]
                    xyxy = box.xyxy[0].cpu().numpy()
                    conf = float(box.conf[0])
                    cls = int(box.cls[0])
                    class_name = result.names[cls]

                    detections.append({
                        "class": class_name,
                        "class_id": cls,
                        "confidence": round(conf, 3),
                        "bbox": {
                            "x1": float(xyxy[0]),
                            "y1": float(xyxy[1]),
                            "x2": float(xyxy[2]),
                            "y2": float(xyxy[3])
                        }
                    })

            return detections

        except Exception as e:
            print(f"[YOLO] Detection error: {e}", flush=True)
            return []

    def detect_from_image_path(self, image_path):
        """Detect objects from an image file"""
        frame = cv2.imread(image_path)
        if frame is None:
            print(f"[YOLO] Failed to read image: {image_path}", flush=True)
            return []
        return self.detect(frame)

def main():
    import argparse
    parser = argparse.ArgumentParser(description='YOLO Detection Backend')
    parser.add_argument('--model', type=str, default='yolo26m.pt', help='Model path')
    parser.add_argument('--conf', type=float, default=0.35, help='Confidence threshold')
    parser.add_argument('--iou', type=float, default=0.45, help='IoU threshold')
    parser.add_argument('--image', type=str, help='Single image detection')
    args = parser.parse_args()

    detector = YoloDetector(args.model, args.conf, args.iou)

    if args.image:
        # Single image mode
        detections = detector.detect_from_image_path(args.image)
        print(json.dumps({"detections": detections}), flush=True)
    else:
        # Stream mode - read frames from stdin
        frame_count = 0
        while True:
            try:
                line = sys.stdin.readline()
                if not line:
                    break

                data = json.loads(line.strip())

                if data.get("command") == "detect":
                    # Decode base64 frame
                    import base64
                    img_data = base64.b64decode(data["image"])
                    nparr = np.frombuffer(img_data, np.uint8)
                    frame = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

                    if frame is not None:
                        detections = detector.detect(frame)
                        response = {
                            "frame_id": data.get("frame_id", frame_count),
                            "detections": detections,
                            "count": len(detections)
                        }
                        print(json.dumps(response), flush=True)
                        frame_count += 1
                    else:
                        print(json.dumps({"error": "Failed to decode frame"}), flush=True)

                elif data.get("command") == "quit":
                    break

            except Exception as e:
                print(json.dumps({"error": str(e)}), flush=True)

if __name__ == "__main__":
    main()