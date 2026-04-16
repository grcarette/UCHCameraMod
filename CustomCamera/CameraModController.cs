using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Rewired;
using Input = UnityEngine.Input;


namespace UCHCameraMod
{
    public class CameraModController : MonoBehaviour
    {
        public static CameraModController Instance { get; private set; }

        public bool ModActive { get; set; } = false;

        public bool ManualZoom { get; set; }
        public bool ManualPosition { get; set; }
        public bool SmoothFollow { get; set; }
        public float LeftBuffer { get; set; }
        public float RightBuffer { get; set; }
        public float TopBuffer { get; set; }
        public float BottomBuffer { get; set; }
        public float FOV { get; set; }

        public Camera Cam => _cam;
        public ZoomCamera Zoom => _zoom;

        private const float BUFFER_STEP = 0.25f;
        private const float FOV_STEP = 1f;
        private const float POS_STEP = 0.5f;
        private bool _firstFrame;

        private Camera _cam;
        private ZoomCamera _zoom;

        private static readonly string PresetFolder = Path.Combine(
            Paths.ConfigPath, "UCHCameraPresets");

        private void Awake()
        {
            Instance = this;
            LoadFromConfig();
            Directory.CreateDirectory(PresetFolder);
        }

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.CfgKeyToggleMod.Value))
            {
                ModActive = !ModActive;

                if (ModActive)
                {
                    UnityEngine.Cursor.visible = true;
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    _firstFrame = true;
                    CacheComponents();
                }
                else
                {
                    StartCoroutine(RestoreCursorNextFrame());
                }

            }

            // Let the runner take over completely when playing
            if (CameraProgramRunner.Instance != null && CameraProgramRunner.Instance.IsPlaying)
            {
                CameraProgramRunner.Instance.Tick();
                return;
            }

            if (!ModActive) return;

            HandleKeybinds();
            if (_firstFrame) { _firstFrame = false; return; }
            if (CameraProgramRunner.Instance != null && CameraProgramRunner.Instance.IsPaused) return;
            if (CameraUI.Instance != null && CameraUI.Instance.IsDraggingBox) return;
            if (GamePlaybackController.Instance != null && GamePlaybackController.Instance.IsPlaying) return;
            ApplyToCamera();
        }

        private System.Collections.IEnumerator RestoreCursorNextFrame()
        {
            yield return null;
            yield return null;
            yield return null;
            UnityEngine.Cursor.visible = false;
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            yield return null;
            // Simulate a focus loss and regain, same as alt-tabbing
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                mb.SendMessage("OnApplicationFocus", false, SendMessageOptions.DontRequireReceiver);
            }
            yield return null;
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
            {
                mb.SendMessage("OnApplicationFocus", true, SendMessageOptions.DontRequireReceiver);
            }
        }

        private void CacheComponents()
        {
            var mainCamGO = GameObject.Find("MainCamera");
            if (mainCamGO == null) { Plugin.Logger.LogWarning("MainCamera not found."); return; }
            _cam = mainCamGO.GetComponent<Camera>();
            _zoom = mainCamGO.GetComponent<ZoomCamera>();
            if (_cam == null) Plugin.Logger.LogWarning("Camera component not found.");
            if (_zoom == null) Plugin.Logger.LogWarning("ZoomCamera component not found.");

            ReadFromCamera();
        }

        private void ReadFromCamera()
        {
            if (_cam == null || _zoom == null) return;

            FOV = _cam.fieldOfView;

            object lb = ReadField(_zoom, "UnitLeftBuffer");
            object rb = ReadField(_zoom, "UnitRightBuffer");
            object tb = ReadField(_zoom, "UnitTopBuffer");
            object bb = ReadField(_zoom, "UnitBottomBuffer");
            object mz = ReadField(_zoom, "manualZoom");
            object sf = ReadField(_zoom, "smoothFollowCamOn");
            object mc = ReadField(_zoom, "manualControls");

            if (lb != null) LeftBuffer = (float)lb;
            if (rb != null) RightBuffer = (float)rb;
            if (tb != null) TopBuffer = (float)tb;
            if (bb != null) BottomBuffer = (float)bb;
            if (mz != null) ManualZoom = (bool)mz;
            if (sf != null) SmoothFollow = (bool)sf;
            if (mc != null) ManualPosition = (bool)mc;
        }
        public void OnMenuOpened()
        {
            CacheComponents();
        }

        public void ApplyToCamera()
        {
            if (_cam == null || _zoom == null)
            {
                CacheComponents();
                if (_cam == null || _zoom == null) return;
            }

            SetField(_zoom, "manualZoom", ManualZoom);
            SetField(_zoom, "smoothFollowCamOn", SmoothFollow);
            SetField(_zoom, "UnitLeftBuffer", LeftBuffer);
            SetField(_zoom, "UnitRightBuffer", RightBuffer);
            SetField(_zoom, "UnitTopBuffer", TopBuffer);
            SetField(_zoom, "UnitBottomBuffer", BottomBuffer);
            SetField(_zoom, "manualControls", ManualPosition);

            _cam.fieldOfView = FOV;
        }

        private void HandleKeybinds()
        {
            if (Input.GetKeyDown(Plugin.CfgKeyManualZoom.Value)) { ManualZoom = !ManualZoom; SaveToConfig(); }
            if (Input.GetKeyDown(Plugin.CfgKeySmoothFollow.Value)) { SmoothFollow = !SmoothFollow; SaveToConfig(); }
            if (Input.GetKeyDown(Plugin.CfgKeyManualPosition.Value)) { ManualPosition = !ManualPosition; SaveToConfig(); }

            if (Input.GetKeyDown(Plugin.CfgKeyFOVDown.Value)) { FOV -= FOV_STEP; SaveToConfig(); }
            if (Input.GetKeyDown(Plugin.CfgKeyFOVUp.Value)) { FOV += FOV_STEP; SaveToConfig(); }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) { TopBuffer += BUFFER_STEP; SaveToConfig(); }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { BottomBuffer += BUFFER_STEP; SaveToConfig(); }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { TopBuffer -= BUFFER_STEP; SaveToConfig(); }
                if (Input.GetKeyDown(KeyCode.RightArrow)) { BottomBuffer -= BUFFER_STEP; SaveToConfig(); }
            }

            if (ManualPosition && _cam != null)
            {
                if (!shift)
                {
                    if (Input.GetKeyDown(KeyCode.UpArrow)) _cam.transform.position += Vector3.up * POS_STEP;
                    if (Input.GetKeyDown(KeyCode.DownArrow)) _cam.transform.position -= Vector3.up * POS_STEP;
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) _cam.transform.position -= Vector3.right * POS_STEP;
                    if (Input.GetKeyDown(KeyCode.RightArrow)) _cam.transform.position += Vector3.right * POS_STEP;
                }
                if (Input.GetKeyDown(KeyCode.PageUp)) _cam.transform.position -= Vector3.forward * POS_STEP;
                if (Input.GetKeyDown(KeyCode.PageDown)) _cam.transform.position += Vector3.forward * POS_STEP;
            }

            if (Input.GetKeyDown(KeyCode.Keypad4)) { LeftBuffer -= BUFFER_STEP; SaveToConfig(); }
            if (Input.GetKeyDown(KeyCode.Keypad6)) { LeftBuffer += BUFFER_STEP; SaveToConfig(); }
            if (Input.GetKeyDown(KeyCode.Keypad7)) { RightBuffer -= BUFFER_STEP; SaveToConfig(); }
            if (Input.GetKeyDown(KeyCode.Keypad9)) { RightBuffer += BUFFER_STEP; SaveToConfig(); }

            if (Input.GetKeyDown(Plugin.CfgKeyReset.Value)) ResetToDefaults();
        }

        // ── Presets ─────────────────────────────────────────────────────
        public void SavePreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string path = Path.Combine(PresetFolder, name + ".cfg");
            var lines = new List<string>
            {
                $"ManualZoom={ManualZoom}",
                $"ManualPosition={ManualPosition}",
                $"SmoothFollow={SmoothFollow}",
                $"LeftBuffer={LeftBuffer}",
                $"RightBuffer={RightBuffer}",
                $"TopBuffer={TopBuffer}",
                $"BottomBuffer={BottomBuffer}",
                $"FOV={FOV}",
            };
            File.WriteAllLines(path, lines);
            Plugin.Logger.LogInfo($"Preset saved: {name}");
        }

        public void LoadPreset(string name)
        {
            string path = Path.Combine(PresetFolder, name + ".cfg");
            if (!File.Exists(path)) { Plugin.Logger.LogWarning($"Preset not found: {name}"); return; }

            foreach (string line in File.ReadAllLines(path))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string val = parts[1].Trim();
                switch (key)
                {
                    case "ManualZoom": bool.TryParse(val, out bool mz); ManualZoom = mz; break;
                    case "ManualPosition": bool.TryParse(val, out bool mp); ManualPosition = mp; break;
                    case "SmoothFollow": bool.TryParse(val, out bool sf); SmoothFollow = sf; break;
                    case "LeftBuffer": float.TryParse(val, out float lb); LeftBuffer = lb; break;
                    case "RightBuffer": float.TryParse(val, out float rb); RightBuffer = rb; break;
                    case "TopBuffer": float.TryParse(val, out float tb); TopBuffer = tb; break;
                    case "BottomBuffer": float.TryParse(val, out float bb); BottomBuffer = bb; break;
                    case "FOV": float.TryParse(val, out float fv); FOV = fv; break;
                }
            }
            SaveToConfig();
            Plugin.Logger.LogInfo($"Preset loaded: {name}");
        }

        public void DeletePreset(string name)
        {
            string path = Path.Combine(PresetFolder, name + ".cfg");
            if (File.Exists(path)) File.Delete(path);
        }

        public List<string> GetPresetNames()
        {
            var names = new List<string>();
            foreach (string file in Directory.GetFiles(PresetFolder, "*.cfg"))
                names.Add(Path.GetFileNameWithoutExtension(file));
            names.Sort();
            return names;
        }

        // ── Config ──────────────────────────────────────────────────────
        public void LoadFromConfig()
        {
            ManualZoom = Plugin.CfgManualZoom.Value;
            ManualPosition = Plugin.CfgManualPosition.Value;
            SmoothFollow = Plugin.CfgSmoothFollow.Value;
            LeftBuffer = Plugin.CfgLeftBuffer.Value;
            RightBuffer = Plugin.CfgRightBuffer.Value;
            TopBuffer = Plugin.CfgTopBuffer.Value;
            BottomBuffer = Plugin.CfgBottomBuffer.Value;
            FOV = Plugin.CfgFOV.Value;
        }

        public void SaveToConfig()
        {
            Plugin.CfgManualZoom.Value = ManualZoom;
            Plugin.CfgManualPosition.Value = ManualPosition;
            Plugin.CfgSmoothFollow.Value = SmoothFollow;
            Plugin.CfgLeftBuffer.Value = LeftBuffer;
            Plugin.CfgRightBuffer.Value = RightBuffer;
            Plugin.CfgTopBuffer.Value = TopBuffer;
            Plugin.CfgBottomBuffer.Value = BottomBuffer;
            Plugin.CfgFOV.Value = FOV;
        }

        public void ResetToDefaults_Public() => ResetToDefaults();

        private void ResetToDefaults()
        {
            ManualZoom = false;
            ManualPosition = false;
            SmoothFollow = true;
            LeftBuffer = 0f;
            RightBuffer = 0f;
            TopBuffer = 0f;
            BottomBuffer = 0f;
            FOV = 3.5f;
            SaveToConfig();
            ApplyToCamera();
            Plugin.Logger.LogInfo("Camera settings reset to defaults.");
        }

        private void SetField(object obj, string fieldName, object value)
        {
            FieldInfo field = obj.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                field.SetValue(obj, value);
            else
                Plugin.Logger.LogWarning($"Field '{fieldName}' not found on {obj.GetType().Name}");
        }

        private object ReadField(object obj, string fieldName)
        {
            FieldInfo field = obj.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field.GetValue(obj);
            Plugin.Logger.LogWarning($"Field '{fieldName}' not found on {obj.GetType().Name}");
            return null;
        }
    }
}