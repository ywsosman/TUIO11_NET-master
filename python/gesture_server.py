"""
Gesture Server - MediaPipe Hands/Holistic + DollarPy
Matches mediapipe.ipynb: HandLandmarker, same template format, INDEX_TIP=8
Uses hand_landmarker.task (hands only) or holistic_landmarker.task (full body)
Communicates with C# via TCP socket (JSON on port 5000)

Per-frame JSON (type "frame") may include:
  - "skeleton", "gesture", "emotion", "gaze"
  - "yolo": optional list of {class, x, y, w, h, conf} (normalized 0-1). Updated on inference
    throttled by YOLO_INFERENCE_EVERY_FRAMES; last boxes are redrawn each camera frame on the server
    preview (stable overlay). TuioDemo does not draw boxes. Not written to disk.
"""

import cv2
import json
import os
import socket
import sys
import threading
import time
from collections import deque

from camera_utils import (
    enumerate_cameras,
    find_splitcam,
    find_default_camera,
    resolve_camera,
    print_camera_list,
)

import numpy as np

try:
    from face_emotion import FaceEmotionAnalyzer, draw_debug_overlay
except ImportError:
    pass  # Optional dependency

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

# YOLO: inference every N frames (keep N well above ~5 so boxes are not jittery/disappearing every few frames).
# The OpenCV preview still draws the last boxes every frame via _yolo_overlay_boxes for a steady overlay.
YOLO_INFERENCE_EVERY_FRAMES = 24

# Webcam: smaller resolution + buffer=1 reduces latency and CPU. Landmarks stay normalized 0-1.
# Override with --width/--height 0 to use the driver default resolution.
_DEFAULT_CAPTURE_WIDTH = 640
_DEFAULT_CAPTURE_HEIGHT = 480

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


def resolve_yolo_model_path():
    """Prefer local yolo26m.pt; fall back so YOLO can still run if only small weights exist."""
    for rel in ("yolo26m.pt", os.path.join("YOLO", "yolo26m.pt")):
        for base in (_PROJECT_ROOT, _SCRIPT_DIR):
            p = os.path.join(base, rel)
            if os.path.isfile(p):
                return p
    return None


def _configure_gesture_capture(cap, width, height):
    """Smaller frame + buffer size 1 = less lag on Windows USB webcams."""
    try:
        cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    except Exception:
        pass
    if width is not None and int(width) > 0:
        try:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, int(width))
        except Exception:
            pass
    if height is not None and int(height) > 0:
        try:
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, int(height))
        except Exception:
            pass
    try:
        cap.set(cv2.CAP_PROP_FPS, 30)
    except Exception:
        pass


def open_gesture_camera(camera_id, width, height, prefer_splitcam=False):
    """
    Resolve the best available camera (SplitCam-aware), then open it.
    Falls back through Windows backends (DSHOW → MSMF → ANY).
    Returns a configured cv2.VideoCapture or None.
    """
    # --- camera selection (may override camera_id with SplitCam index) ---
    resolved_id, cam_name = resolve_camera(
        requested=camera_id,
        prefer_splitcam=prefer_splitcam,
        fallback_any=True,
    )

    if sys.platform == "win32":
        backends = [
            (cv2.CAP_DSHOW, "DSHOW"),
            (cv2.CAP_MSMF, "MSMF"),
            (cv2.CAP_ANY, "ANY"),
        ]
    else:
        backends = [(cv2.CAP_ANY, "ANY")]

    for backend, label in backends:
        cap = cv2.VideoCapture(resolved_id, backend)
        if not cap.isOpened():
            cap.release()
            continue
        ok, frame = cap.read()
        if ok and frame is not None and frame.size > 0:
            _configure_gesture_capture(cap, width, height)
            size_desc = (
                "driver default"
                if not width or not height or int(width) <= 0 or int(height) <= 0
                else f"{int(width)}x{int(height)}"
            )
            print(
                f"[Server] Opened '{cam_name}' index={resolved_id} ({label}), "
                f"buffer=1, target size={size_desc}",
                flush=True,
            )
            return cap
        cap.release()
    print(f"[Server] ERROR: Could not open camera '{cam_name}' (index={resolved_id})", flush=True)
    return None


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
    def __init__(
        self,
        host="127.0.0.1",
        port=5000,
        camera_id=0,
        templates_path=None,
        body=False,
        debug=False,
        capture_width=None,
        capture_height=None,
        prefer_splitcam=False,
    ):
        self.host = host
        self.port = port
        self.camera_id = camera_id
        self.prefer_splitcam = prefer_splitcam
        self.capture_width = (
            _DEFAULT_CAPTURE_WIDTH if capture_width is None else capture_width
        )
        self.capture_height = (
            _DEFAULT_CAPTURE_HEIGHT if capture_height is None else capture_height
        )
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

        try:
            self.face_analyzer = FaceEmotionAnalyzer()
            print("[Server] FaceEmotionAnalyzer loaded successfully.", flush=True)
        except Exception as e:
            print(f"[Server] FaceEmotionAnalyzer disabled: {e}", flush=True)
            self.face_analyzer = None

        # YOLO — msg["yolo"] only on inference ticks; stable preview uses _yolo_overlay_boxes.
        self.yolo_model = None
        try:
            from ultralytics import YOLO

            weights = resolve_yolo_model_path()
            if weights:
                self.yolo_model = YOLO(weights)
                print(f"[Server] YOLO loaded: {weights} (infer every {YOLO_INFERENCE_EVERY_FRAMES} frames)", flush=True)
            else:
                # Ultralytics downloads yolo11n on first use
                self.yolo_model = YOLO("yolo11n.pt")
                print(f"[Server] YOLO using yolo11n.pt (infer every {YOLO_INFERENCE_EVERY_FRAMES} frames). Add yolo26m.pt to project root for larger model.", flush=True)
        except Exception as e:
            print(f"[Server] YOLO disabled: {e}", flush=True)
            exe = getattr(sys, "executable", "python")
            print(f"[Server] This server is using Python: {exe}", flush=True)
            print(
                "[Server] Run: "
                + f'"{exe}" -m pip install ultralytics',
                flush=True,
            )

        # Pixel-space boxes redrawn on preview every frame; updated only when YOLO runs.
        self._yolo_overlay_boxes = []

        # Gaze Tracking — MediaPipe FaceMesh + Kalman filter (from GazeTracking/)
        _gaze_dir = os.path.join(_PROJECT_ROOT, "GazeTracking")
        if _gaze_dir not in sys.path:
            sys.path.insert(0, _gaze_dir)
        try:
            from gaze_tracker import create_tracker as _create_gaze_tracker  # type: ignore[import]
            from gaze_filter import create_gaze_filter as _create_gaze_filter  # type: ignore[import]

            # If SplitCam is preferred, gaze uses the virtual cam independently;
            # otherwise we inject the same frame already captured for hand tracking.
            if prefer_splitcam:
                from camera_utils import find_splitcam, enumerate_cameras
                _cams = enumerate_cameras()
                _sc = find_splitcam(_cams)
                _gaze_cam_idx = _sc["index"] if (_sc and _sc["index"] != camera_id) else camera_id
            else:
                _gaze_cam_idx = camera_id

            self._gaze_tracker = _create_gaze_tracker(camera=_gaze_cam_idx, api="legacy")
            self._gaze_filter  = _create_gaze_filter("kalman", strength=0.5)

            # Open a dedicated VideoCapture only when the gaze cam differs from
            # the gesture cam (avoids fighting over the same device handle).
            if prefer_splitcam and _gaze_cam_idx != camera_id:
                _backend = cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY
                self._gaze_cap = cv2.VideoCapture(_gaze_cam_idx, _backend)
                print(f"[Server] Gaze Tracking on dedicated camera index {_gaze_cam_idx} (SplitCam).", flush=True)
            else:
                self._gaze_cap = None  # reuse the gesture frame
                print("[Server] Gaze Tracking sharing the gesture camera frame.", flush=True)

            self.gaze_enabled = True
            print("[Server] GazeTracker (MediaPipe FaceMesh + Kalman) loaded.", flush=True)
        except Exception as e:
            print(f"[Server] Gaze Tracking disabled: {e}", flush=True)
            self._gaze_tracker = None
            self._gaze_filter  = None
            self._gaze_cap     = None
            self.gaze_enabled  = False

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

        cap = open_gesture_camera(
            self.camera_id,
            self.capture_width,
            self.capture_height,
            prefer_splitcam=self.prefer_splitcam,
        )
        if cap is None:
            print("[Server] ERROR: Could not open any camera", flush=True)
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
                msg = {"type": "frame", "timestamp": time.time(), "skeleton": [], "gesture": None, "emotion": None, "yolo": [], "gaze": None}
                display_frame = frame.copy()

                if self.face_analyzer:
                    emotion_result = self.face_analyzer.analyze(frame)
                    bbox = self.face_analyzer.last_bbox()
                    msg["emotion"] = emotion_result

                    face_debug = frame.copy()
                    draw_debug_overlay(face_debug, emotion_result, bbox)
                    cv2.imshow("Face Emotion Debug", face_debug)

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
                    # Use only first hand for gesture stroke and pinch detection (avoid zigzag from 2 hands)
                    if hand_landmarks:
                        hl = hand_landmarks[0]
                        
                        # Pinch detection for "enter"
                        x_idx, y_idx = hl[INDEX_TIP].x, hl[INDEX_TIP].y
                        x_thb, y_thb = hl[THUMB_TIP].x, hl[THUMB_TIP].y
                        pinch_dist = ((x_idx - x_thb)**2 + (y_idx - y_thb)**2)**0.5
                        is_pinching = pinch_dist < 0.05
                        
                        if is_pinching:
                            if not getattr(self, "pinch_active", False):
                                self.pinch_active = True
                                msg["gesture"] = {"name": "enter", "confidence": 1.0}
                                print("[Server] Pinch detected -> enter", flush=True)
                        else:
                            self.pinch_active = False

                        # Stroke tracking
                        if self.recognizer:
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

                # YOLO (throttled inference; stable boxes on preview via _yolo_overlay_boxes)
                if self.yolo_model and frame_count % YOLO_INFERENCE_EVERY_FRAMES == 0:
                    try:
                        results = self.yolo_model(frame, verbose=False)
                        overlay = []
                        if results and len(results) > 0:
                            boxes = results[0].boxes
                            if boxes is not None and len(boxes) > 0:
                                for box in boxes:
                                    xyxy = box.xyxy[0].cpu().numpy()
                                    conf = float(box.conf[0])
                                    cls = int(box.cls[0])
                                    class_name = results[0].names[cls]
                                    x1, y1, x2, y2 = map(int, xyxy)
                                    msg["yolo"].append({"class": class_name, "x": (x1+x2)/2/w, "y": (y1+y2)/2/h, "w": (x2-x1)/w, "h": (y2-y1)/h, "conf": conf})
                                    overlay.append((x1, y1, x2, y2, class_name, conf))
                        self._yolo_overlay_boxes = overlay
                    except Exception as e:
                        if self._debug:
                            print(f"[Server] YOLO inference error: {e}", flush=True)

                for x1, y1, x2, y2, cname, yconf in self._yolo_overlay_boxes:
                    cv2.rectangle(display_frame, (x1, y1), (x2, y2), (0, 165, 255), 2)
                    lab = f"{cname} {yconf:.2f}"
                    cv2.putText(
                        display_frame,
                        lab,
                        (x1, max(y1 - 6, 14)),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5,
                        (0, 165, 255),
                        1,
                        cv2.LINE_AA,
                    )

                # Gaze tracking (MediaPipe FaceMesh + Kalman) every 2 frames
                if self.gaze_enabled and frame_count % 2 == 0:
                    try:
                        # Use dedicated SplitCam frame if available, else share gesture frame
                        if self._gaze_cap is not None and self._gaze_cap.isOpened():
                            ret_g, gaze_frame = self._gaze_cap.read()
                            if not ret_g or gaze_frame is None:
                                gaze_frame = frame
                        else:
                            gaze_frame = frame

                        gaze_result = self._gaze_tracker.estimate_gaze(gaze_frame)
                        if gaze_result.face_detected:
                            sx, sy = self._gaze_filter.process(gaze_result.x, gaze_result.y)
                            msg["gaze"] = {
                                "x":     round(sx, 4),
                                "y":     round(sy, 4),
                                "yaw":   round(gaze_result.yaw, 4),
                                "pitch": round(gaze_result.pitch, 4),
                            }
                            gx, gy = int(sx * w), int(sy * h)
                            cv2.circle(display_frame, (gx, gy), 15, (255, 255, 0), 2)
                            cv2.putText(display_frame, "Gaze", (gx - 20, gy - 20),
                                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
                    except Exception as e:
                        if self._debug:
                            print(f"[Server] Gaze error: {e}", flush=True)

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
            if self._gaze_cap is not None:
                self._gaze_cap.release()
            cv2.destroyAllWindows()
            server_socket.close()
            self.hand_landmarker.close()
            if self.holistic_landmarker:
                self.holistic_landmarker.close()
            print("[Server] Stopped", flush=True)


def main():
    import argparse
    parser = argparse.ArgumentParser(
        description="Gesture Server – MediaPipe Hands + DollarPy recognizer."
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument(
        "--camera", type=int, default=0,
        help="Camera index to use (default: 0).",
    )
    parser.add_argument(
        "--splitcam", action="store_true",
        help="Prefer SplitCam (or any virtual camera) over the default webcam.",
    )
    parser.add_argument(
        "--list-cameras", action="store_true",
        help="List all detected cameras and exit.",
    )
    parser.add_argument("--templates", default=None, help="Path to gesture_templates.json")
    parser.add_argument(
        "--body", action="store_true",
        help="Use holistic_landmarker (pose+hands) instead of hand only.",
    )
    parser.add_argument("--debug", action="store_true", help="Print recognition attempts.")
    parser.add_argument(
        "--width",
        type=int,
        default=_DEFAULT_CAPTURE_WIDTH,
        help="Capture width (0 = driver default; lower = faster). Default %(default)s.",
    )
    parser.add_argument(
        "--height",
        type=int,
        default=_DEFAULT_CAPTURE_HEIGHT,
        help="Capture height (0 = driver default). Default %(default)s.",
    )
    args = parser.parse_args()

    if args.list_cameras:
        print_camera_list()
        return

    server = GestureServer(
        host=args.host,
        port=args.port,
        camera_id=args.camera,
        templates_path=args.templates,
        body=args.body,
        debug=args.debug,
        capture_width=args.width,
        capture_height=args.height,
        prefer_splitcam=args.splitcam,
    )
    try:
        server.run()
    except KeyboardInterrupt:
        server.running = False


if __name__ == "__main__":
    main()
