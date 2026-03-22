"""
Gesture Server - MediaPipe pose/hand + dollarpy gestures
References: 4.3.ipynb (Holistic, dollarpy, pose landmarks), Lab_2 (hand detection),
           Lab 3 Sockets (TCP server pattern - bind, listen, accept, send)
Communicates with C# via TCP socket (Lab 3 pattern, JSON messages on port 5000)
"""

import cv2
import json
import os
import socket
import threading
import time
from collections import deque

import numpy as np

try:
    from dollarpy import Recognizer, Template, Point
except ImportError:
    raise ImportError("Install dollarpy: pip install dollarpy")

# Detect API: legacy holistic vs Tasks PoseLandmarker
USE_LEGACY_HOLISTIC = False
try:
    import mediapipe as mp
    if hasattr(mp, 'solutions') and hasattr(mp.solutions, 'holistic'):
        USE_LEGACY_HOLISTIC = True
        mp_holistic = mp.solutions.holistic
        print("[Server] Using mp.solutions.holistic (legacy)")
    else:
        from mediapipe.tasks import python as mp_tasks
        from mediapipe.tasks.python import vision
        print("[Server] Using PoseLandmarker (Tasks API)")
except ImportError as e:
    raise ImportError("Install mediapipe: pip install mediapipe") from e

LANDMARK_NAMES = [
    "nose", "left_eye_inner", "left_eye", "left_eye_outer",
    "right_eye_inner", "right_eye", "right_eye_outer",
    "left_ear", "right_ear", "mouth_left", "mouth_right",
    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
    "left_wrist", "right_wrist", "left_pinky", "right_pinky",
    "left_index", "right_index", "left_thumb", "right_thumb",
    "left_hip", "right_hip", "left_knee", "right_knee",
    "left_ankle", "right_ankle", "left_heel", "right_heel",
    "left_foot_index", "right_foot_index"
]
GESTURE_LANDMARKS = [11, 12, 13, 14, 15, 16, 19, 20]
NUM_GESTURE_POINTS = 32

# Pose connections (Lab_2 / MediaPipe pose)
POSE_CONNECTIONS = [
    (11, 12), (11, 13), (13, 15), (12, 14), (14, 16),
    (11, 23), (12, 24), (23, 24), (23, 25), (25, 27), (24, 26), (26, 28),
]

# Hand connections (Lab_2 / mp.solutions.hands HAND_CONNECTIONS)
HAND_CONNECTIONS = [
    (0, 1), (0, 5), (9, 13), (13, 17), (5, 9), (0, 17),
    (1, 2), (2, 3), (3, 4),
    (5, 6), (6, 7), (7, 8),
    (9, 10), (10, 11), (11, 12),
    (13, 14), (14, 15), (15, 16),
    (17, 18), (18, 19), (19, 20),
]
# Hand landmark indices: 0=WRIST, 8=INDEX_FINGER_TIP (Lab_2 HandLandmark)
HAND_WRIST, HAND_INDEX_TIP = 0, 8

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task"
HAND_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"


def draw_hands_on_frame(frame, hand_landmarks_list, h, w):
    """Draw hand landmarks with connections (Lab_2 style - accurate finger points)."""
    if not hand_landmarks_list:
        return
    for hand_lm in hand_landmarks_list:
        landmarks = hand_lm.landmark if hasattr(hand_lm, 'landmark') else hand_lm
        for idx, lm in enumerate(landmarks):
            x, y = int(lm.x * w), int(lm.y * h)
            cv2.circle(frame, (x, y), 4, (255, 255, 0), -1)
        for a, b in HAND_CONNECTIONS:
            if a < len(landmarks) and b < len(landmarks):
                pt1 = (int(landmarks[a].x * w), int(landmarks[a].y * h))
                pt2 = (int(landmarks[b].x * w), int(landmarks[b].y * h))
                cv2.line(frame, pt1, pt2, (255, 255, 0), 2)


def draw_skeleton_on_frame(frame, landmarks, h, w):
    """Draw skeleton overlay on frame. landmarks: list of objects with x, y, visibility."""
    if not landmarks or len(landmarks) < 17:
        return
    for idx, lm in enumerate(landmarks):
        vis = getattr(lm, 'visibility', 1.0) or 1.0
        if vis < 0.5:
            continue
        x, y = int(lm.x * w), int(lm.y * h)
        cv2.circle(frame, (x, y), 4, (0, 255, 0), -1)
    for a, b in POSE_CONNECTIONS:
        if a >= len(landmarks) or b >= len(landmarks):
            continue
        va = getattr(landmarks[a], 'visibility', 1.0) or 1.0
        vb = getattr(landmarks[b], 'visibility', 1.0) or 1.0
        if va < 0.5 or vb < 0.5:
            continue
        pt1 = (int(landmarks[a].x * w), int(landmarks[a].y * h))
        pt2 = (int(landmarks[b].x * w), int(landmarks[b].y * h))
        cv2.line(frame, pt1, pt2, (0, 255, 0), 2)


def get_model_path(filename, url):
    base = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(base, filename)
    if not os.path.exists(path):
        print(f"[Server] Downloading {filename}...")
        import urllib.request
        urllib.request.urlretrieve(url, path)
    return path


def resample_points(points, num_points):
    if len(points) < 2:
        return list(points)
    if len(points) == num_points:
        return list(points)
    dists = [0.0]
    for i in range(1, len(points)):
        dx = points[i][0] - points[i-1][0]
        dy = points[i][1] - points[i-1][1]
        dists.append(dists[-1] + (dx*dx + dy*dy) ** 0.5)
    total = dists[-1]
    if total < 1e-9:
        return [points[0]] * num_points
    result = []
    for k in range(num_points):
        target = k * total / (num_points - 1) if num_points > 1 else 0
        for i in range(len(dists) - 1):
            if dists[i] <= target <= dists[i+1]:
                t = (target - dists[i]) / (dists[i+1] - dists[i]) if dists[i+1] > dists[i] else 0
                x = points[i][0] + t * (points[i+1][0] - points[i][0])
                y = points[i][1] + t * (points[i+1][1] - points[i][1])
                result.append((x, y))
                break
        else:
            result.append(points[-1])
    return result


def create_gesture_templates():
    import math
    wave = resample_points([(0.3, 0.3), (0.5, 0.2), (0.7, 0.3), (0.5, 0.4), (0.3, 0.3)], NUM_GESTURE_POINTS)
    circle = resample_points(
        [(0.5 + 0.2*math.cos(i*2*math.pi/16), 0.5 + 0.2*math.sin(i*2*math.pi/16)) for i in range(17)],
        NUM_GESTURE_POINTS
    )
    push = resample_points([(0.3, 0.5), (0.5, 0.5), (0.7, 0.5)], NUM_GESTURE_POINTS)
    swipe_l = resample_points([(0.8, 0.5), (0.2, 0.5)], NUM_GESTURE_POINTS)
    swipe_r = resample_points([(0.2, 0.5), (0.8, 0.5)], NUM_GESTURE_POINTS)
    return [
        Template("wave", [Point(p[0], p[1], 1) for p in wave]),
        Template("circle", [Point(p[0], p[1], 1) for p in circle]),
        Template("push", [Point(p[0], p[1], 1) for p in push]),
        Template("swipe_left", [Point(p[0], p[1], 1) for p in swipe_l]),
        Template("swipe_right", [Point(p[0], p[1], 1) for p in swipe_r]),
    ]


class GestureServer:
    def __init__(self, host="127.0.0.1", port=5000, camera_id=0):
        self.host = host
        self.port = port
        self.camera_id = camera_id
        self.client_socket = None
        self.running = False
        self.recognizer = Recognizer(create_gesture_templates())
        self.gesture_points = deque(maxlen=150)
        self.last_gesture_time = 0
        self.gesture_cooldown = 1.0
        
        if USE_LEGACY_HOLISTIC:
            self.holistic = mp_holistic.Holistic(
                min_detection_confidence=0.5,
                min_tracking_confidence=0.5,
            )
            self.landmarker = None
            self.hand_landmarker = None
        else:
            self.holistic = None
            pose_path = get_model_path("pose_landmarker_lite.task", MODEL_URL)
            hand_path = get_model_path("hand_landmarker.task", HAND_MODEL_URL)
            self.landmarker = vision.PoseLandmarker.create_from_options(
                vision.PoseLandmarkerOptions(
                    base_options=mp_tasks.BaseOptions(model_asset_path=pose_path),
                    running_mode=vision.RunningMode.IMAGE,
                    num_poses=1,
                    min_pose_detection_confidence=0.5,
                    min_pose_presence_confidence=0.5,
                    min_tracking_confidence=0.5,
                )
            )
            self.hand_landmarker = vision.HandLandmarker.create_from_options(
                vision.HandLandmarkerOptions(
                    base_options=mp_tasks.BaseOptions(model_asset_path=hand_path),
                    running_mode=vision.RunningMode.IMAGE,
                    num_hands=2,
                    min_hand_detection_confidence=0.5,
                    min_hand_presence_confidence=0.5,
                    min_tracking_confidence=0.5,
                )
            )
    
    def landmarks_to_dict_legacy(self, pose_landmarks):
        if not pose_landmarks:
            return []
        result = []
        for i, lm in enumerate(pose_landmarks.landmark):
            vis = getattr(lm, 'visibility', 1.0) or 1.0
            result.append({
                "id": i, "name": LANDMARK_NAMES[i] if i < len(LANDMARK_NAMES) else f"landmark_{i}",
                "x": round(lm.x, 4), "y": round(lm.y, 4), "z": round(lm.z, 4),
                "visibility": round(vis, 4)
            })
        return result
    
    def landmarks_to_dict_tasks(self, landmarks_list):
        if not landmarks_list:
            return []
        landmarks = landmarks_list[0]
        result = []
        for i, lm in enumerate(landmarks):
            vis = getattr(lm, 'visibility', 1.0) or 1.0
            result.append({
                "id": i, "name": LANDMARK_NAMES[i] if i < len(LANDMARK_NAMES) else f"landmark_{i}",
                "x": round(lm.x, 4), "y": round(lm.y, 4), "z": round(lm.z, 4),
                "visibility": round(vis, 4)
            })
        return result

    def _override_wrist_with_hand(self, skeleton, results):
        """Override pose wrist (15,16) with accurate hand points when hands detected (legacy)."""
        if not skeleton or not (results.right_hand_landmarks or results.left_hand_landmarks):
            return skeleton
        out = list(skeleton)
        if results.right_hand_landmarks:
            lm = results.right_hand_landmarks.landmark[HAND_INDEX_TIP]
            for i, s in enumerate(out):
                if s["id"] == 16:
                    out[i] = {**s, "x": round(lm.x, 4), "y": round(lm.y, 4)}
                    break
        if results.left_hand_landmarks:
            lm = results.left_hand_landmarks.landmark[HAND_INDEX_TIP]
            for i, s in enumerate(out):
                if s["id"] == 15:
                    out[i] = {**s, "x": round(lm.x, 4), "y": round(lm.y, 4)}
                    break
        return out

    def _override_wrist_tasks(self, skeleton, hand_result):
        """Override pose wrist with hand index tip when hands detected (Tasks API)."""
        if not skeleton or not hand_result.hand_landmarks:
            return skeleton
        out = list(skeleton)
        handedness = getattr(hand_result, 'handedness', None) or []
        for i, hand_lm in enumerate(hand_result.hand_landmarks):
            lm = hand_lm[HAND_INDEX_TIP]
            is_right = (i < len(handedness) and len(handedness[i]) > 0
                        and getattr(handedness[i][0], 'category_name', '') == 'Right')
            wid = 16 if is_right else 15
            for j, s in enumerate(out):
                if s["id"] == wid:
                    out[j] = {**s, "x": round(lm.x, 4), "y": round(lm.y, 4)}
                    break
        return out

    def _skeleton_from_hands_legacy(self, results):
        """Build minimal skeleton from holistic hand landmarks when pose not detected."""
        result = []
        if results.right_hand_landmarks:
            lm = results.right_hand_landmarks.landmark[HAND_INDEX_TIP]
            result.append({"id": 16, "name": "right_wrist", "x": round(lm.x, 4), "y": round(lm.y, 4),
                          "z": 0.0, "visibility": 1.0})
        if results.left_hand_landmarks:
            lm = results.left_hand_landmarks.landmark[HAND_INDEX_TIP]
            result.append({"id": 15, "name": "left_wrist", "x": round(lm.x, 4), "y": round(lm.y, 4),
                          "z": 0.0, "visibility": 1.0})
        return result

    def _skeleton_from_hands_only_tasks(self, hand_result):
        """Build minimal skeleton (wrist only) from hand landmarks when pose is not detected.
        Allows tracking to work with just a hand in frame, no face/body required."""
        if not hand_result or not hand_result.hand_landmarks:
            return []
        handedness = getattr(hand_result, 'handedness', None) or []
        result = []
        for i, hand_lm in enumerate(hand_result.hand_landmarks):
            lm = hand_lm[HAND_INDEX_TIP]
            is_right = (i < len(handedness) and len(handedness[i]) > 0
                        and getattr(handedness[i][0], 'category_name', '') == 'Right')
            wid = 16 if is_right else 15
            name = "right_wrist" if is_right else "left_wrist"
            result.append({
                "id": wid, "name": name, "x": round(lm.x, 4), "y": round(lm.y, 4),
                "z": 0.0, "visibility": 1.0
            })
        return result
    
    def try_recognize_gesture(self):
        if len(self.gesture_points) < 24:
            return None
        if time.time() - self.last_gesture_time < self.gesture_cooldown:
            return None
        points = list(self.gesture_points)
        dollarpy_points = [Point(p[0], p[1], p[2]) for p in points]
        try:
            name, score = self.recognizer.recognize(dollarpy_points)
            if score > 0.20:
                self.last_gesture_time = time.time()
                self.gesture_points.clear()
                return {"name": name, "confidence": round(score, 2)}
        except Exception:
            pass
        return None
    
    def send_message(self, msg):
        if self.client_socket is None:
            return
        try:
            self.client_socket.sendall((json.dumps(msg) + "\n").encode("utf-8"))
        except (BrokenPipeError, ConnectionResetError, OSError):
            self.client_socket = None
    
    def handle_client(self, client_socket, address):
        print(f"[Server] C# client connected from {address}")
        try:
            client_socket.settimeout(0.5)
            while self.running and self.client_socket is client_socket:
                time.sleep(0.1)
        except Exception as e:
            print(f"[Server] Client error: {e}")
        finally:
            if self.client_socket is client_socket:
                self.client_socket = None
            try:
                client_socket.close()
            except Exception:
                pass
            print("[Server] Client disconnected")
    
    def run(self):
        self.running = True
        server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server_socket.bind((self.host, self.port))
        server_socket.listen(1)
        server_socket.settimeout(0.5)
        print(f"[Server] Listening on {self.host}:{self.port}")
        
        cap = cv2.VideoCapture(self.camera_id)
        if not cap.isOpened():
            print("[Server] ERROR: Could not open camera")
            self.running = False
            return
        
        frame_count = 0
        try:
            while self.running:
                if self.client_socket is None:
                    try:
                        client, addr = server_socket.accept()
                        self.client_socket = client
                        t = threading.Thread(target=self.handle_client, args=(client, addr))
                        t.daemon = True
                        t.start()
                    except socket.timeout:
                        pass
                
                ret, frame = cap.read()
                if not ret:
                    continue
                
                frame = cv2.flip(frame, 1)
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                msg = {"type": "frame", "timestamp": time.time(), "skeleton": [], "gesture": None}
                display_frame = frame.copy()
                
                if USE_LEGACY_HOLISTIC:
                    rgb.flags.writeable = False
                    results = self.holistic.process(rgb)
                    h, w = display_frame.shape[:2]
                    if results.pose_landmarks:
                        msg["skeleton"] = self.landmarks_to_dict_legacy(results.pose_landmarks)
                        plm = results.pose_landmarks.landmark
                        draw_skeleton_on_frame(display_frame, plm, h, w)
                        for idx in GESTURE_LANDMARKS:
                            if idx < len(plm):
                                lm = plm[idx]
                                vis = getattr(lm, 'visibility', 1.0) or 1.0
                                if 0 <= lm.x <= 1 and 0 <= lm.y <= 1 and vis > 0.3:
                                    self.gesture_points.append((lm.x, lm.y, 1))
                    if results.right_hand_landmarks or results.left_hand_landmarks:
                        hands = [h for h in (results.right_hand_landmarks, results.left_hand_landmarks) if h]
                        draw_hands_on_frame(display_frame, hands, h, w)
                        for hand_lm in hands:
                            lm = hand_lm.landmark[HAND_INDEX_TIP]
                            self.gesture_points.append((lm.x, lm.y, 1))
                        if msg["skeleton"]:
                            msg["skeleton"] = self._override_wrist_with_hand(msg["skeleton"], results)
                        else:
                            msg["skeleton"] = self._skeleton_from_hands_legacy(results)
                else:
                    h, w = rgb.shape[:2]
                    size = min(h, w)
                    if w != size or h != size:
                        rgb = cv2.resize(rgb, (size, size), interpolation=cv2.INTER_LINEAR)
                        display_frame = cv2.resize(display_frame, (size, size), interpolation=cv2.INTER_LINEAR)
                    rgb = np.ascontiguousarray(rgb)
                    mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                    pose_result = self.landmarker.detect(mp_image)
                    hand_result = self.hand_landmarker.detect(mp_image)
                    if pose_result.pose_landmarks:
                        msg["skeleton"] = self.landmarks_to_dict_tasks(pose_result.pose_landmarks)
                        landmarks = pose_result.pose_landmarks[0]
                        draw_skeleton_on_frame(display_frame, landmarks, size, size)
                        for idx in GESTURE_LANDMARKS:
                            if idx < len(landmarks):
                                lm = landmarks[idx]
                                vis = getattr(lm, 'visibility', 1.0) or 1.0
                                if 0 <= lm.x <= 1 and 0 <= lm.y <= 1 and vis > 0.3:
                                    self.gesture_points.append((lm.x, lm.y, 1))
                    if hand_result.hand_landmarks:
                        draw_hands_on_frame(display_frame, hand_result.hand_landmarks, size, size)
                        for hand_lm in hand_result.hand_landmarks:
                            lm = hand_lm[HAND_INDEX_TIP]
                            self.gesture_points.append((lm.x, lm.y, 1))
                        if msg["skeleton"]:
                            msg["skeleton"] = self._override_wrist_tasks(msg["skeleton"], hand_result)
                        else:
                            # Hand-only mode: no pose detected, use hand landmarks as cursor
                            msg["skeleton"] = self._skeleton_from_hands_only_tasks(hand_result)
                
                if msg["skeleton"] and frame_count % 15 == 0:
                    gesture = self.try_recognize_gesture()
                    if gesture:
                        msg["gesture"] = gesture
                
                # Status overlay
                status = "Tracking" if msg["skeleton"] else "Move into frame..."
                cv2.putText(display_frame, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
                if msg.get("gesture"):
                    cv2.putText(display_frame, "Gesture: " + msg["gesture"]["name"], (10, 60),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)
                cv2.putText(display_frame, "Press Q to quit", (10, display_frame.shape[0] - 10),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                
                cv2.imshow("Gesture - Skeleton Tracking", display_frame)
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    self.running = False
                
                self.send_message(msg)
                frame_count += 1
        finally:
            cap.release()
            cv2.destroyAllWindows()
            server_socket.close()
            if self.holistic:
                self.holistic.close()
            if self.landmarker:
                self.landmarker.close()
            if self.hand_landmarker:
                self.hand_landmarker.close()
            print("[Server] Stopped")


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument("--camera", type=int, default=0)
    args = parser.parse_args()
    server = GestureServer(host=args.host, port=args.port, camera_id=args.camera)
    try:
        server.run()
    except KeyboardInterrupt:
        server.running = False


if __name__ == "__main__":
    main()
