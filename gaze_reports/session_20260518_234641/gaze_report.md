# Gaze Evaluation Report

**Session:** `session_20260518_234641`  
**Generated:** 2026-05-18T23:53:58

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 437.18 s |
| Total camera frames | 939 |
| Gaze samples recorded | 499 |
| Face detection rate | 53.1 % |
| Mean confidence | 0.795 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5551 | 0.4673 |
| Std deviation | 0.0881 | 0.0609 |
| RMS dispersion | 0.1071 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.55° |
| Mean Pitch | -0.43° |

## Fixation Analysis

**Fixations detected:** 24

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6543 | 0.4837 | 7 |
| 2 | 0.5 | 0.5 | 10 |
| 3 | 0.4917 | 0.4244 | 5 |
| 4 | 0.5 | 0.5 | 9 |
| 5 | 0.5 | 0.5 | 8 |
| 6 | 0.483 | 0.4137 | 5 |
| 7 | 0.4967 | 0.501 | 7 |
| 8 | 0.5 | 0.5 | 5 |
| 9 | 0.5012 | 0.4464 | 20 |
| 10 | 0.5 | 0.5 | 8 |
| 11 | 0.5258 | 0.4718 | 27 |
| 12 | 0.5679 | 0.4876 | 32 |
| 13 | 0.6652 | 0.4848 | 8 |
| 14 | 0.7382 | 0.4728 | 12 |
| 15 | 0.5925 | 0.4605 | 22 |
| 16 | 0.6387 | 0.4707 | 23 |
| 17 | 0.502 | 0.4371 | 11 |
| 18 | 0.4709 | 0.4543 | 24 |
| 19 | 0.5071 | 0.4543 | 14 |
| 20 | 0.4175 | 0.452 | 11 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.2% │ Top-Center:   0.0% │ Top-Right:   0.6% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.4% │ Center:  90.4% │ Mid-Right:   8.0% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.4% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |