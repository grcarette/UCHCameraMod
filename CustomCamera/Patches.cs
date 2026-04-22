using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using GameEvent;

namespace UCHCameraMod
{
    /// <summary>
    /// Suppresses UCH's ZoomCamera.Update while our mod is active so the
    /// game doesn't overwrite our position/FOV changes every frame.
    ///
    /// If UCH uses LateUpdate instead of / as well as Update, duplicate
    /// the patch below replacing "Update" with "LateUpdate".
    /// </summary>
    [HarmonyPatch(typeof(ZoomCamera), "Update")]
    internal static class PatchZoomCameraUpdate
    {
        public static bool Prefix(Character __instance)
        {
            // Guard against destroyed characters during scene transitions
            if (__instance == null) return true;

            try
            {
                if (!__instance.hasAuthority) return true;
            }
            catch
            {
                return true;
            }
            bool modActive = CameraModController.Instance != null
                          && CameraModController.Instance.ModActive;
            bool camProgram = CameraProgramRunner.Instance != null
                           && (CameraProgramRunner.Instance.IsPlaying || CameraProgramRunner.Instance.IsPaused);
            bool recPlaying = GamePlaybackController.Instance != null
                           && GamePlaybackController.Instance.IsPlaying;

            // Let ZoomCamera run during recording playback (it follows characters)
            // Only suppress when camera program is active or UI has manual control
            if (recPlaying && !camProgram)
                return true;

            return !modActive && !camProgram;
        }
    }

    [HarmonyPatch(typeof(Character), "ReceiveEvent")]
    internal static class PatchCharacterReceiveEvent
    {
        static bool Prefix(Character __instance)
        {
            try
            {
                if (CameraModController.Instance != null
                    && CameraModController.Instance.ModActive
                    && __instance.hasAuthority)
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
            return true;
        }
    }

    // Caches the "should block Character updates" decision once per frame
    // so the two patches below don't each do a null check + property access
    internal static class PlaybackState
    {
        private static int _lastFrame = -1;
        private static bool _cachedBlock;

        public static bool ShouldBlock()
        {
            if (Time.frameCount != _lastFrame)
            {
                _lastFrame = Time.frameCount;
                var p = GamePlaybackController.Instance;
                _cachedBlock = p != null && (p.IsPlaying || p.IsPaused);
            }
            return _cachedBlock;
        }
    }

    [HarmonyPatch(typeof(Character), "FixedUpdate")]
    internal static class PatchCharacterFixedUpdate
    {
        [HarmonyPrefix]
        static bool Prefix() => !PlaybackState.ShouldBlock();
    }

    [HarmonyPatch(typeof(Character), "Update")]
    internal static class PatchCharacterUpdate
    {
        [HarmonyPrefix]
        static bool Prefix() => !PlaybackState.ShouldBlock();
    }

    [HarmonyPatch(typeof(GameControl), "Start")]
    internal static class PatchGameControlCachePrefab
    {
        static void Postfix(GameControl __instance)
        {
            if (__instance.CharacterPrefab != null)
            {
                Plugin.CachedCharacterPrefab = __instance.CharacterPrefab;
                Plugin.Logger.LogInfo("[PrefabCache] CharacterPrefab cached from GameControl");
            }
            else
            {
                Plugin.Logger.LogWarning("[PrefabCache] GameControl.CharacterPrefab was null");
            }
        }
    }

    [HarmonyPatch(typeof(GameControl))]
    [HarmonyPatch("SetupStart")]
    [HarmonyPatch(new Type[] { typeof(GameState.GameMode) })]
    internal static class PatchGameControlSetupStart
    {
        static void Postfix(GameControl __instance)
        {
            Plugin.Logger.LogInfo("[ReplayLaunch:Patch] GameControl.SetupStart postfix fired");
            Plugin.Logger.LogInfo($"[ReplayLaunch:Patch] Phase={__instance.Phase} " +
                                  $"Scene={__instance.AssociatedScene} " +
                                  $"hasAuthority={__instance.hasAuthority}");

            if (GamePlaybackController.Instance != null &&
                GamePlaybackController.Instance.HasPendingReplay)
            {
                Plugin.Logger.LogInfo("[ReplayLaunch:Patch] Pending replay detected — " +
                                      "starting OnGameReadyForReplay coroutine");
                __instance.StartCoroutine(
                    GamePlaybackController.Instance.OnGameReadyForReplay(__instance));
            }

            // Cache the character prefab for later use
            if (__instance.CharacterPrefab != null)
            {
                Plugin.CachedCharacterPrefab = __instance.CharacterPrefab;
                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo("[PrefabCache] CharacterPrefab cached from GameControl");
            }
        }
    }

    [HarmonyPatch(typeof(Character), "audioEvent")]
    internal static class PatchCharacterAudioEvent
    {
        static void Postfix(Character __instance, string audioEventName, GameObject go, bool ignoreGhostZombie)
        {
            if (PlaybackState.ShouldBlock()) return;

            var recorder = GameRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            int netNum = __instance.networkNumber;
            if (netNum <= 0) return;

            recorder.RecordSoundEvent(netNum, audioEventName,
                __instance.isZombie && !ignoreGhostZombie,
                __instance.isGhost && !ignoreGhostZombie);
        }
    }

    [HarmonyPatch(typeof(Character), "AudioEventExact")]
    internal static class PatchCharacterAudioEventExact
    {
        static void Postfix(Character __instance, string audioEventName)
        {
            if (PlaybackState.ShouldBlock()) return;

            var recorder = GameRecorder.Instance;
            if (recorder == null || !recorder.IsRecording) return;

            int netNum = __instance.networkNumber;
            if (netNum <= 0) return;

            recorder.RecordSoundEvent(netNum, "EXACT:" + audioEventName, false, false);
        }
    }

    [HarmonyPatch(typeof(ScoreLine), "AddScorePointBlock")]
    internal static class PatchScoreLineTrackPoints
    {
        static void Postfix(PointBlock pb)
        {
            if (pb.type != PointBlock.pointBlockType.win &&
                pb.type != PointBlock.pointBlockType.winDead &&
                pb.type != PointBlock.pointBlockType.soloWin)
                return;

            GameRecorder.Instance?.OnPlayerScored(pb.playerNumber);
        }
    }

    [HarmonyPatch(typeof(Placeable), "DestroySelf")]
    internal static class PlaceableDestroyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Placeable __instance)
        {
            if (GameRecorder.Instance == null || !GameRecorder.Instance.IsRecording) return;
            if (__instance == null || __instance.ID == 0) return;
            GameRecorder.Instance.RecordItemDestroyed(__instance.ID);
        }
    }

    [HarmonyPatch(typeof(PartyBox), "ChoosePieces")]
    internal static class RecordBoxItemPositionsPatch
    {
        private static readonly FieldInfo PiecesField =
            typeof(PartyBox).GetField("pieces", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void Postfix(PartyBox __instance)
        {
            if (GameRecorder.Instance == null || !GameRecorder.Instance.IsRecording) return;

            var pieces = PiecesField?.GetValue(__instance) as List<PickableBlock>;
            if (pieces == null) return;

            GameRecorder.Instance.RecordBoxContents(__instance, pieces);
        }
    }

    [HarmonyPatch(typeof(VersusControl), "Start")]
    internal static class VersusControlStartPatch
    {
        public static PartyBox CachedPartyBoxPrefab;

        [HarmonyPostfix]
        public static void Postfix(VersusControl __instance)
        {
            if (__instance.PartyBoxPrefab != null && CachedPartyBoxPrefab == null)
            {
                CachedPartyBoxPrefab = __instance.PartyBoxPrefab;
                Plugin.Logger.LogInfo(
                    $"[PrefabCache] PartyBoxPrefab cached: {CachedPartyBoxPrefab.name}");
            }
        }
    }

    // ── Cursor diagnostics ──────────────────────────────────────────────────

    [HarmonyPatch(typeof(Cursor), "Enable")]
    internal static class DiagCursorEnablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Cursor __instance)
        {
            if (GamePlaybackController.Instance == null ||
                !GamePlaybackController.Instance.IsPlaying) return;
            if (!__instance.hasAuthority) return;
            Plugin.Logger.LogInfo(
                $"[Diag:Cursor] Enable called during replay, " +
                $"type={__instance.GetType().Name}, " +
                $"netNum={__instance.networkNumber}, " +
                $"caller={new System.Diagnostics.StackTrace(1, false).GetFrame(0).GetMethod().Name}");
        }
    }

    [HarmonyPatch(typeof(Cursor), "Disable")]
    internal static class DiagCursorDisablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Cursor __instance)
        {
            if (GamePlaybackController.Instance == null ||
                !GamePlaybackController.Instance.IsPlaying) return;
            if (!__instance.hasAuthority) return;
            Plugin.Logger.LogInfo(
                $"[Diag:Cursor] Disable called during replay, " +
                $"type={__instance.GetType().Name}, " +
                $"netNum={__instance.networkNumber}");
        }
    }

    [HarmonyPatch(typeof(PiecePlacementCursor), "Update")]
    internal static class DiagCursorUpdatePatch
    {
        private static float _lastLog;

        [HarmonyPostfix]
        public static void Postfix(PiecePlacementCursor __instance)
        {
            if (GamePlaybackController.Instance == null ||
                !GamePlaybackController.Instance.IsPlaying) return;
            if (!__instance.hasAuthority) return;

            if (Time.time - _lastLog >= 1f)
            {
                _lastLog = Time.time;
                Plugin.Logger.LogInfo(
                    $"[Diag:Cursor] PiecePlacementCursor.Update running on host cursor during replay, " +
                    $"netNum={__instance.networkNumber}, " +
                    $"enabled={__instance.Enabled}, " +
                    $"piece={(__instance.Piece != null ? __instance.Piece.ID.ToString() : "null")}");
            }
        }
    }

    [HarmonyPatch(typeof(PartyPickCursor), "Update")]
    internal static class DiagPartyPickCursorUpdatePatch
    {
        private static float _lastLog;

        [HarmonyPostfix]
        public static void Postfix(PartyPickCursor __instance)
        {
            if (GamePlaybackController.Instance == null ||
                !GamePlaybackController.Instance.IsPlaying) return;
            if (!__instance.hasAuthority) return;

            if (Time.time - _lastLog >= 1f)
            {
                _lastLog = Time.time;
                Plugin.Logger.LogInfo(
                    $"[Diag:Cursor] PartyPickCursor.Update running on host cursor during replay, " +
                    $"netNum={__instance.networkNumber}, " +
                    $"enabled={__instance.Enabled}");
            }
        }
    }

    [HarmonyPatch(typeof(PiecePlacementCursor), "ReceiveEvent")]
    internal static class DiagCursorReceiveEventPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PiecePlacementCursor __instance, GameEvent.GameEvent e)
        {
            if (GamePlaybackController.Instance == null ||
                !GamePlaybackController.Instance.IsPlaying) return;
            if (!__instance.hasAuthority) return;

            Plugin.Logger.LogInfo(
                $"[Diag:Cursor] ReceiveEvent during replay, " +
                $"netNum={__instance.networkNumber}, " +
                $"event={e?.GetType().Name ?? "null"}");
        }
    }

}