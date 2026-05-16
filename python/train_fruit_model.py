"""
train_fruit_model.py — fine-tune YOLO on the local fruit_dataset.
Run once from the project root:
    python python/train_fruit_model.py

Output: runs/detect/fruit_train/weights/best.pt
gesture_server.py will automatically prefer this model on next launch.
"""

import os
import sys
import shutil

_SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
_PROJECT_ROOT = os.path.dirname(_SCRIPT_DIR)

DATA_YAML   = os.path.join(_PROJECT_ROOT, "fruit_dataset", "fruit_data.yaml")
OUTPUT_NAME = "fruit_train"
BEST_PT_DST = os.path.join(_PROJECT_ROOT, "fruit_best.pt")


def main():
    try:
        from ultralytics import YOLO
    except ImportError:
        print("ERROR: ultralytics not installed. Run: pip install ultralytics")
        sys.exit(1)

    if not os.path.exists(DATA_YAML):
        print(f"ERROR: Dataset YAML not found: {DATA_YAML}")
        sys.exit(1)

    print("=" * 60)
    print("Fruit model fine-tuning")
    print(f"  Dataset : {DATA_YAML}")
    print(f"  Output  : runs/detect/{OUTPUT_NAME}/weights/best.pt")
    print("=" * 60)

    # Start from the nano model — fastest to fine-tune, good enough for 7 classes
    model = YOLO("yolo11n.pt")

    # Must use absolute path so YOLO resolves images/ correctly
    results = model.train(
        data=os.path.abspath(DATA_YAML),
        epochs=80,
        imgsz=416,
        batch=4,
        name=OUTPUT_NAME,
        project=os.path.join(_PROJECT_ROOT, "runs", "detect"),
        exist_ok=True,
        patience=20,       # early stopping
        augment=True,      # flip/rotate/hsv — critical with tiny dataset
        degrees=15,
        fliplr=0.5,
        mosaic=0.5,
        mixup=0.1,
        verbose=False,
    )

    # Copy best.pt to project root for easy discovery by gesture_server
    best_src = os.path.join(
        _PROJECT_ROOT, "runs", "detect", OUTPUT_NAME, "weights", "best.pt"
    )
    if os.path.exists(best_src):
        shutil.copy2(best_src, BEST_PT_DST)
        print(f"\nDone. Model saved to: {BEST_PT_DST}")
        print("gesture_server.py will use this model automatically next run.")
    else:
        print(f"\nWARNING: best.pt not found at expected path: {best_src}")
        print("Check runs/detect/ for training output.")


if __name__ == "__main__":
    main()
