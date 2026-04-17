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
            sb.AppendLine($"Date={rec.Metadata.Date}");
            sb.AppendLine($"StageCode={rec.Metadata.StageCode}");
            sb.AppendLine($"SceneName={rec.Metadata.SceneName}");
            sb.AppendLine($"GameMode={rec.Metadata.GameMode}");
            sb.AppendLine($"PlayerCount={rec.Metadata.Players.Count}");
            foreach (var pi in rec.Metadata.Players)
            {
                sb.AppendLine("---PLAYER");
                sb.AppendLine($"PN={pi.NetworkNumber}");
                sb.AppendLine($"PName={pi.Name}");
                sb.AppendLine($"PScore={pi.Score}");
                sb.AppendLine($"PAnimal={pi.Animal}");
                sb.AppendLine($"PSkin={pi.IsWearingSkin}");
                if (pi.Outfits != null && pi.Outfits.Length > 0)
                    sb.AppendLine($"POutfits={string.Join(",", pi.Outfits)}");
            }

            sb.AppendLine($"PlaceableCount={rec.Scene.Placeables.Count}");
            sb.AppendLine($"CamBounds={rec.Scene.CameraBoundsMinX},{rec.Scene.CameraBoundsMaxX},{rec.Scene.CameraBoundsMinY},{rec.Scene.CameraBoundsMaxY}");
            sb.AppendLine($"MinCharY={rec.Scene.MinCharacterY}");

            foreach (var ps in rec.Scene.Placeables)
            {
                sb.AppendLine("---PIECE");
                sb.AppendLine($"PID={ps.ID}");
                sb.AppendLine($"PNm={ps.Name}");
                sb.AppendLine($"PCat={ps.Category}");
                sb.AppendLine($"PBy={ps.PlacedByPlayer}");
                sb.AppendLine($"PPos={ps.PosX},{ps.PosY}");
                sb.AppendLine($"PRot={ps.Rotation}");
                sb.AppendLine($"PScl={ps.ScaleX},{ps.ScaleY}");
                sb.AppendLine($"PPar={ps.ParentID}");
                sb.AppendLine($"PSet={ps.IsSetPiece}");
                sb.AppendLine($"PDmg={ps.DamageLevel}");
                sb.AppendLine($"PIdx={ps.BlockIndex}");
                sb.AppendLine($"PLvl={ps.IsLevelGeometry}");
                if (!string.IsNullOrEmpty(ps.CustomColorHex))
                    sb.AppendLine($"PClr={ps.CustomColorHex}");
            }

            if (rec.SnapshotBytes != null && rec.SnapshotBytes.Length > 0)
            {
                string b64 = System.Convert.ToBase64String(rec.SnapshotBytes);
                sb.AppendLine($"SnapshotSize={rec.SnapshotBytes.Length}");
                sb.AppendLine($"Snapshot={b64}");
            }

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
                    sb.AppendLine($"F={snap.FlipSpriteX}");
                    sb.AppendLine($"V={snap.Visible}");
                    sb.AppendLine($"A={snap.AnimationState}|{snap.AnimationTime}|{snap.AnimationStateHash}");
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
                    continue;

                if (line == "---PLAYER")
                    continue;

                if (line == "---PIECE")
                    continue;

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (currentFrame == null)
                {
                    // Header
                    switch (key)
                    {
                        case "Name":      rec.Name = val; break;
                        case "Duration":  float.TryParse(val, out rec.Duration); break;
                        case "TickRate":  float.TryParse(val, out rec.TickRate); break;
                        case "Date":      rec.Metadata.Date = val; break;
                        case "StageCode":  rec.Metadata.StageCode = val; break;
                        case "SceneName": rec.Metadata.SceneName = val; break;
                        case "GameMode":  rec.Metadata.GameMode = val; break;
                        case "PN":
                            var pi = new PlayerInfo();
                            int.TryParse(val, out pi.NetworkNumber);
                            rec.Metadata.Players.Add(pi);
                            break;
                        case "PName":
                            if (rec.Metadata.Players.Count > 0)
                                rec.Metadata.Players[rec.Metadata.Players.Count - 1].Name = val;
                            break;
                        case "PScore":
                            if (rec.Metadata.Players.Count > 0)
                                int.TryParse(val, out rec.Metadata.Players[rec.Metadata.Players.Count - 1].Score);
                            break;
                        case "PAnimal":
                            if (rec.Metadata.Players.Count > 0)
                                rec.Metadata.Players[rec.Metadata.Players.Count - 1].Animal = val;
                            break;
                        case "PSkin":
                            if (rec.Metadata.Players.Count > 0)
                                bool.TryParse(val, out rec.Metadata.Players[rec.Metadata.Players.Count - 1].IsWearingSkin);
                            break;
                        case "POutfits":
                            if (rec.Metadata.Players.Count > 0)
                            {
                                var outfitParts = val.Split(',');
                                int[] outfits = new int[outfitParts.Length];
                                for (int i = 0; i < outfitParts.Length; i++)
                                    int.TryParse(outfitParts[i], out outfits[i]);
                                rec.Metadata.Players[rec.Metadata.Players.Count - 1].Outfits = outfits;
                            }
                            break;
                        case "CamBounds":
                            var cb = val.Split(',');
                            if (cb.Length == 4)
                            {
                                float.TryParse(cb[0], out rec.Scene.CameraBoundsMinX);
                                float.TryParse(cb[1], out rec.Scene.CameraBoundsMaxX);
                                float.TryParse(cb[2], out rec.Scene.CameraBoundsMinY);
                                float.TryParse(cb[3], out rec.Scene.CameraBoundsMaxY);
                            }
                            break;
                        case "MinCharY":
                            float.TryParse(val, out rec.Scene.MinCharacterY);
                            break;
                        case "Snapshot":
                            try
                            {
                                rec.SnapshotBytes = System.Convert.FromBase64String(val);
                            }
                            catch
                            {
                                Plugin.Logger.LogError("[RecordingIO] Failed to decode snapshot base64");
                                rec.SnapshotBytes = null;
                            }
                            break;
                        case "SnapshotSize":
                            break;
                        case "PID":
                            var ps = new PlaceableSnapshot();
                            int.TryParse(val, out ps.ID);
                            rec.Scene.Placeables.Add(ps);
                            break;
                        case "PNm":
                            if (rec.Scene.Placeables.Count > 0)
                                rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].Name = val;
                            break;
                        case "PCat":
                            if (rec.Scene.Placeables.Count > 0)
                                rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].Category = val;
                            break;
                        case "PBy":
                            if (rec.Scene.Placeables.Count > 0)
                                int.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].PlacedByPlayer);
                            break;
                        case "PPos":
                            if (rec.Scene.Placeables.Count > 0)
                            {
                                var pp = val.Split(',');
                                if (pp.Length == 2)
                                {
                                    float.TryParse(pp[0], out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].PosX);
                                    float.TryParse(pp[1], out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].PosY);
                                }
                            }
                            break;
                        case "PRot":
                            if (rec.Scene.Placeables.Count > 0)
                                float.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].Rotation);
                            break;
                        case "PScl":
                            if (rec.Scene.Placeables.Count > 0)
                            {
                                var ps2 = val.Split(',');
                                if (ps2.Length == 2)
                                {
                                    float.TryParse(ps2[0], out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].ScaleX);
                                    float.TryParse(ps2[1], out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].ScaleY);
                                }
                            }
                            break;
                        case "PPar":
                            if (rec.Scene.Placeables.Count > 0)
                                int.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].ParentID);
                            break;
                        case "PSet":
                            if (rec.Scene.Placeables.Count > 0)
                                bool.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].IsSetPiece);
                            break;
                        case "PDmg":
                            if (rec.Scene.Placeables.Count > 0)
                                int.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].DamageLevel);
                            break;
                        case "PIdx":
                            if (rec.Scene.Placeables.Count > 0)
                                int.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].BlockIndex);
                            break;
                        case "PLvl":
                            if (rec.Scene.Placeables.Count > 0)
                                bool.TryParse(val, out rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].IsLevelGeometry);
                            break;
                        case "PClr":
                            if (rec.Scene.Placeables.Count > 0)
                                rec.Scene.Placeables[rec.Scene.Placeables.Count - 1].CustomColorHex = val;
                            break;
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
                            case "F":
                                float.TryParse(val, out snap.FlipSpriteX);
                                break;
                            case "V":
                                bool.TryParse(val, out snap.Visible);
                                break;
                            case "A":
                                var a = val.Split('|');
                                if (a.Length >= 2)
                                {
                                    snap.AnimationState = a[0];
                                    float.TryParse(a[1], out snap.AnimationTime);
                                }
                                if (a.Length >= 3)
                                {
                                    int.TryParse(a[2], out snap.AnimationStateHash);
                                }
                                break;
                        }
                    }
                }
            }

            Plugin.Logger.LogInfo($"[RecordingIO] Loaded recording: " +
                                  $"Name={rec.Name} Duration={rec.Duration:F1}s " +
                                  $"Frames={rec.Frames.Count} " +
                                  $"Players={rec.Metadata?.Players.Count ?? 0} " +
                                  $"ScenePieces={rec.Scene?.Placeables.Count ?? 0}");

            if (Plugin.CfgVerboseReplayLog.Value)
            {
                int badIdx = 0;
                foreach (var ps in rec.Scene.Placeables)
                    if (ps.BlockIndex < 0) badIdx++;
                if (badIdx > 0)
                    Plugin.Logger.LogWarning($"[RecordingIO] {badIdx} pieces have no BlockIndex " +
                                             $"(will use name fallback)");
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
