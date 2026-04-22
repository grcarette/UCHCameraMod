using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UCHCameraMod
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static Plugin Instance;
        internal static Character CachedCharacterPrefab;

        // ── Config entries ──────────────────────────────────────────────
        internal static ConfigEntry<bool> CfgVerboseReplayLog;
        internal static ConfigEntry<bool> CfgManualZoom;
        internal static ConfigEntry<bool> CfgSmoothFollow;
        internal static ConfigEntry<float> CfgLeftBuffer;
        internal static ConfigEntry<float> CfgRightBuffer;
        internal static ConfigEntry<float> CfgTopBuffer;
        internal static ConfigEntry<float> CfgBottomBuffer;
        internal static ConfigEntry<float> CfgFOV;
        internal static ConfigEntry<bool> CfgManualPosition;
        internal static ConfigEntry<KeyCode> CfgKeyToggleMod;
        internal static ConfigEntry<KeyCode> CfgKeyReset;
        internal static ConfigEntry<KeyCode> CfgKeyManualZoom;
        internal static ConfigEntry<KeyCode> CfgKeySmoothFollow;
        internal static ConfigEntry<KeyCode> CfgKeyManualPosition;
        internal static ConfigEntry<KeyCode> CfgKeyFOVDown;
        internal static ConfigEntry<KeyCode> CfgKeyFOVUp;
        internal static ConfigEntry<KeyCode> CfgKeyPlayProgram;
        internal static ConfigEntry<KeyCode> CfgKeyStopProgram;
        internal static ConfigEntry<KeyCode> CfgKeyResetProgram;
        internal static ConfigEntry<KeyCode> CfgKeyRecord;
        internal static ConfigEntry<KeyCode> CfgKeyStopRecord;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            BindConfig();

            var go = new GameObject("UCHCameraModController");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraModController>();
            go.AddComponent<CameraProgramRunner>();
            go.AddComponent<CameraUI>();
            go.AddComponent<GameRecorder>();
            go.AddComponent<GamePlaybackController>();

            new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded. Press F6 to toggle camera customization mode.");

            // Cache PartyBox prefab from HideAndDontSave once it's loaded (typically after MainMenu)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (VersusControlStartPatch.CachedPartyBoxPrefab != null) return;

                var boxes = Resources.FindObjectsOfTypeAll<PartyBox>();
                if (boxes.Length == 0) return;

                PartyBox prefab = null;
                foreach (var b in boxes)
                {
                    if (b == null) continue;
                    if (string.IsNullOrEmpty(b.gameObject.scene.name))
                    {
                        prefab = b;
                        break;
                    }
                }
                if (prefab == null) prefab = boxes[0];

                VersusControlStartPatch.CachedPartyBoxPrefab = prefab;
                Logger.LogInfo(
                    $"[PrefabCache] PartyBox prefab cached via FindObjectsOfTypeAll " +
                    $"after scene '{scene.name}' (found {boxes.Length} total)");
            };
        }

        private void BindConfig()
        {
            CfgVerboseReplayLog = Config.Bind("Debug", "VerboseReplayLog", true,
                "Enable detailed replay reconstruction logging");

            const string SEC = "Camera";
            CfgManualZoom = Config.Bind(SEC, "ManualZoom", false, "Enable manual zoom on ZoomCamera");
            CfgSmoothFollow = Config.Bind(SEC, "SmoothFollow", true, "Enable smooth follow on ZoomCamera");
            CfgLeftBuffer = Config.Bind(SEC, "UnitLeftBuffer", 2f, "Left unit buffer");
            CfgRightBuffer = Config.Bind(SEC, "UnitRightBuffer", 2f, "Right unit buffer");
            CfgTopBuffer = Config.Bind(SEC, "UnitTopBuffer", 2f, "Top unit buffer");
            CfgBottomBuffer = Config.Bind(SEC, "UnitBottomBuffer", 2f, "Bottom unit buffer");
            CfgFOV = Config.Bind(SEC, "FieldOfView", 60f, "Camera field of view");
            CfgManualPosition = Config.Bind(SEC, "ManualPosition", false, "Manually control camera position");

            const string KEYS = "Keybinds";
            CfgKeyToggleMod = Config.Bind(KEYS, "ToggleMod", KeyCode.F6, "Toggle camera customization mode");
            CfgKeyReset = Config.Bind(KEYS, "Reset", KeyCode.F9, "Reset camera to defaults");
            CfgKeyManualZoom = Config.Bind(KEYS, "ManualZoom", KeyCode.F7, "Toggle manual zoom");
            CfgKeySmoothFollow = Config.Bind(KEYS, "SmoothFollow", KeyCode.F8, "Toggle smooth follow");
            CfgKeyManualPosition = Config.Bind(KEYS, "ManualPosition", KeyCode.F10, "Toggle manual position");
            CfgKeyFOVDown = Config.Bind(KEYS, "FOVDown", KeyCode.LeftBracket, "Decrease FOV");
            CfgKeyFOVUp = Config.Bind(KEYS, "FOVUp", KeyCode.RightBracket, "Increase FOV");
            CfgKeyPlayProgram = Config.Bind(KEYS, "PlayProgram", KeyCode.F12, "Play camera program");
            CfgKeyStopProgram = Config.Bind(KEYS, "StopProgram", KeyCode.Keypad0, "Stop camera program");
            CfgKeyResetProgram = Config.Bind(KEYS, "ResetProgram", KeyCode.KeypadPeriod, "Reset camera program");
            CfgKeyRecord = Config.Bind(KEYS, "StartRecording", KeyCode.F1, "Start recording game");
            CfgKeyStopRecord = Config.Bind(KEYS, "StopRecording", KeyCode.F2, "Stop recording game");
        }
    }
}