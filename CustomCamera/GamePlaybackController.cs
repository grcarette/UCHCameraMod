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

        private Recording _recording;
        private Dictionary<int, Character> _characterMap = new Dictionary<int, Character>();
        private Dictionary<int, Rigidbody2D> _rbMap = new Dictionary<int, Rigidbody2D>();
        private Dictionary<int, Animator> _animMap = new Dictionary<int, Animator>();
        private Dictionary<int, Dictionary<string, AnimationClip>> _clipMap
            = new Dictionary<int, Dictionary<string, AnimationClip>>();
        private Dictionary<int, Vector3> _originalSpriteScale = new Dictionary<int, Vector3>();
        private int _animLogCount = 0;
        private string _lastLoggedClip = "";

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

        _animLogCount = 0;
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

        Plugin.Logger.LogInfo("[Play] proceeding to MapCharacters");
        MapCharacters();

        _originalSpriteScale.Clear();
        foreach (var kvp in _animMap)
        {
            if (kvp.Value != null)
                _originalSpriteScale[kvp.Key] = kvp.Value.transform.localScale;
        }

        IsPlaying = true;
            IsPaused = false;
            CurrentTime = 0f;

            // Disable physics on all tracked characters so we can puppet them
            foreach (var kvp in _rbMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.bodyType = RigidbodyType2D.Kinematic;
                    kvp.Value.velocity = Vector2.zero;
                    kvp.Value.angularVelocity = 0f;
                }
            }

            // Take full control by disabling the animator — we'll sample clips ourselves
            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                    kvp.Value.enabled = false;
            }

            ApplyFrame(0f);
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            IsPaused = true;
            IsPlaying = false;
        }

        public void Stop()
        {
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
                CurrentTime = Duration;
                IsPlaying = false;
                ApplyFrame(Duration);
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
