# TUIO + YOLO26m Integration Project

## Overview
This project integrates YOLO26m object detection with the TUIO C# demo application for real-time fruit detection and multi-touch tracking.

## Files Created

| File | Description |
|------|-------------|
| `YoloDetector.py` | Basic YOLO detection backend |
| `EnhancedYoloDetector.py` | Enhanced detector with custom fruit model support |
| `RealtimeYoloDetection.py` | Real-time video capture + YOLO detection |
| `train_fruit_model.py` | Custom fruit model training script |
| `prepare_dataset.py` | Dataset preparation utility |
| `fruit_data.yaml` | YOLO training configuration |

## Existing Fruit Images
Located in `bin/Debug/`:
- apple.jpeg, banana.jpeg, straw.jpeg, watermelon.jpeg, mango.jpeg, Orange.jpeg, kiwi.jpeg

---

## Quick Start

### Step 1: Install Dependencies

```bash
# Python dependencies
pip install ultralytics opencv-python numpy

# Optional: For training (requires GPU)
# pip install torch torchvision
```

### Step 2: Run Basic Detection

**Webcam:**
```bash
python RealtimeYoloDetection.py --source 0 --model yolo26m.pt
```

**Video File:**
```bash
python RealtimeYoloDetection.py --source video.mp4 --model yolo26m.pt
```

### Step 3: Custom Fruit Training (Optional)

```bash
# Prepare dataset
python prepare_dataset.py --source bin/Debug

# Annotate images with labelimg (install separately)
# Then train:
python train_fruit_model.py
```

---

## Usage Examples

### Real-time Detection with YOLO26m

```bash
# Basic detection (COCO objects - cups, bowls, etc.)
python RealtimeYoloDetection.py --source 0

# With custom fruit model (after training)
python RealtimeYoloDetection.py --source 0 --fruit-model fruit_training/fruit_detector/weights/best.pt
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `--source` | 0 | Video source (0=webcam, or video file path) |
| `--model` | yolo26m.pt | YOLO model to use |
| `--fruit-model` | None | Custom fruit detection model |
| `--conf` | 0.35 | Confidence threshold (0-1) |
| `--iou` | 0.45 | IoU threshold for NMS |
| `--show` | True | Display video output |

### Single Image Detection

```bash
python YoloDetector.py --image path/to/image.jpg
```

---

## Training Custom Fruit Model

### 1. Prepare Dataset

```bash
# Create dataset structure
python prepare_dataset.py

# Copy your fruit images to fruit_dataset/images/train/
# Organize as: apple_01.jpg, banana_01.jpg, etc.
```

### 2. Annotate Images

Install labelImg:
```bash
pip install labelImg
labelImg
```

1. Open labelImg
2. Select fruit_dataset/images/train
3. Draw bounding boxes around fruits
4. Save as YOLO format (auto-saves to labels/ folder)

### 3. Train Model

```bash
python train_fruit_model.py
```

### 4. Use Trained Model

```bash
python RealtimeYoloDetection.py --source 0 --fruit-model fruit_training/fruit_detector/weights/best.pt
```

---

## Integration with C# TUIO Application

### Option 1: Run Separate (Recommended)

Run YOLO in separate terminal while TUIO runs:
```bash
# Terminal 1: Run TUIO Demo
mono TuioDemo.exe

# Terminal 2: Run YOLO Detection
python RealtimeYoloDetection.py --source 0
```

### Option 2: Process Communication (Advanced)

For C# integration, you can:
1. Use `System.Diagnostics.Process` to spawn Python script
2. Pass frames via stdin/stdout (JSON communication)
3. Parse YOLO results in C#

See `EnhancedYoloDetector.py` for the communication protocol.

---

## Detection Parameters

### YOLO26m Performance
| Metric | Value |
|--------|-------|
| mAP | 53.1% |
| Speed (CPU) | ~220ms |
| Parameters | 20.4M |
| Size | ~42MB |

### Recommended Settings

For real-time applications:
- `conf`: 0.35 (balance between detection and false positives)
- `iou`: 0.45 (good separation of overlapping boxes)
- Run on every frame (as requested)

For better accuracy:
- `conf`: 0.5
- `iou`: 0.5
- Run on every frame

---

## Troubleshooting

### Model Download Issues
```bash
# Force reinstall ultralytics to get latest models
pip install --upgrade ultralytics
```

### Webcam Not Found
```bash
# Check available cameras (Linux)
ls /dev/video*

# Check camera index
python -c "import cv2; print([i for i in range(5) if cv2.VideoCapture(i).isOpened()])"
```

### GPU Training Issues
For training, you need:
- NVIDIA GPU with CUDA
- Install: `pip install torch torchvision`
- Verify: `python -c "import torch; print(torch.cuda.is_available())"`

---

## Output Format

### YOLO Detection Results

```json
{
  "frame_id": 0,
  "detections": [
    {
      "class": "cup",
      "class_id": 41,
      "confidence": 0.89,
      "bbox": {
        "x1": 100.5,
        "y1": 200.3,
        "x2": 150.7,
        "y2": 280.9
      },
      "source": "coco"
    }
  ],
  "count": 1
}
```

---

## Project Structure

```
#3TuIosLAB/
├── TuioDemo.cs              # Original TUIO demo (modify for integration)
├── TuioDump.cs              # TUIO message dumper
├── YoloDetector.py          # Basic detection backend
├── EnhancedYoloDetector.py  # Enhanced with custom model support
├── RealtimeYoloDetection.py # Real-time video + detection
├── train_fruit_model.py     # Custom training script
├── prepare_dataset.py       # Dataset preparation utility
├── fruit_data.yaml          # Training configuration
├── libTUIO11.nupkg          # TUIO library
├── bin/Debug/               # Compiled app + fruit images
│   ├── apple.jpeg
│   ├── banana.jpeg
│   ├── straw.jpeg
│   ├── watermelon.jpeg
│   ├── mango.jpeg
│   ├── Orange.jpeg
│   └── kiwi.jpeg
└── fruit_dataset/           # Training dataset (created by prepare_dataset.py)
    ├── images/
    │   ├── train/
    │   ├── val/
    │   └── test/
    └── labels/
        ├── train/
        ├── val/
        └── test/
```

---

## Next Steps

1. **Test basic detection**: Run `python RealtimeYoloDetection.py --source 0`
2. **Train custom model**: Prepare annotations and run `python train_fruit_model.py`
3. **Integrate**: Connect YOLO output with TUIO for combined tracking

---

## References

- [Ultralytics YOLO26 Documentation](https://docs.ultralytics.com/models/yolo26/)
- [TUIO Protocol](http://www.tuio.org/)
- [reacTIVision](http://reactivision.sourceforge.net/)