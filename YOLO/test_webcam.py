#!/usr/bin/env python
"""Quick webcam test for YOLO26m"""
import cv2
from ultralytics import YOLO

print("Loading YOLO26m...")
model = YOLO('yolo26m.pt')
print("Model loaded!")

print("\nAvailable cameras:")
for i in range(3):
    cap = cv2.VideoCapture(i)
    if cap.isOpened():
        print(f"  Camera {i}: Available")
        cap.release()
    else:
        print(f"  Camera {i}: Not available")

print("\nStarting detection on camera 0...")
print("Press 'q' to quit")

cap = cv2.VideoCapture(0)
if not cap.isOpened():
    print("ERROR: Cannot open camera 0")
    exit(1)

frame_count = 0
while True:
    ret, frame = cap.read()
    if not ret:
        print("Failed to read frame")
        break

    # Run detection
    results = model(frame, conf=0.35, verbose=False)

    # Draw boxes
    if results:
        result = results[0]
        boxes = result.boxes
        for i in range(len(boxes)):
            box = boxes[i]
            xyxy = box.xyxy[0].cpu().numpy()
            conf = float(box.conf[0])
            cls = int(box.cls[0])
            class_name = result.names[cls]

            x1, y1, x2, y2 = map(int, xyxy)
            cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)
            label = f"{class_name} {conf:.2f}"
            cv2.putText(frame, label, (x1, y1-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)

    cv2.putText(frame, f"Frame: {frame_count}", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
    cv2.imshow("YOLO26m Detection", frame)

    frame_count += 1
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()
print(f"Processed {frame_count} frames")