#!/usr/bin/env python
"""Webcam YOLO detection test for fruit system"""
from ultralytics import YOLO
import cv2
import sys

print("Loading YOLO26m...")
model = YOLO('yolo26m.pt')
print("Model loaded!")

print("Opening camera 0...")
cap = cv2.VideoCapture(0)
if not cap.isOpened():
    print("ERROR: Cannot open camera")
    sys.exit(1)

print("Camera OK - detecting objects...")
print("Press q in window to quit")
print("-" * 40)

frame_count = 0
while True:
    ret, frame = cap.read()
    if not ret:
        print("Failed to read frame")
        break
    
    results = model(frame, conf=0.35, verbose=False)
    
    detections = []
    if results and len(results[0].boxes) > 0:
        for box in results[0].boxes:
            cls_id = int(box.cls[0])
            conf_val = float(box.conf[0])
            name = results[0].names[cls_id]
            detections.append(name + " " + str(int(conf_val*100)) + "%")
    
    if detections:
        print("Frame " + str(frame_count) + ": " + ", ".join(detections))
    else:
        print("Frame " + str(frame_count) + ": No detections")
    
    cv2.putText(frame, "Frame: " + str(frame_count), (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
    cv2.imshow("YOLO26m Webcam Detection", frame)
    frame_count += 1
    
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()
print("Processed " + str(frame_count) + " frames")