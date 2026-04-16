using System.IO;
using System.Text;
using UnityEngine;
using BepInEx;

namespace UCHCameraMod
{
    public static class RecordingIO
    {
        private static readonly string RecordingFolder = Path.Combine(
            Paths.ConfigPath, "UCHCameraRecordings");

        static RecordingIO()
        {
            Directory.CreateDirectory(RecordingFolder);
        }

        public static string GetFolder() => RecordingFolder;

        public static void Save(Recording rec)
        {
            string path = Path.Combine(RecordingFolder, rec.Name + ".camrec");
            var sb = new StringBuilder();

            sb.AppendLine($"Name={rec.Name}");
            sb.AppendLine($"Duration={rec.Duration}");
            sb.AppendLine($"TickRate={rec.TickRate}");
            sb.AppendLine($"FrameCount={rec.Frames.Count}");

            foreach (var frame in rec.Frames)
            {
                sb.AppendLine("---FRAME");
                sb.AppendLine($"Time={frame.Time}");
                sb.AppendLine($"CharCount={frame.Characters.Count}");

                foreach (var snap in frame.Characters)
                {
                    sb.AppendLine("---CHAR");
                    sb.AppendLine($"N={snap.NetworkNumber}");
                    sb.AppendLine($"P={snap.PosX},{snap.PosY}");
                    sb.AppendLine($"S={snap.ScaleX},{snap.ScaleY}");
                    sb.AppendLine($"R={snap.Rotation}");
                    sb.AppendLine($"V={snap.Visible}");
                    sb.AppendLine($"A={snap.AnimationState}|{snap.AnimationTime}");
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        public static Recording Load(string path)
        {
            var rec = new Recording();
            RecordingFrame currentFrame = null;

            foreach (string line in File.ReadAllLines(path))
            {
                if (line == "---FRAME")
                {
                    currentFrame = new RecordingFrame();
                    rec.Frames.Add(currentFrame);
                    continue;
                }

                if (line == "---CHAR")
                    continue; // next lines describe the character

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (currentFrame == null)
                {
                    // Header
                    switch (key)
                    {
                        case "Name": rec.Name = val; break;
                        case "Duration": float.TryParse(val, out rec.Duration); break;
                        case "TickRate": float.TryParse(val, out rec.TickRate); break;
                    }
                }
                else
                {
                    // Inside a frame
                    if (key == "Time")
                    {
                        float.TryParse(val, out currentFrame.Time);
                    }
                    else if (key == "N")
                    {
                        // Start a new character snapshot
                        var snap = new CharacterSnapshot();
                        int.TryParse(val, out snap.NetworkNumber);
                        currentFrame.Characters.Add(snap);
                    }
                    else if (currentFrame.Characters.Count > 0)
                    {
                        var snap = currentFrame.Characters[currentFrame.Characters.Count - 1];
                        switch (key)
                        {
                            case "P":
                                var p = val.Split(',');
                                if (p.Length == 2) { float.TryParse(p[0], out snap.PosX); float.TryParse(p[1], out snap.PosY); }
                                break;
                            case "S":
                                var s = val.Split(',');
                                if (s.Length == 2) { float.TryParse(s[0], out snap.ScaleX); float.TryParse(s[1], out snap.ScaleY); }
                                break;
                            case "R":
                                float.TryParse(val, out snap.Rotation);
                                break;
                            case "V":
                                bool.TryParse(val, out snap.Visible);
                                break;
                            case "A":
                                var a = val.Split('|');
                                if (a.Length == 2)
                                {
                                    snap.AnimationState = a[0];
                                    float.TryParse(a[1], out snap.AnimationTime);
                                }
                                break;
                        }
                    }
                }
            }

            return rec;
        }

        public static string[] GetRecordingFiles()
        {
            var files = Directory.GetFiles(RecordingFolder, "*.camrec");
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            System.Array.Sort(names);
            return names;
        }
    }
}
