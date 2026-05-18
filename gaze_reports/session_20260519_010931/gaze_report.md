# Gaze Evaluation Report

**Session:** `session_20260519_010931`  
**Generated:** 2026-05-19T01:13:00

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 209.56 s |
| Total camera frames | 1003 |
| Gaze samples recorded | 1000 |
| Face detection rate | 99.7 % |
| Mean confidence | 0.836 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.429 | 0.3578 |
| Std deviation | 0.1054 | 0.0833 |
| RMS dispersion | 0.1343 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | -0.24° |
| Mean Pitch | -1.42° |

## Fixation Analysis

**Fixations detected:** 66

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.3595 | 0.5199 | 9 |
| 2 | 0.4103 | 0.3903 | 6 |
| 3 | 0.4472 | 0.3522 | 17 |
| 4 | 0.4334 | 0.2345 | 22 |
| 5 | 0.5823 | 0.2736 | 5 |
| 6 | 0.4967 | 0.2754 | 10 |
| 7 | 0.4877 | 0.2366 | 7 |
| 8 | 0.4329 | 0.2498 | 11 |
| 9 | 0.3559 | 0.31 | 35 |
| 10 | 0.4684 | 0.3119 | 29 |
| 11 | 0.6285 | 0.3081 | 10 |
| 12 | 0.2686 | 0.3547 | 43 |
| 13 | 0.3378 | 0.3771 | 13 |
| 14 | 0.2054 | 0.4533 | 6 |
| 15 | 0.1186 | 0.406 | 9 |
| 16 | 0.4779 | 0.2501 | 10 |
| 17 | 0.3293 | 0.2502 | 7 |
| 18 | 0.5716 | 0.2977 | 5 |
| 19 | 0.4674 | 0.0847 | 8 |
| 20 | 0.4637 | 0.4116 | 7 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   4.5% │ Top-Center:  36.4% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:  12.3% │ Center:  46.0% │ Mid-Right:   0.1% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.7% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |