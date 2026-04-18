using System;
using System.Collections.Generic;

namespace UCHCameraMod
{
    [Serializable]
    public class RecordingMetadata
    {
        public string Date;
        public string StageCode;
        public string SceneName;
        public string GameMode;
        public List<PlayerInfo> Players = new List<PlayerInfo>();
    }

    [Serializable]
    public class PlayerInfo
    {
        public int NetworkNumber;
        public string Name;
        public int Score;
        public string Animal;
        public bool IsWearingSkin;
        public int[] Outfits;
    }

    [Serializable]
    public class SceneSnapshot
    {
        public List<PlaceableSnapshot> Placeables = new List<PlaceableSnapshot>();
        public float CameraBoundsMinX, CameraBoundsMaxX;
        public float CameraBoundsMinY, CameraBoundsMaxY;
        public float MinCharacterY;
    }

    [Serializable]
    public class PlaceableSnapshot
    {
        public int ID;
        public string Name;
        public string Category;
        public int PlacedByPlayer;
        public float PosX, PosY;
        public float Rotation;
        public float ScaleX, ScaleY;
        public int ParentID;
        public bool IsSetPiece;
        public int DamageLevel;
        public string CustomColorHex;
        public int BlockIndex = -1;
        public bool IsLevelGeometry;
    }

    [Serializable]
    public class Recording
    {
        public string Name;
        public float Duration;            // total time in seconds
        public float TickRate;            // seconds per frame (usually 0.02 for FixedUpdate)
        public RecordingMetadata Metadata = new RecordingMetadata();
        public SceneSnapshot Scene = new SceneSnapshot();
        public byte[] SnapshotBytes;      // compressed QuickSaver XML snapshot
        public List<RecordingFrame> Frames = new List<RecordingFrame>();
        public List<SoundEvent> SoundEvents = new List<SoundEvent>();
    }

    [Serializable]
    public class RecordingFrame
    {
        public float Time;                // seconds since recording started
        public List<CharacterSnapshot> Characters = new List<CharacterSnapshot>();
    }

    [Serializable]
    public struct SoundEvent
    {
        public float Time;
        public int NetworkNumber;
        public string EventName;
        public bool IsZombie;
        public bool IsGhost;
    }

    [Serializable]
    public struct CharacterSnapshot
    {
        public int NetworkNumber;
        public float PosX, PosY;
        public float ScaleX, ScaleY;
        public float Rotation;
        public bool Visible;
        public string AnimationState;
        public float AnimationTime;
        public int AnimationStateHash;
        public float FlipSpriteX;
    }
}
