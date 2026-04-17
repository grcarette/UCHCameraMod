using HarmonyLib;
using UnityEngine;

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
        static bool Prefix()
        {
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
            // Block game input for the local player while camera mode is on
            if (CameraModController.Instance != null
                && CameraModController.Instance.ModActive
                && __instance.hasAuthority)
            {
                return false; // skip the original — game receives no input
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
}