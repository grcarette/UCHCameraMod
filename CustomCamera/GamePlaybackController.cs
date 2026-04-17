using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace UCHCameraMod
{
    public class GamePlaybackController : MonoBehaviour
    {
        public static GamePlaybackController Instance { get; private set; }

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public float CurrentTime { get; private set; }
        public float Duration => _recording?.Duration ?? 0f;
        public float Progress => Duration > 0 ? CurrentTime / Duration : 0f;

        private Recording _recording;
        private Dictionary<int, Character> _characterMap = new Dictionary<int, Character>();
        private Dictionary<int, Rigidbody2D> _rbMap = new Dictionary<int, Rigidbody2D>();
        private Dictionary<int, Animator> _animMap = new Dictionary<int, Animator>();
        private Dictionary<int, Dictionary<string, AnimationClip>> _clipMap
            = new Dictionary<int, Dictionary<string, AnimationClip>>();
        private Dictionary<int, Vector3> _originalSpriteScale = new Dictionary<int, Vector3>();
        private string _lastLoggedClip = "";
        private List<GameObject> _reconstructedObjects = new List<GameObject>();
        private List<GameObject> _spawnedCharacters = new List<GameObject>();
        private List<GameObject> _hiddenLobbyObjects = new List<GameObject>();
        private List<MonoBehaviour> _disabledControllers = new List<MonoBehaviour>();
        private bool _standaloneReplay;
        private bool _sceneLoaded;
        private string _previousScene;

        private void Awake()
        {
            Instance = this;
        }

        public void Load(Recording recording)
        {
            Plugin.Logger.LogInfo($"[Load] called. recording={(recording != null ? recording.Name : "null")} " +
                                  $"frames={(recording?.Frames.Count ?? -1)}");
            _recording = recording;
            CurrentTime = 0f;
        }

        public void Play()
        {
            Plugin.Logger.LogInfo($"[Play] called. _recording={(_recording != null ? "set" : "null")} " +
                                  $"frames={(_recording?.Frames.Count ?? -1)} " +
                                  $"IsPaused={IsPaused} IsPlaying={IsPlaying}");

            if (_recording == null || _recording.Frames.Count == 0)
            {
                Plugin.Logger.LogInfo("[Play] early return: no recording loaded");
                return;
            }

            if (IsPaused)
            {
                Plugin.Logger.LogInfo("[Play] resuming from pause");
                IsPaused = false;
                IsPlaying = true;
                return;
            }

            Plugin.Logger.LogInfo($"[Play] Starting playback: " +
                                  $"Frames={_recording.Frames.Count} " +
                                  $"Duration={_recording.Duration:F1}s " +
                                  $"SceneData={_recording.Scene?.Placeables.Count ?? 0} pieces " +
                                  $"Players={_recording.Metadata?.Players.Count ?? 0}");

            bool hasSceneName = _recording.Metadata != null
                             && !string.IsNullOrEmpty(_recording.Metadata.SceneName)
                             && _recording.Metadata.SceneName != "unknown";

            if (hasSceneName)
            {
                _standaloneReplay = true;
                Plugin.Logger.LogInfo("[Play] Mode: STANDALONE (scene load + snapshot)");
                StartCoroutine(PlayStandaloneCoroutine());
            }
            else
            {
                _standaloneReplay = false;
                Plugin.Logger.LogInfo("[Play] Mode: IN-GAME (using existing scene)");
                MapCharacters();
                StartPlayback();
            }
        }

        private System.Collections.IEnumerator PlayStandaloneCoroutine()
        {
            Plugin.Logger.LogInfo("[Play] Starting standalone replay coroutine...");

            // Step 1: Load the level scene
            yield return StartCoroutine(LoadReplayScene(_recording.Metadata.SceneName));

            // Step 2: Wait for scene objects to initialize
            yield return null;
            yield return null;

            // Step 3: Place initial level placeables (set pieces from the scene)
            foreach (Placeable p in Placeable.AllPlaceables)
            {
                if (p != null && !p.Placed)
                    p.Place(0, false, true);
            }
            yield return null;

            // Step 4: Load snapshot to place player-built pieces
            if (_recording.SnapshotBytes != null && _recording.SnapshotBytes.Length > 0)
                LoadSnapshotStandalone(_recording.SnapshotBytes);

            // Step 5: Spawn and map characters
            SpawnReplayCharacters(_recording);
            MapSpawnedCharacters();

            // Step 6: Camera
            SetupReplayCamera(_recording);

            // Step 7: Start playback
            StartPlayback();
        }

        private void StartPlayback()
        {
            _originalSpriteScale.Clear();
            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                    _originalSpriteScale[kvp.Key] = kvp.Value.transform.localScale;
            }

            foreach (var kvp in _rbMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.bodyType = RigidbodyType2D.Kinematic;
                    kvp.Value.velocity = Vector2.zero;
                    kvp.Value.angularVelocity = 0f;
                }
            }

            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                    kvp.Value.enabled = false;
            }

            if (_standaloneReplay)
            {
                foreach (var kvp in _characterMap)
                {
                    if (kvp.Value != null)
                        kvp.Value.gameObject.SetActive(true);
                }
            }

            ApplyFrame(0f);
            IsPlaying = true;
            IsPaused = false;
            CurrentTime = 0f;
            Plugin.Logger.LogInfo("[Play] Playback started");
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPaused = true;
            IsPlaying = false;
        }

        public void Stop()
        {
            Plugin.Logger.LogInfo($"[Stop] Stopping playback. Standalone={_standaloneReplay}");
            IsPlaying = false;
            IsPaused = false;
            CurrentTime = 0f;

            // Restore Sprite child scales to undo any flip we applied
            foreach (var kvp in _originalSpriteScale)
            {
                if (_animMap.TryGetValue(kvp.Key, out Animator anim) && anim != null)
                    anim.transform.localScale = kvp.Value;
            }
            _originalSpriteScale.Clear();

            // Restore physics
            foreach (var kvp in _rbMap)
            {
                if (kvp.Value != null)
                    kvp.Value.bodyType = RigidbodyType2D.Dynamic;
            }

            // Restore animator control to the game
            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.enabled = true;
                    kvp.Value.speed = 1f;
                }
            }

            if (_standaloneReplay)
            {
                DestroyReplayCharacters();
                DestroyReconstructedScene();
                StartCoroutine(UnloadReplayScene());
                _standaloneReplay = false;
            }
        }

        public void Seek(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);
            ApplyFrame(CurrentTime);
        }

        private void Update()
        {
            if (!IsPlaying || _recording == null) return;

            CurrentTime += Time.deltaTime;

            if (CurrentTime >= Duration)
            {
                ApplyFrame(Duration);
                Stop();
                return;
            }

            ApplyFrame(CurrentTime);
        }

        // ── Frame Application ────────────────────────────────────────

        private void ApplyFrame(float time)
        {
            if (_recording == null || _recording.Frames.Count == 0) return;

            // Find the two frames to interpolate between
            int frameA = 0;
            int frameB = 0;

            for (int i = 0; i < _recording.Frames.Count - 1; i++)
            {
                if (_recording.Frames[i + 1].Time >= time)
                {
                    frameA = i;
                    frameB = i + 1;
                    break;
                }
                frameA = i;
                frameB = i;
            }

            if (frameA == frameB)
            {
                // Exact frame or past end — apply directly
                ApplySnapshotsDirectly(_recording.Frames[frameA]);
                return;
            }

            // Interpolate
            float frameATime = _recording.Frames[frameA].Time;
            float frameBTime = _recording.Frames[frameB].Time;
            float t = (time - frameATime) / (frameBTime - frameATime);

            ApplySnapshotsInterpolated(_recording.Frames[frameA], _recording.Frames[frameB], t);
        }

        private void ApplySnapshotsDirectly(RecordingFrame frame)
        {
            foreach (var snap in frame.Characters)
            {
                if (!_characterMap.TryGetValue(snap.NetworkNumber, out Character c)) continue;
                if (c == null) continue;

                c.gameObject.SetActive(snap.Visible);
                if (!snap.Visible) continue;

                c.transform.position = new Vector3(snap.PosX, snap.PosY, c.transform.position.z);
                c.transform.localScale = new Vector3(Mathf.Abs(snap.ScaleX), snap.ScaleY, c.transform.localScale.z);
                c.transform.rotation = Quaternion.Euler(0, 0, snap.Rotation);
                ApplyFlipToSprite(snap.NetworkNumber, snap.FlipSpriteX);

                ApplyAnimation(c, snap);
            }
        }

        private void ApplySnapshotsInterpolated(RecordingFrame frameA, RecordingFrame frameB, float t)
        {
            // Build lookup for frameB
            var frameBLookup = new Dictionary<int, CharacterSnapshot>();
            foreach (var snap in frameB.Characters)
                frameBLookup[snap.NetworkNumber] = snap;

            foreach (var snapA in frameA.Characters)
            {
                if (!_characterMap.TryGetValue(snapA.NetworkNumber, out Character c)) continue;
                if (c == null) continue;

                if (!frameBLookup.TryGetValue(snapA.NetworkNumber, out CharacterSnapshot snapB))
                {
                    // Character not in next frame, apply A directly
                    ApplySingleSnapshot(c, snapA);
                    continue;
                }

                // Visibility: use A's state for first half, B's for second
                bool visible = t < 0.5f ? snapA.Visible : snapB.Visible;
                c.gameObject.SetActive(visible);
                if (!visible) continue;

                // Interpolate position
                float px = Mathf.Lerp(snapA.PosX, snapB.PosX, t);
                float py = Mathf.Lerp(snapA.PosY, snapB.PosY, t);
                c.transform.position = new Vector3(px, py, c.transform.position.z);

                // Scale: snap (don't lerp flips); flip applied to Sprite child only
                float activeScaleX = t < 0.5f ? snapA.ScaleX : snapB.ScaleX;
                c.transform.localScale = new Vector3(
                    Mathf.Abs(activeScaleX),
                    t < 0.5f ? snapA.ScaleY : snapB.ScaleY,
                    c.transform.localScale.z);
                float activeFlip = t < 0.5f ? snapA.FlipSpriteX : snapB.FlipSpriteX;
                ApplyFlipToSprite(snapA.NetworkNumber, activeFlip);

                // Rotation: lerp
                float rot = Mathf.LerpAngle(snapA.Rotation, snapB.Rotation, t);
                c.transform.rotation = Quaternion.Euler(0, 0, rot);

                // Animation: use whichever frame we're closer to
                ApplyAnimation(c, t < 0.5f ? snapA : snapB);
            }
        }

        private void ApplySingleSnapshot(Character c, CharacterSnapshot snap)
        {
            c.gameObject.SetActive(snap.Visible);
            if (!snap.Visible) return;
            c.transform.position = new Vector3(snap.PosX, snap.PosY, c.transform.position.z);
            c.transform.localScale = new Vector3(Mathf.Abs(snap.ScaleX), snap.ScaleY, c.transform.localScale.z);
            c.transform.rotation = Quaternion.Euler(0, 0, snap.Rotation);
            ApplyFlipToSprite(snap.NetworkNumber, snap.FlipSpriteX);
            ApplyAnimation(c, snap);
        }

        private void ApplyFlipToSprite(int netNum, float flipSpriteX)
        {
            if (flipSpriteX == 0f) flipSpriteX = 1f;   // backward compat: old recordings default to no flip
            if (!_animMap.TryGetValue(netNum, out Animator anim) || anim == null) return;

            var spriteTransform = anim.gameObject.transform;
            var s = spriteTransform.localScale;
            spriteTransform.localScale = new Vector3(
                Mathf.Abs(s.x) * flipSpriteX,
                s.y,
                s.z);
        }

        private void ApplyAnimation(Character c, CharacterSnapshot snap)
        {
            if (string.IsNullOrEmpty(snap.AnimationState)) return;
            if (!_clipMap.TryGetValue(snap.NetworkNumber, out var lookup)) return;
            if (!lookup.TryGetValue(snap.AnimationState, out AnimationClip clip)) return;
            if (clip == null) return;

            if (!_animMap.TryGetValue(snap.NetworkNumber, out Animator anim)) return;
            if (anim == null) return;

            // normalizedTime may be cumulative (e.g. 54.05 = looped 54 times + 5%)
            float normalized = snap.AnimationTime % 1f;
            if (normalized < 0f) normalized += 1f;
            float sampleTime = normalized * clip.length;

            clip.SampleAnimation(anim.gameObject, sampleTime);

            if (snap.AnimationState != _lastLoggedClip)
            {
                Plugin.Logger.LogInfo($"[Sample] clip={snap.AnimationState} len={clip.length:F2} " +
                                      $"normT={normalized:F3} sampleT={sampleTime:F3}");
                _lastLoggedClip = snap.AnimationState;
            }
        }

        private void BuildClipLookup(int netNum, Animator anim)
        {
            var lookup = new Dictionary<string, AnimationClip>();
            if (anim.runtimeAnimatorController != null)
            {
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && !lookup.ContainsKey(clip.name))
                        lookup[clip.name] = clip;
                }
            }
            _clipMap[netNum] = lookup;
            Plugin.Logger.LogInfo($"[BuildClipLookup] Built lookup for P{netNum} with {lookup.Count} clips");
        }

        // ── Scene Reconstruction ─────────────────────────────────────

        public System.Collections.IEnumerator LoadReplayScene(string sceneName)
        {
            _previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Plugin.Logger.LogInfo($"[ReplayScene] Loading scene: {sceneName} (from: {_previousScene})");

            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);

            if (op == null)
            {
                Plugin.Logger.LogError($"[ReplayScene] Failed to start loading scene: {sceneName}");
                yield break;
            }

            while (!op.isDone)
                yield return null;

            Plugin.Logger.LogInfo($"[ReplayScene] Scene loaded: {sceneName}");

            HideLobbyScene();
            DisableLevelControllers();
            _sceneLoaded = true;
        }

        public System.Collections.IEnumerator UnloadReplayScene()
        {
            if (!_sceneLoaded) yield break;

            string sceneName = _recording?.Metadata?.SceneName;
            if (string.IsNullOrEmpty(sceneName)) yield break;

            Plugin.Logger.LogInfo($"[ReplayScene] Unloading scene: {sceneName}");

            RestoreLevelControllers();

            var op = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(sceneName);
            if (op != null)
            {
                while (!op.isDone)
                    yield return null;
            }

            RestoreLobbyScene();
            _sceneLoaded = false;
            Plugin.Logger.LogInfo("[ReplayScene] Scene unloaded, lobby restored");
        }

        private void DisableLevelControllers()
        {
            foreach (var gc in FindObjectsOfType<GameControl>())
            {
                if (gc != null && gc.enabled)
                {
                    gc.enabled = false;
                    _disabledControllers.Add(gc);
                    Plugin.Logger.LogInfo($"[ReplayScene] Disabled GameControl: {gc.name}");
                }
            }

            foreach (var zc in FindObjectsOfType<ZoomCamera>())
            {
                if (zc != null && zc != CameraModController.Instance?.Zoom)
                {
                    zc.enabled = false;
                    _disabledControllers.Add(zc);
                    Plugin.Logger.LogInfo($"[ReplayScene] Disabled ZoomCamera: {zc.name}");
                }
            }
        }

        private void RestoreLevelControllers()
        {
            foreach (var mb in _disabledControllers)
            {
                if (mb != null)
                    mb.enabled = true;
            }
            _disabledControllers.Clear();
        }

        public System.Collections.IEnumerator ReconstructSceneCoroutine(Recording recording)
        {
            yield return null;
            yield return null;

            foreach (Placeable p in Placeable.AllPlaceables)
            {
                if (p != null && !p.Placed)
                    p.Place(0, false, true);
            }

            yield return null;

            ReconstructPlayerPieces(recording);
        }

        private void ReconstructPlayerPieces(Recording recording)
        {
            if (recording.Scene == null || recording.Scene.Placeables.Count == 0)
            {
                Plugin.Logger.LogInfo("[Reconstruct] No player-placed pieces in recording");
                return;
            }

            var metaList = PlaceableMetadataList.Instance;
            Plugin.Logger.LogInfo($"[Reconstruct] PlaceableMetadataList available: {metaList != null}");
            if (metaList == null)
            {
                Plugin.Logger.LogWarning("[Reconstruct] PlaceableMetadataList not available");
                return;
            }
            Plugin.Logger.LogInfo($"[Reconstruct] Block prefab count: {metaList.AllBlockListLength()}");

            var idToObject = new Dictionary<int, Placeable>();

            foreach (var snap in recording.Scene.Placeables)
            {
                Object prefab = null;
                if (snap.BlockIndex >= 0)
                    prefab = metaList.GetPrefabForPlaceableIndex(snap.BlockIndex);
                if (prefab == null && !string.IsNullOrEmpty(snap.Name))
                {
                    int idx = metaList.GetIndexForPlaceable(snap.Name);
                    if (idx >= 0)
                    {
                        prefab = metaList.GetPrefabForPlaceableIndex(idx);
                        if (Plugin.CfgVerboseReplayLog.Value)
                            Plugin.Logger.LogInfo($"[Reconstruct]   Fallback name lookup succeeded for: {snap.Name} " +
                                                  $"(resolved to idx={idx})");
                    }
                }
                if (prefab == null)
                {
                    Plugin.Logger.LogWarning($"[Reconstruct]   FAILED: {snap.Name} (idx={snap.BlockIndex})");
                    continue;
                }

                GameObject obj = Object.Instantiate((GameObject)prefab);
                obj.transform.position = new Vector3(snap.PosX, snap.PosY, 0f);
                obj.transform.rotation = Quaternion.Euler(0f, 0f, snap.Rotation);
                obj.transform.localScale = new Vector3(snap.ScaleX, snap.ScaleY, 1f);

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[Reconstruct]   Instantiated: {snap.Name} " +
                                          $"(ID={snap.ID} Idx={snap.BlockIndex}) at ({snap.PosX:F2},{snap.PosY:F2})");

                Placeable placed = obj.GetComponent<Placeable>();
                if (placed != null)
                {
                    placed.ID = snap.ID;
                    placed.Place(snap.PlacedByPlayer, false, true);

                    if (snap.DamageLevel > 0)
                        placed.SetInitialDamageLevel(snap.DamageLevel, false);

                    if (!string.IsNullOrEmpty(snap.CustomColorHex))
                    {
                        Color col;
                        if (ColorUtility.TryParseHtmlString("#" + snap.CustomColorHex, out col))
                            placed.SetColor(col);
                    }

                    placed.EnablePlaced();
                    idToObject[snap.ID] = placed;
                }

                _reconstructedObjects.Add(obj);
            }

            foreach (var snap in recording.Scene.Placeables)
            {
                if (snap.ParentID < 0) continue;
                if (!idToObject.TryGetValue(snap.ID, out Placeable child))
                {
                    Plugin.Logger.LogWarning($"[Reconstruct]   Child not found for linking: ID={snap.ID}");
                    continue;
                }
                if (!idToObject.TryGetValue(snap.ParentID, out Placeable parent))
                {
                    Plugin.Logger.LogWarning($"[Reconstruct]   Parent not found: child ID={snap.ID} " +
                                             $"wanted parent ID={snap.ParentID}");
                    continue;
                }
                parent.AttachPiece(child);
                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[Reconstruct]   Linked child ID={snap.ID} to parent ID={snap.ParentID}");
            }

            Plugin.Logger.LogInfo($"[Reconstruct] Placed {_reconstructedObjects.Count} player pieces");
        }

        public void DestroyReconstructedScene()
        {
            Plugin.Logger.LogInfo($"[Reconstruct] Destroying {_reconstructedObjects.Count} reconstructed objects");
            foreach (var obj in _reconstructedObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _reconstructedObjects.Clear();
        }

        private class SnapshotPiece
        {
            public int SceneID;
            public int BlockID;
            public int PlaceableID = -1;
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Scale;
            public int ParentSceneID = -1;
            public string ParentAttachmentPoint;
            public string ParentPath;
            public Vector3 RelativeAttachPos;
            public int MainSceneID = -1;
            public string SubElementName;
            public string OverrideName;
            public bool Clockwise;
            public Color CustomColor;
            public int DamageLevel;
            public Placeable Placeable;
        }

        private void LoadSnapshotStandalone(byte[] compressedBytes)
        {
            Plugin.Logger.LogInfo("[Snapshot] Loading snapshot standalone...");

            string xml = QuickSaver.GetXmlStringFromBytes(compressedBytes);
            if (xml == null)
            {
                Plugin.Logger.LogError("[Snapshot] Failed to decompress snapshot bytes");
                return;
            }

            XmlDocument doc = new XmlDocument();
            try { doc.LoadXml(xml); }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"[Snapshot] Failed to parse snapshot XML: {ex.Message}");
                return;
            }

            var root = doc.DocumentElement;
            string sceneName = root.Attributes["levelSceneName"]?.Value ?? "";
            Plugin.Logger.LogInfo($"[Snapshot] Snapshot scene: {sceneName}");

            var metaList = PlaceableMetadataList.Instance;
            if (metaList == null)
            {
                Plugin.Logger.LogError("[Snapshot] PlaceableMetadataList not available");
                return;
            }

            var pieces = new Dictionary<int, SnapshotPiece>();

            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.Name != "block") continue;

                int sceneID = QuickSaver.ParseAttrID(node, "sceneID", -1);
                if (sceneID == -1) continue;

                var piece = new SnapshotPiece
                {
                    SceneID = sceneID,
                    BlockID = QuickSaver.ParseAttrID(node, "blockID", -1),
                    PlaceableID = QuickSaver.ParseAttrID(node, "placeableID", -1),
                    Pos = new Vector3(
                        QuickSaver.ParseAttrFloat(node, "pX", 0f),
                        QuickSaver.ParseAttrFloat(node, "pY", 0f),
                        QuickSaver.ParseAttrFloat(node, "pZ", 0f)),
                    Rot = Quaternion.Euler(
                        QuickSaver.ParseAttrFloat(node, "rX", 0f),
                        QuickSaver.ParseAttrFloat(node, "rY", 0f),
                        QuickSaver.ParseAttrFloat(node, "rZ", 0f)),
                    Scale = new Vector3(
                        QuickSaver.ParseAttrFloat(node, "sX", 1f),
                        QuickSaver.ParseAttrFloat(node, "sY", 1f),
                        QuickSaver.ParseAttrFloat(node, "sZ", 1f)),
                    ParentAttachmentPoint = QuickSaver.ParseAttrStr(node, "parentAttachmentPoint", ""),
                    ParentPath = QuickSaver.ParseAttrStr(node, "parentPath", ""),
                    RelativeAttachPos = new Vector3(
                        QuickSaver.ParseAttrFloat(node, "relX", 0f),
                        QuickSaver.ParseAttrFloat(node, "relY", 0f),
                        QuickSaver.ParseAttrFloat(node, "relZ", 0f)),
                    SubElementName = QuickSaver.ParseAttrStr(node, "subElementName", ""),
                    OverrideName = QuickSaver.ParseAttrStr(node, "overrideName", ""),
                    Clockwise = QuickSaver.ParseAttrInt(node, "clockwise", 0) == 1,
                    CustomColor = new Color(
                        QuickSaver.ParseAttrFloat(node, "colR", 0f),
                        QuickSaver.ParseAttrFloat(node, "colG", 0f),
                        QuickSaver.ParseAttrFloat(node, "colB", 0f)),
                    DamageLevel = QuickSaver.ParseAttrInt(node, "damageLevel", 0)
                };

                pieces[sceneID] = piece;

                int parentID = QuickSaver.ParseAttrID(node, "parentID", -1);
                if (parentID != -1)
                {
                    piece.ParentSceneID = parentID;
                }

                int mainID = QuickSaver.ParseAttrID(node, "mainID", -1);
                if (mainID != -1)
                    piece.MainSceneID = mainID;
            }

            Plugin.Logger.LogInfo($"[Snapshot] Parsed {pieces.Count} blocks from snapshot");

            // Handle "moved" nodes — modify existing level geometry
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.Name != "moved") continue;

                string path = QuickSaver.ParseAttrStr(node, "path", "");
                if (string.IsNullOrEmpty(path)) continue;

                Transform t = QuickSaver.GetTransformFromHierarchyPath(path);
                if (t == null)
                {
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogWarning($"[Snapshot] Could not find moved object at: {path}");
                    continue;
                }

                t.position = new Vector3(
                    QuickSaver.ParseAttrFloat(node, "pX", 0f),
                    QuickSaver.ParseAttrFloat(node, "pY", 0f),
                    QuickSaver.ParseAttrFloat(node, "pZ", 0f));
                t.rotation = Quaternion.Euler(
                    QuickSaver.ParseAttrFloat(node, "rX", 0f),
                    QuickSaver.ParseAttrFloat(node, "rY", 0f),
                    QuickSaver.ParseAttrFloat(node, "rZ", 0f));
                t.localScale = new Vector3(
                    QuickSaver.ParseAttrFloat(node, "sX", 1f),
                    QuickSaver.ParseAttrFloat(node, "sY", 1f),
                    QuickSaver.ParseAttrFloat(node, "sZ", 1f));

                Placeable mp = t.GetComponent<Placeable>();
                if (mp != null)
                {
                    mp.OriginalPosition = t.position;
                    mp.OriginalRotation = t.rotation;
                    mp.OriginalScale = t.localScale;

                    int dmg = QuickSaver.ParseAttrInt(node, "damageLevel", 0);
                    if (dmg > 0) mp.SetInitialDamageLevel(dmg, true);
                }

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[Snapshot]   Moved: {path}");
            }

            // Handle "destroyed" nodes
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node.Name != "destroyed") continue;
                string path = QuickSaver.ParseAttrStr(node, "path", "");
                if (string.IsNullOrEmpty(path)) continue;

                Transform t = QuickSaver.GetTransformFromHierarchyPath(path);
                if (t != null)
                {
                    Placeable dp = t.GetComponent<Placeable>();
                    if (dp != null && !dp.MarkedForDestruction)
                    {
                        dp.DestroySelf(false, false, false);
                        if (Plugin.CfgVerboseReplayLog.Value)
                            Plugin.Logger.LogInfo($"[Snapshot]   Destroyed: {path}");
                    }
                }
            }

            // Pass 1: Instantiate non-sub-element blocks
            int maxSeqID = 0;

            foreach (var kvp in pieces)
            {
                var piece = kvp.Value;
                if (!string.IsNullOrEmpty(piece.SubElementName)) continue;

                UnityEngine.Object prefab = metaList.GetPrefabForPlaceableIndex(piece.BlockID);
                if (prefab == null)
                {
                    Plugin.Logger.LogWarning($"[Snapshot] No prefab for blockID={piece.BlockID} (sceneID={piece.SceneID})");
                    continue;
                }

                GameObject obj = Object.Instantiate((GameObject)prefab);
                obj.transform.position = piece.Pos;

                Vector3 euler = piece.Rot.eulerAngles;
                euler.z = Mathf.Round(euler.z / 90f) * 90f;
                obj.transform.rotation = Quaternion.Euler(euler);
                obj.transform.localScale = piece.Scale;

                Placeable placed = obj.GetComponent<PlaceableMetadata>()?.placeableRef
                                ?? obj.GetComponent<Placeable>();

                if (placed != null)
                {
                    if (piece.PlaceableID != -1)
                    {
                        placed.ID = piece.PlaceableID;
                        maxSeqID = Mathf.Max(placed.GetOriginalSequenceID(), maxSeqID);
                    }

                    if (!string.IsNullOrEmpty(piece.OverrideName))
                        placed.gameObject.name = piece.OverrideName;

                    placed.Place(0, false, true);

                    if (placed.canSetCustomColor && piece.CustomColor != Color.black)
                        placed.SetColor(piece.CustomColor);

                    if (piece.DamageLevel > 0)
                        placed.SetInitialDamageLevel(piece.DamageLevel, true);

                    if (placed.RotationDirection != Placeable.RotationDirections.None)
                        placed.RotationDirection = piece.Clockwise
                            ? Placeable.RotationDirections.Clockwise
                            : Placeable.RotationDirections.CounterClockwise;

                    piece.Placeable = placed;
                }

                _reconstructedObjects.Add(obj);

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[Snapshot]   Placed: blockID={piece.BlockID} " +
                                          $"sceneID={piece.SceneID} at ({piece.Pos.x:F1},{piece.Pos.y:F1})");
            }

            // Pass 2: Link sub-elements to their main blocks
            foreach (var kvp in pieces)
            {
                var piece = kvp.Value;
                if (string.IsNullOrEmpty(piece.SubElementName)) continue;
                if (piece.MainSceneID == -1) continue;

                if (!pieces.TryGetValue(piece.MainSceneID, out var mainPiece) || mainPiece.Placeable == null)
                {
                    Plugin.Logger.LogWarning($"[Snapshot] Main block not found for sub-element sceneID={piece.SceneID}");
                    continue;
                }

                var mainMeta = mainPiece.Placeable.GetComponent<PlaceableMetadata>();
                if (mainMeta == null) continue;

                foreach (var sub in mainMeta.subElements)
                {
                    if (sub.name == piece.SubElementName)
                    {
                        piece.Placeable = sub;
                        if (piece.PlaceableID != -1)
                        {
                            sub.ID = piece.PlaceableID;
                            maxSeqID = Mathf.Max(sub.GetOriginalSequenceID(), maxSeqID);
                        }
                        sub.transform.position = piece.Pos;
                        var subEuler = piece.Rot.eulerAngles;
                        subEuler.z = Mathf.Round(subEuler.z / 90f) * 90f;
                        sub.transform.rotation = Quaternion.Euler(subEuler);
                        sub.transform.localScale = piece.Scale;

                        if (sub.canSetCustomColor && piece.CustomColor != Color.black)
                            sub.SetColor(piece.CustomColor);
                        break;
                    }
                }
            }

            // Pass 3: Parent-child attachment via parentPath
            foreach (var kvp in pieces)
            {
                var piece = kvp.Value;
                if (piece.Placeable == null) continue;

                if (!string.IsNullOrEmpty(piece.ParentPath))
                {
                    Transform parentT = QuickSaver.GetTransformFromHierarchyPath(piece.ParentPath);
                    if (parentT != null)
                    {
                        Placeable parentP = parentT.GetComponent<Placeable>();
                        if (parentP != null)
                            parentP.AttachPiece(piece.Placeable);
                        else
                            piece.Placeable.transform.SetParent(parentT, true);
                    }
                }
                else if (piece.ParentSceneID != -1)
                {
                    if (pieces.TryGetValue(piece.ParentSceneID, out var parentPiece) && parentPiece.Placeable != null)
                    {
                        if (!string.IsNullOrEmpty(piece.ParentAttachmentPoint))
                        {
                            var parentMeta = parentPiece.Placeable.GetComponent<PlaceableMetadata>();
                            if (parentMeta != null)
                            {
                                bool found = false;
                                foreach (var ap in parentMeta.attachmentPoints)
                                {
                                    if (ap.name == piece.ParentAttachmentPoint)
                                    {
                                        ap.AttachPiece(piece.Placeable);
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                    parentPiece.Placeable.AttachPiece(piece.Placeable);
                            }
                            else
                            {
                                parentPiece.Placeable.AttachPiece(piece.Placeable);
                            }
                        }
                        else
                        {
                            parentPiece.Placeable.AttachPiece(piece.Placeable);
                        }
                    }
                }
            }

            // Pass 4: Set relative attach positions
            foreach (var kvp in pieces)
            {
                var piece = kvp.Value;
                if (piece.Placeable != null && !string.IsNullOrEmpty(piece.ParentPath))
                {
                    piece.Placeable.transform.localPosition = piece.RelativeAttachPos;
                    piece.Placeable.OriginalPosition = piece.Placeable.transform.position;
                    piece.Placeable.relativeAttachPosition = piece.RelativeAttachPos;
                }
            }

            // Pass 5: Enable all placed pieces
            foreach (var kvp in pieces)
            {
                if (kvp.Value.Placeable != null && !kvp.Value.Placeable.MarkedForDestruction)
                    kvp.Value.Placeable.EnablePlaced();
            }

            Placeable.SetInitialSequenceID(maxSeqID);

            Plugin.Logger.LogInfo($"[Snapshot] Reconstruction complete: {_reconstructedObjects.Count} objects");
        }

        public void SpawnReplayCharacters(Recording recording)
        {
            DestroyReplayCharacters();

            Character charPrefab = null;

            var gc = Object.FindObjectOfType<GameControl>();
            if (gc != null && gc.CharacterPrefab != null)
                charPrefab = gc.CharacterPrefab;

            if (charPrefab == null)
                charPrefab = Plugin.CachedCharacterPrefab;

            Plugin.Logger.LogInfo($"[SpawnChars] Spawning characters for {recording.Metadata.Players.Count} players");
            Plugin.Logger.LogInfo($"[SpawnChars] Prefab source: " +
                                  $"{(gc != null ? "GameControl" : (Plugin.CachedCharacterPrefab != null ? "Cached" : "NONE"))}");

            if (charPrefab == null)
            {
                Plugin.Logger.LogWarning("Cannot spawn characters — no CharacterPrefab available. " +
                                         "Play at least one game first to cache the prefab.");
                return;
            }

            foreach (var pi in recording.Metadata.Players)
            {
                Character.Animals animal = Character.Animals.NONE;
                if (!string.IsNullOrEmpty(pi.Animal))
                    System.Enum.TryParse(pi.Animal, out animal);

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[SpawnChars]   P{pi.NetworkNumber}: Animal={pi.Animal} " +
                                          $"Name={pi.Name} HasOutfits={pi.Outfits != null && pi.Outfits.Length > 0} " +
                                          $"IsWearingSkin={pi.IsWearingSkin}");

                if (animal == Character.Animals.NONE)
                {
                    Plugin.Logger.LogWarning($"[SpawnChars]   Skipping P{pi.NetworkNumber}: Animal is NONE");
                    continue;
                }

                Character c = Object.Instantiate(charPrefab);
                c.gameObject.name = animal.ToString();
                c.NetworkCharacterSprite = animal;
                c.NetworknetworkNumber = pi.NetworkNumber;

                if (pi.Outfits != null && pi.Outfits.Length > 0)
                    c.SetOutfitsFromArray(pi.Outfits);

                c.Enable(false);
                c.Disable(true);

                _spawnedCharacters.Add(c.gameObject);
            }

            Plugin.Logger.LogInfo($"[SpawnChars] Spawned {_spawnedCharacters.Count} characters");
        }

        public void DestroyReplayCharacters()
        {
            Plugin.Logger.LogInfo($"[SpawnChars] Destroying {_spawnedCharacters.Count} replay characters");
            foreach (var obj in _spawnedCharacters)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _spawnedCharacters.Clear();
        }

        private void MapSpawnedCharacters()
        {
            Plugin.Logger.LogInfo("[MapSpawned] Mapping spawned characters to recording data");
            Plugin.Logger.LogInfo($"[MapSpawned] Spawned objects: {_spawnedCharacters.Count}");

            _characterMap.Clear();
            _rbMap.Clear();
            _animMap.Clear();

            if (_recording == null || _recording.Frames.Count == 0) return;

            HashSet<int> recordedNumbers = new HashSet<int>();
            foreach (var snap in _recording.Frames[0].Characters)
                recordedNumbers.Add(snap.NetworkNumber);

            Plugin.Logger.LogInfo($"[MapSpawned] Recorded network numbers: {string.Join(",", recordedNumbers)}");

            foreach (var obj in _spawnedCharacters)
            {
                if (obj == null) continue;
                Character c = obj.GetComponent<Character>();
                if (c == null) continue;

                int netNum = c.networkNumber;
                if (!recordedNumbers.Contains(netNum))
                {
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogInfo($"[MapSpawned]   Skipped P{netNum}: not in recording");
                    continue;
                }

                _characterMap[netNum] = c;

                Rigidbody2D rb = c.GetComponent<Rigidbody2D>();
                if (rb != null) _rbMap[netNum] = rb;

                Animator anim = c.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    _animMap[netNum] = anim;
                    BuildClipLookup(netNum, anim);
                }

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[MapSpawned]   Mapped P{netNum}: " +
                                          $"Char={c.CharacterSprite} RB={rb != null} Anim={anim != null}");
            }

            Plugin.Logger.LogInfo($"[MapSpawned] Done: {_characterMap.Count} characters, " +
                                  $"{_animMap.Count} animators, {_rbMap.Count} rigidbodies");

            if (_characterMap.Count != recordedNumbers.Count)
                Plugin.Logger.LogWarning($"[MapSpawned] MISMATCH: recording has {recordedNumbers.Count} " +
                                         $"characters but only {_characterMap.Count} were mapped");
        }

        private void HideLobbyScene()
        {
            Plugin.Logger.LogInfo("[LobbyHide] Hiding lobby scene objects...");
            _hiddenLobbyObjects.Clear();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;

                string n = root.name;
                if (n == "UCHCameraModController" ||
                    n == "LobbyManager" ||
                    n == "NetworkManager" ||
                    n == "EventSystem" ||
                    n == "PlaceableMetadataList" ||
                    n.Contains("Canvas") ||
                    n.Contains("Camera"))
                {
                    if (Plugin.CfgVerboseReplayLog.Value)
                        Plugin.Logger.LogInfo($"[LobbyHide]   Kept: {n}");
                    continue;
                }

                root.SetActive(false);
                _hiddenLobbyObjects.Add(root);

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[LobbyHide]   Hidden: {n}");
            }

            Plugin.Logger.LogInfo($"[LobbyHide] Hidden {_hiddenLobbyObjects.Count} objects");
        }

        private void RestoreLobbyScene()
        {
            Plugin.Logger.LogInfo($"[LobbyHide] Restoring {_hiddenLobbyObjects.Count} lobby objects");
            int nullCount = 0;
            foreach (var obj in _hiddenLobbyObjects)
            {
                if (obj != null)
                    obj.SetActive(true);
                else
                    nullCount++;
            }
            if (nullCount > 0)
                Plugin.Logger.LogWarning($"[LobbyHide] {nullCount} objects were destroyed during replay");
            _hiddenLobbyObjects.Clear();

            Plugin.Logger.LogInfo("Lobby scene restored");
        }

        public void SetupReplayCamera(Recording recording)
        {
            var scene = recording.Scene;
            if (scene == null) return;

            Camera cam = CameraModController.Instance?.Cam;
            ZoomCamera zoom = CameraModController.Instance?.Zoom;

            Plugin.Logger.LogInfo($"[ReplayCam] Setting up camera: " +
                                  $"bounds=({scene.CameraBoundsMinX:F1},{scene.CameraBoundsMinY:F1})" +
                                  $" to ({scene.CameraBoundsMaxX:F1},{scene.CameraBoundsMaxY:F1})");
            Plugin.Logger.LogInfo($"[ReplayCam] Camera found: {cam != null}");
            Plugin.Logger.LogInfo($"[ReplayCam] ZoomCamera found: {zoom != null}");

            if (cam == null) return;

            float centerX = (scene.CameraBoundsMinX + scene.CameraBoundsMaxX) * 0.5f;
            float centerY = (scene.CameraBoundsMinY + scene.CameraBoundsMaxY) * 0.5f;
            cam.transform.position = new Vector3(centerX, centerY, cam.transform.position.z);

            if (zoom != null)
            {
                foreach (var obj in _spawnedCharacters)
                {
                    Character c = obj?.GetComponent<Character>();
                    if (c != null)
                    {
                        zoom.AddTarget(c);
                        if (Plugin.CfgVerboseReplayLog.Value)
                            Plugin.Logger.LogInfo($"[ReplayCam]   Added target: P{c.networkNumber} ({c.CharacterSprite})");
                    }
                }
            }
        }

        // ── Character Mapping ────────────────────────────────────────

        private void MapCharacters()
        {
            Plugin.Logger.LogInfo("[MapCharacters] entered");

            _characterMap.Clear();
            _rbMap.Clear();
            _animMap.Clear();

            if (_recording == null || _recording.Frames.Count == 0)
            {
                Plugin.Logger.LogInfo("[MapCharacters] no recording or no frames");
                return;
            }

            HashSet<int> recordedNumbers = new HashSet<int>();
            foreach (var snap in _recording.Frames[0].Characters)
                recordedNumbers.Add(snap.NetworkNumber);

            Plugin.Logger.LogInfo($"[MapCharacters] Recorded network numbers: {string.Join(",", recordedNumbers)}");

            var allPlayers = FindObjectsOfType<GamePlayer>();
            Plugin.Logger.LogInfo($"[MapCharacters] Found {allPlayers.Length} GamePlayer objects in scene");

            foreach (GamePlayer gp in allPlayers)
            {
                Plugin.Logger.LogInfo($"[MapCharacters] GP netNum={gp.networkNumber} " +
                                      $"charInst={(gp.CharacterInstance != null ? gp.CharacterInstance.name : "null")} " +
                                      $"inRecording={recordedNumbers.Contains(gp.networkNumber)}");

                if (gp.CharacterInstance != null && recordedNumbers.Contains(gp.networkNumber))
                {
                    _characterMap[gp.networkNumber] = gp.CharacterInstance;

                    Rigidbody2D rb = gp.CharacterInstance.GetComponent<Rigidbody2D>();
                    if (rb != null) _rbMap[gp.networkNumber] = rb;
                    Plugin.Logger.LogInfo($"[MapCharacters]   Rigidbody2D: {(rb != null ? "found" : "NOT FOUND")}");

                    Animator anim = gp.CharacterInstance.GetComponentInChildren<Animator>();
                    if (anim != null)
                    {
                        _animMap[gp.networkNumber] = anim;
                        BuildClipLookup(gp.networkNumber, anim);
                    }
                    Plugin.Logger.LogInfo($"[MapCharacters]   Animator: {(anim != null ? $"found on '{anim.gameObject.name}'" : "NOT FOUND")}");

                    if (anim == null)
                    {
                        Plugin.Logger.LogInfo($"[MapCharacters]   Hierarchy dump for {gp.CharacterInstance.name}:");
                        DumpHierarchy(gp.CharacterInstance.transform, 0);
                    }
                }
            }

            Plugin.Logger.LogInfo($"[MapCharacters] Done. Mapped {_characterMap.Count} characters, {_animMap.Count} animators, {_rbMap.Count} rigidbodies");
        }

        private void DumpHierarchy(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            var components = t.GetComponents<Component>();
            var compNames = new List<string>();
            foreach (var c in components)
                if (c != null) compNames.Add(c.GetType().Name);
            Plugin.Logger.LogInfo($"[MapCharacters]   {indent}{t.name} [{string.Join(",", compNames)}]");
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), depth + 1);
        }
    }
}
