# Gaze Evaluation Report

**Session:** `session_20260518_223115`  
**Generated:** 2026-05-18T22:36:22

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 307.31 s |
| Total camera frames | 820 |
| Gaze samples recorded | 796 |
| Face detection rate | 97.1 % |
| Mean confidence | 0.836 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5882 | 0.4889 |
| Std deviation | 0.1095 | 0.0571 |
| RMS dispersion | 0.1235 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 1.97° |
| Mean Pitch | -0.5° |

## Fixation Analysis

**Fixations detected:** 39

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6537 | 0.4521 | 6 |
| 2 | 0.758 | 0.4592 | 7 |
| 3 | 0.7638 | 0.4445 | 21 |
| 4 | 0.5 | 0.5 | 5 |
| 5 | 0.7232 | 0.4437 | 5 |
| 6 | 0.686 | 0.4568 | 7 |
| 7 | 0.5717 | 0.4176 | 12 |
| 8 | 0.6436 | 0.4098 | 5 |
| 9 | 0.6834 | 0.5191 | 18 |
| 10 | 0.6885 | 0.5107 | 8 |
| 11 | 0.5451 | 0.4438 | 15 |
| 12 | 0.4743 | 0.4665 | 25 |
| 13 | 0.5215 | 0.4295 | 5 |
| 14 | 0.6622 | 0.5555 | 37 |
| 15 | 0.6632 | 0.5598 | 15 |
| 16 | 0.6615 | 0.5315 | 8 |
| 17 | 0.669 | 0.5483 | 12 |
| 18 | 0.6657 | 0.5726 | 8 |
| 19 | 0.6452 | 0.5596 | 11 |
| 20 | 0.5692 | 0.5079 | 7 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.1% │ Top-Right:   0.3% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.8% │ Center:  72.2% │ Mid-Right:  26.3% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.3% │ Bot-Center:   0.1% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |