#!/usr/bin/env python
"""Test YOLO on real objects"""
from ultralytics import YOLO

print("Testing YOLO26m on sample images...")
model = YOLO('yolo26m.pt')

# Test on bus image (has person, bus, etc.)
print("\n1. Testing on bus.jpg")
results = model("bus.jpg", conf=0.35, verbose=False)
if results:
    boxes = results[0].boxes
    print(f"   Detections: {len(boxes)}")
    for box in boxes:
        cls = results[0].names[int(box.cls[0])]
        conf = float(box.conf[0])
        print(f"   - {cls}: {conf:.1%}")

print("\n2. Testing on zidane.jpg")
results = model("zidane.jpg", conf=0.35, verbose=False)
if results:
    boxes = results[0].boxes
    print(f"   Detections: {len(boxes)}")
    for box in boxes:
        cls = results[0].names[int(box.cls[0])]
        conf = float(box.conf[0])
        print(f"   - {cls}: {conf:.1%}")

print("\n✓ YOLO26m Detection Test Complete!")