using System.Collections.Generic;
using UnityEngine;

namespace UCHCameraMod
{
    public class GameRecorder : MonoBehaviour
    {
        public static GameRecorder Instance { get; private set; }

        public bool IsRecording { get; private set; }
        public Recording CurrentRecording { get; private set; }

        private float _startTime;

        // Cached at recording start — no per-frame lookups
        private struct TrackedCharacter
        {
            public Character Character;
            public int NetworkNumber;
            public SpriteRenderer Sprite;
            public Animator Animator;
            public Transform Transform;
            // Per-character clip name cache — avoids GetCurrentAnimatorClipInfo alloc every tick
            public int LastHash;
            public string LastClipName;
        }

        private List<TrackedCharacter> _tracked = new List<TrackedCharacter>();
        private Dictionary<int, int> _scores = new Dictionary<int, int>();

        private void Awake()
        {
            Instance = this;
        }

        public void StartRecording()
        {
            _tracked.Clear();
            _scores.Clear();

            // Build a lookup from Character -> networkNumber once
            var charToNumber = new Dictionary<Character, int>();
            foreach (GamePlayer gp in FindObjectsOfType<GamePlayer>())
            {
                if (gp.CharacterInstance != null)
                    charToNumber[gp.CharacterInstance] = gp.networkNumber;
            }

            foreach (Character c in FindObjectsOfType<Character>())
            {
                if (c == null) continue;
                int netNum = charToNumber.ContainsKey(c) ? charToNumber[c] : -1;

                _tracked.Add(new TrackedCharacter
                {
                    Character = c,
                    NetworkNumber = netNum,
                    Sprite = c.GetComponentInChildren<SpriteRenderer>(),
                    Animator = c.GetComponentInChildren<Animator>(),
                    Transform = c.transform
                });
            }

            if (_tracked.Count == 0)
            {
                Debug.LogWarning("GameRecorder: No characters found.");
                return;
            }

            CurrentRecording = new Recording
            {
                Name = "Recording_" + System.DateTime.Now.ToString("HHmmss"),
                TickRate = Time.fixedDeltaTime
            };

            var meta = CurrentRecording.Metadata;
            meta.Date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            meta.StageCode = "unknown"; // TODO: find level/stage field on LobbyManager via dnSpy

            try
            {
                var gc = FindObjectOfType<GameControl>();
                if (gc != null)
                {
                    meta.GameMode = gc.GetType().Name;
                    if (!string.IsNullOrEmpty(gc.AssociatedScene))
                        meta.SceneName = gc.AssociatedScene;
                    else
                        meta.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                }
                else
                {
                    meta.GameMode = "unknown";
                    meta.SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                }
            }
            catch { meta.GameMode = "unknown"; meta.SceneName = "unknown"; }

            foreach (GamePlayer gp in FindObjectsOfType<GamePlayer>())
            {
                if (gp == null) continue;
                meta.Players.Add(new PlayerInfo
                {
                    NetworkNumber = gp.networkNumber,
                    Name = gp.playerName,
                    Score = 0,
                    Animal = gp.PickedAnimal.ToString(),
                    IsWearingSkin = gp.IsWearingSkin,
                    Outfits = (gp.CharacterInstance != null)
                        ? gp.CharacterInstance.GetOutfitsAsArray()
                        : new int[0]
                });
            }

            CaptureSceneSnapshot();

            // Capture QuickSaver snapshot
            try
            {
                var gc = FindObjectOfType<GameControl>();
                if (gc != null)
                {
                    var qs = gc.GetComponent<QuickSaver>();
                    if (qs != null)
                    {
                        var xmlDoc = qs.GetCurrentXmlSnapshot(true);
                        CurrentRecording.SnapshotBytes = QuickSaver.GetCompressedBytesFromXmlDoc(xmlDoc);
                        Plugin.Logger.LogInfo($"[Recorder] Snapshot captured: {CurrentRecording.SnapshotBytes.Length} bytes (compressed)");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning("[Recorder] No QuickSaver component on GameControl");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"[Recorder] Failed to capture snapshot: {ex.Message}");
            }

            _startTime = Time.realtimeSinceStartup;
            IsRecording = true;
        }

        public void OnPlayerScored(int networkNumber)
        {
            if (!IsRecording) return;
            if (!_scores.ContainsKey(networkNumber))
                _scores[networkNumber] = 0;
            _scores[networkNumber]++;
            if (Plugin.CfgVerboseReplayLog.Value)
                Plugin.Logger.LogInfo($"[Recorder] Player {networkNumber} scored " +
                                      $"(total: {_scores[networkNumber]})");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            CurrentRecording.Duration = Time.realtimeSinceStartup - _startTime;

            foreach (var pi in CurrentRecording.Metadata.Players)
            {
                if (_scores.TryGetValue(pi.NetworkNumber, out int score))
                    pi.Score = score;
            }
        }

        private void CaptureSceneSnapshot()
        {
            var scene = CurrentRecording.Scene;
            scene.Placeables.Clear();

            Plugin.Logger.LogInfo("[SceneSnapshot] Starting capture...");
            Plugin.Logger.LogInfo($"[SceneSnapshot] Placeable.AllPlaceables count: {Placeable.AllPlaceables.Count}");

            int levelGeoSkipped = 0;
            int setPieceSkipped = 0;
            int captured = 0;

            foreach (Placeable p in Placeable.AllPlaceables)
            {
                if (p == null || !p.Placed || p.MarkedForDestruction) continue;

                var pmeta = p.GetComponent<PlaceableMetadata>();

                if (pmeta != null && pmeta.isLevelGeometry)
                {
                    levelGeoSkipped++;
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogInfo($"[SceneSnapshot]   SKIPPED (level geo): Name={p.Name} ID={p.ID}");
                    continue;
                }

                if (p.isSetPiece && p.placedByPlayerNumber == 0)
                {
                    setPieceSkipped++;
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogInfo($"[SceneSnapshot]   SKIPPED (base set piece): Name={p.Name} ID={p.ID}");
                    continue;
                }

                if (p.IsSubElement && p.ParentPiece != null)
                {
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogInfo($"[SceneSnapshot]   SKIPPED (sub-element): Name={p.Name} ID={p.ID}");
                    continue;
                }

                if (pmeta == null)
                    Plugin.Logger.LogWarning($"[SceneSnapshot]   No PlaceableMetadata on: {p.Name} (ID={p.ID})");

                int blockIndex = -1;
                if (pmeta != null)
                    blockIndex = pmeta.blockSerializeIndex;
                if (blockIndex < 0 && PlaceableMetadataList.Instance != null)
                    blockIndex = PlaceableMetadataList.Instance.GetIndexForPlaceable(p.Name);
                if (blockIndex < 0)
                    Plugin.Logger.LogWarning($"[SceneSnapshot]   Could not resolve BlockIndex for: {p.Name}");

                var snap = new PlaceableSnapshot
                {
                    ID = p.ID,
                    Name = p.Name,
                    Category = p.Category.ToString(),
                    PlacedByPlayer = p.placedByPlayerNumber,
                    PosX = p.OriginalPosition.x,
                    PosY = p.OriginalPosition.y,
                    Rotation = p.OriginalRotation.eulerAngles.z,
                    ScaleX = p.OriginalScale.x,
                    ScaleY = p.OriginalScale.y,
                    ParentID = (p.ParentPiece != null) ? p.ParentPiece.ID : -1,
                    IsSetPiece = p.isSetPiece,
                    DamageLevel = p.damageLevel,
                    CustomColorHex = p.canSetCustomColor
                        ? ColorUtility.ToHtmlStringRGBA(p.CustomColor)
                        : "",
                    BlockIndex = blockIndex,
                    IsLevelGeometry = (pmeta != null && pmeta.isLevelGeometry)
                };

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[SceneSnapshot]   Piece: Name={p.Name} ID={p.ID} " +
                                          $"Pos=({p.OriginalPosition.x:F2},{p.OriginalPosition.y:F2}) " +
                                          $"BlockIdx={snap.BlockIndex} IsLevelGeo={snap.IsLevelGeometry}");

                scene.Placeables.Add(snap);
                captured++;
            }

            Plugin.Logger.LogInfo($"[SceneSnapshot] Captured {captured} player-placed pieces " +
                                  $"(skipped {levelGeoSkipped} level geo, {setPieceSkipped} base set pieces)");

            var gc = FindObjectOfType<GameControl>();
            if (gc != null && gc.LevelLayout != null)
            {
                var bounds = gc.LevelLayout.GetCameraBounds();
                scene.CameraBoundsMinX = bounds.min.x;
                scene.CameraBoundsMaxX = bounds.max.x;
                scene.CameraBoundsMinY = bounds.min.y;
                scene.CameraBoundsMaxY = bounds.max.y;
                scene.MinCharacterY = gc.LevelLayout.MinimumCharacterPosition;
            }

            Plugin.Logger.LogInfo($"[SceneSnapshot] Capture complete: {scene.Placeables.Count} pieces stored");
            Plugin.Logger.LogInfo($"[SceneSnapshot] Camera bounds: " +
                                  $"({scene.CameraBoundsMinX:F1},{scene.CameraBoundsMinY:F1}) to " +
                                  $"({scene.CameraBoundsMaxX:F1},{scene.CameraBoundsMaxY:F1})");
            Plugin.Logger.LogInfo($"[SceneSnapshot] MinCharacterY: {scene.MinCharacterY:F1}");
        }

        private void FixedUpdate()
        {
            if (!IsRecording) return;
            CaptureFrame(Time.realtimeSinceStartup - _startTime);
        }

        private void CaptureFrame(float elapsed)
        {
            var frame = new RecordingFrame { Time = elapsed };

            for (int i = 0; i < _tracked.Count; i++)
            {
                var t = _tracked[i];
                if (t.Character == null) continue;

                var snap = new CharacterSnapshot
                {
                    NetworkNumber = t.NetworkNumber,
                    PosX = t.Transform.position.x,
                    PosY = t.Transform.position.y,
                    ScaleX = t.Transform.localScale.x,
                    ScaleY = t.Transform.localScale.y,
                    Rotation = t.Transform.eulerAngles.z,
                    FlipSpriteX = t.Character.FlipSpriteX,
                    Visible = t.Character.gameObject.activeInHierarchy
                              && t.Sprite != null && t.Sprite.enabled
                };

                // Animation — only if animator is cached and active
                if (t.Animator != null && t.Animator.isActiveAndEnabled)
                {
                    var stateInfo = t.Animator.GetCurrentAnimatorStateInfo(0);
                    snap.AnimationTime = stateInfo.normalizedTime;
                    snap.AnimationStateHash = stateInfo.fullPathHash;

                    // Only call GetCurrentAnimatorClipInfo when the state changes — avoids per-tick alloc
                    if (stateInfo.fullPathHash != t.LastHash)
                    {
                        var clips = t.Animator.GetCurrentAnimatorClipInfo(0);
                        t.LastHash = stateInfo.fullPathHash;
                        t.LastClipName = (clips.Length > 0 && clips[0].clip != null) ? clips[0].clip.name : "";
                        _tracked[i] = t;  // write back mutated struct
                    }
                    snap.AnimationState = t.LastClipName;
                }

                frame.Characters.Add(snap);
            }

            CurrentRecording.Frames.Add(frame);
        }
    }
}
