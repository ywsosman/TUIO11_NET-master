# Gaze Evaluation Report

**Session:** `session_20260519_000337`  
**Generated:** 2026-05-19T00:40:47

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 2230.13 s |
| Total camera frames | 4049 |
| Gaze samples recorded | 3236 |
| Face detection rate | 79.9 % |
| Mean confidence | 0.812 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.563 | 0.4907 |
| Std deviation | 0.1008 | 0.0999 |
| RMS dispersion | 0.1419 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.6° |
| Mean Pitch | -0.57° |

## Fixation Analysis

**Fixations detected:** 173

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6459 | 0.5166 | 5 |
| 2 | 0.6567 | 0.5232 | 5 |
| 3 | 0.5254 | 0.4646 | 5 |
| 4 | 0.5678 | 0.4365 | 6 |
| 5 | 0.7263 | 0.4702 | 8 |
| 6 | 0.7008 | 0.5247 | 11 |
| 7 | 0.5 | 0.5 | 6 |
| 8 | 0.5 | 0.5 | 6 |
| 9 | 0.6362 | 0.5434 | 7 |
| 10 | 0.6685 | 0.5008 | 5 |
| 11 | 0.6539 | 0.4497 | 7 |
| 12 | 0.5 | 0.5 | 5 |
| 13 | 0.5913 | 0.3851 | 7 |
| 14 | 0.5455 | 0.4922 | 9 |
| 15 | 0.7273 | 0.4692 | 8 |
| 16 | 0.6947 | 0.4799 | 6 |
| 17 | 0.5334 | 0.3867 | 5 |
| 18 | 0.54 | 0.4414 | 5 |
| 19 | 0.6007 | 0.4582 | 18 |
| 20 | 0.6046 | 0.4357 | 8 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.2% │ Top-Center:   3.6% │ Top-Right:   0.6% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.4% │ Center:  79.8% │ Mid-Right:  11.7% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.1% │ Bot-Center:   1.9% │ Bot-Right:   1.8% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |