using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UCHCameraMod
{
    public class CameraProgram
    {
        public string Name;
        public List<CameraKeyframe> Keyframes = new List<CameraKeyframe>();

        public void SaveToFile(string folder)
        {
            string path = Path.Combine(folder, Name + ".camprog");
            var sb = new StringBuilder();
            sb.AppendLine($"Name={Name}");
            sb.AppendLine($"KeyframeCount={Keyframes.Count}");
            foreach (var kf in Keyframes)
            {
                sb.AppendLine("---");
                sb.AppendLine($"FOV={kf.FOV}");
                sb.AppendLine($"PosX={kf.PosX}");
                sb.AppendLine($"PosY={kf.PosY}");
                sb.AppendLine($"PosZ={kf.PosZ}");
                sb.AppendLine($"LeftBuffer={kf.LeftBuffer}");
                sb.AppendLine($"RightBuffer={kf.RightBuffer}");
                sb.AppendLine($"TopBuffer={kf.TopBuffer}");
                sb.AppendLine($"BottomBuffer={kf.BottomBuffer}");
                sb.AppendLine($"Duration={kf.Duration}");
                sb.AppendLine($"Easing={kf.Easing}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        public static CameraProgram LoadFromFile(string path)
        {
            var prog = new CameraProgram();
            prog.Keyframes = new List<CameraKeyframe>();
            CameraKeyframe current = null;

            foreach (string line in File.ReadAllLines(path))
            {
                if (line == "---")
                {
                    if (current != null) prog.Keyframes.Add(current);
                    current = new CameraKeyframe { Duration = 1f };
                    continue;
                }

                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (key == "Name") { prog.Name = val; continue; }

                if (current == null) continue;

                switch (key)
                {
                    case "FOV": float.TryParse(val, out current.FOV); break;
                    case "PosX": float.TryParse(val, out current.PosX); break;
                    case "PosY": float.TryParse(val, out current.PosY); break;
                    case "PosZ": float.TryParse(val, out current.PosZ); break;
                    case "LeftBuffer": float.TryParse(val, out current.LeftBuffer); break;
                    case "RightBuffer": float.TryParse(val, out current.RightBuffer); break;
                    case "TopBuffer": float.TryParse(val, out current.TopBuffer); break;
                    case "BottomBuffer": float.TryParse(val, out current.BottomBuffer); break;
                    case "Duration": float.TryParse(val, out current.Duration); break;
                    case "Easing":
                        System.Enum.TryParse(val, out EasingType e);
                        current.Easing = e;
                        break;
                }
            }

            if (current != null) prog.Keyframes.Add(current);
            return prog;
        }
    }
}