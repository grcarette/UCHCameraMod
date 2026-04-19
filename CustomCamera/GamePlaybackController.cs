using GameEvent;
using System;
using System.Collections;
using System.Collections.Generic;
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
        public bool HasPendingReplay => _pendingReplayRecording != null;

        private Recording _recording;
        private Dictionary<int, Character> _characterMap = new Dictionary<int, Character>();
        private Dictionary<int, Rigidbody2D> _rbMap = new Dictionary<int, Rigidbody2D>();
        private Dictionary<int, Animator> _animMap = new Dictionary<int, Animator>();
        private Dictionary<int, Dictionary<string, AnimationClip>> _clipMap
            = new Dictionary<int, Dictionary<string, AnimationClip>>();
        private Dictionary<int, Vector3> _originalSpriteScale = new Dictionary<int, Vector3>();
        private List<GameObject> _spawnedCharacters = new List<GameObject>();
        private Dictionary<int, CharacterSnapshot> _frameBLookup = new Dictionary<int, CharacterSnapshot>();
        private bool _standaloneReplay;
        private int _nextSoundIndex;
        public bool SoundEnabled = true;

        private Recording _pendingReplayRecording;
        private int _replayLocalNetworkNumber = -1;
        private GameControl _replayGameControl;
        private Character.Animals _originalAnimal;
        private int[] _originalOutfits;

        private List<Player> _fakePlayers = new List<Player>();
        private List<GameObject> _dummyControllerObjects = new List<GameObject>();
        private Dictionary<int, int> _lobbyNumToRecordedNum = new Dictionary<int, int>();

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
                Plugin.Logger.LogInfo("[Play] Mode: STANDALONE (game scene transition)");
                LaunchReplay(_recording);
            }
            else
            {
                _standaloneReplay = false;
                Plugin.Logger.LogInfo("[Play] Mode: IN-GAME (using existing scene)");
                MapCharacters();
                StartPlayback();
            }
        }

        // ── Replay Launch ─────────────────────────────────────────────

        public void LaunchReplay(Recording recording)
        {
            Plugin.Logger.LogInfo("[ReplayLaunch] === LAUNCH START ===");

            if (recording == null || recording.Frames.Count == 0) return;

            string sceneName = recording.Metadata?.SceneName;
            if (string.IsNullOrEmpty(sceneName) || sceneName == "unknown") return;

            var lsc = LevelSelectController.lastInstance;
            if (lsc == null) return;

            if (recording.Metadata.Players.Count == 0)
            {
                Plugin.Logger.LogError("[ReplayLaunch] Recording has no players");
                return;
            }

            _pendingReplayRecording = recording;
            _replayLocalNetworkNumber = recording.Metadata.Players[0].NetworkNumber;
            _lobbyNumToRecordedNum.Clear();

            if (CameraModController.Instance != null && CameraModController.Instance.ModActive)
            {
                CameraModController.Instance.ModActive = false;
                Plugin.Logger.LogInfo("[ReplayLaunch] Deactivated camera mod for replay");
            }

            var levelName = LevelSelectController.GetLevelNameEnumFromSceneName(sceneName);
            CustomLevelPortal portal = lsc.snapshotPortals[0];
            portal.TargetLevel = levelName;
            portal.Networkpopulated = true;
            portal.snapshotInfo = new CustomLevelPortal.SnapshotInfo();
            portal.snapshotInfo.targetLevel = levelName;
            portal.snapshotInfo.snapshotName = recording.Name ?? "Replay";
            portal.snapshotInfo.code = null;
            portal.snapshotInfo.xml = null;

            if (recording.SnapshotBytes != null && recording.SnapshotBytes.Length > 0)
            {
                string xml = QuickSaver.GetXmlStringFromBytes(recording.SnapshotBytes);
                portal.snapshotXml = xml;
            }
            else
            {
                portal.snapshotXml = null;
            }

            StartCoroutine(ConfigureAndLaunchReplay(recording, portal, lsc));
        }

        private IEnumerator ConfigureAndLaunchReplay(
            Recording recording,
            CustomLevelPortal portal,
            LevelSelectController lsc)
        {
            var players = new List<PlayerInfo>(recording.Metadata.Players);
            players.Sort((a, b) => a.NetworkNumber.CompareTo(b.NetworkNumber));
            if (players.Count == 0)
            {
                Plugin.Logger.LogError("[ReplayLaunch] Recording has no players");
                yield break;
            }

            // ── Phase 0a: Clear all non-host locals ────────────────────────
            yield return ClearNonHostLocals();

            // ── Phase 0b: Find the host ────────────────────────────────────
            LobbyPlayer host = null;
            foreach (var slot in LobbyManager.instance.lobbySlots)
            {
                var lp = slot as LobbyPlayer;
                if (lp != null && lp.IsLocalPlayer) { host = lp; break; }
            }

            if (host == null)
            {
                Plugin.Logger.LogError("[ReplayLaunch] No host LobbyPlayer found");
                yield break;
            }

            // Save host's original state for restore on Stop
            _originalAnimal = host.PickedAnimal;
            _originalOutfits = new int[host.characterOutfitsList.Count];
            for (int i = 0; i < host.characterOutfitsList.Count; i++)
                _originalOutfits[i] = host.characterOutfitsList[i];

            Plugin.Logger.LogInfo(
                $"[ReplayLaunch:Enum] Host P{host.networkNumber} ({host.PickedAnimal}), " +
                $"recording has {players.Count} player(s)");

            // ── Phase 1: Unpick host's character ───────────────────────────
            if (host.CharacterInstance != null)
            {
                Plugin.Logger.LogInfo(
                    $"[ReplayLaunch:Unpick] P{host.networkNumber} unpicking {host.PickedAnimal}");
                host.CallCmdRemoveCharacter();
                if (host.LocalPlayer != null)
                {
                    if (host.LocalPlayer.UseController != null)
                        host.LocalPlayer.UseController.AssociateCharacter(
                            Character.Animals.NONE, host.localNumber);
                    host.LocalPlayer.PlayerCharacter = null;
                }

                // Wait for unpick to propagate
                float elapsed = 0f;
                while (elapsed < 2f
                    && (host.CharacterInstance != null
                        || host.PickedAnimal != Character.Animals.NONE))
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // ── Phase 2: Spawn fakes for players[1..] ──────────────────────
            for (int i = 1; i < players.Count; i++)
                yield return SpawnFakePlayer(players[i]);

            // Collect all locals now (host + fakes)
            var allLocals = new List<LobbyPlayer>();
            foreach (var slot in LobbyManager.instance.lobbySlots)
            {
                var lp = slot as LobbyPlayer;
                if (lp != null && lp.IsLocalPlayer)
                    allLocals.Add(lp);
            }
            Plugin.Logger.LogInfo($"[ReplayLaunch:Fakes] {allLocals.Count} local(s) total");

            // ── Phase 3: Assign — host gets preferred animal first ────────
            _lobbyNumToRecordedNum.Clear();

            // Build a pair-up plan. If the host's original animal matches a recorded
            // player's animal, pair them. Otherwise host takes players[0]. Everyone
            // else fills in order.
            var pairs = new List<(LobbyPlayer lp, PlayerInfo info)>();
            var remainingPlayers = new List<PlayerInfo>(players);

            // Try to match host by animal
            PlayerInfo hostMatch = null;
            foreach (var p in remainingPlayers)
            {
                if (System.Enum.TryParse(p.Animal, out Character.Animals a)
                    && a == _originalAnimal)
                {
                    hostMatch = p;
                    break;
                }
            }
            if (hostMatch != null)
            {
                pairs.Add((host, hostMatch));
                remainingPlayers.Remove(hostMatch);
            }
            else
            {
                pairs.Add((host, remainingPlayers[0]));
                remainingPlayers.RemoveAt(0);
            }

            // Fakes take whatever's left, in order
            int fakeIdx = 0;
            for (int i = 1; i < allLocals.Count && fakeIdx < remainingPlayers.Count; i++)
            {
                pairs.Add((allLocals[i], remainingPlayers[fakeIdx]));
                fakeIdx++;
            }

            // Apply the pairings
            foreach (var (lp, info) in pairs)
            {
                if (!System.Enum.TryParse(info.Animal, out Character.Animals targetAnimal))
                    targetAnimal = Character.Animals.CHICKEN;

                Character targetChar = FindUnpickedCharacter(targetAnimal);
                if (targetChar == null)
                {
                    Plugin.Logger.LogError(
                        $"[ReplayLaunch:Assign] P{lp.networkNumber}: no unpicked " +
                        $"{targetAnimal} available");
                    continue;
                }

                if (info.Outfits != null && info.Outfits.Length > 0)
                    lp.CallCmdSetOutfitsFromArray(info.Outfits);

                lp.CallCmdAssignCharacter(
                    targetChar.netId.Value,
                    lp.networkNumber,
                    lp.localNumber,
                    true);

                lp.NetworkPickedAnimal = targetAnimal;
                lp.NetworkplayerStatus = LobbyPlayer.Status.CHARACTER;

                if (lp.LocalPlayer != null)
                {
                    lp.LocalPlayer.PlayerCharacter = targetChar;
                    if (lp.LocalPlayer.UseController != null)
                        lp.LocalPlayer.UseController.AssociateCharacter(
                            targetAnimal, lp.localNumber);
                }

                _lobbyNumToRecordedNum[lp.networkNumber] = info.NetworkNumber;

                Plugin.Logger.LogInfo(
                    $"[ReplayLaunch:Assign] P{lp.networkNumber} -> {targetAnimal} " +
                    $"(recorded P{info.NetworkNumber})");
            }

            // ── Phase 5: Launch ────────────────────────────────────────────
            yield return null;
            yield return null;

            GameSettings.GetInstance().GameMode = GameState.GameMode.FREEPLAY;
            lsc.LaunchLevel(portal);
        }

        private IEnumerator ClearNonHostLocals()
        {
            var toRemove = new List<LobbyPlayer>();
            for (int i = 1; i < LobbyManager.instance.lobbySlots.Length; i++)
            {
                var lp = LobbyManager.instance.lobbySlots[i] as LobbyPlayer;
                if (lp != null && lp.IsLocalPlayer)
                    toRemove.Add(lp);
            }

            if (toRemove.Count == 0) yield break;

            Plugin.Logger.LogInfo(
                $"[ReplayLaunch:Clear] Removing {toRemove.Count} non-host local(s)");

            foreach (var lp in toRemove)
            {
                Plugin.Logger.LogInfo(
                    $"[ReplayLaunch:Clear] Removing P{lp.networkNumber} ({lp.PickedAnimal})");
                lp.RemovePlayer();
            }

            // Wait up to 2s for slots to actually clear
            float elapsed = 0f;
            while (elapsed < 2f)
            {
                bool allGone = true;
                for (int i = 1; i < LobbyManager.instance.lobbySlots.Length; i++)
                {
                    var lp = LobbyManager.instance.lobbySlots[i] as LobbyPlayer;
                    if (lp != null && lp.IsLocalPlayer) { allGone = false; break; }
                }
                if (allGone) yield break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Plugin.Logger.LogWarning(
                "[ReplayLaunch:Clear] Timed out waiting for non-host locals to clear");
        }

        private IEnumerator WaitForUnpickPropagation(List<LobbyPlayer> lobbyPlayers, float timeout = 2f)
        {
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                bool allClear = true;
                foreach (var lp in lobbyPlayers)
                {
                    if (lp == null) continue;
                    if (lp.CharacterInstance != null || lp.PickedAnimal != Character.Animals.NONE)
                    {
                        allClear = false;
                        break;
                    }
                }
                if (allClear) yield break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Plugin.Logger.LogWarning(
                "[ReplayLaunch:Unpick] Timed out waiting for unpick propagation; proceeding anyway");
        }

        private Character FindUnpickedCharacter(Character.Animals animal)
        {
            var lsc = LevelSelectController.lastInstance;
            if (lsc == null) return null;

            foreach (var startPoint in lsc.StartingPoints)
            {
                Character c = startPoint.GetComponentInChildren<Character>();
                if (c != null && c.CharacterSprite == animal && !c.Picked)
                    return c;
            }
            return null;
        }

        private IEnumerator SpawnFakePlayer(PlayerInfo info)
        {
            var go = new GameObject($"ReplayDummyController_P{info.NetworkNumber}");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var dummy = go.AddComponent<DummyController>();
            _dummyControllerObjects.Add(go);

            var fakePlayer = PlayerManager.GetInstance().AddPlayer(dummy);
            if (fakePlayer == null)
            {
                Plugin.Logger.LogError(
                    $"[ReplayLaunch:Fake] Could not add Player (PlayerManager full? " +
                    $"max={PlayerManager.maxPlayers})");
                UnityEngine.Object.Destroy(go);
                yield break;
            }
            _fakePlayers.Add(fakePlayer);

            // Stage 1: wait for LobbyPlayer to spawn and InitPlayer to finish
            float timeout = 5f;
            while (timeout > 0f)
            {
                if (fakePlayer.AssociatedLobbyPlayer != null
                    && fakePlayer.AssociatedLobbyPlayer.Initialized)
                    break;
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            var lp = fakePlayer.AssociatedLobbyPlayer;
            if (lp == null || !lp.Initialized)
            {
                Plugin.Logger.LogError(
                    "[ReplayLaunch:Fake] Timed out waiting for LobbyPlayer init");
                yield break;
            }

            // Stage 2: wait for cursor to spawn — a stronger signal that the LobbyPlayer
            // is fully integrated. Mirror's `connectionToClient.isReady` is per-connection
            // and doesn't flip false when a new PlayerController is added, so it's not
            // a reliable per-fake signal. The cursor is spawned via CmdCreateCursorForPlayer
            // only after LobbyPlayer's own setup completes.
            float cursorTimeout = 3f;
            while (cursorTimeout > 0f)
            {
                if (lp.CursorInstance != null) break;
                cursorTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (lp.CursorInstance == null)
            {
                Plugin.Logger.LogWarning(
                    $"[ReplayLaunch:Fake] P{lp.networkNumber} has no cursor after 3s; " +
                    $"LobbyPlayer may not be fully registered");
            }

            Plugin.Logger.LogInfo(
                $"[ReplayLaunch:Fake] Spawned LobbyPlayer netNum={lp.networkNumber} " +
                $"for recorded P{info.NetworkNumber} " +
                $"(cursor={(lp.CursorInstance != null ? "yes" : "no")})");
        }

        public IEnumerator OnGameReadyForReplay(GameControl gc)
        {
            float timeout = 15f;
            while (gc.Phase != GameControl.GamePhase.PLACE && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (gc.Phase != GameControl.GamePhase.PLACE)
            {
                _pendingReplayRecording = null;
                yield break;
            }

            yield return null;

            var recording = _pendingReplayRecording;
            _pendingReplayRecording = null;

            // Switch to play mode using the game's built-in pathway
            foreach (GamePlayer gp in gc.CurrentPlayerQueue)
            {
                GameEventManager.SendEvent(
                    new FreePlayPlayerSwitchEvent(gp.networkNumber, GameControl.GamePhase.PLAY));
            }

            // Wait for resetPlayerCharacter to finish
            yield return new WaitForSeconds(0.5f);

            // Diagnostic: log what the game spawned
            Plugin.Logger.LogInfo($"[Diag] Queue has {gc.CurrentPlayerQueue.Count} entries");
            foreach (var gp in gc.CurrentPlayerQueue)
            {
                var ch = gp.CharacterInstance;
                Plugin.Logger.LogInfo(
                    $"[Diag] Queue: netNum={gp.networkNumber} " +
                    $"hasAuthority={ch?.hasAuthority} sprite={ch?.CharacterSprite} " +
                    $"sfx={ch?.CharacterSFXName} name={ch?.gameObject.name}");
            }

            _characterMap.Clear();
            _rbMap.Clear();
            _animMap.Clear();

            // Map game-spawned characters using lobby→recorded number table
            foreach (GamePlayer gp in gc.CurrentPlayerQueue)
            {
                if (gp.CharacterInstance == null) continue;
                if (!_lobbyNumToRecordedNum.TryGetValue(gp.networkNumber, out int recordedNetNum))
                {
                    Plugin.Logger.LogWarning(
                        $"[ReplayLaunch:Ready] GP netNum={gp.networkNumber} not in lobby->recorded map, skipping");
                    continue;
                }
                MapCharacterInternal(recordedNetNum, gp.CharacterInstance);
                Plugin.Logger.LogInfo(
                    $"[ReplayLaunch:Ready] Mapped GP netNum={gp.networkNumber} " +
                    $"(animal={gp.CharacterInstance.CharacterSprite}) to recorded P{recordedNetNum}");
            }

            // Diagnostic: log final map and scene state
            foreach (var kvp in _characterMap)
            {
                Plugin.Logger.LogInfo(
                    $"[Diag] Map[{kvp.Key}] -> {kvp.Value.gameObject.name} " +
                    $"sprite={kvp.Value.CharacterSprite}");
            }
            foreach (var ch in UnityEngine.Object.FindObjectsOfType<Character>())
            {
                Plugin.Logger.LogInfo(
                    $"[Diag] Scene: name={ch.gameObject.name} " +
                    $"sprite={ch.CharacterSprite} netNum={ch.networkNumber} " +
                    $"active={ch.gameObject.activeInHierarchy}");
            }

            // Disable gameplay and start playback
            _replayGameControl = gc;
            _standaloneReplay = true;
            _recording = recording;

            DisableGameplay(gc);
            StartPlayback();

            Plugin.Logger.LogInfo("[ReplayLaunch:Ready] === PLAYBACK STARTED ===");
        }

        private void MapCharacterInternal(int networkNumber, Character c)
        {
            _characterMap[networkNumber] = c;

            Rigidbody2D rb = c.GetComponent<Rigidbody2D>();
            if (rb != null) _rbMap[networkNumber] = rb;
            else Plugin.Logger.LogWarning($"[ReplayLaunch:Map] P{networkNumber}: No Rigidbody2D");

            Animator anim = c.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                _animMap[networkNumber] = anim;
                BuildClipLookup(networkNumber, anim);

                if (Plugin.CfgVerboseReplayLog.Value)
                    Plugin.Logger.LogInfo($"[ReplayLaunch:Map] P{networkNumber}: " +
                                          $"Animator has {anim.runtimeAnimatorController?.animationClips?.Length ?? 0} clips");
            }
            else
            {
                Plugin.Logger.LogWarning($"[ReplayLaunch:Map] P{networkNumber}: No Animator found");
            }

            AkSoundEngine.SetSwitch("Character", c.CharacterSprite.ToString(), c.gameObject);
        }

        private void DisableGameplay(GameControl gc)
        {
            Plugin.Logger.LogInfo("[ReplayLaunch:Disable] Disabling gameplay...");

            int cursorsDisabled = 0;
            foreach (GamePlayer gp in gc.CurrentPlayerQueue)
            {
                if (gp != null && gp.CursorInstance != null)
                {
                    gp.CursorInstance.Disable(false, false);
                    cursorsDisabled++;
                }
            }
            Plugin.Logger.LogInfo($"[ReplayLaunch:Disable] Cursors disabled: {cursorsDisabled}");

            GameState.GetInstance().Keyboard.RemoveReceiver(gc);
            Plugin.Logger.LogInfo("[ReplayLaunch:Disable] Input receiver removed from GameControl");

            foreach (var kvp in _rbMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.bodyType = RigidbodyType2D.Kinematic;
                    kvp.Value.velocity = Vector2.zero;
                    kvp.Value.angularVelocity = 0f;
                }
            }
            Plugin.Logger.LogInfo($"[ReplayLaunch:Disable] Physics disabled on {_rbMap.Count} rigidbodies");

            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                    kvp.Value.enabled = false;
            }
            Plugin.Logger.LogInfo($"[ReplayLaunch:Disable] Animators disabled: {_animMap.Count}");

            Plugin.Logger.LogInfo("[ReplayLaunch:Disable] Gameplay disabled");
        }

        private void ReturnToTreehouse()
        {
            Plugin.Logger.LogInfo("[ReplayLaunch:Return] Returning to treehouse...");

            var gc = _replayGameControl ?? UnityEngine.Object.FindObjectOfType<GameControl>();
            if (gc != null)
                gc.EndGame();
            else
                LobbyManager.instance.ServerChangeScene("TreeHouseLobby");
        }

        // ── Playback Core ─────────────────────────────────────────────

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

            _nextSoundIndex = 0;
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
            Plugin.Logger.LogInfo($"[ReplayLaunch:Stop] Stopping playback. " +
                                  $"Standalone={_standaloneReplay} " +
                                  $"Characters={_characterMap.Count} " +
                                  $"SpawnedExtras={_spawnedCharacters.Count}");
            IsPlaying = false;
            IsPaused = false;
            CurrentTime = 0f;

            foreach (var kvp in _originalSpriteScale)
            {
                if (_animMap.TryGetValue(kvp.Key, out Animator anim) && anim != null)
                    anim.transform.localScale = kvp.Value;
            }
            _originalSpriteScale.Clear();

            foreach (var kvp in _rbMap)
            {
                if (kvp.Value != null)
                    kvp.Value.bodyType = RigidbodyType2D.Dynamic;
            }

            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.enabled = true;
                    kvp.Value.speed = 1f;
                }
            }

            Plugin.Logger.LogInfo($"[ReplayLaunch:Stop] Restored {_rbMap.Count} rigidbodies, " +
                                  $"{_animMap.Count} animators");

            if (_standaloneReplay)
            {
                Plugin.Logger.LogInfo("[ReplayLaunch:Stop] Cleaning up standalone replay...");
                DestroyReplayCharacters();
                Plugin.Logger.LogInfo("[ReplayLaunch:Stop] Destroyed extra characters");

                // Tear down fake local players. Don't touch LobbyPlayer GameObjects —
                // the scene transition handles their removal, and our `AssociatedLobbyPlayer`
                // pointers are unreliable after a scene change.
                foreach (var p in _fakePlayers)
                {
                    if (p == null) continue;
                    PlayerManager.GetInstance().RemovePlayer(p.Number);
                }
                _fakePlayers.Clear();

                foreach (var go in _dummyControllerObjects)
                {
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
                _dummyControllerObjects.Clear();

                _lobbyNumToRecordedNum.Clear();

                Plugin.Logger.LogInfo("[ReplayLaunch:Stop] Cleaned up fake players");
                Plugin.Logger.LogInfo("[ReplayLaunch:Stop] Returning to treehouse...");
                ReturnToTreehouse();
                _standaloneReplay = false;
                _replayGameControl = null;
                _replayLocalNetworkNumber = -1;
            }

            foreach (var kvp in _characterMap)
            {
                if (kvp.Value != null)
                {
                    string sfxName = kvp.Value.CharacterSFXName;
                    if (!string.IsNullOrEmpty(sfxName))
                    {
                        AkSoundEngine.PostEvent("SFX_" + sfxName + "_Move_Stop", kvp.Value.gameObject);
                        AkSoundEngine.PostEvent("SFX_" + sfxName + "_Sprint_Stop", kvp.Value.gameObject);
                        AkSoundEngine.PostEvent("SFX_" + sfxName + "_Dance_Stop", kvp.Value.gameObject);
                        AkSoundEngine.PostEvent("SFX_" + sfxName + "_WallSlideStop", kvp.Value.gameObject);
                    }
                }
            }

            _characterMap.Clear();
            _rbMap.Clear();
            _animMap.Clear();

            Plugin.Logger.LogInfo("[ReplayLaunch:Stop] === PLAYBACK STOPPED ===");
        }

        public void Seek(float time)
        {
            CurrentTime = Mathf.Clamp(time, 0f, Duration);

            _nextSoundIndex = 0;
            if (_recording?.SoundEvents != null)
            {
                for (int i = 0; i < _recording.SoundEvents.Count; i++)
                {
                    if (_recording.SoundEvents[i].Time > CurrentTime) break;
                    _nextSoundIndex = i + 1;
                }
            }

            ApplyFrame(CurrentTime);
        }

        private void Update()
        {
            if (!IsPlaying || _recording == null) return;

            CurrentTime += Time.deltaTime;

            if (CurrentTime >= Duration)
            {
                CurrentTime = Duration;
                Stop();
            }
        }

        private void LateUpdate()
        {
            if (!IsPlaying || _recording == null) return;
            ProcessSoundEvents();
            ApplyFrame(CurrentTime);
        }

        private void ProcessSoundEvents()
        {
            if (_recording == null || _recording.SoundEvents == null || !SoundEnabled) return;

            while (_nextSoundIndex < _recording.SoundEvents.Count)
            {
                var snd = _recording.SoundEvents[_nextSoundIndex];
                if (snd.Time > CurrentTime) break;

                if (_characterMap.TryGetValue(snd.NetworkNumber, out Character c) && c != null)
                {
                    if (snd.EventName.StartsWith("EXACT:"))
                    {
                        AkSoundEngine.PostEvent(snd.EventName.Substring(6), c.gameObject);
                    }
                    else
                    {
                        string sfxName = c.CharacterSFXName;
                        if (!string.IsNullOrEmpty(sfxName))
                        {
                            string fullEvent;
                            if (snd.IsZombie)
                                fullEvent = "SFX_" + sfxName + "_Zombie" + snd.EventName;
                            else if (snd.IsGhost)
                                fullEvent = "SFX_" + sfxName + "_Ghost" + snd.EventName;
                            else
                                fullEvent = "SFX_" + sfxName + snd.EventName;

                            AkSoundEngine.PostEvent(fullEvent, c.gameObject);
                        }
                    }
                }

                _nextSoundIndex++;
            }
        }

        // ── Frame Application ─────────────────────────────────────────

        private void ApplyFrame(float time)
        {
            if (_recording == null || _recording.Frames.Count == 0) return;

            var frames = _recording.Frames;
            int lo = 0, hi = frames.Count - 1;

            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].Time <= time)
                    lo = mid;
                else
                    hi = mid;
            }

            int frameA = lo;
            int frameB = hi;

            if (frameA == frameB || frames[frameB].Time <= time)
            {
                ApplySnapshotsDirectly(frames[frameA]);
                return;
            }

            float frameATime = frames[frameA].Time;
            float frameBTime = frames[frameB].Time;
            float t = (time - frameATime) / (frameBTime - frameATime);

            ApplySnapshotsInterpolated(frames[frameA], frames[frameB], t);
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
            _frameBLookup.Clear();
            foreach (var snap in frameB.Characters)
                _frameBLookup[snap.NetworkNumber] = snap;

            foreach (var snapA in frameA.Characters)
            {
                if (!_characterMap.TryGetValue(snapA.NetworkNumber, out Character c)) continue;
                if (c == null) continue;

                if (!_frameBLookup.TryGetValue(snapA.NetworkNumber, out CharacterSnapshot snapB))
                {
                    ApplySingleSnapshot(c, snapA);
                    continue;
                }

                bool visible = t < 0.5f ? snapA.Visible : snapB.Visible;
                c.gameObject.SetActive(visible);
                if (!visible) continue;

                float px = Mathf.Lerp(snapA.PosX, snapB.PosX, t);
                float py = Mathf.Lerp(snapA.PosY, snapB.PosY, t);
                c.transform.position = new Vector3(px, py, c.transform.position.z);

                float activeScaleX = t < 0.5f ? snapA.ScaleX : snapB.ScaleX;
                c.transform.localScale = new Vector3(
                    Mathf.Abs(activeScaleX),
                    t < 0.5f ? snapA.ScaleY : snapB.ScaleY,
                    c.transform.localScale.z);
                float activeFlip = t < 0.5f ? snapA.FlipSpriteX : snapB.FlipSpriteX;
                ApplyFlipToSprite(snapA.NetworkNumber, activeFlip);

                float rot = Mathf.LerpAngle(snapA.Rotation, snapB.Rotation, t);
                c.transform.rotation = Quaternion.Euler(0, 0, rot);

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
            if (flipSpriteX == 0f) flipSpriteX = 1f;
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

            float normalized = snap.AnimationTime % 1f;
            if (normalized < 0f) normalized += 1f;
            clip.SampleAnimation(anim.gameObject, normalized * clip.length);
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

        // ── Character Management ──────────────────────────────────────

        public void DestroyReplayCharacters()
        {
            Plugin.Logger.LogInfo($"[SpawnChars] Destroying {_spawnedCharacters.Count} replay characters");
            foreach (var obj in _spawnedCharacters)
            {
                if (obj != null)
                    UnityEngine.Object.Destroy(obj);
            }
            _spawnedCharacters.Clear();
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
                        zoom.AddTarget(c);
                }
            }
        }

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