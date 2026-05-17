# Gaze Evaluation Report

**Session:** `session_20260518_001551`  
**Generated:** 2026-05-18T00:16:29

---

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 37.98 s |
| Total camera frames | 58 |
| Gaze samples recorded | 51 |
| Face detection rate | 87.9 % |
| Mean confidence | 0.843 |

## Gaze Position

| Metric | X | Y |
|--------|---|---|
| Mean (normalised 0-1) | 0.6765 | 0.4328 |
| Std deviation | 0.0558 | 0.0681 |
| RMS dispersion | 0.088 | — |

## Head Pose

| Metric | Value |
|--------|-------|
| Mean Yaw | 2.36° |
| Mean Pitch | -0.79° |

## Fixation Analysis

**Fixations detected:** 4

| # | Center X | Center Y | Duration (frames) |
|---|----------|----------|------------------|
| 1 | 0.6696 | 0.4472 | 13 |
| 2 | 0.6872 | 0.4555 | 8 |
| 3 | 0.7164 | 0.4468 | 12 |
| 4 | 0.6091 | 0.3873 | 6 |

## Gaze Zone Distribution (3×3 Grid)

Percentage of gaze time in each screen region:

```
┌──────────────┬──────────────┬──────────────┐
│ Top-Left:   0.0% │ Top-Center:   2.0% │ Top-Right:   0.0% │
├──────────────┼──────────────┼──────────────┤
│ Mid-Left:   0.0% │ Center:  39.2% │ Mid-Right:  58.8% │
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