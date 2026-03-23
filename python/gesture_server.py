"""
Gesture Server - MediaPipe Hands/Holistic + DollarPy
Matches mediapipe.ipynb: HandLandmarker, same template format, INDEX_TIP=8
Uses hand_landmarker.task (hands only) or holistic_landmarker.task (full body)
Communicates with C# via TCP socket (JSON on port 5000)
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

from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.vision.core import image as mp_image

# Same as mediapipe.ipynb
INDEX_TIP, THUMB_TIP = 8, 4
mp_drawing = vision.drawing_utils
mp_styles = vision.drawing_styles

# Pose landmark names (for holistic / C# compatibility)
POSE_LANDMARK_NAMES = [
    "nose", "left_eye_inner", "left_eye", "left_eye_outer",
    "right_eye_inner", "right_eye", "right_eye_outer",
    "left_ear", "right_ear", "mouth_left", "mouth_right",
    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
    "left_wrist", "right_wrist", "left_pinky", "right_pinky",
    "left_index", "right_index", "left_thumb", "right_thumb",
    "left_hip", "right_hip", "left_knee", "right_knee",
    "left_ankle", "right_ankle", "left_heel", "right_heel",
    "left_foot_index", "right_foot_index",
]

# Paths
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_PROJECT_ROOT = os.path.dirname(_SCRIPT_DIR)
GESTURE_TEMPLATES_DEFAULT = os.path.join(_PROJECT_ROOT, "gesture_templates.json")

# C# radial menu mapping
GESTURE_TO_RADIAL = {
    "circle": "pointer_up",
    "square": "fist",
    "rectangle": "open_hand",
}


def get_model_path(filename):
    """Find model in python/, project root. Download if missing."""
    for base in (_SCRIPT_DIR, _PROJECT_ROOT):
        path = os.path.join(base, filename)
        if os.path.isfile(path):
            return path
    urls = {
        "hand_landmarker.task": "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task",
        "holistic_landmarker.task": "https://storage.googleapis.com/mediapipe-models/holistic_landmarker/holistic_landmarker/float16/1/holistic_landmarker.task",
    }
    if filename in urls:
        path = os.path.join(_PROJECT_ROOT, filename)
        print(f"[Server] Downloading {filename}...", flush=True)
        import urllib.request
        urllib.request.urlretrieve(urls[filename], path)
        return path
    raise FileNotFoundError(f"Model not found: {filename}")


def load_templates(filepath=GESTURE_TEMPLATES_DEFAULT):
    """Same as mediapipe.ipynb load_templates. Checks project root, python/, and cwd."""
    candidates = [
        filepath if os.path.isabs(filepath) else os.path.join(_PROJECT_ROOT, filepath),
        os.path.join(_PROJECT_ROOT, "gesture_templates.json"),
        os.path.join(_SCRIPT_DIR, "gesture_templates.json"),
        os.path.join(os.getcwd(), "gesture_templates.json"),
    ]
    seen = set()
    for path in candidates:
        path = os.path.normpath(os.path.abspath(path))
        if path in seen or not os.path.isfile(path):
            continue
        seen.add(path)
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            print(f"[Server] Failed to parse {path}: {e}", flush=True)
            return []
        if not isinstance(data, dict) or "templates" not in data:
            return []
        result = []
        for d in data["templates"]:
            if not d.get("name") or not d.get("points") or len(d["points"]) < 5:
                continue
            pts = []
            for p in d["points"]:
                if isinstance(p, (list, tuple)) and len(p) >= 2:
                    pts.append(Point(float(p[0]), float(p[1]), p[2] if len(p) > 2 else 1))
                elif isinstance(p, dict):
                    pts.append(Point(float(p.get("x", 0)), float(p.get("y", 0)), 1))
            if len(pts) >= 5:
                result.append(Template(d["name"], pts))
        if result:
            print(f"[Server] Loaded {len(result)} templates from {path}", flush=True)
            return result
    print(f"[Server] gesture_templates.json not found", flush=True)
    return []


class GestureServer:
    def __init__(self, host="127.0.0.1", port=5000, camera_id=0, templates_path=None, body=False, debug=False):
        self.host = host
        self.port = port
        self.camera_id = camera_id
        self.client_socket = None
        self.running = False
        self._debug = debug

        # Load templates (same as notebook)
        path = templates_path or GESTURE_TEMPLATES_DEFAULT
        if not os.path.isabs(path):
            path = os.path.join(_PROJECT_ROOT, path)
        templates = load_templates(path)
        if templates:
            self.recognizer = Recognizer(templates)
            self._use_radial_mapping = True
            names = list(dict.fromkeys(t.name for t in templates))
            print("[Server] Using gesture_templates.json:", names, flush=True)
        else:
            self.recognizer = None
            self._use_radial_mapping = False
            print("[Server] No templates - train in mediapipe.ipynb first", flush=True)

        self.gesture_points = deque(maxlen=150)
        self.last_gesture_time = 0
        self.gesture_cooldown = 1.0

        # Models: hand_landmarker (hands only) or holistic_landmarker (body)
        hand_path = get_model_path("hand_landmarker.task")
        hand_opts = vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=hand_path),
            num_hands=2,
            min_hand_detection_confidence=0.5,
            min_hand_presence_confidence=0.5,
            min_tracking_confidence=0.5,
        )
        self.hand_landmarker = vision.HandLandmarker.create_from_options(hand_opts)

        if body:
            holistic_path = get_model_path("holistic_landmarker.task")
            holistic_opts = vision.HolisticLandmarkerOptions(
                base_options=BaseOptions(model_asset_path=holistic_path),
                running_mode=vision.RunningMode.IMAGE,
                min_pose_detection_confidence=0.5,
                min_hand_landmarks_confidence=0.5,
            )
            self.holistic_landmarker = vision.HolisticLandmarker.create_from_options(holistic_opts)
            print("[Server] Using holistic_landmarker (pose + hands)", flush=True)
        else:
            self.holistic_landmarker = None
            print("[Server] Using hand_landmarker only (matches notebook)", flush=True)

    def _skeleton_from_hands(self, hand_landmarks, handedness=None):
        """Build cursor skeleton from hand index tips (C# expects right_wrist/left_wrist)."""
        if not hand_landmarks:
            return []
        result = []
        handedness = handedness or []
        for i, hl in enumerate(hand_landmarks):
            x, y = hl[INDEX_TIP].x, hl[INDEX_TIP].y
            is_right = i < len(handedness) and handedness[i] and getattr(handedness[i][0], "category_name", "") == "Right"
            wid = 16 if is_right else 15
            name = "right_wrist" if is_right else "left_wrist"
            result.append({"id": wid, "name": name, "x": round(x, 4), "y": round(y, 4), "z": 0.0, "visibility": 1.0})
        return result

    def try_recognize_gesture(self):
        if not self.recognizer:
            return None
        min_pts = 12 if self._use_radial_mapping else 24
        if len(self.gesture_points) < min_pts:
            return None
        if time.time() - self.last_gesture_time < self.gesture_cooldown:
            return None
        points = [Point(p[0], p[1], p[2] if len(p) > 2 else 1) for p in list(self.gesture_points)]
        try:
            name, score = self.recognizer.recognize(points)
            if self._debug:
                status = "MATCH" if score > 0.12 else "low"
                print(f"[Server] try_recognize: {len(points)} pts -> {name}={score:.3f} ({status})", flush=True)
            if name and score > 0.12:
                self.last_gesture_time = time.time()
                self.gesture_points.clear()
                out_name = GESTURE_TO_RADIAL.get(name.lower(), name) if self._use_radial_mapping else name
                result = {"name": out_name, "confidence": round(score, 2)}
                if self._use_radial_mapping:
                    result["_template"] = name
                return result
        except Exception as e:
            if self._debug:
                print(f"[Server] recognize error: {e}", flush=True)
        return None

    def send_message(self, msg):
        if self.client_socket is None:
            return
        try:
            self.client_socket.sendall((json.dumps(msg) + "\n").encode("utf-8"))
        except (BrokenPipeError, ConnectionResetError, OSError):
            self.client_socket = None

    def handle_client(self, client_socket, address):
        print(f"[Server] C# client connected from {address}", flush=True)
        try:
            client_socket.settimeout(0.5)
            while self.running and self.client_socket is client_socket:
                time.sleep(0.1)
        except Exception as e:
            print(f"[Server] Client error: {e}", flush=True)
        finally:
            if self.client_socket is client_socket:
                self.client_socket = None
            try:
                client_socket.close()
            except Exception:
                pass
            print("[Server] Client disconnected", flush=True)

    def run(self):
        self.running = True
        server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server_socket.bind((self.host, self.port))
        server_socket.listen(1)
        server_socket.settimeout(0.5)
        print(f"[Server] Listening on {self.host}:{self.port}", flush=True)

        cap = cv2.VideoCapture(self.camera_id)
        if not cap.isOpened():
            print("[Server] ERROR: Could not open camera", flush=True)
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
                rgb = np.ascontiguousarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                h, w = rgb.shape[:2]
                msg = {"type": "frame", "timestamp": time.time(), "skeleton": [], "gesture": None}
                display_frame = frame.copy()

                # Full frame - no resize (matches notebook coordinate system)
                mp_img = mp_image.Image(image_format=mp_image.ImageFormat.SRGB, data=rgb)

                if self.holistic_landmarker:
                    result = self.holistic_landmarker.detect(mp_img)
                    if result.pose_landmarks and len(result.pose_landmarks) > 0:
                        plm = result.pose_landmarks[0]
                        for idx, lm in enumerate(plm):
                            name = POSE_LANDMARK_NAMES[idx] if idx < len(POSE_LANDMARK_NAMES) else f"landmark_{idx}"
                            msg["skeleton"].append({
                                "id": idx, "name": name,
                                "x": round(lm.x, 4), "y": round(lm.y, 4), "z": round(lm.z, 4),
                                "visibility": 1.0
                            })
                        for idx, lm in enumerate(plm):
                            x, y = int(lm.x * w), int(lm.y * h)
                            cv2.circle(display_frame, (x, y), 3, (0, 255, 0), -1)
                    hand_landmarks = []
                    handedness = []
                    if result.right_hand_landmarks and len(result.right_hand_landmarks) >= 21:
                        hand_landmarks.append(result.right_hand_landmarks)
                        handedness.append([type("C", (), {"category_name": "Right"})()])
                    if result.left_hand_landmarks and len(result.left_hand_landmarks) >= 21:
                        hand_landmarks.append(result.left_hand_landmarks)
                        handedness.append([type("C", (), {"category_name": "Left"})()])
                else:
                    result = self.hand_landmarker.detect(mp_img)
                    hand_landmarks = list(result.hand_landmarks) if result.hand_landmarks else []
                    handedness = list(result.handedness) if result.handedness else []

                if hand_landmarks:
                    if not msg["skeleton"]:
                        msg["skeleton"] = self._skeleton_from_hands(hand_landmarks, handedness)
                    else:
                        # Override wrist with hand index tip
                        for i, hl in enumerate(hand_landmarks):
                            x, y = hl[INDEX_TIP].x, hl[INDEX_TIP].y
                            is_right = i < len(handedness) and handedness[i] and getattr(handedness[i][0], "category_name", "") == "Right"
                            wid = 16 if is_right else 15
                            for s in msg["skeleton"]:
                                if s["id"] == wid:
                                    s["x"], s["y"] = round(x, 4), round(y, 4)
                                    break

                    for hl in hand_landmarks:
                        mp_drawing.draw_landmarks(
                            display_frame, hl,
                            vision.HandLandmarksConnections.HAND_CONNECTIONS,
                            mp_styles.get_default_hand_landmarks_style(),
                            mp_styles.get_default_hand_connections_style(),
                        )
                        x, y = hl[INDEX_TIP].x, hl[INDEX_TIP].y
                        cv2.circle(display_frame, (int(x * w), int(y * h)), 8, (0, 255, 0), -1)
                    # Use only first hand for gesture stroke (avoid zigzag from 2 hands)
                    if hand_landmarks and self.recognizer:
                        hl = hand_landmarks[0]
                        x, y = hl[INDEX_TIP].x, hl[INDEX_TIP].y
                        if not self.gesture_points or (x - self.gesture_points[-1][0])**2 + (y - self.gesture_points[-1][1])**2 > 0.00005:
                            self.gesture_points.append((x, y, 1))

                if msg["skeleton"] and self.recognizer and frame_count % 10 == 0 and len(self.gesture_points) >= 12:
                    gesture = self.try_recognize_gesture()
                    if gesture:
                        msg["gesture"] = gesture
                        tpl = gesture.get("_template", gesture["name"])
                        print(f"[Server] Matched: {tpl} -> {gesture['name']} (score={gesture['confidence']})", flush=True)
                    self.gesture_points.clear()

                status = "Tracking" if msg["skeleton"] else "Move into frame..."
                cv2.putText(display_frame, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
                if self.gesture_points and self._use_radial_mapping:
                    cv2.putText(display_frame, f"Gesture pts: {len(self.gesture_points)}", (10, 55),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 0), 1)
                if msg.get("gesture"):
                    cv2.putText(display_frame, "Gesture: " + msg["gesture"]["name"], (10, 80),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)
                cv2.putText(display_frame, "Press Q to quit", (10, h - 10),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)

                cv2.imshow("Gesture - Radial Menu", display_frame)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    self.running = False

                self.send_message(msg)
                frame_count += 1
        finally:
            cap.release()
            cv2.destroyAllWindows()
            server_socket.close()
            self.hand_landmarker.close()
            if self.holistic_landmarker:
                self.holistic_landmarker.close()
            print("[Server] Stopped", flush=True)


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--templates", default=None, help="Path to gesture_templates.json")
    parser.add_argument("--body", action="store_true", help="Use holistic_landmarker (pose+hands) instead of hand only")
    parser.add_argument("--debug", action="store_true", help="Print recognition attempts")
    args = parser.parse_args()
    server = GestureServer(
        host=args.host,
        port=args.port,
        camera_id=args.camera,
        templates_path=args.templates,
        body=args.body,
        debug=args.debug,
    )
    try:
        server.run()
    except KeyboardInterrupt:
        server.running = False


if __name__ == "__main__":
    main()
