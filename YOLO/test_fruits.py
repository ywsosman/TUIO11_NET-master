#!/usr/bin/env python
"""Test YOLO26m on fruit images from the project"""
from ultralytics import YOLO
import cv2
import os

print("=" * 60)
print("TESTING YOLO26m ON FRUIT IMAGES")
print("=" * 60)

model = YOLO('yolo26m.pt')

# Fruit images from the project
fruit_images = [
    'bin/Debug/apple.jpeg',
    'bin/Debug/banana.jpeg',
    'bin/Debug/straw.jpeg',
    'bin/Debug/watermelon.jpeg',
    'bin/Debug/mango.jpeg',
    'bin/Debug/Orange.jpeg',
    'bin/Debug/kiwi.jpeg'
]

print("\nRunning detection on fruit images...\n")

for img_path in fruit_images:
    if not os.path.exists(img_path):
        print(f"⚠️  File not found: {img_path}")
        continue
    
    print(f"--- {os.path.basename(img_path)} ---")
    
    # Run detection
    results = model(img_path, conf=0.25, verbose=False)
    
    if results and len(results) > 0:
        boxes = results[0].boxes
        
        if len(boxes) > 0:
            print(f"  ✅ Detected {len(boxes)} object(s):")
            for box in boxes:
                cls = results[0].names[int(box.cls[0])]
                conf = float(box.conf[0])
                print(f"     - {cls}: {conf:.1%}")
            
            # Save annotated image
            annotated = results[0].plot()
            output_name = f"fruit_test_{os.path.basename(img_path).replace('.jpeg', '.jpg')}"
            cv2.imwrite(output_name, annotated)
            print(f"     Saved: {output_name}")
        else:
            print(f"  ❌ No objects detected")
            # Save original as-is
            img = cv2.imread(img_path)
            cv2.imwrite(f"fruit_test_{os.path.basename(img_path).replace('.jpeg', '.jpg')}", img)
    
    print()

print("=" * 60)
print("FRUIT DETECTION TEST COMPLETE")
print("=" * 60)

print("""

Note: YOLO26m is pretrained on COCO dataset (80 classes).
COCO doesn't include specific fruit classes like apple, banana, etc.
The model may detect:
  - Generic objects (bowl, cup, etc.)
  - Or nothing if objects don't match COCO classes

For fruit-specific detection, you need to train a custom model!
See: python train_fruit_model.py
""")