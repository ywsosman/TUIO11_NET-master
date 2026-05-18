# Gaze Evaluation Report

**Session:** `session_20260519_010359`  
**Generated:** 2026-05-19T01:07:22

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 203.37 s |
| Total camera frames | 826 |
| Gaze samples recorded | 809 |
| Face detection rate | 97.9 % |
| Mean confidence | 0.84 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.4583 | 0.3327 |
| Std deviation | 0.0868 | 0.0795 |
| RMS dispersion | 0.1177 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.05° |
| Mean Pitch | -1.61° |

## Fixation Analysis

**Fixations detected:** 57

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5284 | 0.3226 | 5 |
| 2 | 0.4115 | 0.4456 | 7 |
| 3 | 0.5359 | 0.4509 | 6 |
| 4 | 0.5015 | 0.3866 | 5 |
| 5 | 0.3912 | 0.2616 | 16 |
| 6 | 0.425 | 0.2785 | 48 |
| 7 | 0.4081 | 0.2437 | 5 |
| 8 | 0.5737 | 0.2856 | 6 |
| 9 | 0.4365 | 0.2765 | 9 |
| 10 | 0.4895 | 0.2941 | 13 |
| 11 | 0.4392 | 0.3061 | 5 |
| 12 | 0.3613 | 0.308 | 8 |
| 13 | 0.5609 | 0.3209 | 12 |
| 14 | 0.5448 | 0.3253 | 16 |
| 15 | 0.2776 | 0.3664 | 8 |
| 16 | 0.4148 | 0.2563 | 10 |
| 17 | 0.438 | 0.4186 | 10 |
| 18 | 0.4499 | 0.4289 | 11 |
| 19 | 0.6026 | 0.4042 | 5 |
| 20 | 0.4226 | 0.2531 | 8 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   1.7% │ Top-Center:  59.6% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   7.8% │ Center:  30.0% │ Mid-Right:   0.4% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.5% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |