import cv2
import numpy as np
import os

haar_path = cv2.data.haarcascades
face_file = os.path.join(haar_path, 'haarcascade_frontalface_default.xml')
eye_file = os.path.join(haar_path, 'haarcascade_eye.xml')

cap = cv2.VideoCapture(0)
face_cascade = cv2.CascadeClassifier(face_file)
eye_cascade = cv2.CascadeClassifier(eye_file)

print("Gaze Tracker - Look around to move cursor!")

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    h, w = frame.shape[:2]
    cx, cy = w//2, h//2
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    left_eye_x = right_eye_x = cx
    left_eye_y = right_eye_y = cy
    
    for (fx, fy, fw, fh) in faces:
        roi = gray[fy:fy+fh, fx:fx+fw]
        eyes = eye_cascade.detectMultiScale(roi, 1.1, 5)
        
        if len(eyes) >= 2:
            ex1, ey1, ew1, eh1 = eyes[0]
            ex2, ey2, ew2, eh2 = eyes[1]
            
            left_eye_x = fx + ex1 + ew1//2
            left_eye_y = fy + ey1 + eh1//2
            right_eye_x = fx + ex2 + ew2//2
            right_eye_y = fy + ey2 + eh2//2
            
            cv2.circle(frame, (left_eye_x, left_eye_y), 10, (0, 255, 0), -1)
            cv2.circle(frame, (right_eye_x, right_eye_y), 10, (0, 255, 0), -1)
            
            cx = (left_eye_x + right_eye_x) // 2
            cy = (left_eye_y + right_eye_y) // 2
        elif len(eyes) == 1:
            ex, ey, ew, eh = eyes[0]
            cx = fx + ex + ew//2
            cy = fy + ey + eh//2
    
    cv2.circle(frame, (cx, cy), 30, (255, 255, 0), 4)
    cv2.line(frame, (cx-50, cy), (cx-20, cy), (255, 255, 0), 3)
    cv2.line(frame, (cx+20, cy), (cx+50, cy), (255, 255, 0), 3)
    cv2.line(frame, (cx, cy-50), (cx, cy-20), (255, 255, 0), 3)
    cv2.line(frame, (cx, cy+20), (cx, cy+50), (255, 255, 0), 3)
    
    cv2.putText(frame, f"({cx}, {cy})", (cx+35, cy), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
    cv2.imshow('Gaze Cursor', frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()