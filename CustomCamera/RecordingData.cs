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
        public string GameMode = "FreePlayControl";
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
        public List<PhaseEvent> PhaseEvents = new List<PhaseEvent>();
        public List<PartyBoxVisibilityEvent> PartyBoxEvents = new List<PartyBoxVisibilityEvent>();
        public List<ItemPickupEvent> ItemPickupEvents = new List<ItemPickupEvent>();
        public List<ItemPlacedEvent> ItemPlacedEvents = new List<ItemPlacedEvent>();
        public List<ItemDestroyedEvent> ItemDestroyedEvents = new List<ItemDestroyedEvent>();
    }

    [Serializable]
    public class RecordingFrame
    {
        public float Time;                // seconds since recording started
        public List<CharacterSnapshot> Characters = new List<CharacterSnapshot>();
        public List<CursorSnapshot> Cursors = new List<CursorSnapshot>();
        public List<PickCursorSnapshot> PickCursors = new List<PickCursorSnapshot>();
        public List<ItemStateSnapshot> ItemStates = new List<ItemStateSnapshot>();
    }

    [Serializable]
    public class ItemPickupEvent
    {
        public float Time;
        public int CursorNetNum;
        public int BlockIndex;
        public int PieceID;
    }

    [Serializable]
    public class ItemPlacedEvent
    {
        public float Time;
        public int PieceID;
        public float PosX, PosY, RotZ, ScaleX, ScaleY;
    }

    [Serializable]
    public class ItemDestroyedEvent
    {
        public float Time;
        public int PieceID;
    }

    [Serializable]
    public class ItemStateSnapshot
    {
        public int PieceID;
        public float PosX, PosY, RotZ, ScaleX, ScaleY;
    }

    [Serializable]
    public class CursorSnapshot
    {
        public int NetworkNumber;
        public float PosX;
        public float PosY;
        public bool Visible;
    }

    [Serializable]
    public class PickCursorSnapshot
    {
        public int NetworkNumber;
        public float PosX;
        public float PosY;
        public bool Visible;
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
    public class PhaseEvent
    {
        public float Time;
        public string Phase;   // stringified GameControl.GamePhase
    }

    [Serializable]
    public class PartyBoxVisibilityEvent
    {
        public float Time;
        public bool Opened;
        public bool IsExtraBox;
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
