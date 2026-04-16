using System.Reflection;
using UnityEngine;

namespace UCHCameraMod
{
    public class CameraProgramRunner : MonoBehaviour
    {
        public static CameraProgramRunner Instance { get; private set; }

        public bool IsPlaying { get; private set; }
        public int CurrentKeyframeIndex { get; private set; }
        public float Progress { get; private set; } // 0-1 within current keyframe

        private CameraProgram _program;
        private float _elapsed;
        private Camera _cam;
        private ZoomCamera _zoom;

        private void Awake()
        {
            Instance = this;
        }

        public bool IsPaused { get; private set; }

        public void Play(CameraProgram program, Camera cam, ZoomCamera zoom)
        {
            if (IsPaused && _program == program)
            {
                // Resume from where we left off
                IsPaused = false;
                IsPlaying = true;
                _cam = cam;
                _zoom = zoom;
                return;
            }

            if (program == null || program.Keyframes.Count == 0) return;
            _program = program;
            _cam = cam;
            _zoom = zoom;
            CurrentKeyframeIndex = 0;
            _elapsed = 0f;
            Progress = 0f;
            IsPlaying = true;
            IsPaused = false;

            ApplyKeyframe(program.Keyframes[0]);
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
            CurrentKeyframeIndex = 0;
            _elapsed = 0f;
            Progress = 0f;
        }

        public void Rewind()
        {
            CurrentKeyframeIndex = 0;
            _elapsed = 0f;
            Progress = 0f;
            if (_program != null && _program.Keyframes.Count > 0 && _cam != null)
                ApplyKeyframe(_program.Keyframes[0]);
        }

        public void Reset()
        {
            Stop();
            if (_program != null && _program.Keyframes.Count > 0 && _cam != null)
                ApplyKeyframe(_program.Keyframes[0]);
        }

        public void Tick()
        {
            if (!IsPlaying || _program == null || _cam == null) return;

            if (Input.GetKeyDown(Plugin.CfgKeyStopProgram.Value)) { Stop(); return; }
            if (Input.GetKeyDown(Plugin.CfgKeyResetProgram.Value)) { Reset(); return; }

            if (CurrentKeyframeIndex >= _program.Keyframes.Count - 1)
            {
                ApplyKeyframe(_program.Keyframes[_program.Keyframes.Count - 1]);
                IsPlaying = false;
                return;
            }

            var from = _program.Keyframes[CurrentKeyframeIndex];
            var to = _program.Keyframes[CurrentKeyframeIndex + 1];
            float duration = Mathf.Max(to.Duration, 0.001f);
            _elapsed += Time.deltaTime;
            Progress = Mathf.Clamp01(_elapsed / duration);
            float t = ApplyEasing(Progress, to.Easing);
            ApplyInterpolated(from, to, t);

            if (_elapsed >= duration)
            {
                CurrentKeyframeIndex++;
                _elapsed = 0f;
                Progress = 0f;
            }
        }

        private float ApplyEasing(float t, EasingType easing)
        {
            switch (easing)
            {
                case EasingType.EaseIn: return t * t;
                case EasingType.EaseOut: return t * (2f - t);
                case EasingType.Smooth: return t * t * (3f - 2f * t);
                default: return t;
            }
        }

        private void ApplyInterpolated(CameraKeyframe from, CameraKeyframe to, float t)
        {
            _cam.fieldOfView = Mathf.Lerp(from.FOV, to.FOV, t);

            _cam.transform.position = new Vector3(
                Mathf.Lerp(from.PosX, to.PosX, t),
                Mathf.Lerp(from.PosY, to.PosY, t),
                Mathf.Lerp(from.PosZ, to.PosZ, t));

            SetField(_zoom, "UnitLeftBuffer", Mathf.Lerp(from.LeftBuffer, to.LeftBuffer, t));
            SetField(_zoom, "UnitRightBuffer", Mathf.Lerp(from.RightBuffer, to.RightBuffer, t));
            SetField(_zoom, "UnitTopBuffer", Mathf.Lerp(from.TopBuffer, to.TopBuffer, t));
            SetField(_zoom, "UnitBottomBuffer", Mathf.Lerp(from.BottomBuffer, to.BottomBuffer, t));
        }

        private void ApplyKeyframe(CameraKeyframe kf)
        {
            _cam.fieldOfView = kf.FOV;
            _cam.transform.position = new Vector3(kf.PosX, kf.PosY, kf.PosZ);
            SetField(_zoom, "UnitLeftBuffer", kf.LeftBuffer);
            SetField(_zoom, "UnitRightBuffer", kf.RightBuffer);
            SetField(_zoom, "UnitTopBuffer", kf.TopBuffer);
            SetField(_zoom, "UnitBottomBuffer", kf.BottomBuffer);
        }

        private void SetField(object obj, string fieldName, object value)
        {
            FieldInfo field = obj.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null) field.SetValue(obj, value);
        }
    }
}