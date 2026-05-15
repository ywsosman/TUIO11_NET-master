# Eye Tracking - Simple & Reliable

## Quick Start

```bash
cd GazeTracking
python main.py --camera 0
```

If camera 0 doesn't work, try `--camera 1`.

---

## What It Does

- **Tracks both eyes** using MediaPipe FaceLandmarker
- **Shows quadrant position** (left/center/right + up/mid/down)
- **Detects blinking** (holds last position during blink)
- **Visual feedback** on screen

---

## Dependencies

```
pip install mediapipe opencv-python numpy
```

The `face_landmarker.task` model file is auto-downloaded.

---

## Controls

| Key | Action |
|-----|--------|
| `Q` | Quit |
| `S` | Toggle smoothing (smooth/fast) |
| `B` | Adjust blink threshold |

---

## Eye Data Output

```json
{
  "face_detected": true,
  "left_x": 180,
  "left_y": 126,
  "right_x": 157,
  "right_y": 123,
  "left_openness": 0.85,
  "right_openness": 0.82,
  "blink_state": "open",
  "quadrant": "center-mid",
  "gaze_x": 0.54,
  "gaze_y": 0.48
}
```

---

## Quadrants

```
| UP-LEFT | UP-MID | UP-RIGHT |
| MID-LEFT | CENTER | MID-RIGHT |
| DOWN-LEFT | DOWN-MID | DOWN-RIGHT |
```

---

## Files

| File | Purpose |
|------|---------|
| `main.py` | Main demo |
| `eye_tracker_simple.py` | Eye tracker class |
| `face_landmarker.task` | MediaPipe model (auto-downloaded) |