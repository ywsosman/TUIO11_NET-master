# Eye Gaze Estimation using Webcam

Based on the Medium article by Amit Aflalo and GitHub: amitt1236/Gaze_estimation

## Requirements
```
pip install opencv-python mediapipe numpy
```

## Usage
```bash
python main.py
```

## How it works
1. Uses MediaPipe Face Mesh for 468 facial landmarks
2. Uses solvePnP for head pose estimation  
3. Uses estimateAffine3D to project 2D pupil to 3D
4. Calculates gaze direction accounting for head movement

## Camera
Change camera index in main.py:
- `cap = cv2.VideoCapture(0)` for default camera
- `cap = cv2.VideoCapture(1)` for second camera

## Controls
- Press ESC to exit

## Original Article
https://medium.com/mlearning-ai/eye-gaze-estimation-using-a-webcam-in-100-lines-of-code-570d4683fe23
