# Gaze Evaluation Report

**Session:** `session_20260518_000502`  
**Generated:** 2026-05-18T00:05:38

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 35.52 s |
| Total camera frames | 42 |
| Gaze samples recorded | 26 |
| Face detection rate | 61.9 % |
| Mean confidence | 0.796 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.5557 | 0.4636 |
| Std deviation | 0.0345 | 0.0373 |
| RMS dispersion | 0.0508 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 2.08° |
| Mean Pitch | -0.4° |

## Fixation Analysis

**Fixations detected:** 1

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.5696 | 0.4414 | 15 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   0.0% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center: 100.0% │ Mid-Right:   0.0% │
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