# Gaze Evaluation Report

**Session:** `session_20260518_004940`  
**Generated:** 2026-05-18T00:51:21

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 101.38 s |
| Total camera frames | 128 |
| Gaze samples recorded | 118 |
| Face detection rate | 92.2 % |
| Mean confidence | 0.811 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.6092 | 0.4114 |
| Std deviation | 0.0766 | 0.0705 |
| RMS dispersion | 0.1041 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 1.12° |
| Mean Pitch | -0.27° |

## Fixation Analysis

**Fixations detected:** 7

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5998 | 0.3737 | 10 |
| 2 | 0.6036 | 0.3708 | 10 |
| 3 | 0.5429 | 0.4304 | 6 |
| 4 | 0.639 | 0.465 | 7 |
| 5 | 0.6145 | 0.4046 | 16 |
| 6 | 0.6624 | 0.3962 | 6 |
| 7 | 0.5 | 0.5 | 5 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.8% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center:  83.9% │ Mid-Right:  13.6% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.0% │ Bot-Right:   1.7% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |