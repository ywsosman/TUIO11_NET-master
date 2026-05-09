#!/usr/bin/env python
"""Test fruit detector on all fruits"""
import subprocess
import os

fruits = [
    'apple.jpeg',
    'banana.jpeg', 
    'straw.jpeg',
    'watermelon.jpeg',
    'mango.jpeg',
    'Orange.jpeg',
    'kiwi.jpeg'
]

print("=" * 60)
print("FRUIT DETECTION TEST RESULTS")
print("=" * 60)

for fruit in fruits:
    img_path = f"bin/Debug/{fruit}"
    if not os.path.exists(img_path):
        print(f"  [Skip] {fruit} - not found")
        continue
    
    print(f"\n>>> Testing: {fruit}")
    result = subprocess.run(
        ['python', 'FruitDetector.py', '--image', img_path],
        capture_output=True,
        text=True
    )
    print(result.stdout)
    if result.stderr:
        print(result.stderr)

print("\n" + "=" * 60)
print("ALL FRUIT TESTS COMPLETE")
print("=" * 60)
print("\nOutput images saved as: fruit_detected_*.jpeg")