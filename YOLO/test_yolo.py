#!/usr/bin/env python
"""Quick test script to verify YOLO26m works"""
from ultralytics import YOLO
import sys

print("Testing YOLO26m...")
print("Downloading model (first run)...")

model = YOLO('yolo26m.pt')

print("Model loaded successfully!")
print(f"Model: {model.model_name if hasattr(model, 'model_name') else 'YOLO26m'}")
print(f"Classes available: {len(model.names)}")
print(f"Sample classes: {list(model.names.values())[:10]}...")
print("\nTest passed!")