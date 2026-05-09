import cv2
import numpy as np

cap = cv2.VideoCapture(0)

face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

print("Gaze - Raw pupil detection")
print("Press ESC to exit")

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    frame = cv2.flip(frame, 1)
    h, w = frame.shape[:2]
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    print(f"Faces: {len(faces)}")
    
    for (fx, fy, fw, fh) in faces:
        roi = gray[fy:fy+fh, fx:fx+fw]
        eyes = eye_cascade.detectMultiScale(roi, 1.1, 5)
        
        print(f"  Eyes: {len(eyes)}")
        
        for (ex, ey, ew, eh) in eyes[:2]:
            eye_img = roi[ey:ey+eh, ex:ex+ew]
            
            blurred = cv2.GaussianBlur(eye_img, (5, 5), 0)
            _, thresh = cv2.threshold(blurred, 50, 255, cv2.THRESH_BINARY_INV)
            contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            if contours:
                c = max(contours, key=cv2.contourArea)
                area = cv2.contourArea(c)
                print(f"    Largest contour area: {area}")
                
                M = cv2.moments(c)
                if M['m00'] > 0 and area > 10:
                    px = int(M['m10'] / M['m00'])
                    py = int(M['m01'] / M['m00'])
                    
                    cx = fx + ex + px
                    cy = fy + ey + py
                    
                    print(f"    Pupil: ({cx}, {cy})")
                    
                    cv2.circle(frame, (cx, cy), 15, (0, 0, 255), -1)
    
    cv2.imshow('Debug', frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()