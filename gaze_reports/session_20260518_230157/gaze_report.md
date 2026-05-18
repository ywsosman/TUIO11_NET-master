# Gaze Evaluation Report

**Session:** `session_20260518_230157`  
**Generated:** 2026-05-18T23:05:21

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 204.1 s |
| Total camera frames | 417 |
| Gaze samples recorded | 386 |
| Face detection rate | 92.6 % |
| Mean confidence | 0.832 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5434 | 0.453 |
| Std deviation | 0.1028 | 0.0728 |
| RMS dispersion | 0.126 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.98° |
| Mean Pitch | -0.58° |

## Fixation Analysis

**Fixations detected:** 14

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5534 | 0.4145 | 5 |
| 2 | 0.4722 | 0.45 | 7 |
| 3 | 0.5137 | 0.4249 | 29 |
| 4 | 0.4569 | 0.453 | 69 |
| 5 | 0.6439 | 0.5157 | 11 |
| 6 | 0.4818 | 0.3925 | 8 |
| 7 | 0.4602 | 0.4207 | 10 |
| 8 | 0.4696 | 0.416 | 9 |
| 9 | 0.4834 | 0.4011 | 38 |
| 10 | 0.6183 | 0.5304 | 9 |
| 11 | 0.5245 | 0.5318 | 12 |
| 12 | 0.5535 | 0.5292 | 14 |
| 13 | 0.5873 | 0.5209 | 15 |
| 14 | 0.6535 | 0.4904 | 6 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.0% │ Top-Right:   1.6% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center:  89.4% │ Mid-Right:   8.5% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.3% │ Bot-Right:   0.3% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |