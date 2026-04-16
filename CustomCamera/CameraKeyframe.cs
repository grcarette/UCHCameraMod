namespace UCHCameraMod
{
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        Smooth
    }

    public class CameraKeyframe
    {
        public float FOV;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float LeftBuffer;
        public float RightBuffer;
        public float TopBuffer;
        public float BottomBuffer;
        public float Duration;
        public EasingType Easing;

        public CameraKeyframe Clone()
        {
            return new CameraKeyframe
            {
                FOV = FOV,
                PosX = PosX,
                PosY = PosY,
                PosZ = PosZ,
                LeftBuffer = LeftBuffer,
                RightBuffer = RightBuffer,
                TopBuffer = TopBuffer,
                BottomBuffer = BottomBuffer,
                Duration = Duration,
                Easing = Easing
            };
        }
    }
}