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
_DATASET_DIR  = os.path.join(_PROJECT_ROOT, "fruit_dataset")

DATA_YAML     = os.path.join(_DATASET_DIR, "fruit_data.yaml")
RESOLVED_YAML = os.path.join(_DATASET_DIR, "fruit_data.resolved.yaml")
OUTPUT_NAME   = "fruit_train"
BEST_PT_DST   = os.path.join(_PROJECT_ROOT, "fruit_best.pt")


def _write_resolved_yaml():
    """Rewrite the dataset YAML with an absolute path so Ultralytics resolves
    images/ relative to the dataset directory, not CWD."""
    import yaml
    with open(DATA_YAML, "r", encoding="utf-8") as f:
        cfg = yaml.safe_load(f)
    cfg["path"] = _DATASET_DIR.replace("\\", "/")
    with open(RESOLVED_YAML, "w", encoding="utf-8") as f:
        yaml.safe_dump(cfg, f, sort_keys=False)
    return RESOLVED_YAML


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

    resolved = _write_resolved_yaml()
    print(f"  Using   : {resolved}")

    results = model.train(
        data=resolved,
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
