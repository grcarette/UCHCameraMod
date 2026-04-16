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
        private int _animLogCount = 0;

        private void Awake()
        {
            Instance = this;
        }

        public void Load(Recording recording)
        {
            _recording = recording;
            CurrentTime = 0f;
        }

        public void Play()
        {
            _animLogCount = 0;
            if (_recording == null || _recording.Frames.Count == 0) return;

            if (IsPaused)
            {
                // Resume
                IsPaused = false;
                IsPlaying = true;
                return;
            }

            MapCharacters();

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

            // Take full control of animators — prevent game logic from touching them
            foreach (var kvp in _animMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.speed = 0f;
                    kvp.Value.updateMode = AnimatorUpdateMode.Normal;
                }
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
                    kvp.Value.speed = 1f;
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
                c.transform.localScale = new Vector3(snap.ScaleX, snap.ScaleY, c.transform.localScale.z);
                c.transform.rotation = Quaternion.Euler(0, 0, snap.Rotation);

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

                // Scale: snap (don't lerp flips)
                c.transform.localScale = new Vector3(
                    t < 0.5f ? snapA.ScaleX : snapB.ScaleX,
                    t < 0.5f ? snapA.ScaleY : snapB.ScaleY,
                    c.transform.localScale.z);

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
            c.transform.localScale = new Vector3(snap.ScaleX, snap.ScaleY, c.transform.localScale.z);
            c.transform.rotation = Quaternion.Euler(0, 0, snap.Rotation);
            ApplyAnimation(c, snap);
        }

        private void ApplyAnimation(Character c, CharacterSnapshot snap)
        {
            if (string.IsNullOrEmpty(snap.AnimationState)) return;
            if (!_animMap.TryGetValue(snap.NetworkNumber, out Animator anim)) return;
            if (anim == null) return;

            // Force enable in case something disabled it
            if (!anim.enabled) anim.enabled = true;

            float normTime = snap.AnimationTime % 1f;

            // Temporarily enable speed so Play takes effect
            anim.speed = 1f;
            anim.Play(snap.AnimationState, 0, normTime);
            anim.Update(0.0001f);
            anim.speed = 0f;
        }

        // ── Character Mapping ────────────────────────────────────────

        private void MapCharacters()
        {
            _characterMap.Clear();
            _rbMap.Clear();
            _animMap.Clear();

            if (_recording == null || _recording.Frames.Count == 0) return;

            // Get all network numbers from the recording
            HashSet<int> recordedNumbers = new HashSet<int>();
            foreach (var snap in _recording.Frames[0].Characters)
                recordedNumbers.Add(snap.NetworkNumber);

            // Map to scene characters via GamePlayer
            foreach (GamePlayer gp in FindObjectsOfType<GamePlayer>())
            {
                if (gp.CharacterInstance != null && recordedNumbers.Contains(gp.networkNumber))
                {
                    _characterMap[gp.networkNumber] = gp.CharacterInstance;
                    Rigidbody2D rb = gp.CharacterInstance.GetComponent<Rigidbody2D>();
                    if (rb != null)
                        _rbMap[gp.networkNumber] = rb;
                    Animator anim = gp.CharacterInstance.GetComponentInChildren<Animator>();
                    if (anim != null)
                        _animMap[gp.networkNumber] = anim;
                }
            }

            Debug.Log($"GamePlayback: Mapped {_characterMap.Count} characters, {_animMap.Count} animators, {_rbMap.Count} rigidbodies");
            foreach (var kvp in _animMap)
                Debug.Log($"  Animator for P{kvp.Key}: {kvp.Value.name}, enabled={kvp.Value.enabled}, activeAndEnabled={kvp.Value.isActiveAndEnabled}");
        }
    }
}
