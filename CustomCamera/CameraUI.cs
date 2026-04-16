using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BepInEx;

namespace UCHCameraMod
{
    public class CameraUI : MonoBehaviour
    {
        public static CameraUI Instance { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsDraggingBox => _drag != DragMode.None;

        // ── Tab state ────────────────────────────────────────────────────
        private enum Tab { Camera, Keyframe }
        private Tab _activeTab = Tab.Camera;

        // ── Map snapshot ─────────────────────────────────────────────────
        private Texture2D _snapshotTex;
        private float _worldMinX = -85f, _worldMaxX = 60f, _worldMinY = -46f, _worldMaxY = 45f;
        private bool _hasSnapshot;

        // ── Preview camera ───────────────────────────────────────────────
        private RenderTexture _previewRT;
        private Camera _previewCam;
        private GameObject _previewCamGO;
        private const int PREVIEW_W = 960;
        private const int PREVIEW_H = 540;

        // ── Drag box (world coords) ─────────────────────────────────────
        private float _boxWorldLeft, _boxWorldRight, _boxWorldTop, _boxWorldBottom;
        private enum DragMode { None, Move, ResizeTL, ResizeTR, ResizeBL, ResizeBR, ResizeL, ResizeR, ResizeT, ResizeB }
        private DragMode _drag = DragMode.None;
        private Vector2 _dragStartMouse;
        private float _dragStartBoxL, _dragStartBoxR, _dragStartBoxT, _dragStartBoxB;
        private const float CORNER_GRAB = 12f;
        private const float EDGE_GRAB = 8f;
        private const float MIN_BOX_WORLD = 1f;
        private bool _orthoDrag;
        private bool _orthoDragLocked;
        private bool _orthoDragIsHorizontal;
        private bool _dragJustEnded;

        // ── Camera options buffers ───────────────────────────────────────
        private string _bufLeft, _bufRight, _bufTop, _bufBottom, _bufFOV;
        private string _presetNameInput = "";
        private Vector2 _presetScrollPos;

        // ── Keyframe state ───────────────────────────────────────────────
        private CameraProgram _program = new CameraProgram { Name = "New Program" };
        private int _selectedKF = -1;
        private Vector2 _kfScrollPos;
        private string _programNameInput = "New Program";
        private string _saveNameInput = "";
        private Vector2 _fileScrollPos;
        private Vector2 _recScrollPos;
        private Dictionary<int, string> _durationBufs = new Dictionary<int, string>();

        // ── Notification ─────────────────────────────────────────────────
        private string _notification = "";
        private float _notificationTime = -1f;
        private const float NOTIFICATION_DURATION = 2f;

        // ── Timeline ─────────────────────────────────────────────────────
        private bool _draggingPlayhead;
        private float _playheadTime;

        // ── Styles ───────────────────────────────────────────────────────
        private GUIStyle _headerStyle, _sectionStyle, _tabActive, _tabInactive;
        private GUIStyle _activeToggle, _selectedBtn;
        private bool _stylesInit;

        // ── Paths ────────────────────────────────────────────────────────
        private static readonly string PresetFolder = Path.Combine(Paths.ConfigPath, "UCHCameraPresets");
        private static readonly string ProgramFolder = Path.Combine(Paths.ConfigPath, "UCHCameraPrograms");

        // ── References ───────────────────────────────────────────────────
        private CameraModController Ctrl => CameraModController.Instance;
        private CameraProgramRunner Runner => CameraProgramRunner.Instance;

        // =================================================================
        // LIFECYCLE
        // =================================================================
        private void Awake()
        {
            Instance = this;
            Directory.CreateDirectory(PresetFolder);
            Directory.CreateDirectory(ProgramFolder);
        }

        private void Update()
        {
            if (Ctrl == null) return;

            if (Input.GetKeyDown(Plugin.CfgKeyToggleMod.Value))
            {
                IsOpen = !IsOpen;
                if (IsOpen)
                {
                    Ctrl.ModActive = true;
                    UnityEngine.Cursor.visible = true;
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    Ctrl.OnMenuOpened();
                    TakeSnapshot();
                    CreatePreviewCamera();
                    SyncBuffersFromCtrl();
                }
                else
                {
                    DestroyPreviewCamera();
                    Ctrl.ModActive = false;
                    StartCoroutine(RestoreCursor());
                }
            }

            // Playback hotkeys work regardless of UI state
            if (Input.GetKeyDown(Plugin.CfgKeyPlayProgram.Value))
            {
                var pb = GamePlaybackController.Instance;
                bool anyPlaying = Runner.IsPlaying || (pb != null && pb.IsPlaying);

                if (anyPlaying)
                {
                    Runner.Pause();
                    if (pb != null) pb.Pause();
                }
                else
                {
                    if (pb != null && pb.Duration > 0f)
                        pb.Play();
                    if (_program.Keyframes.Count >= 2 && Ctrl != null && Ctrl.Cam != null)
                    {
                        Ctrl.ModActive = true;
                        SetField(Ctrl.Zoom, "manualControls", true);
                        Runner.Play(_program, Ctrl.Cam, Ctrl.Zoom);
                    }
                }
            }
            if (Input.GetKeyDown(Plugin.CfgKeyStopProgram.Value))
            {
                Runner.Stop();
                var pb = GamePlaybackController.Instance;
                if (pb != null) pb.Stop();
                _playheadTime = 0f;
            }
            if (Input.GetKeyDown(Plugin.CfgKeyResetProgram.Value))
            {
                Runner.Rewind();
                var pb = GamePlaybackController.Instance;
                if (pb != null) pb.Seek(0f);
                _playheadTime = 0f;
            }

            if (!IsOpen) return;

            // Sync playhead from whichever system is driving playback
            var pbCtrl = GamePlaybackController.Instance;
            if (pbCtrl != null && pbCtrl.IsPlaying)
                _playheadTime = pbCtrl.CurrentTime;
            else if (Runner.IsPlaying)
                _playheadTime = GetPlaybackTime();

            // Keep cursor visible while UI is open
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }

        private void LateUpdate()
        {
            if (!IsOpen || _previewCam == null || Ctrl == null || Ctrl.Cam == null) return;

            bool recActive = GamePlaybackController.Instance != null
                          && (GamePlaybackController.Instance.IsPlaying || GamePlaybackController.Instance.IsPaused);
            bool camProgActive = Runner != null
                              && (Runner.IsPlaying || Runner.IsPaused);

            if (_activeTab == Tab.Camera)
            {
                // Camera tab: always mirror game camera
                _previewCam.transform.position = Ctrl.Cam.transform.position;
                _previewCam.transform.rotation = Ctrl.Cam.transform.rotation;
                _previewCam.fieldOfView = Ctrl.Cam.fieldOfView;
                _previewCam.aspect = (float)PREVIEW_W / PREVIEW_H;
                _previewCam.cullingMask = Ctrl.Cam.cullingMask;
                _previewCam.clearFlags = Ctrl.Cam.clearFlags;
                _previewCam.backgroundColor = Ctrl.Cam.backgroundColor;
                _previewCam.Render();
            }
            else if (_activeTab == Tab.Keyframe && (recActive || camProgActive))
            {
                // Keyframe tab: only render when recording or camera program is active
                _previewCam.transform.position = Ctrl.Cam.transform.position;
                _previewCam.transform.rotation = Ctrl.Cam.transform.rotation;
                _previewCam.fieldOfView = Ctrl.Cam.fieldOfView;
                _previewCam.aspect = (float)PREVIEW_W / PREVIEW_H;
                _previewCam.cullingMask = Ctrl.Cam.cullingMask;
                _previewCam.clearFlags = Ctrl.Cam.clearFlags;
                _previewCam.backgroundColor = Ctrl.Cam.backgroundColor;
                _previewCam.Render();
            }

            // Sync yellow box on camera tab
            if (_activeTab == Tab.Camera && _drag == DragMode.None && !_dragJustEnded)
                SyncBoxFromCamera();
            _dragJustEnded = false;
        }

        private System.Collections.IEnumerator RestoreCursor()
        {
            yield return null; yield return null; yield return null;
            UnityEngine.Cursor.visible = false;
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            yield return null;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                mb.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
            yield return null;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                mb.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
        }

        // =================================================================
        // GUI ENTRY
        // =================================================================
        private void OnGUI()
        {
            if (!IsOpen) return;
            InitStyles();

            // Fullscreen opaque backdrop
            DrawFilledRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.18f, 0.18f, 0.22f, 1f));

            float margin = 24f;
            float tabH = 36f;
            float tabY = margin;
            float contentY = tabY + tabH;
            float contentH = Screen.height - contentY - margin;
            float contentW = Screen.width - margin * 2f;
            float contentX = margin;

            // ── Tab bar ──────────────────────────────────────────────────
            float tabW = contentW * 0.5f;
            if (GUI.Button(new Rect(contentX, tabY, tabW, tabH), "Camera",
                _activeTab == Tab.Camera ? _tabActive : _tabInactive))
                _activeTab = Tab.Camera;
            if (GUI.Button(new Rect(contentX + tabW, tabY, tabW, tabH), "Keyframe",
                _activeTab == Tab.Keyframe ? _tabActive : _tabInactive))
                _activeTab = Tab.Keyframe;

            // ── Content area ─────────────────────────────────────────────
            Rect content = new Rect(contentX, contentY, contentW, contentH);

            if (_activeTab == Tab.Camera)
                DrawCameraTab(content);
            else
                DrawKeyframeTab(content);

            // ── Notification ─────────────────────────────────────────────
            if (_notificationTime >= 0f)
            {
                float elapsed = Time.time - _notificationTime;
                if (elapsed < NOTIFICATION_DURATION)
                {
                    float alpha = elapsed < NOTIFICATION_DURATION - 0.5f ? 1f : (NOTIFICATION_DURATION - elapsed) / 0.5f;
                    float notifW = 300f;
                    float notifH = 30f;
                    float notifX = (Screen.width - notifW) * 0.5f;
                    float notifY = Screen.height - 80f;
                    DrawFilledRect(new Rect(notifX, notifY, notifW, notifH),
                        new Color(0.2f, 0.7f, 0.3f, alpha * 0.9f));
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.Label(new Rect(notifX, notifY + 5f, notifW, notifH),
                        _notification, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = (FontStyle)1 });
                    GUI.color = Color.white;
                }
                else
                {
                    _notificationTime = -1f;
                }
            }
        }

        // =================================================================
        // CAMERA TAB
        // =================================================================
        private void DrawCameraTab(Rect area)
        {
            float gap = 12f;
            float upperH = area.height * 0.52f;
            float lowerH = area.height - upperH - gap;
            float toolbarW = 70f;
            float panelsW = area.width - toolbarW - gap;
            float halfPanelW = (panelsW - gap) * 0.5f;

            // ── Toolbar ──────────────────────────────────────────────────
            Rect toolbarRect = new Rect(area.x, area.y, toolbarW, upperH);
            DrawPanel(toolbarRect, "TOOLS");
            DrawMapToolbar(Inset(toolbarRect, 4f, 24f, 4f, 4f));

            // ── Upper left: Map Overview ─────────────────────────────────
            float panelsX = area.x + toolbarW + gap;
            Rect mapPanel = new Rect(panelsX, area.y, halfPanelW, upperH);
            DrawPanel(mapPanel, "MAP OVERVIEW");
            Rect mapInner = Inset(mapPanel, 8f, 26f, 8f, 8f);

            if (_hasSnapshot && _snapshotTex != null)
            {
                Rect drawRect = FitTextureRect(_snapshotTex.width, _snapshotTex.height, mapInner);
                GUI.DrawTexture(drawRect, _snapshotTex, ScaleMode.StretchToFill);
                Rect boxPx = WorldBoxToPixelRect(drawRect);
                DrawDarkenedOverlay(drawRect, boxPx);
                DrawRectOutline(boxPx, new Color(1f, 0.85f, 0.3f), 2f);
                DrawCornerHandle(new Vector2(boxPx.xMin, boxPx.yMin));
                DrawCornerHandle(new Vector2(boxPx.xMax, boxPx.yMin));
                DrawCornerHandle(new Vector2(boxPx.xMin, boxPx.yMax));
                DrawCornerHandle(new Vector2(boxPx.xMax, boxPx.yMax));
                HandleDragInteraction(drawRect, boxPx);
            }

            // ── Upper right: Camera Preview ──────────────────────────────
            Rect prevPanel = new Rect(panelsX + halfPanelW + gap, area.y, halfPanelW, upperH);
            DrawPanel(prevPanel, "CAMERA PREVIEW");
            Rect prevInner = Inset(prevPanel, 8f, 26f, 8f, 8f);

            if (_previewRT != null)
            {
                Rect prevRect = FitTextureRect(PREVIEW_W, PREVIEW_H, prevInner);
                GUI.DrawTexture(prevRect, _previewRT, ScaleMode.StretchToFill);
            }

            // ── Lower section (unchanged) ────────────────────────────────
            float lowerY = area.y + upperH + gap;
            float optionsW = area.width * 0.38f;
            float presetSaveW = area.width * 0.30f;
            float presetListW = area.width - optionsW - presetSaveW - gap * 2f;

            Rect optPanel = new Rect(area.x, lowerY, optionsW, lowerH);
            DrawPanel(optPanel, "CAMERA OPTIONS");
            DrawCameraOptions(Inset(optPanel, 10f, 28f, 10f, 10f));

            Rect savePanel = new Rect(area.x + optionsW + gap, lowerY, presetSaveW, lowerH);
            DrawPanel(savePanel, "SAVE PRESET");
            DrawPresetSave(Inset(savePanel, 10f, 28f, 10f, 10f));

            Rect listPanel = new Rect(area.x + optionsW + presetSaveW + gap * 2f, lowerY, presetListW, lowerH);
            DrawPanel(listPanel, "PRESETS");
            DrawPresetList(Inset(listPanel, 10f, 28f, 10f, 10f));
        }

        private void DrawMapToolbar(Rect r)
        {
            float btnH = 26f;
            float btnW = r.width;
            float btnY = r.y;
            float spacing = 6f;

            // Orthogonal drag toggle
            GUI.color = _orthoDrag ? new Color(0.3f, 0.8f, 1f) : new Color(0.5f, 0.5f, 0.55f);
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Ortho"))
            {
                _orthoDrag = !_orthoDrag;
                ShowNotification(_orthoDrag ? "Orthogonal drag ON" : "Orthogonal drag OFF");
            }
            GUI.color = Color.white;

            // Refresh snapshot
            btnY += btnH + spacing;
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Refresh"))
            {
                TakeSnapshot();
                ShowNotification("Snapshot refreshed");
            }

            // Sync box from camera
            btnY += btnH + spacing;
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Sync"))
            {
                SyncBoxFromCamera();
                ShowNotification("Synced from camera");
            }

            // Apply box to camera
            btnY += btnH + spacing;
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Apply"))
            {
                ApplyBoxToCamera();
                ShowNotification("Applied to camera");
            }

            // Add keyframe from map
            btnY += btnH + spacing;
            GUI.color = new Color(0.4f, 1f, 0.5f);
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Add KF"))
            {
                CaptureKeyframeFromBox();
            }
            GUI.color = Color.white;

            // Reset camera to defaults
            btnY += btnH + spacing;
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Reset"))
            {
                Ctrl.ResetToDefaults_Public();
                SyncBuffersFromCtrl();
                SyncBoxFromCamera();
                ShowNotification("Camera reset to defaults");
            }
            GUI.color = Color.white;

            // Center on character
            btnY += btnH + spacing;
            if (GUI.Button(new Rect(r.x, btnY, btnW, btnH), "Center"))
            {
                CenterOnCharacter();
            }
        }

        private void DrawCameraOptions(Rect r)
        {
            if (Ctrl == null) return;
            GUILayout.BeginArea(r);

            // Toggles
            bool mz = GUILayout.Toggle(Ctrl.ManualZoom, Ctrl.ManualZoom ? "Manual Zoom ON" : "Manual Zoom OFF",
                Ctrl.ManualZoom ? _activeToggle : GUI.skin.toggle);
            if (mz != Ctrl.ManualZoom) { Ctrl.ManualZoom = mz; Ctrl.SaveToConfig(); }

            bool sf = GUILayout.Toggle(Ctrl.SmoothFollow, Ctrl.SmoothFollow ? "Smooth Follow ON" : "Smooth Follow OFF",
                Ctrl.SmoothFollow ? _activeToggle : GUI.skin.toggle);
            if (sf != Ctrl.SmoothFollow) { Ctrl.SmoothFollow = sf; Ctrl.SaveToConfig(); }

            bool mp = GUILayout.Toggle(Ctrl.ManualPosition, Ctrl.ManualPosition ? "Manual Position ON" : "Manual Position OFF",
                Ctrl.ManualPosition ? _activeToggle : GUI.skin.toggle);
            if (mp != Ctrl.ManualPosition) { Ctrl.ManualPosition = mp; Ctrl.SaveToConfig(); }

            GUILayout.Space(6);

            // FOV
            GUILayout.Label("FIELD OF VIEW", _sectionStyle);
            float newFOV = DrawSliderRow("FOV", Ctrl.FOV, 0.1f, 100f, ref _bufFOV, true);
            if (!Mathf.Approximately(newFOV, Ctrl.FOV)) { Ctrl.FOV = newFOV; Ctrl.SaveToConfig(); }

            GUILayout.Space(4);

            // Buffers
            GUILayout.Label("UNIT BUFFERS", _sectionStyle);
            float nL = DrawSliderRow("Left", Ctrl.LeftBuffer, 0f, 20f, ref _bufLeft, false);
            float nR = DrawSliderRow("Right", Ctrl.RightBuffer, 0f, 20f, ref _bufRight, false);
            float nT = DrawSliderRow("Top", Ctrl.TopBuffer, 0f, 20f, ref _bufTop, false);
            float nB = DrawSliderRow("Bottom", Ctrl.BottomBuffer, 0f, 20f, ref _bufBottom, false);

            if (!Mathf.Approximately(nL, Ctrl.LeftBuffer)) { Ctrl.LeftBuffer = nL; Ctrl.SaveToConfig(); }
            if (!Mathf.Approximately(nR, Ctrl.RightBuffer)) { Ctrl.RightBuffer = nR; Ctrl.SaveToConfig(); }
            if (!Mathf.Approximately(nT, Ctrl.TopBuffer)) { Ctrl.TopBuffer = nT; Ctrl.SaveToConfig(); }
            if (!Mathf.Approximately(nB, Ctrl.BottomBuffer)) { Ctrl.BottomBuffer = nB; Ctrl.SaveToConfig(); }

            GUILayout.Space(6);
            if (GUILayout.Button("Reset to Defaults")) { Ctrl.ResetToDefaults_Public(); SyncBuffersFromCtrl(); }

            GUILayout.EndArea();
        }

        private void DrawPresetSave(Rect r)
        {
            if (Ctrl == null) return;
            GUILayout.BeginArea(r);

            GUILayout.Label("Preset Name:", _sectionStyle);
            _presetNameInput = GUILayout.TextField(_presetNameInput);
            GUILayout.Space(4);
            if (GUILayout.Button("Save Preset") && !string.IsNullOrEmpty(_presetNameInput))
            {
                Ctrl.SavePreset(_presetNameInput);
                ShowNotification($"Preset '{_presetNameInput}' saved!");
                _presetNameInput = "";
            }

            GUILayout.EndArea();
        }

        private void DrawPresetList(Rect r)
        {
            if (Ctrl == null) return;
            GUILayout.BeginArea(r);

            List<string> presets = Ctrl.GetPresetNames();
            if (presets.Count == 0)
            {
                GUILayout.Label("No presets saved.", _sectionStyle);
            }
            else
            {
                _presetScrollPos = GUILayout.BeginScrollView(_presetScrollPos);
                foreach (string p in presets)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(p, GUILayout.ExpandWidth(true)))
                    {
                        Ctrl.LoadPreset(p);
                        SyncBuffersFromCtrl();
                        ShowNotification($"Preset '{p}' loaded!");
                    }
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                        Ctrl.DeletePreset(p);
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        // =================================================================
        // KEYFRAME TAB
        // =================================================================
        private void DrawKeyframeTab(Rect area)
        {
            float gap = 12f;
            float upperH = area.height * 0.45f;
            float timelineH = 80f;
            float transportH = 30f;
            float lowerH = area.height - upperH - timelineH - transportH - gap * 3f;
            float halfW = (area.width - gap) * 0.5f;

            // ── Upper left: Keyframe list ────────────────────────────────
            Rect kfPanel = new Rect(area.x, area.y, halfW, upperH);
            DrawPanel(kfPanel, $"KEYFRAMES ({_program.Keyframes.Count})");
            DrawKeyframeList(Inset(kfPanel, 8f, 26f, 8f, 8f));

            // ── Upper right: Preview ─────────────────────────────────────
            Rect prevPanel = new Rect(area.x + halfW + gap, area.y, halfW, upperH);
            DrawPanel(prevPanel, "PREVIEW");
            Rect prevInner = Inset(prevPanel, 8f, 26f, 8f, 8f);

            bool hasRecording = GamePlaybackController.Instance != null
                             && GamePlaybackController.Instance.Duration > 0f;
            bool isPlayingBack = GamePlaybackController.Instance != null
                              && (GamePlaybackController.Instance.IsPlaying || GamePlaybackController.Instance.IsPaused);
            bool isCamProgram = Runner != null
                             && (Runner.IsPlaying || Runner.IsPaused);

            if (_previewRT != null && (isPlayingBack || isCamProgram))
            {
                Rect prevRect = FitTextureRect(PREVIEW_W, PREVIEW_H, prevInner);
                GUI.DrawTexture(prevRect, _previewRT, ScaleMode.StretchToFill);
            }
            else if (hasRecording)
            {
                GUI.Label(new Rect(prevInner.x + prevInner.width * 0.5f - 100f,
                    prevInner.y + prevInner.height * 0.5f - 10f, 200f, 20f),
                    "Press play to preview recording", _sectionStyle);
            }
            else
            {
                GUI.Label(new Rect(prevInner.x + prevInner.width * 0.5f - 80f,
                    prevInner.y + prevInner.height * 0.5f - 10f, 160f, 20f),
                    "Load a recording to preview", _sectionStyle);
            }

            // ── Transport controls ───────────────────────────────────────
            float transportY = area.y + upperH + gap;
            float btnW = 40f;
            float totalBtnW = btnW * 3f + 8f;
            float transportX = area.x + (area.width - totalBtnW) * 0.5f;

            // Rewind
            if (GUI.Button(new Rect(transportX, transportY, btnW, transportH), "RW"))
            {
                Runner.Rewind();
                var pb = GamePlaybackController.Instance;
                if (pb != null) pb.Seek(0f);
                _playheadTime = 0f;
            }

            // Play / Pause
            bool playing = Runner.IsPlaying;
            bool recPlaying = GamePlaybackController.Instance != null && GamePlaybackController.Instance.IsPlaying;
            bool anyPlaying = playing || recPlaying;
            string playLabel = anyPlaying ? "||" : ">";

            if (GUI.Button(new Rect(transportX + btnW + 4f, transportY, btnW, transportH), playLabel))
            {
                var pb = GamePlaybackController.Instance;

                if (anyPlaying)
                {
                    Runner.Pause();
                    if (pb != null) pb.Pause();
                }
                else
                {
                    // Start recording playback if loaded
                    if (pb != null && pb.Duration > 0f)
                        pb.Play();

                    // Start camera program if it has keyframes
                    if (_program.Keyframes.Count >= 2 && Ctrl != null && Ctrl.Cam != null)
                    {
                        Ctrl.ModActive = true;
                        SetField(Ctrl.Zoom, "manualControls", true);
                        Runner.Play(_program, Ctrl.Cam, Ctrl.Zoom);
                    }
                }
            }

            // Stop
            if (GUI.Button(new Rect(transportX + (btnW + 4f) * 2f, transportY, btnW, transportH), "Stop"))
            {
                Runner.Stop();
                var pb = GamePlaybackController.Instance;
                if (pb != null) pb.Stop();
                _playheadTime = 0f;
            }

            // Playback status
            var pbStatus = GamePlaybackController.Instance;
            if (Runner.IsPlaying || (pbStatus != null && pbStatus.IsPlaying))
            {
                GUI.color = new Color(0.4f, 1f, 0.5f);
                string status = "Playing";
                if (Runner.IsPlaying)
                    status += $" — KF {Runner.CurrentKeyframeIndex + 1}/{_program.Keyframes.Count}";
                if (pbStatus != null && pbStatus.IsPlaying)
                    status += $"  [{pbStatus.CurrentTime:F1}s/{pbStatus.Duration:F1}s]";
                GUI.Label(new Rect(transportX + totalBtnW + 12f, transportY + 4f, 400f, 24f), status, _sectionStyle);
                GUI.color = Color.white;
            }
            else if (Runner.IsPaused || (pbStatus != null && pbStatus.IsPaused))
            {
                GUI.color = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(transportX + totalBtnW + 12f, transportY + 4f, 200f, 24f), "Paused", _sectionStyle);
                GUI.color = Color.white;
            }

            // ── Timeline ─────────────────────────────────────────────────
            float timelineY = transportY + transportH + gap;
            Rect timelineRect = new Rect(area.x, timelineY, area.width, timelineH);
            DrawPanel(timelineRect, "");
            DrawTimeline(Inset(timelineRect, 12f, 8f, 12f, 12f));

            // ── Lower section ────────────────────────────────────────────
            float lowerY = timelineY + timelineH + gap;
            float colW = (area.width - gap * 3f) * 0.25f;

            // Recording controls
            Rect recPanel = new Rect(area.x, lowerY, colW, lowerH);
            DrawPanel(recPanel, "RECORDING");
            DrawRecordingControls(Inset(recPanel, 10f, 28f, 10f, 10f));

            // Program name & capture
            Rect namePanel = new Rect(area.x + colW + gap, lowerY, colW, lowerH);
            DrawPanel(namePanel, "PROGRAM");
            DrawProgramName(Inset(namePanel, 10f, 28f, 10f, 10f));

            // Save options
            Rect savePanel = new Rect(area.x + (colW + gap) * 2f, lowerY, colW, lowerH);
            DrawPanel(savePanel, "SAVE / LOAD");
            DrawProgramSave(Inset(savePanel, 10f, 28f, 10f, 10f));

            // Saved programs list
            Rect listPanel = new Rect(area.x + (colW + gap) * 3f, lowerY, colW, lowerH);
            DrawPanel(listPanel, "SAVED PROGRAMS");
            DrawProgramList(Inset(listPanel, 10f, 28f, 10f, 10f));
        }

        private void DrawKeyframeList(Rect r)
        {
            GUILayout.BeginArea(r);

            if (GUILayout.Button("+ Capture Current Camera State"))
                CaptureKeyframe();
            GUILayout.Space(4);

            _kfScrollPos = GUILayout.BeginScrollView(_kfScrollPos);
            for (int i = 0; i < _program.Keyframes.Count; i++)
            {
                var kf = _program.Keyframes[i];
                bool sel = i == _selectedKF;

                GUILayout.BeginHorizontal();

                // Keyframe label
                string label = i == 0 ? $"KF {i + 1}  (start)" : $"KF {i + 1}";
                if (GUILayout.Button(label, sel ? _selectedBtn : GUI.skin.button, GUILayout.Width(80)))
                {
                    _selectedKF = sel ? -1 : i;
                    JumpPreviewToKeyframe(i);
                }

                // Inline config
                if (i > 0)
                {
                    if (!_durationBufs.ContainsKey(i))
                        _durationBufs[i] = kf.Duration.ToString("F2");

                    GUILayout.Label("Dur:", GUILayout.Width(28));
                    _durationBufs[i] = GUILayout.TextField(_durationBufs[i], GUILayout.Width(42));
                    if (float.TryParse(_durationBufs[i], out float dur))
                        kf.Duration = Mathf.Max(0.01f, dur);

                    GUILayout.Label("Ease:", GUILayout.Width(34));
                    foreach (EasingType e in System.Enum.GetValues(typeof(EasingType)))
                    {
                        bool active = kf.Easing == e;
                        if (GUILayout.Button(e.ToString().Substring(0, Mathf.Min(3, e.ToString().Length)),
                            active ? _selectedBtn : GUI.skin.button, GUILayout.Width(36)))
                            kf.Easing = e;
                    }
                }
                else
                {
                    GUILayout.Label("  (start keyframe)", _sectionStyle);
                }

                // Recapture
                if (GUILayout.Button("RC", GUILayout.Width(28)))
                    RecaptureKeyframe(i);

                // Delete
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    _program.Keyframes.RemoveAt(i);
                    _durationBufs.Remove(i);
                    if (_selectedKF >= _program.Keyframes.Count)
                        _selectedKF = _program.Keyframes.Count - 1;
                    i--;
                }
                GUI.color = Color.white;

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawTimeline(Rect r)
        {
            float totalDur = GetTotalDuration();
            if (totalDur <= 0f) totalDur = 1f;

            // Track background
            Rect track = new Rect(r.x, r.y + r.height * 0.3f, r.width, r.height * 0.4f);
            DrawFilledRect(track, new Color(0.25f, 0.25f, 0.3f));

            // Recording track background (shows loaded recording range)
            var pbTrack = GamePlaybackController.Instance;
            if (pbTrack != null && pbTrack.Duration > 0f)
            {
                float recNorm = Mathf.Clamp01(pbTrack.Duration / totalDur);
                Rect recBar = new Rect(r.x, track.y, r.width * recNorm, track.height);
                DrawFilledRect(recBar, new Color(0.3f, 0.5f, 0.3f, 0.4f));
            }

            // Keyframe markers
            float cumulative = 0f;
            for (int i = 0; i < _program.Keyframes.Count; i++)
            {
                float normPos = cumulative / totalDur;
                float markerX = r.x + normPos * r.width;
                Rect marker = new Rect(markerX - 5f, track.y - 4f, 10f, track.height + 8f);

                bool sel = i == _selectedKF;
                DrawFilledRect(marker, sel ? new Color(0.3f, 0.8f, 1f) : new Color(0.4f, 0.6f, 0.9f));

                // Click to select
                if (Event.current.type == EventType.MouseDown && marker.Contains(Event.current.mousePosition))
                {
                    _selectedKF = i;
                    _playheadTime = cumulative;
                    JumpPreviewToKeyframe(i);

                    var pb = GamePlaybackController.Instance;
                    if (pb != null && pb.Duration > 0f)
                        pb.Seek(cumulative);

                    Event.current.Use();
                }

                if (i < _program.Keyframes.Count - 1)
                    cumulative += Mathf.Max(_program.Keyframes[i + 1].Duration, 0.001f);
            }

            // Playhead
            float phNorm = Mathf.Clamp01(_playheadTime / totalDur);
            float phX = r.x + phNorm * r.width;
            DrawFilledRect(new Rect(phX - 1.5f, r.y, 3f, r.height), new Color(1f, 0.2f, 0.2f));

            // Playhead top marker
            DrawFilledRect(new Rect(phX - 6f, r.y - 2f, 12f, 6f), new Color(1f, 0.2f, 0.2f));

            // Drag playhead
            Rect dragZone = new Rect(r.x, r.y - 8f, r.width, r.height + 16f);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && dragZone.Contains(e.mousePosition)
                && !MarkerHit(e.mousePosition, r, totalDur))
            {
                _draggingPlayhead = true;
                e.Use();
            }
            if (e.type == EventType.MouseDrag && _draggingPlayhead)
            {
                float norm = Mathf.Clamp01((e.mousePosition.x - r.x) / r.width);
                _playheadTime = norm * totalDur;
                JumpPreviewToTime(_playheadTime);

                // Also scrub the recording
                var playback = GamePlaybackController.Instance;
                if (playback != null && playback.Duration > 0f)
                    playback.Seek(_playheadTime);

                e.Use();
            }
            if (e.type == EventType.MouseUp && _draggingPlayhead)
            {
                _draggingPlayhead = false;
                e.Use();
            }

            // Time labels
            GUI.Label(new Rect(r.x, r.yMax + 2f, 60f, 16f), "0.00s", _sectionStyle);
            GUI.Label(new Rect(r.xMax - 60f, r.yMax + 2f, 60f, 16f), $"{totalDur:F2}s", _sectionStyle);
        }

        private bool MarkerHit(Vector2 mouse, Rect r, float totalDur)
        {
            float cumulative = 0f;
            for (int i = 0; i < _program.Keyframes.Count; i++)
            {
                float normPos = cumulative / totalDur;
                float markerX = r.x + normPos * r.width;
                if (Mathf.Abs(mouse.x - markerX) < 8f) return true;
                if (i < _program.Keyframes.Count - 1)
                    cumulative += Mathf.Max(_program.Keyframes[i + 1].Duration, 0.001f);
            }
            return false;
        }

        private void DrawRecordingControls(Rect r)
        {
            var recorder = GameRecorder.Instance;
            var playback = GamePlaybackController.Instance;

            GUILayout.BeginArea(r);

            // Record button
            if (recorder.IsRecording)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("Stop Recording"))
                {
                    recorder.StopRecording();
                    ShowNotification($"Recording stopped — {recorder.CurrentRecording.Frames.Count} frames");
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("Start Recording"))
                {
                    recorder.StartRecording();
                    ShowNotification("Recording started...");
                }
            }

            GUILayout.Space(4);

            // Save current recording
            if (recorder.CurrentRecording != null && !recorder.IsRecording)
            {
                if (GUILayout.Button("Save Recording"))
                {
                    RecordingIO.Save(recorder.CurrentRecording);
                    ShowNotification($"Recording '{recorder.CurrentRecording.Name}' saved!");
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("SAVED RECORDINGS", _sectionStyle);

            // List saved recordings
            string[] files = RecordingIO.GetRecordingFiles();
            if (files.Length == 0)
            {
                GUILayout.Label("No recordings.", _sectionStyle);
            }
            else
            {
                _recScrollPos = GUILayout.BeginScrollView(_recScrollPos);
                foreach (string file in files)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(file, GUILayout.ExpandWidth(true)))
                    {
                        string path = System.IO.Path.Combine(RecordingIO.GetFolder(), file + ".camrec");
                        var rec = RecordingIO.Load(path);
                        playback.Load(rec);
                        ShowNotification($"Recording '{file}' loaded ({rec.Duration:F1}s)");
                    }
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                        System.IO.File.Delete(System.IO.Path.Combine(RecordingIO.GetFolder(), file + ".camrec"));
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(4);

            // Playback controls
            if (playback.Duration > 0)
            {
                GUILayout.Label($"Loaded: {playback.Duration:F1}s", _sectionStyle);
                GUILayout.BeginHorizontal();
                if (playback.IsPlaying)
                {
                    if (GUILayout.Button("||")) playback.Pause();
                }
                else
                {
                    if (GUILayout.Button(">")) playback.Play();
                }
                if (GUILayout.Button("Stop")) playback.Stop();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void DrawProgramName(Rect r)
        {
            GUILayout.BeginArea(r);

            GUILayout.Label("Program Name:", _sectionStyle);
            _programNameInput = GUILayout.TextField(_programNameInput);
            if (_programNameInput != _program.Name) _program.Name = _programNameInput;

            GUILayout.Space(8);
            GUILayout.Label("CAPTURE", _sectionStyle);
            if (GUILayout.Button("+ Capture Current Camera"))
                CaptureKeyframe();
            if (GUILayout.Button("+ Capture from Map View"))
                CaptureKeyframeFromBox();

            GUILayout.EndArea();
        }

        private void DrawProgramSave(Rect r)
        {
            GUILayout.BeginArea(r);

            GUILayout.Label("Save As:", _sectionStyle);
            GUILayout.BeginHorizontal();
            _saveNameInput = GUILayout.TextField(_saveNameInput, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Save", GUILayout.Width(50)) && !string.IsNullOrEmpty(_saveNameInput))
            {
                _program.Name = _saveNameInput;
                _programNameInput = _saveNameInput;
                _program.SaveToFile(ProgramFolder);
                ShowNotification($"Program '{_saveNameInput}' saved!");
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawProgramList(Rect r)
        {
            GUILayout.BeginArea(r);

            List<string> files = GetProgramFiles();
            if (files.Count == 0)
            {
                GUILayout.Label("No saved programs.", _sectionStyle);
            }
            else
            {
                _fileScrollPos = GUILayout.BeginScrollView(_fileScrollPos);
                foreach (string file in files)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(file, GUILayout.ExpandWidth(true)))
                    {
                        string path = Path.Combine(ProgramFolder, file + ".camprog");
                        _program = CameraProgram.LoadFromFile(path);
                        _programNameInput = _program.Name;
                        _selectedKF = -1;
                        _durationBufs.Clear();
                        ShowNotification($"Program '{file}' loaded!");
                    }
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                        File.Delete(Path.Combine(ProgramFolder, file + ".camprog"));
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        // =================================================================
        // PREVIEW CAMERA
        // =================================================================
        private void CreatePreviewCamera()
        {
            if (_previewCamGO != null) return;

            _previewRT = new RenderTexture(PREVIEW_W, PREVIEW_H, 16, RenderTextureFormat.ARGB32);
            _previewRT.filterMode = FilterMode.Bilinear;

            _previewCamGO = new GameObject("UIPreviewCam");
            _previewCamGO.hideFlags = HideFlags.HideAndDontSave;
            _previewCam = _previewCamGO.AddComponent<Camera>();
            _previewCam.enabled = false;
            _previewCam.targetTexture = _previewRT;

            if (Ctrl != null && Ctrl.Cam != null)
            {
                _previewCam.CopyFrom(Ctrl.Cam);
                _previewCam.enabled = false;
                _previewCam.targetTexture = _previewRT;
            }
        }

        private void JumpPreviewToKeyframe(int index)
        {
            if (index < 0 || index >= _program.Keyframes.Count) return;
            if (Ctrl == null || Ctrl.Cam == null) return;
            var kf = _program.Keyframes[index];

            Ctrl.ManualZoom = true;
            Ctrl.ManualPosition = true;
            Ctrl.Cam.transform.position = new Vector3(kf.PosX, kf.PosY, kf.PosZ);
            Ctrl.Cam.fieldOfView = kf.FOV;
            Ctrl.FOV = kf.FOV;
            Ctrl.ApplyToCamera();

            // Update drag box to match
            float camZ = Mathf.Abs(kf.PosZ);
            float halfH = camZ * Mathf.Tan(kf.FOV * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * Ctrl.Cam.aspect;
            _boxWorldLeft = kf.PosX - halfW;
            _boxWorldRight = kf.PosX + halfW;
            _boxWorldTop = kf.PosY + halfH;
            _boxWorldBottom = kf.PosY - halfH;
        }

        private void JumpPreviewToTime(float time)
        {
            if (_program.Keyframes.Count < 2 || Ctrl == null || Ctrl.Cam == null) return;

            float cumulative = 0f;
            for (int i = 0; i < _program.Keyframes.Count - 1; i++)
            {
                float dur = Mathf.Max(_program.Keyframes[i + 1].Duration, 0.001f);
                if (time <= cumulative + dur)
                {
                    float t = (time - cumulative) / dur;
                    var from = _program.Keyframes[i];
                    var to = _program.Keyframes[i + 1];

                    float fov = Mathf.Lerp(from.FOV, to.FOV, t);
                    float px = Mathf.Lerp(from.PosX, to.PosX, t);
                    float py = Mathf.Lerp(from.PosY, to.PosY, t);
                    float pz = Mathf.Lerp(from.PosZ, to.PosZ, t);

                    Ctrl.ManualZoom = true;
                    Ctrl.ManualPosition = true;
                    Ctrl.Cam.transform.position = new Vector3(px, py, pz);
                    Ctrl.Cam.fieldOfView = fov;
                    Ctrl.FOV = fov;
                    Ctrl.ApplyToCamera();

                    float camZ = Mathf.Abs(pz);
                    float halfH = camZ * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                    float halfW = halfH * Ctrl.Cam.aspect;
                    _boxWorldLeft = px - halfW;
                    _boxWorldRight = px + halfW;
                    _boxWorldTop = py + halfH;
                    _boxWorldBottom = py - halfH;
                    return;
                }
                cumulative += dur;
            }

            JumpPreviewToKeyframe(_program.Keyframes.Count - 1);
        }

        private void DestroyPreviewCamera()
        {
            if (_previewCamGO != null) { DestroyImmediate(_previewCamGO); _previewCam = null; _previewCamGO = null; }
            if (_previewRT != null) { _previewRT.Release(); Destroy(_previewRT); _previewRT = null; }
        }

        // =================================================================
        // MAP SNAPSHOT
        // =================================================================
        public void TakeSnapshot()
        {
            float worldW = _worldMaxX - _worldMinX;
            float worldH = _worldMaxY - _worldMinY;
            float aspect = worldW / Mathf.Max(worldH, 0.01f);
            int texH = 512;
            int texW = Mathf.Clamp(Mathf.RoundToInt(texH * aspect), 128, 2048);

            var rt = RenderTexture.GetTemporary(texW, texH, 16, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            var camGO = new GameObject("SnapshotCam");
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = worldH * 0.5f;
            cam.aspect = aspect;
            cam.transform.position = new Vector3(
                (_worldMinX + _worldMaxX) * 0.5f,
                (_worldMinY + _worldMaxY) * 0.5f, -50f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
            cam.cullingMask = ~0;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            if (_snapshotTex != null) Destroy(_snapshotTex);
            _snapshotTex = new Texture2D(texW, texH, TextureFormat.RGB24, false);
            _snapshotTex.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
            _snapshotTex.Apply();
            RenderTexture.active = prev;

            RenderTexture.ReleaseTemporary(rt);
            DestroyImmediate(camGO);
            _hasSnapshot = true;
            SyncBoxFromCamera();
        }

        // =================================================================
        // BOX / CAMERA SYNC
        // =================================================================
        private void SyncBoxFromCamera()
        {
            if (Ctrl == null || Ctrl.Cam == null) return;
            Camera c = Ctrl.Cam;
            float camZ = Mathf.Abs(c.transform.position.z);
            float halfH = camZ * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * c.aspect;
            _boxWorldLeft = c.transform.position.x - halfW;
            _boxWorldRight = c.transform.position.x + halfW;
            _boxWorldTop = c.transform.position.y + halfH;
            _boxWorldBottom = c.transform.position.y - halfH;
        }

        private void ApplyBoxToCamera()
        {
            if (Ctrl == null || Ctrl.Cam == null) return;
            Ctrl.ManualZoom = true;
            Ctrl.ManualPosition = true;

            float centerX = (_boxWorldLeft + _boxWorldRight) * 0.5f;
            float centerY = (_boxWorldTop + _boxWorldBottom) * 0.5f;
            float boxH = _boxWorldTop - _boxWorldBottom;
            float camZ = Ctrl.Cam.transform.position.z;
            float fov = 2f * Mathf.Atan2(boxH * 0.5f, Mathf.Abs(camZ)) * Mathf.Rad2Deg;
            fov = Mathf.Clamp(fov, 1f, 170f);

            // Set camera directly — bypass ZoomCamera reflection
            Ctrl.Cam.transform.position = new Vector3(centerX, centerY, camZ);
            Ctrl.Cam.fieldOfView = fov;

            // Update controller state so it doesn't revert next frame
            Ctrl.FOV = fov;

            // Keep the UI text buffer in sync so the slider doesn't revert it
            _bufFOV = fov.ToString("F2");
        }

        private void CenterOnCharacter()
        {
            if (Ctrl == null || Ctrl.Cam == null) return;

            Character localChar = null;
            foreach (Character c in FindObjectsOfType<Character>())
            {
                if (c != null && c.hasAuthority) { localChar = c; break; }
            }

            if (localChar == null)
            {
                ShowNotification("No character found");
                return;
            }

            Ctrl.ManualPosition = true;

            Vector3 charPos = localChar.transform.position;
            float camZ = Ctrl.Cam.transform.position.z;
            Ctrl.Cam.transform.position = new Vector3(charPos.x, charPos.y, camZ);

            Ctrl.ApplyToCamera();
            SyncBoxFromCamera();
            ShowNotification("Centered on character");
        }

        // =================================================================
        // KEYFRAME HELPERS
        // =================================================================
        private void CaptureKeyframe()
        {
            if (Ctrl == null || Ctrl.Cam == null) return;
            var kf = new CameraKeyframe
            {
                FOV = Ctrl.Cam.fieldOfView,
                PosX = Ctrl.Cam.transform.position.x,
                PosY = Ctrl.Cam.transform.position.y,
                PosZ = Ctrl.Cam.transform.position.z,
                LeftBuffer = Ctrl.LeftBuffer,
                RightBuffer = Ctrl.RightBuffer,
                TopBuffer = Ctrl.TopBuffer,
                BottomBuffer = Ctrl.BottomBuffer,
                Duration = 1f,
                Easing = EasingType.Smooth
            };
            _program.Keyframes.Add(kf);
            _selectedKF = _program.Keyframes.Count - 1;
            ShowNotification($"Keyframe {_program.Keyframes.Count} captured!");
        }

        private void RecaptureKeyframe(int index)
        {
            if (Ctrl == null || Ctrl.Cam == null) return;
            var kf = _program.Keyframes[index];
            kf.FOV = Ctrl.Cam.fieldOfView;
            kf.PosX = Ctrl.Cam.transform.position.x;
            kf.PosY = Ctrl.Cam.transform.position.y;
            kf.PosZ = Ctrl.Cam.transform.position.z;
            kf.LeftBuffer = Ctrl.LeftBuffer; kf.RightBuffer = Ctrl.RightBuffer;
            kf.TopBuffer = Ctrl.TopBuffer; kf.BottomBuffer = Ctrl.BottomBuffer;
            ShowNotification($"Keyframe {index + 1} recaptured!");
        }

        private void CaptureKeyframeFromBox()
        {
            if (Ctrl == null || Ctrl.Cam == null) return;
            ApplyBoxToCamera();
            float centerX = (_boxWorldLeft + _boxWorldRight) * 0.5f;
            float centerY = (_boxWorldTop + _boxWorldBottom) * 0.5f;
            float boxH = _boxWorldTop - _boxWorldBottom;
            float camZ = Ctrl.Cam.transform.position.z;
            float fov = 2f * Mathf.Atan2(boxH * 0.5f, Mathf.Abs(camZ)) * Mathf.Rad2Deg;
            var kf = new CameraKeyframe
            {
                FOV = fov,
                PosX = centerX,
                PosY = centerY,
                PosZ = camZ,
                LeftBuffer = Ctrl.LeftBuffer,
                RightBuffer = Ctrl.RightBuffer,
                TopBuffer = Ctrl.TopBuffer,
                BottomBuffer = Ctrl.BottomBuffer,
                Duration = 1f,
                Easing = EasingType.Smooth
            };
            _program.Keyframes.Add(kf);
            _selectedKF = _program.Keyframes.Count - 1;
            ShowNotification($"Keyframe {_program.Keyframes.Count} captured from map!");
        }

        public void AddKeyframeExternally(CameraKeyframe kf)
        {
            _program.Keyframes.Add(kf);
            _selectedKF = _program.Keyframes.Count - 1;
        }

        // =================================================================
        // TIMELINE HELPERS
        // =================================================================
        private float GetTotalDuration()
        {
            var playback = GamePlaybackController.Instance;

            // If a recording is loaded, use its duration as the timeline length
            if (playback != null && playback.Duration > 0f)
                return playback.Duration;

            // Fallback to camera keyframe duration
            float total = 0f;
            for (int i = 1; i < _program.Keyframes.Count; i++)
                total += Mathf.Max(_program.Keyframes[i].Duration, 0.001f);
            return total;
        }

        private float GetPlaybackTime()
        {
            if (!Runner.IsPlaying) return _playheadTime;
            float time = 0f;
            for (int i = 1; i <= Runner.CurrentKeyframeIndex && i < _program.Keyframes.Count; i++)
                time += Mathf.Max(_program.Keyframes[i].Duration, 0.001f);
            if (Runner.CurrentKeyframeIndex < _program.Keyframes.Count - 1)
                time += Runner.Progress * Mathf.Max(_program.Keyframes[Runner.CurrentKeyframeIndex + 1].Duration, 0.001f);
            return time;
        }

        // =================================================================
        // DRAG BOX INTERACTION
        // =================================================================
        private void HandleDragInteraction(Rect drawRect, Rect boxPixels)
        {
            Event e = Event.current;
            Vector2 mouse = e.mousePosition;

            if (e.type == EventType.MouseDown && e.button == 0 && drawRect.Contains(mouse))
            {
                _drag = DetectDragMode(mouse, boxPixels);
                if (_drag != DragMode.None)
                {
                    _dragStartMouse = mouse;
                    _dragStartBoxL = _boxWorldLeft; _dragStartBoxR = _boxWorldRight;
                    _dragStartBoxT = _boxWorldTop; _dragStartBoxB = _boxWorldBottom;
                    _orthoDragLocked = false;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _drag != DragMode.None)
            {
                Vector2 ws = PixelToWorld(_dragStartMouse, drawRect);
                Vector2 wn = PixelToWorld(mouse, drawRect);
                float dx = wn.x - ws.x, dy = wn.y - ws.y;
                float aspect = (float)Screen.width / Screen.height;

                switch (_drag)
                {
                    case DragMode.Move:
                        if (_orthoDrag)
                        {
                            if (!_orthoDragLocked)
                            {
                                if (Mathf.Abs(dx) > 0.3f || Mathf.Abs(dy) > 0.3f)
                                {
                                    _orthoDragIsHorizontal = Mathf.Abs(dx) > Mathf.Abs(dy);
                                    _orthoDragLocked = true;
                                }
                            }
                            if (_orthoDragLocked)
                            {
                                if (_orthoDragIsHorizontal)
                                {
                                    _boxWorldLeft = _dragStartBoxL + dx;
                                    _boxWorldRight = _dragStartBoxR + dx;
                                    _boxWorldTop = _dragStartBoxT;
                                    _boxWorldBottom = _dragStartBoxB;
                                }
                                else
                                {
                                    _boxWorldLeft = _dragStartBoxL;
                                    _boxWorldRight = _dragStartBoxR;
                                    _boxWorldTop = _dragStartBoxT + dy;
                                    _boxWorldBottom = _dragStartBoxB + dy;
                                }
                            }
                        }
                        else
                        {
                            _boxWorldLeft = _dragStartBoxL + dx;
                            _boxWorldRight = _dragStartBoxR + dx;
                            _boxWorldTop = _dragStartBoxT + dy;
                            _boxWorldBottom = _dragStartBoxB + dy;
                        }
                        break;
                    case DragMode.ResizeTL: ResizeCorner(dx, dy, aspect, true, true); break;
                    case DragMode.ResizeTR: ResizeCorner(dx, dy, aspect, false, true); break;
                    case DragMode.ResizeBL: ResizeCorner(dx, dy, aspect, true, false); break;
                    case DragMode.ResizeBR: ResizeCorner(dx, dy, aspect, false, false); break;
                    case DragMode.ResizeL: ResizeEdgeH(dx, aspect, true); break;
                    case DragMode.ResizeR: ResizeEdgeH(dx, aspect, false); break;
                    case DragMode.ResizeT: ResizeEdgeV(dy, aspect, true); break;
                    case DragMode.ResizeB: ResizeEdgeV(dy, aspect, false); break;
                }
                ApplyBoxToCamera();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _drag != DragMode.None)
            {
                _drag = DragMode.None;
                _orthoDragLocked = false;
                _dragJustEnded = true;
                e.Use();
            }
        }

        private void ResizeCorner(float dx, float dy, float aspect, bool anchorRight, bool anchorBottom)
        {
            float fixX = anchorRight ? _dragStartBoxR : _dragStartBoxL;
            float fixY = anchorBottom ? _dragStartBoxB : _dragStartBoxT;
            float rawW = anchorRight ? (fixX - (_dragStartBoxL + dx)) : ((_dragStartBoxR + dx) - fixX);
            float rawH = anchorBottom ? ((_dragStartBoxT + dy) - fixY) : (fixY - (_dragStartBoxB + dy));
            float newW, newH;
            if (Mathf.Abs(dx) * (1f / aspect) > Mathf.Abs(dy))
            { newW = Mathf.Max(rawW, MIN_BOX_WORLD); newH = newW / aspect; }
            else
            { newH = Mathf.Max(rawH, MIN_BOX_WORLD); newW = newH * aspect; }

            if (anchorRight) { _boxWorldRight = fixX; _boxWorldLeft = fixX - newW; }
            else { _boxWorldLeft = fixX; _boxWorldRight = fixX + newW; }
            if (anchorBottom) { _boxWorldBottom = fixY; _boxWorldTop = fixY + newH; }
            else { _boxWorldTop = fixY; _boxWorldBottom = fixY - newH; }
        }

        private void ResizeEdgeH(float dx, float aspect, bool anchorRight)
        {
            float fix = anchorRight ? _dragStartBoxR : _dragStartBoxL;
            float centerY = (_dragStartBoxT + _dragStartBoxB) * 0.5f;
            float newW = anchorRight ? Mathf.Max(fix - (_dragStartBoxL + dx), MIN_BOX_WORLD)
                                     : Mathf.Max((_dragStartBoxR + dx) - fix, MIN_BOX_WORLD);
            float newH = newW / aspect;
            if (anchorRight) { _boxWorldRight = fix; _boxWorldLeft = fix - newW; }
            else { _boxWorldLeft = fix; _boxWorldRight = fix + newW; }
            _boxWorldTop = centerY + newH * 0.5f;
            _boxWorldBottom = centerY - newH * 0.5f;
        }

        private void ResizeEdgeV(float dy, float aspect, bool anchorBottom)
        {
            float fix = anchorBottom ? _dragStartBoxB : _dragStartBoxT;
            float centerX = (_dragStartBoxL + _dragStartBoxR) * 0.5f;
            float newH = anchorBottom ? Mathf.Max((_dragStartBoxT + dy) - fix, MIN_BOX_WORLD)
                                      : Mathf.Max(fix - (_dragStartBoxB + dy), MIN_BOX_WORLD);
            float newW = newH * aspect;
            if (anchorBottom) { _boxWorldBottom = fix; _boxWorldTop = fix + newH; }
            else { _boxWorldTop = fix; _boxWorldBottom = fix - newH; }
            _boxWorldLeft = centerX - newW * 0.5f;
            _boxWorldRight = centerX + newW * 0.5f;
        }

        private DragMode DetectDragMode(Vector2 mouse, Rect box)
        {
            bool nL = Mathf.Abs(mouse.x - box.xMin) < CORNER_GRAB;
            bool nR = Mathf.Abs(mouse.x - box.xMax) < CORNER_GRAB;
            bool nT = Mathf.Abs(mouse.y - box.yMin) < CORNER_GRAB;
            bool nB = Mathf.Abs(mouse.y - box.yMax) < CORNER_GRAB;
            if (nL && nT) return DragMode.ResizeTL; if (nR && nT) return DragMode.ResizeTR;
            if (nL && nB) return DragMode.ResizeBL; if (nR && nB) return DragMode.ResizeBR;
            bool inY = mouse.y >= box.yMin - EDGE_GRAB && mouse.y <= box.yMax + EDGE_GRAB;
            bool inX = mouse.x >= box.xMin - EDGE_GRAB && mouse.x <= box.xMax + EDGE_GRAB;
            if (nL && inY) return DragMode.ResizeL; if (nR && inY) return DragMode.ResizeR;
            if (nT && inX) return DragMode.ResizeT; if (nB && inX) return DragMode.ResizeB;
            if (box.Contains(mouse)) return DragMode.Move;
            return DragMode.None;
        }

        // =================================================================
        // COORDINATE CONVERSION
        // =================================================================
        private Rect FitTextureRect(float texW, float texH, Rect area)
        {
            float tA = texW / texH, aA = area.width / area.height;
            if (tA > aA) { float h = area.width / tA; return new Rect(area.x, area.y + (area.height - h) * 0.5f, area.width, h); }
            else { float w = area.height * tA; return new Rect(area.x + (area.width - w) * 0.5f, area.y, w, area.height); }
        }

        private Rect WorldBoxToPixelRect(Rect drawRect)
        {
            float wW = _worldMaxX - _worldMinX, wH = _worldMaxY - _worldMinY;
            float nL = (_boxWorldLeft - _worldMinX) / wW, nR = (_boxWorldRight - _worldMinX) / wW;
            float nT = 1f - (_boxWorldTop - _worldMinY) / wH, nB = 1f - (_boxWorldBottom - _worldMinY) / wH;
            return new Rect(drawRect.x + nL * drawRect.width, drawRect.y + nT * drawRect.height,
                (nR - nL) * drawRect.width, (nB - nT) * drawRect.height);
        }

        private Vector2 PixelToWorld(Vector2 px, Rect drawRect)
        {
            float nX = (px.x - drawRect.x) / drawRect.width;
            float nY = (px.y - drawRect.y) / drawRect.height;
            return new Vector2(_worldMinX + nX * (_worldMaxX - _worldMinX),
                               _worldMaxY - nY * (_worldMaxY - _worldMinY));
        }

        // =================================================================
        // NOTIFICATION
        // =================================================================
        private void ShowNotification(string msg)
        {
            _notification = msg;
            _notificationTime = Time.time;
        }

        // =================================================================
        // DRAWING HELPERS
        // =================================================================
        private void DrawPanel(Rect r, string title)
        {
            DrawFilledRect(r, new Color(0.14f, 0.14f, 0.18f));
            DrawRectOutline(r, new Color(0.3f, 0.3f, 0.35f), 1f);
            if (!string.IsNullOrEmpty(title))
                GUI.Label(new Rect(r.x + 8, r.y + 4, r.width, 20), title, _sectionStyle);
        }

        private Rect Inset(Rect r, float left, float top, float right, float bottom)
        {
            return new Rect(r.x + left, r.y + top, r.width - left - right, r.height - top - bottom);
        }

        private void DrawDarkenedOverlay(Rect drawRect, Rect box)
        {
            Color dark = new Color(0f, 0f, 0f, 0.55f);
            float bL = Mathf.Max(box.xMin, drawRect.xMin), bR = Mathf.Min(box.xMax, drawRect.xMax);
            float bT = Mathf.Max(box.yMin, drawRect.yMin), bB = Mathf.Min(box.yMax, drawRect.yMax);
            if (bT > drawRect.yMin) DrawFilledRect(new Rect(drawRect.x, drawRect.y, drawRect.width, bT - drawRect.y), dark);
            if (bB < drawRect.yMax) DrawFilledRect(new Rect(drawRect.x, bB, drawRect.width, drawRect.yMax - bB), dark);
            if (bL > drawRect.xMin) DrawFilledRect(new Rect(drawRect.x, bT, bL - drawRect.x, bB - bT), dark);
            if (bR < drawRect.xMax) DrawFilledRect(new Rect(bR, bT, drawRect.xMax - bR, bB - bT), dark);
        }

        private void DrawFilledRect(Rect r, Color c)
        { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        private void DrawRectOutline(Rect r, Color c, float t)
        {
            DrawFilledRect(new Rect(r.x, r.y, r.width, t), c);
            DrawFilledRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            DrawFilledRect(new Rect(r.x, r.y, t, r.height), c);
            DrawFilledRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void DrawCornerHandle(Vector2 center)
        { DrawFilledRect(new Rect(center.x - 4f, center.y - 4f, 8f, 8f), new Color(1f, 0.85f, 0.3f)); }

        private float DrawSliderRow(string label, float current, float min, float max, ref string buf, bool logScale)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            float sliderVal;
            if (logScale)
            {
                float logMin = Mathf.Log(Mathf.Max(min, 0.01f)), logMax = Mathf.Log(max);
                float logCur = Mathf.Log(Mathf.Max(current, 0.01f));
                float logNew = GUILayout.HorizontalSlider(logCur, logMin, logMax, GUILayout.ExpandWidth(true));
                sliderVal = Mathf.Exp(logNew);
                if (!Mathf.Approximately(logNew, logCur)) buf = sliderVal.ToString("F2");
            }
            else
            {
                sliderVal = GUILayout.HorizontalSlider(current, min, max, GUILayout.ExpandWidth(true));
                if (!Mathf.Approximately(sliderVal, current)) buf = sliderVal.ToString("F2");
            }
            if (buf == null) buf = current.ToString("F2");
            buf = GUILayout.TextField(buf, GUILayout.Width(48));
            GUILayout.EndHorizontal();
            if (float.TryParse(buf, out float parsed)) return Mathf.Clamp(parsed, min, max);
            return sliderVal;
        }

        // =================================================================
        // UTILITY
        // =================================================================
        private void SyncBuffersFromCtrl()
        {
            if (Ctrl == null) return;
            _bufLeft = Ctrl.LeftBuffer.ToString("F2");
            _bufRight = Ctrl.RightBuffer.ToString("F2");
            _bufTop = Ctrl.TopBuffer.ToString("F2");
            _bufBottom = Ctrl.BottomBuffer.ToString("F2");
            _bufFOV = Ctrl.FOV.ToString("F2");
        }

        private List<string> GetProgramFiles()
        {
            var names = new List<string>();
            foreach (string file in Directory.GetFiles(ProgramFolder, "*.camprog"))
                names.Add(Path.GetFileNameWithoutExtension(file));
            names.Sort();
            return names;
        }

        private void SetField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field != null) field.SetValue(obj, value);
        }

        private void OnDestroy()
        {
            DestroyPreviewCamera();
            if (_snapshotTex != null) Destroy(_snapshotTex);
        }

        // =================================================================
        // STYLES
        // =================================================================
        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _headerStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 16, fontStyle = (FontStyle)1, normal = { textColor = Color.white } };

            _sectionStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 11, fontStyle = (FontStyle)1, normal = { textColor = new Color(0.7f, 0.9f, 1f) } };

            _tabActive = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = (FontStyle)1,
                normal = { textColor = Color.white, background = MakeTex(1, 1, new Color(0.25f, 0.25f, 0.35f)) },
                hover = { textColor = Color.white, background = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.4f)) }
            };

            _tabInactive = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.6f, 0.6f, 0.7f), background = MakeTex(1, 1, new Color(0.18f, 0.18f, 0.22f)) },
                hover = { textColor = new Color(0.8f, 0.8f, 0.9f), background = MakeTex(1, 1, new Color(0.22f, 0.22f, 0.28f)) }
            };

            _activeToggle = new GUIStyle(GUI.skin.toggle)
            {
                normal = { textColor = new Color(0.4f, 1f, 0.5f) },
                focused = { textColor = new Color(0.4f, 1f, 0.5f) }
            };

            _selectedBtn = new GUIStyle(GUI.skin.button)
            { normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
        }

        private Texture2D MakeTex(int w, int h, Color col)
        {
            var t = new Texture2D(w, h);
            for (int i = 0; i < w * h; i++) t.SetPixel(i % w, i / w, col);
            t.Apply();
            return t;
        }
    }
}