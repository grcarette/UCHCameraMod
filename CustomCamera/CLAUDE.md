# UCH Camera Mod

## Architecture
- CameraUI.cs - Unified fullscreen UI with Camera and Keyframe tabs
- CameraModController.cs - Applies settings to game camera via reflection
- CameraProgramRunner.cs - Plays back keyframe sequences with pause/resume
- CameraProgram.cs / CameraKeyframe.cs - Data classes for keyframe programs
- Patches.cs - Harmony patches to suppress ZoomCamera during mod use

## Key details
- Unity IMGUI-based UI (OnGUI)
- Preview camera renders to RenderTexture
- Map overview uses hardcoded bounds (-85,60,-46,45)
- Camera settings applied via reflection on ZoomCamera fields
- BepInEx plugin for Ultimate Chicken Horse