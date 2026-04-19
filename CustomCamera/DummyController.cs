using UnityEngine;

namespace UCHCameraMod
{
    /// <summary>
    /// Stub Controller for fabricated local players used during replay playback.
    /// Produces no input and skips AFK detection.
    /// ControllerType is GENERIC to avoid the KEYBOARD branch in LobbyPlayer.InitPlayer
    /// that overwrites GameState.ChatSystem.keyBoardLobbyPlayer.
    /// </summary>
    public class DummyController : Controller
    {
        public override bool IsUsingPosition() => false;
        public override Vector2 GetVector(bool absolute = false) => Vector2.zero;
        public override void Reset() { }
        public override Controller.ControllerType GetControllerType()
            => Controller.ControllerType.GENERIC;

        // Override Update to skip the AFK detection that would otherwise
        // send MsgAFKPlayer to the server every few seconds.
        public override void Update()
        {
            TimeSinceLastInput = 0f;
        }
    }
}
