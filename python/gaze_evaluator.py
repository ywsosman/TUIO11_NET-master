"""
GazeEvaluator
=============
Collects gaze samples from GazeResult objects and, on session end, writes:
  - gaze_log.json          Raw per-frame gaze data + summary stats
  - gaze_heatmap.png       Gaussian-blurred heat-map (colour-mapped)
  - gaze_scanpath.png      Ordered scanpath on a blank canvas
  - gaze_report.md         Human-readable markdown summary

Usage:
    evaluator = GazeEvaluator(output_dir="reports/session_001")
    evaluator.record(gaze_result)   # call each frame gaze succeeds
    evaluator.save_report()         # call on shutdown
"""

import os
import json
import time
import math
from datetime import datetime

import cv2
import numpy as np


# Screen region labels for the 3×3 gaze-zone analysis
_ZONE_LABELS = [
    ["Top-Left",    "Top-Center",    "Top-Right"],
    ["Mid-Left",    "Center",        "Mid-Right"],
    ["Bot-Left",    "Bot-Center",    "Bot-Right"],
]


class GazeEvaluator:
    """
    Lightweight gaze data recorder and report generator.

    Parameters
    ----------
    output_dir : str
        Directory where all report files will be saved.
        Created automatically if it doesn't exist.
    canvas_w, canvas_h : int
        Resolution of the heatmap / scanpath canvas (pixels).
        Doesn't need to match the camera; just affects report resolution.
    """

    def __init__(
        self,
        output_dir: str = "gaze_reports",
        canvas_w: int = 1280,
        canvas_h: int = 720,
    ):
        self.output_dir = output_dir
        self.canvas_w = canvas_w
        self.canvas_h = canvas_h

        self._samples: list[dict] = []
        self._start_time: float = time.time()
        self._frame_total: int = 0   # total frames presented (call tick() each frame)
        self._face_miss: int = 0     # frames where face was NOT detected

        os.makedirs(output_dir, exist_ok=True)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def tick(self) -> None:
        """Call once per camera frame regardless of face detection."""
        self._frame_total += 1

    def record(self, gaze_result) -> None:
        """
        Record a valid gaze result.  Call only when gaze_result.face_detected.

        Accepts any object with attributes:
            x, y, yaw, pitch, head_yaw, head_pitch, confidence, face_detected
        Also accepts dicts (for convenience).
        """
        if isinstance(gaze_result, dict):
            sample = {
                "t":         round(time.time() - self._start_time, 3),
                "x":         round(float(gaze_result.get("x", 0)), 4),
                "y":         round(float(gaze_result.get("y", 0)), 4),
                "yaw":       round(float(gaze_result.get("yaw", 0)), 4),
                "pitch":     round(float(gaze_result.get("pitch", 0)), 4),
                "head_yaw":  round(float(gaze_result.get("head_yaw", 0)), 4),
                "head_pitch":round(float(gaze_result.get("head_pitch", 0)), 4),
                "conf":      round(float(gaze_result.get("confidence", 1)), 3),
            }
        else:
            sample = {
                "t":          round(time.time() - self._start_time, 3),
                "x":          round(float(gaze_result.x), 4),
                "y":          round(float(gaze_result.y), 4),
                "yaw":        round(float(gaze_result.yaw), 4),
                "pitch":      round(float(gaze_result.pitch), 4),
                "head_yaw":   round(float(getattr(gaze_result, "head_yaw", 0)), 4),
                "head_pitch": round(float(getattr(gaze_result, "head_pitch", 0)), 4),
                "conf":       round(float(gaze_result.confidence), 3),
            }
        self._samples.append(sample)

    def miss(self) -> None:
        """Call when a frame was processed but no face was detected."""
        self._face_miss += 1

    def save_report(self) -> str:
        """
        Generate and save all report artefacts.
        Returns the output directory path.
        """
        if not self._samples:
            print("[GazeEval] No gaze samples recorded — skipping report.", flush=True)
            return self.output_dir

        print(f"[GazeEval] Saving report to: {self.output_dir}", flush=True)

        stats = self._compute_stats()

        # Each artefact is wrapped so a failure in one doesn't skip the rest.
        # Symptoms before this guard: only gaze_log.json appeared because the
        # heatmap routine threw / hung and aborted save_report() midway.
        for name, fn in [
            ("gaze_log.json",     lambda: self._save_json(stats)),
            ("gaze_heatmap.png",  lambda: self._save_heatmap()),
            ("gaze_scanpath.png", lambda: self._save_scanpath()),
            ("gaze_report.md",    lambda: self._save_markdown(stats)),
        ]:
            try:
                fn()
            except Exception as ex:
                print(f"[GazeEval] {name} FAILED: {ex}", flush=True)

        print(f"[GazeEval] Report saved  ({len(self._samples)} samples, "
              f"{stats['duration_s']:.1f}s session)", flush=True)
        return self.output_dir

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _compute_stats(self) -> dict:
        xs = [s["x"] for s in self._samples]
        ys = [s["y"] for s in self._samples]
        yaws   = [s["yaw"]   for s in self._samples]
        pitches= [s["pitch"] for s in self._samples]
        confs  = [s["conf"]  for s in self._samples]

        # Dispersion (RMS distance from centroid)
        mx, my = float(np.mean(xs)), float(np.mean(ys))
        disp = float(np.sqrt(np.mean([(x - mx)**2 + (y - my)**2
                                      for x, y in zip(xs, ys)])))

        # Gaze zone histogram (3×3 grid)
        zone_counts = [[0, 0, 0], [0, 0, 0], [0, 0, 0]]
        for x, y in zip(xs, ys):
            col = min(int(x * 3), 2)
            row = min(int(y * 3), 2)
            zone_counts[row][col] += 1
        n = len(self._samples)
        zone_pct = [[round(zone_counts[r][c] / n * 100, 1)
                     for c in range(3)] for r in range(3)]

        # Fixation detection (simplified: cluster nearby points)
        fixations = self._detect_fixations(xs, ys)

        duration = time.time() - self._start_time
        face_det_rate = (
            (self._frame_total - self._face_miss) / self._frame_total * 100
            if self._frame_total > 0 else 0.0
        )

        return {
            "session_id":        os.path.basename(self.output_dir),
            "generated_at":      datetime.now().isoformat(timespec="seconds"),
            "duration_s":        round(duration, 2),
            "total_frames":      self._frame_total,
            "gaze_samples":      n,
            "face_detection_pct":round(face_det_rate, 1),
            "mean_x":            round(mx, 4),
            "mean_y":            round(my, 4),
            "std_x":             round(float(np.std(xs)), 4),
            "std_y":             round(float(np.std(ys)), 4),
            "dispersion_rms":    round(disp, 4),
            "mean_yaw_deg":      round(float(np.degrees(np.mean(yaws))), 2),
            "mean_pitch_deg":    round(float(np.degrees(np.mean(pitches))), 2),
            "mean_confidence":   round(float(np.mean(confs)), 3),
            "fixation_count":    len(fixations),
            "fixations":         fixations,
            "zone_percent_3x3":  zone_pct,
            "zone_labels_3x3":   _ZONE_LABELS,
            "samples":           self._samples,
        }

    def _detect_fixations(self, xs, ys,
                          radius: float = 0.05, min_dur_frames: int = 5) -> list:
        """
        Simple velocity-threshold fixation detector.
        Returns list of {cx, cy, duration_frames, start_idx}.
        """
        fixations = []
        in_fix = False
        fix_start = 0
        fix_xs: list[float] = []
        fix_ys: list[float] = []

        for i in range(len(xs)):
            if i == 0:
                in_fix = True
                fix_start = 0
                fix_xs, fix_ys = [xs[0]], [ys[0]]
                continue
            dx = xs[i] - xs[i - 1]
            dy = ys[i] - ys[i - 1]
            dist = math.sqrt(dx * dx + dy * dy)
            if dist < radius:
                fix_xs.append(xs[i])
                fix_ys.append(ys[i])
            else:
                if in_fix and len(fix_xs) >= min_dur_frames:
                    fixations.append({
                        "cx": round(float(np.mean(fix_xs)), 4),
                        "cy": round(float(np.mean(fix_ys)), 4),
                        "duration_frames": len(fix_xs),
                        "start_idx": fix_start,
                    })
                in_fix = True
                fix_start = i
                fix_xs, fix_ys = [xs[i]], [ys[i]]

        # close last fixation
        if in_fix and len(fix_xs) >= min_dur_frames:
            fixations.append({
                "cx": round(float(np.mean(fix_xs)), 4),
                "cy": round(float(np.mean(fix_ys)), 4),
                "duration_frames": len(fix_xs),
                "start_idx": fix_start,
            })
        return fixations

    # ------------------------------------------------------------------

    def _save_json(self, stats: dict) -> None:
        path = os.path.join(self.output_dir, "gaze_log.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(stats, f, indent=2)
        print(f"[GazeEval]   gaze_log.json      ({os.path.getsize(path)//1024} KB)", flush=True)

    def _save_heatmap(self) -> None:
        """
        Build a 2-D gaussian heat-map of gaze density using a single
        cv2.GaussianBlur on a sparse hit-canvas. This runs in tens of
        milliseconds even for tens of thousands of samples — the previous
        per-sample Python loop took minutes and was the reason long
        sessions only produced gaze_log.json before the user closed
        the server.
        """
        W, H = self.canvas_w, self.canvas_h
        sigma = max(W, H) / 20.0   # ~5% of canvas

        # 1. Mark every gaze sample as a single hit pixel
        hits = np.zeros((H, W), dtype=np.float32)
        for s in self._samples:
            px = int(np.clip(s["x"], 0, 1) * (W - 1))
            py = int(np.clip(s["y"], 0, 1) * (H - 1))
            hits[py, px] += 1.0

        # 2. Blur once → continuous density field
        accum = cv2.GaussianBlur(hits, ksize=(0, 0), sigmaX=sigma, sigmaY=sigma)

        # 3. Normalise to 0-255 with safe division
        max_val = float(accum.max())
        if max_val > 0:
            accum = (accum / max_val * 255).astype(np.uint8)
        else:
            accum = accum.astype(np.uint8)

        heatmap = cv2.applyColorMap(accum, cv2.COLORMAP_JET)

        # 4. Draw 3×3 zone grid overlay (matches scanpath + report)
        for i in range(1, 3):
            cv2.line(heatmap, (W * i // 3, 0), (W * i // 3, H), (255, 255, 255), 1)
            cv2.line(heatmap, (0, H * i // 3), (W, H * i // 3), (255, 255, 255), 1)

        path = os.path.join(self.output_dir, "gaze_heatmap.png")
        cv2.imwrite(path, heatmap)
        print(f"[GazeEval]   gaze_heatmap.png   ({os.path.getsize(path)//1024} KB)", flush=True)

    def _save_scanpath(self) -> None:
        W, H = self.canvas_w, self.canvas_h
        canvas = np.full((H, W, 3), 30, dtype=np.uint8)   # dark background

        pts = [
            (int(np.clip(s["x"], 0, 1) * (W - 1)),
             int(np.clip(s["y"], 0, 1) * (H - 1)))
            for s in self._samples
        ]

        # Draw lines between consecutive gaze points
        for i in range(1, len(pts)):
            alpha = i / len(pts)
            color = (int(255 * (1 - alpha)), int(200 * alpha), int(255 * alpha))
            cv2.line(canvas, pts[i - 1], pts[i], color, 1)

        # Draw fixation circles (larger)
        fixations = self._detect_fixations(
            [s["x"] for s in self._samples],
            [s["y"] for s in self._samples],
        )
        for fix in fixations:
            cx = int(fix["cx"] * (W - 1))
            cy = int(fix["cy"] * (H - 1))
            r  = max(6, min(30, fix["duration_frames"] // 2))
            cv2.circle(canvas, (cx, cy), r, (0, 220, 255), 2)

        # Draw 3×3 zone grid
        for i in range(1, 3):
            cv2.line(canvas, (W * i // 3, 0), (W * i // 3, H), (80, 80, 80), 1)
            cv2.line(canvas, (0, H * i // 3), (W, H * i // 3), (80, 80, 80), 1)

        cv2.putText(canvas, "Gaze Scanpath", (10, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 2)

        path = os.path.join(self.output_dir, "gaze_scanpath.png")
        cv2.imwrite(path, canvas)
        print(f"[GazeEval]   gaze_scanpath.png  ({os.path.getsize(path)//1024} KB)", flush=True)

    def _save_markdown(self, stats: dict) -> None:
        lines = [
            f"# Gaze Evaluation Report",
            f"",
            f"**Session:** `{stats['session_id']}`  ",
            f"**Generated:** {stats['generated_at']}",
            f"",
            f"---",
            f"",
            f"## Session Overview",
            f"",
            f"| Metric | Value |",
            f"|--------|-------|",
            f"| Duration | {stats['duration_s']} s |",
            f"| Total camera frames | {stats['total_frames']} |",
            f"| Gaze samples recorded | {stats['gaze_samples']} |",
            f"| Face detection rate | {stats['face_detection_pct']} % |",
            f"| Mean confidence | {stats['mean_confidence']} |",
            f"",
            f"## Gaze Position",
            f"",
            f"| Metric | X | Y |",
            f"|--------|---|---|",
            f"| Mean (normalised 0-1) | {stats['mean_x']} | {stats['mean_y']} |",
            f"| Std deviation | {stats['std_x']} | {stats['std_y']} |",
            f"| RMS dispersion | {stats['dispersion_rms']} | — |",
            f"",
            f"## Head Pose",
            f"",
            f"| Metric | Value |",
            f"|--------|-------|",
            f"| Mean Yaw | {stats['mean_yaw_deg']}° |",
            f"| Mean Pitch | {stats['mean_pitch_deg']}° |",
            f"",
            f"## Fixation Analysis",
            f"",
            f"**Fixations detected:** {stats['fixation_count']}",
            f"",
        ]

        if stats["fixations"]:
            lines += [
                f"| # | Center X | Center Y | Duration (frames) |",
                f"|---|----------|----------|------------------|",
            ]
            for i, fix in enumerate(stats["fixations"][:20], 1):
                lines.append(
                    f"| {i} | {fix['cx']} | {fix['cy']} | {fix['duration_frames']} |"
                )

        lines += [
            f"",
            f"## Gaze Zone Distribution (3×3 Grid)",
            f"",
            f"Percentage of gaze time in each screen region:",
            f"",
            f"```",
            f"┌──────────────┬──────────────┬──────────────┐",
        ]
        for r in range(3):
            row_parts = []
            for c in range(3):
                label = _ZONE_LABELS[r][c]
                pct   = stats["zone_percent_3x3"][r][c]
                row_parts.append(f"{label}: {pct:5.1f}%".center(14))
            lines.append(f"│ {' │ '.join(row_parts)} │")
            if r < 2:
                lines.append(f"├──────────────┼──────────────┼──────────────┤")
        lines += [
            f"└──────────────┴──────────────┴──────────────┘",
            f"```",
            f"",
            f"## Artefacts",
            f"",
            f"| File | Description |",
            f"|------|-------------|",
            f"| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |",
            f"| `gaze_heatmap.png` | Gaussian heat-map of gaze density |",
            f"| `gaze_scanpath.png` | Ordered scanpath with fixation circles |",
            f"| `gaze_report.md` | This report |",
        ]

        path = os.path.join(self.output_dir, "gaze_report.md")
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines))
        print(f"[GazeEval]   gaze_report.md     ({os.path.getsize(path)//1024} KB)", flush=True)
