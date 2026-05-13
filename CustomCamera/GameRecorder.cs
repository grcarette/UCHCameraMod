using System.Collections.Generic;
using GameEvent;
using UnityEngine;

namespace UCHCameraMod
{
    public class GameRecorder : MonoBehaviour, IGameEventListener
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

        private struct TrackedCursor
        {
            public Cursor Cursor;
            public int NetworkNumber;
            public Transform Transform;
        }

        private struct TrackedPickCursor
        {
            public PartyPickCursor Cursor;
            public int NetworkNumber;
            public Transform Transform;
        }

        private List<TrackedCharacter> _tracked = new List<TrackedCharacter>();
        private List<TrackedCursor> _trackedCursors = new List<TrackedCursor>();
        private List<TrackedPickCursor> _trackedPickCursors = new List<TrackedPickCursor>();
        private Dictionary<int, int> _scores = new Dictionary<int, int>();
        private Dictionary<int, int> _cursorLastHeldPieceID = new Dictionary<int, int>();
        private GameControl _gameControl;
        private GameControl.GamePhase _lastCapturedPhase = (GameControl.GamePhase)(-1);
        private PartyBox _trackedBox;
        private bool _lastBoxVisible;
        private List<BoxItemSnapshot> _pendingBoxItems;
        private PartyBoxVisibilityEvent _currentOpenBoxEvent;

        private static readonly System.Reflection.FieldInfo _partyBoxPiecesField =
            typeof(PartyBox).GetField("pieces",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private void Awake()
        {
            Instance = this;
        }

        public void handleEvent(GameEvent.GameEvent e)
        {
            if (!IsRecording || CurrentRecording == null) return;

            if (e is PiecePlacedEvent placedEvt && placedEvt.PlacedBlock != null)
            {
                float elapsed = Time.realtimeSinceStartup - _startTime;
                CurrentRecording.ItemPlacedEvents.Add(new ItemPlacedEvent
                {
                    Time = elapsed,
                    PlayerNetNum = placedEvt.PlacedBlock.placedByPlayerNumber,
                    PieceID = placedEvt.PlacedBlock.ID,
                    PosX = placedEvt.PlacedBlock.transform.position.x,
                    PosY = placedEvt.PlacedBlock.transform.position.y,
                    RotZ = placedEvt.PlacedBlock.transform.rotation.eulerAngles.z,
                    ScaleX = placedEvt.PlacedBlock.transform.localScale.x,
                    ScaleY = placedEvt.PlacedBlock.transform.localScale.y
                });
                Plugin.Logger.LogInfo($"[Recorder:Item] t={elapsed:F2} placed piece={placedEvt.PlacedBlock.ID} via PiecePlacedEvent");
            }
        }

        public void RecordBoxShown(bool isExtraBox)
        {
            if (!IsRecording || CurrentRecording == null) return;

            float elapsed = Time.realtimeSinceStartup - _startTime;
            var boxEvt = new PartyBoxVisibilityEvent
            {
                Time = elapsed,
                FlapsOpenTime = -1f,
                Opened = true,
                IsExtraBox = isExtraBox,
                Items = new List<BoxItemSnapshot>()
            };

            if (_pendingBoxItems != null)
            {
                boxEvt.Items = _pendingBoxItems;
                _pendingBoxItems = null;
                Plugin.Logger.LogInfo(
                    $"[Recorder:Box] t={elapsed:F2} ShowBox with {boxEvt.Items.Count} item(s)");
            }
            else
            {
                Plugin.Logger.LogWarning(
                    $"[Recorder:Box] t={elapsed:F2} ShowBox but no pending items " +
                    "(ChoosePieces didn't fire before ShowBox)");
            }

            CurrentRecording.PartyBoxEvents.Add(boxEvt);
            _currentOpenBoxEvent = boxEvt;
            _lastBoxVisible = true;
        }

        public void RecordBoxFlapsOpened()
        {
            if (!IsRecording || _currentOpenBoxEvent == null) return;

            float elapsed = Time.realtimeSinceStartup - _startTime;
            _currentOpenBoxEvent.FlapsOpenTime = elapsed;
            Plugin.Logger.LogInfo($"[Recorder:Box] t={elapsed:F2} openFlaps fired");
            _currentOpenBoxEvent = null;
        }

        private List<BoxItemSnapshot> CaptureCurrentBoxItems(PartyBox box)
        {
            var metaList = LobbyManager.instance?.CurrentGameController?.MetaList;
            var pieces = _partyBoxPiecesField?.GetValue(box) as List<PickableBlock>;
            if (pieces == null || metaList == null) return new List<BoxItemSnapshot>();

            var items = new List<BoxItemSnapshot>(pieces.Count);
            foreach (var piece in pieces)
            {
                if (piece == null || piece.placeablePrefab == null) continue;
                int blockIdx = metaList.GetIndexForPlaceable(piece.placeablePrefab.Name);
                var localPos = piece.transform.localPosition;
                items.Add(new BoxItemSnapshot
                {
                    BlockIndex = blockIdx,
                    LocalX = localPos.x,
                    LocalY = localPos.y,
                });
            }
            return items;
        }

        public void RecordBoxContents(PartyBox box, List<PickableBlock> pieces)
        {
            if (!IsRecording) return;

            var metaList = LobbyManager.instance?.CurrentGameController?.MetaList;
            if (metaList == null) return;

            var items = new List<BoxItemSnapshot>(pieces.Count);
            foreach (var piece in pieces)
            {
                if (piece == null || piece.placeablePrefab == null) continue;
                int blockIdx = metaList.GetIndexForPlaceable(piece.placeablePrefab.Name);
                var localPos = piece.transform.localPosition;
                items.Add(new BoxItemSnapshot
                {
                    BlockIndex = blockIdx,
                    LocalX = localPos.x,
                    LocalY = localPos.y,
                });
            }

            _pendingBoxItems = items;

            Plugin.Logger.LogInfo(
                $"[Recorder:Box] Stashed {items.Count} item position(s) from ChoosePieces");
        }

        public void RecordItemDestroyed(int pieceID)
        {
            if (!IsRecording || CurrentRecording == null) return;

            float elapsed = Time.realtimeSinceStartup - _startTime;
            foreach (var ev in CurrentRecording.ItemDestroyedEvents)
            {
                if (ev.PieceID == pieceID && Mathf.Abs(ev.Time - elapsed) < 0.01f) return;
            }

            CurrentRecording.ItemDestroyedEvents.Add(new ItemDestroyedEvent
            {
                Time = elapsed,
                PieceID = pieceID
            });
            Plugin.Logger.LogInfo($"[Recorder:Item] t={elapsed:F2} destroyed piece={pieceID}");
        }

        public void StartRecording()
        {
            _tracked.Clear();
            _scores.Clear();
            _cursorLastHeldPieceID.Clear();

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
                TickRate = Time.fixedDeltaTime,
                Frames = new List<RecordingFrame>(3000)
            };

            var meta = CurrentRecording.Metadata;
            meta.Date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            meta.StageCode = "unknown"; // TODO: find level/stage field on LobbyManager via dnSpy

            try
            {
                _gameControl = FindObjectOfType<GameControl>();
                if (_gameControl != null)
                {
                    meta.GameMode = _gameControl.GetType().Name;
                    if (!string.IsNullOrEmpty(_gameControl.AssociatedScene))
                        meta.SceneName = _gameControl.AssociatedScene;
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

            _lastCapturedPhase = (GameControl.GamePhase)(-1);

            _trackedBox = null;
            if (_gameControl is VersusControl vc && vc.PartyBox != null)
            {
                _trackedBox = vc.PartyBox;
                _lastBoxVisible = _trackedBox.Visible;
                Plugin.Logger.LogInfo(
                    $"[Recorder] Tracking PartyBox (initialVisible={_lastBoxVisible}, " +
                    $"hash={_trackedBox.GetHashCode()})");

                // Bootstrap: if the box is already open when recording starts, synthesize an
                // open event at t=0. ShowBox() already ran before IsRecording was true, so the
                // Harmony postfix missed it. Without this, the recording has only a close event
                // and the replay box never appears.
                if (_lastBoxVisible)
                {
                    var bootstrapEvt = new PartyBoxVisibilityEvent
                    {
                        Time = 0f,
                        FlapsOpenTime = 0f, // flaps are already open — no gating needed
                        Opened = true,
                        IsExtraBox = false,
                        Items = CaptureCurrentBoxItems(_trackedBox)
                    };
                    CurrentRecording.PartyBoxEvents.Add(bootstrapEvt);
                    Plugin.Logger.LogInfo(
                        $"[Recorder:Box] Bootstrap open event at t=0 with {bootstrapEvt.Items.Count} item(s)");
                    // _currentOpenBoxEvent intentionally left null — no openFlaps coming for this one
                }
            }

            _trackedCursors.Clear();
            foreach (GamePlayer gp in FindObjectsOfType<GamePlayer>())
            {
                if (gp == null || gp.CursorInstance == null) continue;
                _trackedCursors.Add(new TrackedCursor
                {
                    Cursor = gp.CursorInstance,
                    NetworkNumber = gp.networkNumber,
                    Transform = gp.CursorInstance.transform
                });
            }
            Plugin.Logger.LogInfo($"[Recorder] Tracking {_trackedCursors.Count} cursor(s)");

            _trackedPickCursors.Clear();
            foreach (GamePlayer gp in FindObjectsOfType<GamePlayer>())
            {
                if (gp == null || gp.PartyPickCursor == null) continue;
                _trackedPickCursors.Add(new TrackedPickCursor
                {
                    Cursor = gp.PartyPickCursor,
                    NetworkNumber = gp.networkNumber,
                    Transform = gp.PartyPickCursor.transform
                });
            }
            Plugin.Logger.LogInfo($"[Recorder] Tracking {_trackedPickCursors.Count} pick cursor(s)");

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
                    Plugin.Logger.LogInfo($"[Recorder] QuickSaver found: {qs != null}");
                    if (qs != null)
                    {
                        Plugin.Logger.LogInfo("[Recorder] Capturing snapshot...");
                        var xmlDoc = qs.GetCurrentXmlSnapshot(true);
                        CurrentRecording.SnapshotBytes = QuickSaver.GetCompressedBytesFromXmlDoc(xmlDoc);
                        Plugin.Logger.LogInfo($"[Recorder] Snapshot captured: {CurrentRecording.SnapshotBytes.Length} bytes compressed");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning("[Recorder] No QuickSaver on GameControl — snapshot not captured");
                    }
                }
                else
                {
                    Plugin.Logger.LogWarning("[Recorder] No GameControl — snapshot not captured");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"[Recorder] Failed to capture snapshot: {ex.Message}");
            }

            GameEventManager.ChangeListener<PiecePlacedEvent>(this, true);

            _startTime = Time.realtimeSinceStartup;
            IsRecording = true;
        }

        public void RecordSoundEvent(int networkNumber, string eventName, bool isZombie, bool isGhost)
        {
            if (!IsRecording || CurrentRecording == null) return;

            CurrentRecording.SoundEvents.Add(new SoundEvent
            {
                Time = Time.realtimeSinceStartup - _startTime,
                EventName = eventName,
                SourceKind = SoundSourceKind.Character,
                SourceID = networkNumber,
                IsZombie = isZombie,
                IsGhost = isGhost,
            });
        }

        public void RecordPostEvent(string eventName, GameObject sourceGO)
        {
            if (!IsRecording || CurrentRecording == null) return;
            if (string.IsNullOrEmpty(eventName)) return;

            SoundSourceKind kind;
            int sourceID;

            if (sourceGO == null)
                return; // global/world sound — too noisy to capture blindly

            // Character: let the audioEvent/AudioEventExact patches handle it to avoid duplication
            var character = sourceGO.GetComponentInParent<Character>();
            if (character != null && character.networkNumber > 0)
                return;

            var cursor = sourceGO.GetComponentInParent<Cursor>();
            if (cursor != null && cursor.networkNumber > 0)
            {
                kind = SoundSourceKind.Cursor;
                sourceID = cursor.networkNumber;
            }
            else
            {
                var placeable = sourceGO.GetComponentInParent<Placeable>();
                if (placeable != null && placeable.ID != 0)
                {
                    kind = SoundSourceKind.Piece;
                    sourceID = placeable.ID;
                }
                else
                {
                    var box = sourceGO.GetComponent<PartyBox>();
                    if (box != null)
                    {
                        kind = SoundSourceKind.PartyBox;
                        sourceID = -1;
                    }
                    else
                    {
                        return; // unrecognized source — skip to avoid ambient/music/UI sounds
                    }
                }
            }

            CurrentRecording.SoundEvents.Add(new SoundEvent
            {
                Time = Time.realtimeSinceStartup - _startTime,
                EventName = eventName,
                SourceKind = kind,
                SourceID = sourceID,
            });
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
            GameEventManager.ChangeListener<PiecePlacedEvent>(this, false);
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
            float elapsed = Time.realtimeSinceStartup - _startTime;

            if (_gameControl != null && _gameControl.Phase != _lastCapturedPhase)
            {
                CurrentRecording.PhaseEvents.Add(new PhaseEvent
                {
                    Time = elapsed,
                    Phase = _gameControl.Phase.ToString()
                });
                Plugin.Logger.LogInfo(
                    $"[Recorder:Phase] t={elapsed:F2} phase={_gameControl.Phase}");
                _lastCapturedPhase = _gameControl.Phase;
            }

            // Close-side: poll for visibility going false (open side is event-driven via patches).
            if (_trackedBox != null)
            {
                bool currentVisible = _trackedBox.Visible;
                if (!currentVisible && _lastBoxVisible)
                {
                    _currentOpenBoxEvent = null;
                    CurrentRecording.PartyBoxEvents.Add(new PartyBoxVisibilityEvent
                    {
                        Time = elapsed,
                        Opened = false,
                        IsExtraBox = false,
                    });
                    Plugin.Logger.LogInfo($"[Recorder:Box] t={elapsed:F2} closed");
                    _lastBoxVisible = false;
                }
            }

            CaptureFrame(elapsed);
        }

        private void CaptureFrame(float elapsed)
        {
            int count = _tracked.Count;
            var frame = new RecordingFrame
            {
                Time = elapsed,
                Characters = new List<CharacterSnapshot>(count)
            };

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

            frame.Cursors = new List<CursorSnapshot>(_trackedCursors.Count);
            for (int i = 0; i < _trackedCursors.Count; i++)
            {
                var tc = _trackedCursors[i];
                if (tc.Cursor == null) continue;
                frame.Cursors.Add(new CursorSnapshot
                {
                    NetworkNumber = tc.NetworkNumber,
                    PosX = tc.Transform.position.x,
                    PosY = tc.Transform.position.y,
                    Visible = tc.Cursor.Enabled
                });
            }

            frame.PickCursors = new List<PickCursorSnapshot>(_trackedPickCursors.Count);
            for (int i = 0; i < _trackedPickCursors.Count; i++)
            {
                var c = _trackedPickCursors[i];
                if (c.Cursor == null) continue;
                frame.PickCursors.Add(new PickCursorSnapshot
                {
                    NetworkNumber = c.NetworkNumber,
                    PosX = c.Transform.position.x,
                    PosY = c.Transform.position.y,
                    Visible = c.Cursor.Enabled
                });
            }

            foreach (var tc in _trackedCursors)
            {
                if (tc.Cursor == null) continue;
                var ppc = tc.Cursor as PiecePlacementCursor;
                if (ppc == null) continue;

                int lastID = _cursorLastHeldPieceID.TryGetValue(tc.NetworkNumber, out int li) ? li : 0;
                int currentID = ppc.Piece != null ? ppc.Piece.ID : 0;

                if (currentID != lastID)
                {
                    if (currentID != 0)
                    {
                        int blockIdx = PlaceableMetadataList.Instance != null
                            ? PlaceableMetadataList.Instance.GetIndexForPlaceable(ppc.Piece.Name)
                            : -1;
                        CurrentRecording.ItemPickupEvents.Add(new ItemPickupEvent
                        {
                            Time = elapsed,
                            CursorNetNum = tc.NetworkNumber,
                            BlockIndex = blockIdx,
                            PieceID = currentID
                        });
                        Plugin.Logger.LogInfo($"[Recorder:Item] t={elapsed:F2} pickup cursor={tc.NetworkNumber} piece={currentID} block={blockIdx}");
                    }
                    _cursorLastHeldPieceID[tc.NetworkNumber] = currentID;
                }

                if (currentID != 0 && ppc.Piece != null)
                {
                    frame.ItemStates.Add(new ItemStateSnapshot
                    {
                        PieceID = currentID,
                        PosX = ppc.Piece.transform.position.x,
                        PosY = ppc.Piece.transform.position.y,
                        RotZ = ppc.Piece.transform.rotation.eulerAngles.z,
                        ScaleX = ppc.Piece.transform.localScale.x,
                        ScaleY = ppc.Piece.transform.localScale.y
                    });
                }
            }

            CurrentRecording.Frames.Add(frame);
        }
    }
}
