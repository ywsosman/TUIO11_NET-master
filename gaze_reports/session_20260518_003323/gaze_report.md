# Gaze Evaluation Report

**Session:** `session_20260518_003323`  
**Generated:** 2026-05-18T00:36:08

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 165.29 s |
| Total camera frames | 254 |
| Gaze samples recorded | 221 |
| Face detection rate | 87.0 % |
| Mean confidence | 0.836 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5999 | 0.5052 |
| Std deviation | 0.0585 | 0.048 |
| RMS dispersion | 0.0757 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 1.95° |
| Mean Pitch | -0.21° |

## Fixation Analysis

**Fixations detected:** 14

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5872 | 0.4809 | 14 |
| 2 | 0.5896 | 0.4756 | 14 |
| 3 | 0.5649 | 0.4608 | 13 |
| 4 | 0.5302 | 0.534 | 5 |
| 5 | 0.5104 | 0.6422 | 6 |
| 6 | 0.5145 | 0.642 | 11 |
| 7 | 0.6332 | 0.5069 | 12 |
| 8 | 0.6205 | 0.4976 | 18 |
| 9 | 0.6547 | 0.4797 | 22 |
| 10 | 0.634 | 0.4864 | 18 |
| 11 | 0.5062 | 0.4876 | 15 |
| 12 | 0.6236 | 0.5256 | 9 |
| 13 | 0.6412 | 0.5448 | 5 |
| 14 | 0.6774 | 0.4598 | 7 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.0% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center:  87.3% │ Mid-Right:  12.7% │
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