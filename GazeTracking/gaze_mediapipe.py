import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.vision import FaceLandmarker
from mediapipe.tasks.vision import FaceLandmarkerOptions
import numpy as np

model_path = 'face_landmarker.task'

base_options = python.BaseOptions(model_asset_path=model_path)
options = FaceLandmarkerOptions(base_options=base_options, num_faces=1)
face_landmarker = FaceLandmarker.create_from_options(options)

cap = cv2.VideoCapture(0)

while True:
    success, image = cap.read()
    if not success:
        print("Ignoring empty camera frame.")
        continue
    
    h, w = image.shape[:2]
    rgb_image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_image)
    
    results = face_landmarker.detect(mp_image)
    
    cx, cy = w // 2, h // 2
    
    if results.face_landmarks and len(results.face_landmarks) > 0:
        landmarks = results.face_landmarks[0]
        
        left_iris = landmarks[468]
        right_iris = landmarks[473]
        
        left_iris_x = int(left_iris.x * w)
        left_iris_y = int(left_iris.y * h)
        right_iris_x = int(right_iris.x * w)
        right_iris_y = int(right_iris.y * h)
        
        cx = (left_iris_x + right_iris_x) // 2
        cy = (left_iris_y + right_iris_y) // 2
        
        cv2.circle(image, (left_iris_x, left_iris_y), 8, (0, 255, 0), -1)
        cv2.circle(image, (right_iris_x, right_iris_y), 8, (0, 255, 0), -1)
    
    cv2.circle(image, (cx, cy), 30, (255, 255, 0), 4)
    cv2.line(image, (cx - 50, cy), (cx - 20, cy), (255, 255, 0), 3)
    cv2.line(image, (cx + 20, cy), (cx + 50, cy), (255, 255, 0), 3)
    cv2.line(image, (cx, cy - 50), (cx, cy - 20), (255, 255, 0), 3)
    cv2.line(image, (cx, cy + 20), (cx, cy + 50), (255, 255, 0), 3)
    
    cv2.putText(image, f"({cx}, {cy})", (cx + 35, cy), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
    cv2.imshow('Gaze Cursor', image)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()