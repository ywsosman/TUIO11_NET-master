# Gaze Evaluation Report

**Session:** `session_20260518_233847`  
**Generated:** 2026-05-18T23:46:19

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 452.16 s |
| Total camera frames | 1118 |
| Gaze samples recorded | 834 |
| Face detection rate | 74.6 % |
| Mean confidence | 0.819 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5219 | 0.4883 |
| Std deviation | 0.0873 | 0.0615 |
| RMS dispersion | 0.1068 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.3° |
| Mean Pitch | -0.46° |

## Fixation Analysis

**Fixations detected:** 47

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6165 | 0.5206 | 32 |
| 2 | 0.6004 | 0.5195 | 10 |
| 3 | 0.5951 | 0.5201 | 13 |
| 4 | 0.5923 | 0.5067 | 5 |
| 5 | 0.6218 | 0.4978 | 8 |
| 6 | 0.6077 | 0.5128 | 7 |
| 7 | 0.5862 | 0.5254 | 9 |
| 8 | 0.5455 | 0.5097 | 13 |
| 9 | 0.5531 | 0.5071 | 15 |
| 10 | 0.5 | 0.5 | 15 |
| 11 | 0.5 | 0.5 | 5 |
| 12 | 0.5 | 0.5 | 11 |
| 13 | 0.4901 | 0.3797 | 8 |
| 14 | 0.5016 | 0.4973 | 8 |
| 15 | 0.5271 | 0.4829 | 11 |
| 16 | 0.5802 | 0.4718 | 5 |
| 17 | 0.6095 | 0.5102 | 11 |
| 18 | 0.5724 | 0.5078 | 10 |
| 19 | 0.5936 | 0.5101 | 13 |
| 20 | 0.4066 | 0.499 | 5 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.1% │ Top-Center:   1.1% │ Top-Right:   0.1% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.7% │ Center:  93.5% │ Mid-Right:   3.8% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.1% │ Bot-Center:   0.1% │ Bot-Right:   0.4% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |