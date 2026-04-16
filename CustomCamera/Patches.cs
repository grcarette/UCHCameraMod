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

    [HarmonyPatch(typeof(Character), "FixedUpdate")]
    internal static class PatchCharacterFixedUpdate
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (GamePlaybackController.Instance != null
                && (GamePlaybackController.Instance.IsPlaying || GamePlaybackController.Instance.IsPaused))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), "Update")]
    internal static class PatchCharacterUpdate
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (GamePlaybackController.Instance != null
                && (GamePlaybackController.Instance.IsPlaying || GamePlaybackController.Instance.IsPaused))
                return false;
            return true;
        }
    }
}