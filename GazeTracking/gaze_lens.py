import cv2
import numpy as np

cap = cv2.VideoCapture(0)

face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')

LENS_SIZE = 200
ZOOM = 2.5

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

def get_lens_frame(frame, gaze_x, gaze_y, lens_size, zoom):
    h, w = frame.shape[:2]
    
    lens_x = max(lens_size//2, min(w - lens_size//2, gaze_x))
    lens_y = max(lens_size//2, min(h - lens_size//2, gaze_y))
    
    half = lens_size // 2
    
    roi = frame[lens_y - half:lens_y + half, lens_x - half:lens_x + half]
    
    if roi.size == 0:
        return np.zeros((lens_size, lens_size, 3), dtype=np.uint8)
    
    magnified = cv2.resize(roi, None, fx=zoom, fy=zoom, interpolation=cv2.INTER_CUBIC)
    
    mag_h, mag_w = magnified.shape[:2]
    
    start_y = (mag_h - lens_size) // 2
    start_x = (mag_w - lens_size) // 2
    
    if start_y < 0 or start_x < 0:
        return cv2.resize(roi, (lens_size, lens_size))
    
    return magnified[start_y:start_y + lens_size, start_x:start_x + lens_size]

cv2.namedWindow('Main Feed', cv2.WINDOW_NORMAL)
cv2.namedWindow('Gaze Lens', cv2.WINDOW_NORMAL)

cv2.resizeWindow('Main Feed', 640, 480)
cv2.resizeWindow('Gaze Lens', LENS_SIZE, LENS_SIZE)

cv2.moveWindow('Main Feed', 100, 100)
cv2.moveWindow('Gaze Lens', 800, 100)

current_gaze = None

print("Gaze Lens Running!")
print("- Main Feed: Shows your face with eye tracking")
print("- Gaze Lens: Follows your eyes with magnification")
print("Press ESC to exit")

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    frame = cv2.flip(frame, 1)
    display = frame.copy()
    
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    faces = face_cascade.detectMultiScale(gray, 1.3, 5)
    
    gaze_x, gaze_y = frame.shape[1] // 2, frame.shape[0] // 2
    
    for (x, y, fw, fh) in faces:
        cv2.rectangle(display, (x, y), (x + fw, y + fh), (0, 255, 0), 2)
        
        roi_gray = gray[y:y + fh, x:x + fw]
        eyes = eye_cascade.detectMultiScale(roi_gray, 1.1, 5)
        
        pupil_x_list = []
        pupil_y_list = []
        
        for (ex, ey, ew, eh) in eyes[:2]:
            cv2.rectangle(display, (x + ex, y + ey), (x + ex + ew, y + ey + eh), (255, 0, 0), 2)
            
            eye_region = roi_gray[ey:ey + eh, ex:ex + ew]
            px, py = estimate_gaze(eye_region)
            
            pupil_x_list.append(x + ex + px)
            pupil_y_list.append(y + ey + py)
            
            cv2.circle(display, (x + ex + px, y + ey + py), 6, (0, 0, 255), -1)
        
        if pupil_x_list:
            gaze_x = int(sum(pupil_x_list) / len(pupil_x_list))
            gaze_y = int(sum(pupil_y_list) / len(pupil_y_list))
            
            cv2.circle(display, (gaze_x, gaze_y), 12, (0, 255, 255), 2)
    
    lens_frame = get_lens_frame(frame, gaze_x, gaze_y, LENS_SIZE, ZOOM)
    
    cv2.circle(display, (gaze_x, gaze_y), LENS_SIZE // 2, (0, 255, 255), 3)
    
    cv2.putText(display, f"Position: ({gaze_x}, {gaze_y})", (10, 30), 
              cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
    
    cv2.imshow('Main Feed', display)
    cv2.imshow('Gaze Lens', lens_frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()
print("Done!")