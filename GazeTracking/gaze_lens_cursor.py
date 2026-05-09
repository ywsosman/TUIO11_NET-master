import cv2
import numpy as np

cap = cv2.VideoCapture(0)

face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

LENS_RADIUS = 100
ZOOM = 2.0

def estimate_gaze(eye_region):
    h, w = eye_region.shape[:2]
    gray = cv2.GaussianBlur(eye_region, (5, 5), 0)
    _, threshold = cv2.threshold(gray, 30, 255, cv2.THRESH_BINARY_INV)
    contours, _ = cv2.findContours(threshold, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
    if contours:
        largest = max(contours, key=cv2.contourArea)
        M = cv2.moments(largest)
        if M['m00'] != 0:
            return int(M['m10'] / M['m00']), int(M['m01'] / M['m00'])
    return w // 2, h // 2

print("Gaze Lens Cursor - Move your eyes to control the lens!")
print("Press ESC to exit")

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    frame = cv2.flip(frame, 1)
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    gaze_x, gaze_y = frame.shape[1] // 2, frame.shape[0] // 2
    
    for (x, y, fw, fh) in faces:
        roi_gray = gray[y:y + fh, x:x + fw]
        eyes = eye_cascade.detectMultiScale(roi_gray, 1.1, 5)
        
        pupil_x_list = []
        pupil_y_list = []
        
        for (ex, ey, ew, eh) in eyes[:2]:
            eye_region = roi_gray[ey:ey + eh, ex:ex + ew]
            px, py = estimate_gaze(eye_region)
            
            pupil_x_list.append(x + ex + px)
            pupil_y_list.append(y + ey + py)
        
        if pupil_x_list:
            gaze_x = int(sum(pupil_x_list) / len(pupil_x_list))
            gaze_y = int(sum(pupil_y_list) / len(pupil_y_list))
    
    gaze_x = max(LENS_RADIUS, min(frame.shape[1] - LENS_RADIUS, gaze_x))
    gaze_y = max(LENS_RADIUS, min(frame.shape[0] - LENS_RADIUS, gaze_y))
    
    mask = frame.copy()
    
    cv2.circle(mask, (gaze_x, gaze_y), LENS_RADIUS, (0, 0, 0), -1)
    
    result = frame.copy()
    
    y1 = max(0, gaze_y - LENS_RADIUS)
    y2 = min(frame.shape[0], gaze_y + LENS_RADIUS)
    x1 = max(0, gaze_x - LENS_RADIUS)
    x2 = min(frame.shape[1], gaze_x + LENS_RADIUS)
    
    roi = frame[y1:y2, x1:x2]
    
    if roi.size > 0:
        magnified = cv2.resize(roi, None, fx=ZOOM, fy=ZOOM, interpolation=cv2.INTER_CUBIC)
        
        mag_h, mag_w = magnified.shape[:2]
        
        target_size = LENS_RADIUS * 2
        start_y = (mag_h - target_size) // 2
        start_x = (mag_w - target_size) // 2
        
        if start_y > 0 and start_x > 0:
            magnified = magnified[start_y:start_y + target_size, start_x:start_x + target_size]
        else:
            magnified = cv2.resize(roi, (target_size, target_size))
        
        result[y1:y2, x1:x2] = magnified
    
    cv2.circle(result, (gaze_x, gaze_y), LENS_RADIUS, (0, 255, 255), 4)
    
    cv2.line(result, (gaze_x - 15, gaze_y), (gaze_x - 30, gaze_y), (0, 255, 255), 3)
    cv2.line(result, (gaze_x + 15, gaze_y), (gaze_x + 30, gaze_y), (0, 255, 255), 3)
    cv2.line(result, (gaze_x, gaze_y - 15), (gaze_x, gaze_y - 30), (0, 255, 255), 3)
    cv2.line(result, (gaze_x, gaze_y + 15), (gaze_x, gaze_y + 30), (0, 255, 255), 3)
    
    cv2.putText(result, f"({gaze_x}, {gaze_y})", (gaze_x + LENS_RADIUS + 10, gaze_y - 10),
              cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)
    
    cv2.imshow('Gaze Lens Cursor', result)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()