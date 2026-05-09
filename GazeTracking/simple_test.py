import cv2

cap = cv2.VideoCapture(0)

while True:
    ret, frame = cap.read()
    if not ret:
        break
    
    h, w = frame.shape[:2]
    cx, cy = w//2, h//2
    
    cv2.circle(frame, (cx, cy), 40, (255, 255, 0), 4)
    cv2.line(frame, (cx-60, cy), (cx-20, cy), (255, 255, 0), 3)
    cv2.line(frame, (cx+20, cy), (cx+60, cy), (255, 255, 0), 3)
    cv2.line(frame, (cx, cy-60), (cx, cy-20), (255, 255, 0), 3)
    cv2.line(frame, (cx, cy+20), (cx, cy+60), (255, 255, 0), 3)
    
    cv2.imshow('Simple Cursor', frame)
    
    if cv2.waitKey(1) & 0xFF == 27:
        break

cap.release()
cv2.destroyAllWindows()