#!/usr/bin/env python3
"""
camera_utils.py  –  shared camera enumeration & selection helper
Detects real webcams AND virtual cameras (SplitCam, OBS, ManyCam, …).

Exported helpers
----------------
enumerate_cameras(max_index)  -> list[dict]
find_splitcam(cameras)        -> dict | None
find_default_camera(cameras)  -> dict | None
resolve_camera(requested, prefer_splitcam, fallback_any) -> (index, name)
print_camera_list()
"""

import subprocess
import sys
import cv2
from typing import List, Optional, Tuple

# Substrings that identify virtual / software cameras (case-insensitive)
VIRTUAL_CAM_KEYWORDS = [
    "splitcam", "split cam",
    "obs", "obs virtual",
    "manycam",
    "droidcam",
    "iriun",
    "epoccam",
    "xsplit",
    "virtual cam",
    "ndi",
]


# ---------------------------------------------------------------------------
# Name detection
# ---------------------------------------------------------------------------

def _camera_names_win32() -> List[str]:
    """
    Query DirectShow / PnP friendly names via PowerShell.
    Returns an ordered list; index i → camera at OS capture index i (best-effort).
    Falls back to an empty list if PowerShell is unavailable.
    """
    if sys.platform != "win32":
        return []
    try:
        result = subprocess.run(
            [
                "powershell", "-NoProfile", "-NonInteractive", "-Command",
                (
                    "Get-PnpDevice -Class Camera -Status OK "
                    "| Select-Object -ExpandProperty FriendlyName"
                ),
            ],
            capture_output=True, text=True, timeout=6,
        )
        return [n.strip() for n in result.stdout.strip().splitlines() if n.strip()]
    except Exception:
        return []


# ---------------------------------------------------------------------------
# Core enumeration
# ---------------------------------------------------------------------------

def enumerate_cameras(max_index: int = 10) -> List[dict]:
    """
    Probe indices 0 … max_index-1 and return info for every camera that opens.

    Each dict:
        index      (int)
        name       (str)   friendly name or "Camera N"
        width      (int)
        height     (int)
        fps        (float)
        is_virtual (bool)
    """
    friendly_names = _camera_names_win32()
    backend = cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY

    cameras: List[dict] = []
    for idx in range(max_index):
        cap = cv2.VideoCapture(idx, backend)
        if not cap.isOpened():
            cap.release()
            continue

        width  = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        fps    = cap.get(cv2.CAP_PROP_FPS)
        cap.release()

        name = friendly_names[idx] if idx < len(friendly_names) else f"Camera {idx}"
        is_virtual = any(kw in name.lower() for kw in VIRTUAL_CAM_KEYWORDS)

        cameras.append(
            {
                "index":      idx,
                "name":       name,
                "width":      width,
                "height":     height,
                "fps":        fps,
                "is_virtual": is_virtual,
            }
        )

    return cameras


# ---------------------------------------------------------------------------
# Selection helpers
# ---------------------------------------------------------------------------

def find_splitcam(cameras: Optional[List[dict]] = None) -> Optional[dict]:
    """Return the SplitCam entry, or the first virtual camera, or None."""
    cams = cameras if cameras is not None else enumerate_cameras()
    # Prefer explicit SplitCam
    for cam in cams:
        if "splitcam" in cam["name"].lower() or "split cam" in cam["name"].lower():
            return cam
    # Fall back to any virtual camera
    for cam in cams:
        if cam["is_virtual"]:
            return cam
    return None


def find_default_camera(cameras: Optional[List[dict]] = None) -> Optional[dict]:
    """Return the first non-virtual (physical) camera, or None."""
    cams = cameras if cameras is not None else enumerate_cameras()
    for cam in cams:
        if not cam["is_virtual"]:
            return cam
    return None


def resolve_camera(
    requested: int = 0,
    prefer_splitcam: bool = False,
    fallback_any: bool = True,
) -> Tuple[int, str]:
    """
    Choose the best camera index based on user preferences.

    Priority (prefer_splitcam=True):
        1. SplitCam / any virtual camera
        2. The explicitly requested index (if it's available)
        3. Any available camera  (if fallback_any)

    Priority (prefer_splitcam=False):
        1. The explicitly requested index (if it's available)
        2. First real (non-virtual) camera
        3. Any available camera  (if fallback_any)

    Returns (index: int, name: str).
    """
    cameras = enumerate_cameras()

    if not cameras:
        print("[CAM] WARNING: No cameras found.")
        return requested, f"Camera {requested} (not found)"

    cam_by_index = {c["index"]: c for c in cameras}

    if prefer_splitcam:
        sc = find_splitcam(cameras)
        if sc:
            print(f"[CAM] Using virtual/SplitCam camera: [{sc['index']}] {sc['name']}")
            return sc["index"], sc["name"]

    if requested in cam_by_index:
        cam = cam_by_index[requested]
        print(f"[CAM] Using camera [{cam['index']}] {cam['name']}")
        return cam["index"], cam["name"]

    if fallback_any:
        real = find_default_camera(cameras)
        target = real if real else cameras[0]
        print(
            f"[CAM] Camera {requested} unavailable – "
            f"falling back to [{target['index']}] {target['name']}"
        )
        return target["index"], target["name"]

    print(f"[CAM] Camera {requested} not found and fallback is disabled.")
    return requested, f"Camera {requested} (unavailable)"


# ---------------------------------------------------------------------------
# Pretty-print helper
# ---------------------------------------------------------------------------

def print_camera_list():
    """Print all detected cameras to stdout."""
    cameras = enumerate_cameras()
    if not cameras:
        print("[CAM] No cameras detected.")
        return

    print("=" * 62)
    print(f"{'Detected Cameras':^62}")
    print("=" * 62)
    for cam in cameras:
        tag = " [VIRTUAL]" if cam["is_virtual"] else ""
        print(
            f"  [{cam['index']}] {cam['name']}{tag}"
            f"  ({cam['width']}x{cam['height']} @ {cam['fps']:.0f} fps)"
        )
    print("=" * 62)


# ---------------------------------------------------------------------------
# CLI quick-test
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    print_camera_list()
    splitcam = find_splitcam()
    default  = find_default_camera()
    if splitcam:
        print(f"\nSplitCam/virtual → [{splitcam['index']}] {splitcam['name']}")
    if default:
        print(f"Default physical  → [{default['index']}] {default['name']}")
