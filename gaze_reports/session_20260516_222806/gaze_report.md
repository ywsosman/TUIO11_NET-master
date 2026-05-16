# Gaze Evaluation Report

**Session:** `session_20260516_222806`  
**Generated:** 2026-05-16T22:28:25

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 19.43 s |
| Total camera frames | 24 |
| Gaze samples recorded | 23 |
| Face detection rate | 95.8 % |
| Mean confidence | 0.82 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5453 | 0.5553 |
| Std deviation | 0.0404 | 0.0284 |
| RMS dispersion | 0.0494 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 1.41° |
| Mean Pitch | -0.42° |

## Fixation Analysis

**Fixations detected:** 2

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5683 | 0.5825 | 6 |
| 2 | 0.5512 | 0.5483 | 7 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.0% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center: 100.0% │ Mid-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.0% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |