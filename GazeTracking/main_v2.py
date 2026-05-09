import cv2
import numpy as np
from matplotlib import pyplot as plt
from scipy.spatial import distance
import csv

import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.vision import FaceLandmarker
from mediapipe.tasks.vision import FaceLandmarkerOptions
from mediapipe.tasks import python as mp_task

IMG_PATH = "image.jpg"
OUTCsv_PATH = "gaze_direction.csv"

def gaze_estimation(img_path, out_csv_path):
    cap = cv2.VideoCapture(0)
    
    model_path = 'face_landmarker.task'
    if not Path(model_path).exists():
        print(f"Model not found: {model_path}")
        print("Downloading face_landmarker.task...")
        return
    
    base_options = python.BaseOptions(model_asset_path=model_path)
    options = FaceLandmarkerOptions(base_options=base_options, num_faces=1)
    detector = FaceLandmarker.create_from_options(options)
    
    with open(out_csv_path, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['yaw', 'pitch'])
        
        while True:
            ret, frame = cap.read()
            if not ret:
                break
            
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            result = detector.detect(mp_image)
            
            if result.face_landmarks:
                landmarks = result.face_landmarks[0]
                
                left_iris = landmarks[468]
                right_iris = landmarks[473]
                left_eye_center = landmarks[33]
                right_eye_center = landmarks[263]
                
                iris_x = (left_iris.x + right_iris.x) / 2
                iris_y = (left_iris.y + right_iris.y) / 2
                eye_center_x = (left_eye_center.x + right_eye_center.x) / 2
                eye_center_y = (left_eye_center.y + right_eye_center.y) / 2
                
                dx = iris_x - eye_center_x
                dy = iris_y - eye_center_y
                
                yaw = np.arctan2(dx, 0.1)
                pitch = np.arctan2(dy, 0.1)
                
                writer.writerow([yaw, pitch])
                
                cv2.putText(frame, f"Yaw:{yaw*180/np.pi:.1f} Pitch:{pitch*180/np.pi:.1f}", 
                          (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0,255,0), 2)
                
                h, w = frame.shape[:2]
                cv2.circle(frame, (int(left_iris.x*w), int(left_iris.y*h)), 5, (0,255,0), -1)
                cv2.circle(frame, (int(right_iris.x*w), int(right_iris.y*h)), 5, (0,255,0), -1)
            
            cv2.imshow('Gaze', frame)
            if cv2.waitKey(1) & 0xFF == 27:
                break
    
    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    from pathlib import Path
    gaze_estimation(IMG_PATH, OUTCsv_PATH)