# Gaze Evaluation Report

**Session:** `session_20260518_231333`  
**Generated:** 2026-05-18T23:24:53

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 680.4 s |
| Total camera frames | 2310 |
| Gaze samples recorded | 2149 |
| Face detection rate | 93.0 % |
| Mean confidence | 0.808 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5038 | 0.4689 |
| Std deviation | 0.0869 | 0.0583 |
| RMS dispersion | 0.1046 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 0.53° |
| Mean Pitch | -0.32° |

## Fixation Analysis

**Fixations detected:** 98

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6377 | 0.4266 | 5 |
| 2 | 0.5084 | 0.4887 | 8 |
| 3 | 0.5 | 0.5 | 33 |
| 4 | 0.5 | 0.5 | 18 |
| 5 | 0.5095 | 0.448 | 9 |
| 6 | 0.495 | 0.4585 | 17 |
| 7 | 0.4939 | 0.3814 | 5 |
| 8 | 0.4949 | 0.4028 | 18 |
| 9 | 0.5561 | 0.4096 | 5 |
| 10 | 0.4807 | 0.4184 | 6 |
| 11 | 0.4578 | 0.3604 | 5 |
| 12 | 0.5077 | 0.3602 | 5 |
| 13 | 0.4889 | 0.3643 | 6 |
| 14 | 0.5 | 0.5 | 9 |
| 15 | 0.4796 | 0.4242 | 23 |
| 16 | 0.5216 | 0.4817 | 7 |
| 17 | 0.5113 | 0.411 | 5 |
| 18 | 0.5057 | 0.4187 | 5 |
| 19 | 0.4997 | 0.3871 | 25 |
| 20 | 0.5477 | 0.5809 | 7 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   1.1% │ Top-Right:   0.3% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   1.5% │ Center:  92.6% │ Mid-Right:   4.2% │
├──────────────┼──────────────┼──────────────┤
│ Bot-Left:   0.0% │ Bot-Center:   0.1% │ Bot-Right:   0.0% │
└──────────────┴──────────────┴──────────────┘
```

## Artefacts

| File | Description |
|------|-------------|
| `gaze_log.json` | Raw gaze samples + all stats (machine-readable) |
| `gaze_heatmap.png` | Gaussian heat-map of gaze density |
| `gaze_scanpath.png` | Ordered scanpath with fixation circles |
| `gaze_report.md` | This report |