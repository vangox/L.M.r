# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**L.M.R.** is a Unity 6000.3.9f1 MediaPipe-based hand gesture recognition and pose-driven AR framework. It runs pose, hand, and face landmark detection entirely within Unity (no external Python) using the [homuler/mediapipe](https://github.com/homuler/MediaPipeUnityPlugin) plugin.

## Building

This is a Unity project — there are no CLI build commands. Open the `.slnx` solution or the project folder directly in **Unity 6000.3.9f1**. Use Unity's **File > Build Settings** for builds.

**Required model files** (must be manually placed in `Assets/StreamingAssets/` before running):
- `pose_landmarker_full.task`
- `hand_landmarker.task`
- `face_landmarker_v2_with_blendshapes.bytes`
- `selfie_multiclass_256x256.tflite` (optional, for person segmentation)

## Architecture

### Data Flow

```
WebCamTexture / VideoPlayer / Texture2D
    → MediaPipeDataProvider (worker thread callbacks)
    → volatile fields: pose_data, left_hand_data, right_hand_data, face_data
    → FaceAnchor / TorsoAnchor / HandGestureDetector (main thread)
```

`MediaPipeDataProvider` is the central hub. All consumer scripts call `FindFirstObjectByType<MediaPipeDataProvider>()` if not assigned in Inspector. MediaPipe runs in `LIVE_STREAM` mode on worker threads; results are stored in `volatile float[][]` fields safe for main-thread reads.

### Key Scripts (`Assets/GoGs/Scripts/`)

| Script | Role |
|--------|------|
| `MediaPipeDataProvider.cs` | Initializes landmarkers, manages input source, exposes landmark arrays |
| `HandGestureDetector.cs` | Swipe detection (wrist velocity buffers), hand pointer mapping, optional OS mouse emulation |
| `HandGesturePointer.cs` | Singleton — exposes `ScreenPosition` / `IsHandVisible` for UI elements |
| `HandGestureDwellButton.cs` | Dwell-to-click: fires after hand hovers over RectTransform for N seconds |
| `FaceAnchor.cs` | Anchors accessories to face using cheek-width depth estimation |
| `TorsoAnchor.cs` | Anchors garments to torso using shoulder-width depth estimation |
| `WebcamFillCover.cs` | "Cover" scaling for webcam preview; static coordinate remap helpers |
| `PersonSegmenter.cs` | Optional background removal via `selfie_multiclass_256x256.tflite` |

### Coordinate Systems

- **MediaPipe normalized**: X 0→1 left-to-right, Y 0→1 top-to-bottom, Z ≈ depth
- **Unity viewport**: Y is inverted (0=bottom)
- **`WebcamFillCover`** provides `RemapX()`, `RemapViewportY()`, `RemapGuiY()` to convert between them

All components expose `mirrorX` to handle horizontally-flipped camera feeds — apply consistently across FaceAnchor, TorsoAnchor, and HandGestureDetector.

### Conventions

- **Thread safety**: MediaPipe callbacks write to `volatile` fields; main thread captures to local variable before use. No locks.
- **Smoothing**: Frame-rate-independent `Mathf.Lerp(..., Time.deltaTime * smoothingSpeed)` everywhere.
- **Naming**: public fields PascalCase, private fields `_camelCase`.
- **Singleton**: `HandGesturePointer` uses static `Instance` with Awake duplicate guard.
- **Dual RenderTextures**: `_displayRT` for stable rendering, `_detectionRT` for downscaled GPU→CPU detection.

### Scenes

- `Assets/Scenes/Main.unity` — Primary scene with all detection and gesture interaction
- `Assets/GoGs/Games/WhacAMole/` — Demo game using gesture input
