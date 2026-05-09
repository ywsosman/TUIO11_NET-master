#!/usr/bin/env python
"""Test the enhanced YOLO interface on a static image"""
import cv2
from ultralytics import YOLO
import numpy as np

print("Testing Enhanced YOLO Interface...")
print("=" * 50)

# Load model
model = YOLO('yolo26m.pt')
print("Model loaded: YOLO26m")

# Get a test image from the dataset
test_images = [
    'fruit_dataset/images/train/apple_01.jpg',
    'bin/Debug/apple.jpeg'
]

# Find existing test image
test_img = None
import os
for img_path in test_images:
    if os.path.exists(img_path):
        test_img = img_path
        break

if not test_img:
    # Use sample from ultralytics
    test_img = "ultralytics/assets/bus.jpg"

print(f"Testing with image: {test_img}")

# Run detection
results = model(test_img, conf=0.35, verbose=False)

if results:
    result = results[0]
    boxes = result.boxes
    print(f"\nDetections found: {len(boxes)}")
    
    print("\nDetected objects:")
    print("-" * 40)
    for i in range(len(boxes)):
        box = boxes[i]
        conf = float(box.conf[0])
        cls = int(box.cls[0])
        class_name = result.names[cls]
        xyxy = box.xyxy[0].cpu().numpy()
        
        print(f"  {i+1}. {class_name}: {conf:.1%}")
        print(f"      Bbox: [{xyxy[0]:.0f}, {xyxy[1]:.0f}, {xyxy[2]:.0f}, {xyxy[3]:.0f}]")
    
    print("\n" + "=" * 50)
    print("✓ YOLO26m Detection Working!")
    print("=" * 50)
    
    # Try saving annotated image
    annotated = result.plot()
    output_path = "test_output.jpg"
    cv2.imwrite(output_path, annotated)
    print(f"Annotated image saved to: {output_path}")
    
else:
    print("No detections found")

print("\n✓ Python YOLO26m Enhanced Interface Test Complete!")