"""
yolo_tuio_bridge.py — run the fruit YOLO model on the webcam and emit
TUIO 1.1 `/tuio/2Dobj` OSC messages on UDP 3333.

Each detected fruit appears to any TUIO client as a tangible object whose
class_id matches the fruit (apple=0, banana=1, strawberry=2, watermelon=3,
mango=4, orange=5, kiwi=6). The bbox center becomes (x, y) in [0..1].

Run:
    pip install ultralytics opencv-python python-osc
    python python/yolo_tuio_bridge.py
"""

import os
import sys
import time
import math

import cv2
from ultralytics import YOLO
from pythonosc.udp_client import SimpleUDPClient
from pythonosc import osc_bundle_builder, osc_message_builder

_SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
_PROJECT_ROOT = os.path.dirname(_SCRIPT_DIR)

# Prefer the fine-tuned model if it exists
FRUIT_MODEL  = os.path.join(_PROJECT_ROOT, "fruit_best.pt")
FALLBACK     = "yolo11n.pt"

TUIO_HOST    = "127.0.0.1"
TUIO_PORT    = 3333
CAM_INDEX    = 0
CONF_THRESH  = 0.45
IOU_THRESH   = 0.45
MAX_FPS      = 20

# Match the order in fruit_dataset/fruit_data.yaml
FRUIT_CLASS_IDS = {
    "apple": 0, "banana": 1, "strawberry": 2, "watermelon": 3,
    "mango": 4, "orange": 5, "kiwi": 6,
}

# Fruits whose silhouette is elongated enough that we can read orientation
# from the bounding-box aspect ratio. For round fruits (apple/orange/kiwi)
# rotation is undefined from an axis-aligned bbox, so we always report 0.
ELONGATED_FRUIT_CIDS = {1, 2, 3, 4}   # banana, strawberry, watermelon, mango
# Bbox is considered "rotated 90°" when its height exceeds its width by this
# ratio. 1.3 is forgiving enough for hand-held framing yet still clearly
# separates a horizontal banana (h/w ≈ 0.4) from a vertical one (h/w ≈ 2.5).
ROTATED_ASPECT_RATIO = 1.3


def estimate_angle(cid, x1, y1, x2, y2):
    """Approximate the TUIO 'angle' field from a YOLO bbox.

    YOLO boxes are axis-aligned so true rotation is unknowable, but for
    elongated fruits we can tell horizontal from vertical by aspect ratio.
    Returns 0 for "upright" or math.pi/2 for "rotated 90°"; TuioDemo's
    isRotated90 check is `abs(cos(angle)) < 0.707`, so these two values
    map cleanly to false/true respectively.
    """
    if cid not in ELONGATED_FRUIT_CIDS:
        return 0.0
    w = max(1e-6, x2 - x1)
    h = max(1e-6, y2 - y1)
    return math.pi / 2.0 if (h / w) >= ROTATED_ASPECT_RATIO else 0.0


def build_bundle(frame_id, tracked):
    """Build one TUIO 1.1 bundle: source -> alive -> set... -> fseq."""
    bundle = osc_bundle_builder.OscBundleBuilder(osc_bundle_builder.IMMEDIATELY)

    def msg(*args):
        m = osc_message_builder.OscMessageBuilder(address="/tuio/2Dobj")
        for a in args:
            m.add_arg(a)
        return m.build()

    bundle.add_content(msg("source", "yolo_fruit@localhost"))
    bundle.add_content(msg("alive", *[t["sid"] for t in tracked]))

    for t in tracked:
        bundle.add_content(msg(
            "set", t["sid"], t["cid"],
            t["x"], t["y"], t["angle"],
            t["vx"], t["vy"], t["va"],
            t["ma"], t["ra"],
        ))

    bundle.add_content(msg("fseq", frame_id))
    return bundle.build()


class FruitTracker:
    """Greedy IoU tracker to keep stable TUIO session IDs across frames."""

    def __init__(self, iou_min=0.3, max_missed=8):
        self.next_sid = 1
        self.tracks = {}        # sid -> dict
        self.iou_min = iou_min
        self.max_missed = max_missed

    @staticmethod
    def _iou(a, b):
        ax1, ay1, ax2, ay2 = a
        bx1, by1, bx2, by2 = b
        ix1, iy1 = max(ax1, bx1), max(ay1, by1)
        ix2, iy2 = min(ax2, bx2), min(ay2, by2)
        iw, ih = max(0.0, ix2 - ix1), max(0.0, iy2 - iy1)
        inter = iw * ih
        if inter <= 0:
            return 0.0
        area_a = (ax2 - ax1) * (ay2 - ay1)
        area_b = (bx2 - bx1) * (by2 - by1)
        return inter / (area_a + area_b - inter)

    def update(self, detections, w, h, dt):
        # detections: list of (cid, x1, y1, x2, y2)
        for tr in self.tracks.values():
            tr["matched"] = False

        for cid, x1, y1, x2, y2 in detections:
            best_sid, best_iou = None, self.iou_min
            for sid, tr in self.tracks.items():
                if tr["cid"] != cid or tr["matched"]:
                    continue
                i = self._iou(tr["bbox"], (x1, y1, x2, y2))
                if i > best_iou:
                    best_iou, best_sid = i, sid

            cx = (x1 + x2) / 2.0 / w
            cy = (y1 + y2) / 2.0 / h

            angle = estimate_angle(cid, x1, y1, x2, y2)

            if best_sid is None:
                sid = self.next_sid
                self.next_sid += 1
                self.tracks[sid] = {
                    "sid": sid, "cid": cid, "bbox": (x1, y1, x2, y2),
                    "x": cx, "y": cy, "angle": angle,
                    "vx": 0.0, "vy": 0.0, "va": 0.0,
                    "ma": 0.0, "ra": 0.0,
                    "matched": True, "missed": 0,
                }
            else:
                tr = self.tracks[best_sid]
                vx = (cx - tr["x"]) / dt if dt > 0 else 0.0
                vy = (cy - tr["y"]) / dt if dt > 0 else 0.0
                va = (angle - tr["angle"]) / dt if dt > 0 else 0.0
                tr["bbox"] = (x1, y1, x2, y2)
                tr["x"], tr["y"] = cx, cy
                tr["angle"] = angle
                tr["vx"], tr["vy"] = vx, vy
                tr["va"] = va
                tr["ma"] = math.hypot(vx, vy)
                tr["matched"] = True
                tr["missed"] = 0

        # Age out unmatched
        dead = []
        for sid, tr in self.tracks.items():
            if not tr["matched"]:
                tr["missed"] += 1
                if tr["missed"] > self.max_missed:
                    dead.append(sid)
        for sid in dead:
            del self.tracks[sid]

        return list(self.tracks.values())


def main():
    model_path = FRUIT_MODEL if os.path.exists(FRUIT_MODEL) else FALLBACK
    if model_path == FALLBACK:
        print(f"[WARN] {FRUIT_MODEL} not found — using base {FALLBACK}.")
        print("       Train first: python python/train_fruit_model.py")

    print(f"[YOLO] Loading {model_path}")
    model = YOLO(model_path)

    # Build class_id -> TUIO class_id remap from the model's own names
    name_to_tuio_cid = {}
    for idx, name in model.names.items():
        n = name.lower().strip()
        if n in FRUIT_CLASS_IDS:
            name_to_tuio_cid[idx] = FRUIT_CLASS_IDS[n]

    if not name_to_tuio_cid:
        print("[WARN] Model has no fruit classes that match the TUIO map.")
        print(f"       Model classes: {list(model.names.values())}")

    client = SimpleUDPClient(TUIO_HOST, TUIO_PORT)
    print(f"[TUIO] Sending /tuio/2Dobj to {TUIO_HOST}:{TUIO_PORT}")

    # MSMF (Media Foundation) supports Win11's multi-app camera sharing,
    # which lets gesture_server.py use the same physical camera at the same
    # time. DSHOW would lock the device.
    cap = cv2.VideoCapture(CAM_INDEX, cv2.CAP_MSMF)
    if not cap.isOpened():
        print(f"[ERR] Cannot open camera {CAM_INDEX}")
        sys.exit(1)

    tracker = FruitTracker()
    frame_id = 0
    last_t = time.time()
    frame_interval = 1.0 / MAX_FPS

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                break

            now = time.time()
            dt = now - last_t
            if dt < frame_interval:
                continue
            last_t = now
            h, w = frame.shape[:2]

            results = model(frame, conf=CONF_THRESH, iou=IOU_THRESH, verbose=False)
            dets = []
            if results:
                for box in results[0].boxes:
                    cid_model = int(box.cls[0])
                    if cid_model not in name_to_tuio_cid:
                        continue
                    x1, y1, x2, y2 = box.xyxy[0].cpu().numpy().tolist()
                    dets.append((name_to_tuio_cid[cid_model], x1, y1, x2, y2))

            tracked = tracker.update(dets, w, h, dt)
            frame_id += 1

            # Send TUIO bundle (always — empty alive == "all objects gone")
            client.send(build_bundle(frame_id, tracked))

            # Optional debug overlay
            for t in tracked:
                x1, y1, x2, y2 = map(int, t["bbox"])
                label = f'sid={t["sid"]} cid={t["cid"]}'
                cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)
                cv2.putText(frame, label, (x1, max(y1 - 6, 12)),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
            cv2.imshow("YOLO -> TUIO bridge (q to quit)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

    finally:
        cap.release()
        cv2.destroyAllWindows()
        # Final "alive []" so clients clear stale objects
        client.send(build_bundle(frame_id + 1, []))


if __name__ == "__main__":
    main()
