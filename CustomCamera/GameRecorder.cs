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
        }

        private List<TrackedCharacter> _tracked = new List<TrackedCharacter>();

        private void Awake()
        {
            Instance = this;
        }

        public void StartRecording()
        {
            _tracked.Clear();

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

            _startTime = Time.time;
            IsRecording = true;
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            CurrentRecording.Duration = Time.time - _startTime;
        }

        private void FixedUpdate()
        {
            if (!IsRecording) return;

            var frame = new RecordingFrame
            {
                Time = Time.time - _startTime
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
                    Visible = t.Character.gameObject.activeInHierarchy
                              && t.Sprite != null && t.Sprite.enabled
                };

                // Animation — only if animator is cached and active
                if (t.Animator != null && t.Animator.isActiveAndEnabled)
                {
                    var stateInfo = t.Animator.GetCurrentAnimatorStateInfo(0);
                    snap.AnimationTime = stateInfo.normalizedTime;

                    // Use cached clip info only if available
                    var clips = t.Animator.GetCurrentAnimatorClipInfo(0);
                    if (clips.Length > 0 && clips[0].clip != null)
                        snap.AnimationState = clips[0].clip.name;
                }

                frame.Characters.Add(snap);
            }

            CurrentRecording.Frames.Add(frame);
        }
    }
}
