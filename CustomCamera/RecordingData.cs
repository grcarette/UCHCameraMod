using System;
using System.Collections.Generic;

namespace UCHCameraMod
{
    [Serializable]
    public class Recording
    {
        public string Name;
        public float Duration;            // total time in seconds
        public float TickRate;            // seconds per frame (usually 0.02 for FixedUpdate)
        public List<RecordingFrame> Frames = new List<RecordingFrame>();
    }

    [Serializable]
    public class RecordingFrame
    {
        public float Time;                // seconds since recording started
        public List<CharacterSnapshot> Characters = new List<CharacterSnapshot>();
    }

    [Serializable]
    public class CharacterSnapshot
    {
        public int NetworkNumber;         // identifies which player
        public float PosX, PosY;          // transform position
        public float ScaleX, ScaleY;      // for detecting flipped sprites
        public float Rotation;            // z euler angle
        public bool Visible;              // alive and on screen
        public bool Grounded;             // useful for animation state
        public string AnimationState;     // clip name for debugging
        public float AnimationTime;       // normalized time within that state
        public int AnimationStateHash;    // fullPathHash from AnimatorStateInfo
        public float FlipSpriteX;         // -1 = facing left, 1 = facing right
    }
}
