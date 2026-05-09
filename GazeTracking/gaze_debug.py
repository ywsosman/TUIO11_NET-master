import cv2
import numpy as np

cap = cv2.VideoCapture(0)

face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

print("Gaze - Debug version")
print("Press ESC to exit")

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    frame = cv2.flip(frame, 1)
    h, w = frame.shape[:2]
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    cx, cy = w//2, h//2
    
    for (fx, fy, fw, fh) in faces:
        roi = gray[fy:fy+fh, fx:fx+fw]
        eyes = eye_cascade.detectMultiScale(roi, 1.1, 5)
        
        print(f"Found {len(eyes)} eyes")
        
        for (ex, ey, ew, eh) in eyes[:2]:
            eye_img = roi[ey:ey+eh, ex:ex+ew]
            
            blur = cv2.GaussianBlur(eye_img, (7, 7), 0)
            _, thresh = cv2.threshold(blur, 25, 255, cv2.THRESH_BINARY_INV)
            contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            print(f"  Eye: {len(contours)} contours")
            
            if contours:
                for i, c in enumerate(contours[:3]):
                    area = cv2.contourArea(c)
                    print(f"    Contour {i}: area={area}")
                    
                    if area > 30:
                        M = cv2.moments(c)
                        if M['m00'] > 0:
                            px = int(M['m10'] / M['m00'])
                            py = int(M['m01'] / M['m00'])
                            
                            cx = fx + ex + px
                            cy = fy + ey + py
                            
                            cv2.circle(frame, (cx, cy), 15, (0, 0, 255), -1)
                            print(f"    Pupil at ({cx}, {cy})")
    
    cv2.circle(frame, (cx, cy), 30, (0, 255, 0), 4)
    cv2.putText(frame, f"{cx}, {cy}", (cx+35, cy),
              cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
    
    cv2.imshow('Gaze Debug', frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()