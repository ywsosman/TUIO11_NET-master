# Gaze Evaluation Report

**Session:** `session_20260519_005634`  
**Generated:** 2026-05-19T00:58:03

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 88.68 s |
| Total camera frames | 327 |
| Gaze samples recorded | 327 |
| Face detection rate | 100.0 % |
| Mean confidence | 0.833 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.4839 | 0.3216 |
| Std deviation | 0.0845 | 0.0653 |
| RMS dispersion | 0.1068 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.25° |
| Mean Pitch | -1.69° |

## Fixation Analysis

**Fixations detected:** 18

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.4542 | 0.2656 | 10 |
| 2 | 0.4602 | 0.3006 | 8 |
| 3 | 0.3883 | 0.3014 | 7 |
| 4 | 0.3345 | 0.3068 | 37 |
| 5 | 0.3795 | 0.3055 | 6 |
| 6 | 0.52 | 0.2441 | 6 |
| 7 | 0.3721 | 0.3271 | 6 |
| 8 | 0.5304 | 0.3322 | 12 |
| 9 | 0.5374 | 0.2977 | 5 |
| 10 | 0.6084 | 0.2978 | 10 |
| 11 | 0.5469 | 0.312 | 30 |
| 12 | 0.5524 | 0.318 | 10 |
| 13 | 0.5191 | 0.293 | 28 |
| 14 | 0.5848 | 0.3001 | 6 |
| 15 | 0.5289 | 0.3079 | 27 |
| 16 | 0.5817 | 0.4106 | 6 |
| 17 | 0.5415 | 0.4412 | 14 |
| 18 | 0.5 | 0.5 | 9 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   4.6% │ Top-Center:  69.7% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   4.0% │ Center:  21.7% │ Mid-Right:   0.0% │
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