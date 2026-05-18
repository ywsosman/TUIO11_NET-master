# Gaze Evaluation Report

**Session:** `session_20260518_222658`  
**Generated:** 2026-05-18T22:29:04

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 126.62 s |
| Total camera frames | 220 |
| Gaze samples recorded | 216 |
| Face detection rate | 98.2 % |
| Mean confidence | 0.803 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5218 | 0.5055 |
| Std deviation | 0.0522 | 0.0716 |
| RMS dispersion | 0.0886 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.96° |
| Mean Pitch | -0.67° |

## Fixation Analysis

**Fixations detected:** 10

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5284 | 0.5735 | 12 |
| 2 | 0.5382 | 0.5513 | 39 |
| 3 | 0.5448 | 0.582 | 6 |
| 4 | 0.5 | 0.5 | 5 |
| 5 | 0.4977 | 0.5699 | 8 |
| 6 | 0.5675 | 0.452 | 10 |
| 7 | 0.565 | 0.4502 | 11 |
| 8 | 0.4443 | 0.3978 | 7 |
| 9 | 0.4577 | 0.4027 | 32 |
| 10 | 0.4584 | 0.3942 | 5 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.0% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.5% │ Center:  99.1% │ Mid-Right:   0.5% │
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