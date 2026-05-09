import cv2
import numpy as np
from pathlib import Path

cap = cv2.VideoCapture(0)

face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

def estimate_gaze(eye_region, face_center_x, face_center_y):
    h, w = eye_region.shape[:2]
    gray = cv2.GaussianBlur(eye_region, (5, 5), 0)
    
    _, threshold = cv2.threshold(gray, 30, 255, cv2.THRESH_BINARY_INV)
    
    contours, _ = cv2.findContours(threshold, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
    
    if contours:
        largest = max(contours, key=cv2.contourArea)
        M = cv2.moments(largest)
        if M['m00'] != 0:
            cx = int(M['m10'] / M['m00'])
            cy = int(M['m01'] / M['m00'])
            
            eye_center_x = w // 2
            eye_center_y = h // 2
            
            dx = cx - eye_center_x
            dy = cy - eye_center_y
            
            yaw = np.arctan2(dx, w) * 2
            pitch = np.arctan2(dy, h) * 2
            
            return yaw, pitch, (cx, cy)
    return 0, 0, (w//2, h//2)

print("Starting gaze tracking (press ESC to exit)...")
while True:
    ret, frame = cap.read()
    if not ret:
        print("Failed to read frame")
        break
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    for (x, y, w, h) in faces:
        cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 0), 2)
        
        roi_gray = gray[y:y+h, x:x+w]
        roi_color = frame[y:y+h, x:x+w]
        
        eyes = eye_cascade.detectMultiScale(roi_gray)
        
        for (ex, ey, ew, eh) in eyes[:2]:
            cv2.rectangle(roi_color, (ex, ey), (ex+ew, ey+eh), (255, 0, 0), 2)
            
            eye_region = roi_gray[ey:ey+eh, ex:ex+ew]
            
            yaw, pitch, pupil = estimate_gaze(eye_region, ex + ew//2, ey + eh//2)
            
            cv2.circle(roi_color, (ex + pupil[0], ey + pupil[1]), 5, (0, 0, 255), -1)
            
            cv2.putText(roi_color, f"Y:{yaw*180/np.pi:.0f} P:{pitch*180/np.pi:.0f}", 
                      (ex, ey-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
    
    cv2.imshow('Gaze Tracking (Haar Cascade)', frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()
print("Done.")