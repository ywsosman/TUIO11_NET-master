# Radial Menu with C# (MediaPipe + Trained Models)

Use the C# TuioDemo radial menu controlled by hand gestures from `mediapipe.ipynb` trained models.

## Prerequisites
- Python: `mediapipe`, `opencv-python`, `dollarpy`
- `hand_landmarker.task` (project root or python/; matches mediapipe.ipynb)
- `holistic_landmarker.task` (optional, for `--body` full-body mode)
- `gesture_templates.json` (project root; saved from mediapipe.ipynb training cells)

## Setup

1. **Train and save templates (in mediapipe.ipynb):**
   - Run the training cells to record circle, rectangle, square (2–3 samples each)
   - Templates are saved to `gesture_templates.json` automatically

2. **Start the gesture server:**
   ```bash
   cd python
   python gesture_server.py
   ```
   - Uses **hand-only** by default (no body tracking; faster and simpler)
   - `--body` to track full body; `--templates path` to specify JSON location
   - Port 5000 (C# connects here; TUIO port 3333 is separate)

3. **Run TuioDemo** and click **"Radial Menu (Gesture)"** at the login screen.

## Gesture mapping
| Hand gesture | Action      |
|-------------|-------------|
| Circle      | Open menu (pointer_up) |
| Square      | Close menu (fist)      |
| Rectangle   | Unlock cursor (open_hand) |
| O key       | Open menu (keyboard)   |
| X key       | Close menu (keyboard)  |

## Flow
1. **Circle** → Opens radial menu (Colors, Info, Level Control, Audio, Exit)
2. **Rectangle** → Cursor follows your hand; dwell over a sector to select
3. **Square** → Closes menu

## Tips
- Draw gestures with your **index finger** – trace circle/rectangle/square deliberately (~1 sec)
- Watch the camera window: **"Gesture pts: N"** shows points collected – draw until it reaches 15+
- When recognized, server logs: `[Server] Gesture recognized: pointer_up (score=0.85)`
- **Drawing tip**: Draw the shape, then **pause for ~0.5 sec** (keep hand still) – recognition runs when you stop moving.
- **If nothing is recognized**, run with `--debug` to see scores. If scores stay below 0.12, re-train in the notebook with clearer, slower strokes.
- Re-train in the notebook (2–3 samples per shape) if recognition is poor
