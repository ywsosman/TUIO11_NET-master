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

import numpy as np

try:
    from gaze_evaluator import GazeEvaluator
except ImportError:
    GazeEvaluator = None  # report generation optional

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

# Add GazeTracking/ and YOLO/ to sys.path so their modules can be imported directly.
_GAZE_DIR = os.path.join(_PROJECT_ROOT, "GazeTracking")
_YOLO_DIR = os.path.join(_PROJECT_ROOT, "YOLO")
for _extra_dir in (_GAZE_DIR, _YOLO_DIR):
    if _extra_dir not in sys.path:
        sys.path.insert(0, _extra_dir)

# YOLO: inference every N frames (keep N well above ~5 so boxes are not jittery/disappearing every few frames).
# The OpenCV preview still draws the last boxes every frame via _yolo_overlay_boxes for a steady overlay.
YOLO_INFERENCE_EVERY_FRAMES = 24

# Gaze: run FaceLandmarker every N frames. 1 = every frame (best cursor fidelity).
# Raise to 2 on slow machines if the main loop lags.
GAZE_EVERY_N_FRAMES = 1

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
        "face_landmarker.task": "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task",
    }
    if filename in urls:
        path = os.path.join(_PROJECT_ROOT, filename)
        print(f"[Server] Downloading {filename}...", flush=True)
        import urllib.request
        urllib.request.urlretrieve(urls[filename], path)
        return path
    raise FileNotFoundError(f"Model not found: {filename}")


def resolve_yolo_model_path():
    """
    Priority order:
      1. fruit_best.pt  — custom-trained fruit model (produced by train_fruit_model.py)
      2. yolo26m.pt     — local custom weights (COCO-based fallback)
      3. None           — YOLO will auto-download yolo11n.pt (generic, no fruits)
    """
    candidates = [
        "fruit_best.pt",
        os.path.join("runs", "detect", "fruit_train", "weights", "best.pt"),
        "yolo26m.pt",
        os.path.join("YOLO", "yolo26m.pt"),
    ]
    for rel in candidates:
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


def open_gesture_camera(camera_id, width, height):
    """
    Try Windows backends (DSHOW first) then fall back.
    Returns a configured cv2.VideoCapture or None.
    """
    if sys.platform == "win32":
        # MSMF first: it supports Win11's multi-app camera sharing so the
        # yolo_tuio_bridge can use the same physical camera concurrently.
        backends = [
            (cv2.CAP_MSMF, "MSMF"),
            (cv2.CAP_DSHOW, "DSHOW"),
            (cv2.CAP_ANY, "ANY"),
        ]
    else:
        backends = [(cv2.CAP_ANY, "ANY")]

    for backend, label in backends:
        cap = cv2.VideoCapture(camera_id, backend)
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
                f"[Server] Camera index {camera_id} ({label}), buffer=1, target size={size_desc}",
                flush=True,
            )
            return cap
        cap.release()
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
        gaze_camera=None,
    ):
        self.host = host
        self.port = port
        self.camera_id = camera_id
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
                self.yolo_model = YOLO("yolo11n.pt")
                print(f"[Server] YOLO using yolo11n.pt (infer every {YOLO_INFERENCE_EVERY_FRAMES} frames).", flush=True)
        except Exception as e:
            print(f"[Server] YOLO disabled: {e}", flush=True)
            exe = getattr(sys, "executable", "python")
            print(f'[Server] Run: "{exe}" -m pip install ultralytics', flush=True)

        # SORT tracker — gives stable track IDs across YOLO inference ticks.
        self._sort_tracker = None
        try:
            from sort_tracker import SORTTracker
            self._sort_tracker = SORTTracker(max_age=30, min_hits=1, iou_threshold=0.3)
            print("[Server] SORT tracker loaded.", flush=True)
        except Exception as e:
            print(f"[Server] SORT tracker disabled (boxes won't have IDs): {e}", flush=True)

        # Pixel-space boxes redrawn on preview every frame; updated only when YOLO runs.
        self._yolo_overlay_boxes = []   # (x1, y1, x2, y2, class_name, conf, track_id)

        # Object and Laser trackers for DollarPy
        self.object_tracker = None
        try:
            from object_tracker import ObjectTracker
            self.object_tracker = ObjectTracker()
            print("[Server] ObjectTracker loaded for DollarPy.", flush=True)
        except Exception as e:
            print(f"[Server] ObjectTracker disabled: {e}", flush=True)
            
        self.laser_tracker = None
        try:
            from laser_tracker import LaserStrokeAccumulator, find_laser_blob
            self.laser_tracker = LaserStrokeAccumulator()
            self.find_laser_blob = find_laser_blob
            self.laser_hsv_lower = np.array([160, 100, 100]) # default red
            self.laser_hsv_upper = np.array([180, 255, 255])
            print("[Server] LaserTracker loaded for DollarPy.", flush=True)
        except Exception as e:
            print(f"[Server] LaserTracker disabled: {e}", flush=True)

        # Gaze Tracking — MediaPipe FaceMesh + Kalman filter
        # Uses gaze_camera if specified (e.g. SplitCam); otherwise shares the gesture frame.
        self._gaze_tracker = None
        self._gaze_filter  = None
        self._gaze_cap     = None
        self.gaze_enabled  = False
        try:
            from gaze_tracker import create_tracker as _create_gaze_tracker
            from gaze_filter import create_gaze_filter as _create_gaze_filter

            _gaze_cam_idx = gaze_camera if gaze_camera is not None else camera_id
            _face_model = get_model_path("face_landmarker.task")
            self._gaze_tracker = _create_gaze_tracker(camera=_gaze_cam_idx, api="new", model_path=_face_model)
            # OneEuroFilter: fast-moving → responsive (no lag), stationary → smooth (no jitter).
            # Ideal for live cursor use. beta=0.07 adds speed-dependent cutoff boost.
            self._gaze_filter  = _create_gaze_filter("euro", strength=0.7)

            # Open a dedicated capture only when gaze uses a different camera index.
            if gaze_camera is not None and gaze_camera != camera_id:
                _backend = cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY
                self._gaze_cap = cv2.VideoCapture(gaze_camera, _backend)
                print(f"[Server] Gaze using dedicated camera {gaze_camera} (e.g. SplitCam).", flush=True)
            else:
                self._gaze_cap = None   # reuse gesture frame — no extra device opened
                print("[Server] Gaze sharing the gesture camera frame.", flush=True)

            self.gaze_enabled = True
            print("[Server] GazeTracker (MediaPipe FaceMesh + Kalman) loaded.", flush=True)
        except Exception as e:
            print(f"[Server] Gaze Tracking disabled: {e}", flush=True)

        # Gaze Evaluator — generates heatmap / scanpath / JSON log / markdown report on exit
        self._gaze_evaluator = None
        if self.gaze_enabled and GazeEvaluator is not None:
            _ts = time.strftime("%Y%m%d_%H%M%S")
            _report_dir = os.path.join(_PROJECT_ROOT, "gaze_reports", f"session_{_ts}")
            self._gaze_evaluator = GazeEvaluator(output_dir=_report_dir)
            print(f"[Server] Gaze evaluation active → {_report_dir}", flush=True)

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
            self.camera_id, self.capture_width, self.capture_height
        )
        if cap is None:
            print("[Server] ERROR: Could not open camera", flush=True)
            self.running = False
            return

        frame_count = 0
        _gaze_face_hits = 0
        _gaze_status_t  = time.time()
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
                        gesture["source"] = "skeleton"
                        msg["gesture"] = gesture
                        tpl = gesture.get("_template", gesture["name"])
                        print(f"[Server] Matched: {tpl} -> {gesture['name']} (score={gesture['confidence']})", flush=True)
                    self.gesture_points.clear()

                # YOLO (throttled inference; stable boxes on preview via _yolo_overlay_boxes)
                if self.yolo_model and frame_count % YOLO_INFERENCE_EVERY_FRAMES == 0:
                    try:
                        results = self.yolo_model(frame, verbose=False)
                        raw_dets = []   # [x1, y1, x2, y2, conf] for SORT
                        det_meta = []   # (class_name) matching raw_dets order
                        if results and len(results) > 0:
                            boxes = results[0].boxes
                            if boxes is not None and len(boxes) > 0:
                                self.current_proximity = "ok"
                                for box in boxes:
                                    xyxy = box.xyxy[0].cpu().numpy()
                                    conf = float(box.conf[0])
                                    cls  = int(box.cls[0])
                                    class_name = results[0].names[cls]
                                    raw_dets.append([xyxy[0], xyxy[1], xyxy[2], xyxy[3], conf])
                                    det_meta.append(class_name)
                                    
                                    if class_name == "person":
                                        area = ((xyxy[2] - xyxy[0]) * (xyxy[3] - xyxy[1])) / (w * h)
                                        if area > 0.6:
                                            self.current_proximity = "too_close"
                                        elif area < 0.08:
                                            self.current_proximity = "too_far"

                        # Run SORT to assign stable track IDs
                        overlay = []
                        if self._sort_tracker and raw_dets:
                            import numpy as _np
                            tracked = self._sort_tracker.update(_np.array(raw_dets))
                            # tracked rows: [x1, y1, x2, y2, track_id]
                            for track in tracked:
                                tx1, ty1, tx2, ty2, tid = int(track[0]), int(track[1]), int(track[2]), int(track[3]), int(track[4])
                                # Match back to a class name by IoU with raw_dets
                                best_cls, best_conf = "object", 0.0
                                for i, rd in enumerate(raw_dets):
                                    if abs(rd[0] - tx1) < 10 and abs(rd[1] - ty1) < 10:
                                        best_cls, best_conf = det_meta[i], rd[4]
                                        break
                                msg["yolo"].append({"track_id": tid, "class": best_cls,
                                                    "x": (tx1+tx2)/2/w, "y": (ty1+ty2)/2/h,
                                                    "w": (tx2-tx1)/w,   "h": (ty2-ty1)/h,
                                                    "conf": round(best_conf, 3)})
                                overlay.append((tx1, ty1, tx2, ty2, best_cls, best_conf, tid))
                                
                                # Update object tracker for DollarPy gestures
                                if self.object_tracker:
                                    cx, cy = (tx1+tx2)/2/w, (ty1+ty2)/2/h
                                    self.object_tracker.update(tid, cx, cy, best_cls)
                                    
                                    if self.object_tracker.ready_for_recognition(tid) and self.recognizer:
                                        stroke = self.object_tracker.get_stroke(tid)
                                        if len(stroke) >= 5:
                                            from dollarpy import Point
                                            pts = [Point(p[0], p[1], 1) for p in stroke]
                                            res = self.recognizer.recognize(pts)
                                            if res and res[0] and res[1] > 0.4:
                                                name = GESTURE_TO_RADIAL.get(res[0].name, res[0].name) if self._use_radial_mapping else res[0].name
                                                msg["gesture"] = {"source": "object", "name": name, "confidence": round(res[1], 3), "track_id": tid}
                                                print(f"[Server] Object Gesture Matched: {res[0].name} (score={res[1]:.2f})", flush=True)
                                        self.object_tracker.mark_recognized(tid)
                        else:
                            # SORT unavailable — fall back to raw detections (no IDs)
                            for i, rd in enumerate(raw_dets):
                                x1, y1, x2, y2 = int(rd[0]), int(rd[1]), int(rd[2]), int(rd[3])
                                conf = rd[4]
                                msg["yolo"].append({"track_id": -1, "class": det_meta[i],
                                                    "x": (x1+x2)/2/w, "y": (y1+y2)/2/h,
                                                    "w": (x2-x1)/w,   "h": (y2-y1)/h,
                                                    "conf": round(conf, 3)})
                                overlay.append((x1, y1, x2, y2, det_meta[i], conf, -1))

                        self._yolo_overlay_boxes = overlay
                    except Exception as e:
                        if self._debug:
                            print(f"[Server] YOLO inference error: {e}", flush=True)

                msg["proximity"] = getattr(self, "current_proximity", "ok")

                for x1, y1, x2, y2, cname, yconf, tid in self._yolo_overlay_boxes:
                    cv2.rectangle(display_frame, (x1, y1), (x2, y2), (0, 165, 255), 2)
                    id_str = f"#{tid} " if tid >= 0 else ""
                    lab = f"{id_str}{cname} {yconf:.2f}"
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

                # Gaze tracking — MediaPipe FaceMesh + OneEuroFilter, every GAZE_EVERY_N_FRAMES frames
                if self.gaze_enabled and frame_count % GAZE_EVERY_N_FRAMES == 0:
                    if self._gaze_evaluator:
                        self._gaze_evaluator.tick()
                    try:
                        # Use dedicated SplitCam frame when available, else reuse gesture frame
                        if self._gaze_cap is not None and self._gaze_cap.isOpened():
                            ret_g, gaze_frame = self._gaze_cap.read()
                            if not ret_g or gaze_frame is None:
                                gaze_frame = frame
                        else:
                            gaze_frame = frame

                        gaze_result = self._gaze_tracker.estimate_gaze(gaze_frame)
                        if gaze_result.face_detected:
                            _gaze_face_hits += 1
                            if time.time() - _gaze_status_t >= 5.0:
                                print(f"[Gaze] Tracking OK — {_gaze_face_hits} samples so far", flush=True)
                                _gaze_status_t = time.time()
                            sx, sy = self._gaze_filter.process(gaze_result.x, gaze_result.y, time.time())
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
                            # Record for evaluation report
                            if self._gaze_evaluator:
                                self._gaze_evaluator.record(gaze_result)
                        else:
                            if self._gaze_evaluator:
                                self._gaze_evaluator.miss()
                    except Exception as e:
                        if not getattr(self, "_gaze_error_shown", False):
                            print(f"[Server] Gaze error (first occurrence): {e}", flush=True)
                            self._gaze_error_shown = True

                # Laser Tracking
                if getattr(self, "laser_tracker", None) is not None:
                    laser_blob = self.find_laser_blob(frame, self.laser_hsv_lower, self.laser_hsv_upper)
                    if laser_blob:
                        cx, cy = laser_blob
                        self.laser_tracker.update(cx, cy)
                        # Draw preview
                        cv2.circle(display_frame, (int(cx*w), int(cy*h)), 5, (0, 0, 255), -1)
                        
                        if self.laser_tracker.is_dwell():
                            msg["gesture"] = {"source": "laser", "name": "tap", "confidence": 1.0, "x": cx, "y": cy}
                            self.laser_tracker.clear()
                        elif self.laser_tracker.ready_for_recognition() and self.recognizer:
                            stroke = self.laser_tracker.get_stroke()
                            if len(stroke) >= 5:
                                from dollarpy import Point
                                pts = [Point(p[0], p[1], 1) for p in stroke]
                                res = self.recognizer.recognize(pts)
                                if res and res[0] and res[1] > 0.4:
                                    name = GESTURE_TO_RADIAL.get(res[0].name, res[0].name) if self._use_radial_mapping else res[0].name
                                    msg["gesture"] = {"source": "laser", "name": name, "confidence": round(res[1], 3)}
                                    print(f"[Server] Laser Gesture Matched: {res[0].name} (score={res[1]:.2f})", flush=True)
                            self.laser_tracker.clear()

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
            if self._gaze_evaluator is not None:
                self._gaze_evaluator.save_report()
            cv2.destroyAllWindows()
            server_socket.close()
            self.hand_landmarker.close()
            if self.holistic_landmarker:
                self.holistic_landmarker.close()
            print("[Server] Stopped", flush=True)


def main():
    import argparse
    parser = argparse.ArgumentParser(
        description="Gesture Server — MediaPipe Hands + Gaze (FaceMesh) + YOLO + Emotion."
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument("--camera", type=int, default=0,
                        help="Camera index for hand tracking (default: 0).")
    parser.add_argument("--gaze-camera", type=int, default=None,
                        help="Separate camera index for gaze tracking (e.g. SplitCam). "
                             "Omit to share the --camera frame.")
    parser.add_argument("--templates", default=None, help="Path to gesture_templates.json")
    parser.add_argument("--body", action="store_true",
                        help="Use holistic_landmarker (pose+hands) instead of hand only.")
    parser.add_argument("--debug", action="store_true", help="Print recognition attempts.")
    parser.add_argument(
        "--width", type=int, default=_DEFAULT_CAPTURE_WIDTH,
        help="Capture width (0 = driver default). Default %(default)s.",
    )
    parser.add_argument(
        "--height", type=int, default=_DEFAULT_CAPTURE_HEIGHT,
        help="Capture height (0 = driver default). Default %(default)s.",
    )
    parser.add_argument("--face-id", default=None, help="Face ID of the current player (used for adaptive hints).")
    args = parser.parse_args()
    server = GestureServer(
        host=args.host,
        port=args.port,
        camera_id=args.camera,
        templates_path=args.templates,
        body=args.body,
        debug=args.debug,
        capture_width=args.width,
        capture_height=args.height,
        gaze_camera=args.gaze_camera,
    )
    try:
        server.run()
    except KeyboardInterrupt:
        server.running = False
    finally:
        if args.face_id:
            try:
                import gaze_adapter, emotion_adapter
                gaze_adapter.generate_hints(args.face_id)
                emotion_adapter.generate_hints(args.face_id)
            except Exception as e:
                print(f"[Server] Adaptive hints generation failed: {e}", flush=True)


if __name__ == "__main__":
    main()
