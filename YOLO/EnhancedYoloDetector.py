#!/usr/bin/env python3
"""
Enhanced YOLO Detection with Custom Fruit Classes
Supports both COCO objects and custom fruit detection
"""

import sys
import json
import cv2
import numpy as np
from ultralytics import YOLO
import os

# Custom fruit class mappings
FRUIT_CLASSES = {
    "apple": 0,
    "banana": 1,
    "strawberry": 2,
    "watermelon": 3,
    "mango": 4,
    "orange": 5,
    "kiwi": 6
}

class EnhancedYoloDetector:
    def __init__(self, model_path="yolo26m.pt", fruit_model_path=None, conf_threshold=0.35, iou_threshold=0.45):
        print(f"[YOLO] Loading main model: {model_path}", flush=True)
        self.main_model = YOLO(model_path)
        self.conf_threshold = conf_threshold
        self.iou_threshold = iou_threshold

        # Try to load custom fruit model if available
        self.fruit_model = None
        if fruit_model_path and os.path.exists(fruit_model_path):
            print(f"[YOLO] Loading fruit model: {fruit_model_path}", flush=True)
            try:
                self.fruit_model = YOLO(fruit_model_path)
                print("[YOLO] Fruit model loaded successfully", flush=True)
            except Exception as e:
                print(f"[YOLO] Failed to load fruit model: {e}", flush=True)

        print("[YOLO] Initialization complete", flush=True)

    def detect(self, frame):
        """Detect objects using both main model and fruit model if available"""
        detections = []

        # COCO model detection (main model)
        try:
            results = self.main_model(
                frame,
                conf=self.conf_threshold,
                iou=self.iou_threshold,
                verbose=False
            )

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
                        },
                        "source": "coco"
                    })
        except Exception as e:
            print(f"[YOLO] COCO detection error: {e}", flush=True)

        # Fruit model detection (if available)
        if self.fruit_model:
            try:
                fruit_results = self.fruit_model(
                    frame,
                    conf=self.conf_threshold,
                    iou=self.iou_threshold,
                    verbose=False
                )

                if fruit_results and len(fruit_results) > 0:
                    result = fruit_results[0]
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
                            },
                            "source": "fruit"
                        })
            except Exception as e:
                print(f"[YOLO] Fruit detection error: {e}", flush=True)

        return detections

def main():
    import argparse
    parser = argparse.ArgumentParser(description='Enhanced YOLO Detection')
    parser.add_argument('--model', type=str, default='yolo26m.pt', help='Main model path')
    parser.add_argument('--fruit-model', type=str, default=None, help='Custom fruit model path')
    parser.add_argument('--conf', type=float, default=0.35, help='Confidence threshold')
    parser.add_argument('--iou', type=float, default=0.45, help='IoU threshold')
    args = parser.parse_args()

    detector = EnhancedYoloDetector(args.model, args.fruit_model, args.conf, args.iou)

    # Stream mode
    frame_count = 0
    while True:
        try:
            line = sys.stdin.readline()
            if not line:
                break

            data = json.loads(line.strip())

            if data.get("command") == "detect":
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

            elif data.get("command") == "quit":
                break

        except Exception as e:
            print(json.dumps({"error": str(e)}), flush=True)

if __name__ == "__main__":
    main()