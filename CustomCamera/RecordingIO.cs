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

            sb.AppendLine($"SoundCount={rec.SoundEvents.Count}");
            foreach (var snd in rec.SoundEvents)
            {
                sb.AppendLine("---SND");
                sb.AppendLine($"ST={snd.Time}");
                sb.AppendLine($"SK={(byte)snd.SourceKind}");
                sb.AppendLine($"SN={snd.SourceID}");
                sb.AppendLine($"SE={snd.EventName}");
                sb.AppendLine($"SZ={snd.IsZombie}");
                sb.AppendLine($"SG={snd.IsGhost}");
            }

            sb.AppendLine($"PhaseEventCount={rec.PhaseEvents.Count}");
            if (rec.PhaseEvents.Count > 0)
            {
                sb.AppendLine("---PHASES");
                foreach (var pe in rec.PhaseEvents)
                {
                    sb.AppendLine($"T={pe.Time}");
                    sb.AppendLine($"P={pe.Phase}");
                }
            }

            sb.AppendLine($"PartyBoxEventCount={rec.PartyBoxEvents.Count}");
            foreach (var be in rec.PartyBoxEvents)
            {
                sb.AppendLine("---BOX");
                sb.AppendLine($"T={be.Time}");
                sb.AppendLine($"F={be.FlapsOpenTime}");
                sb.AppendLine($"O={be.Opened}");
                sb.AppendLine($"E={be.IsExtraBox}");
                sb.AppendLine($"ItemCount={be.Items.Count}");
                foreach (var item in be.Items)
                {
                    sb.AppendLine("---BITEM");
                    sb.AppendLine($"B={item.BlockIndex}");
                    sb.AppendLine($"X={item.LocalX:F4}");
                    sb.AppendLine($"Y={item.LocalY:F4}");
                }
            }

            sb.AppendLine($"ItemPickupCount={rec.ItemPickupEvents.Count}");
            foreach (var ip in rec.ItemPickupEvents)
            {
                sb.AppendLine("---IPICKUP");
                sb.AppendLine($"T={ip.Time}");
                sb.AppendLine($"C={ip.CursorNetNum}");
                sb.AppendLine($"B={ip.BlockIndex}");
                sb.AppendLine($"P={ip.PieceID}");
            }

            sb.AppendLine($"ItemPlacedCount={rec.ItemPlacedEvents.Count}");
            foreach (var ip in rec.ItemPlacedEvents)
            {
                sb.AppendLine("---IPLACED");
                sb.AppendLine($"T={ip.Time}");
                sb.AppendLine($"P={ip.PieceID}");
                sb.AppendLine($"XY={ip.PosX},{ip.PosY}");
                sb.AppendLine($"R={ip.RotZ}");
                sb.AppendLine($"S={ip.ScaleX},{ip.ScaleY}");
            }

            sb.AppendLine($"ItemDestroyedCount={rec.ItemDestroyedEvents.Count}");
            foreach (var id in rec.ItemDestroyedEvents)
            {
                sb.AppendLine("---IDESTROY");
                sb.AppendLine($"T={id.Time}");
                sb.AppendLine($"P={id.PieceID}");
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

                sb.AppendLine($"CursorCount={frame.Cursors.Count}");
                foreach (var cur in frame.Cursors)
                {
                    sb.AppendLine("---CUR");
                    sb.AppendLine($"N={cur.NetworkNumber}");
                    sb.AppendLine($"P={cur.PosX},{cur.PosY}");
                    sb.AppendLine($"V={cur.Visible}");
                }

                sb.AppendLine($"PickCursorCount={frame.PickCursors.Count}");
                foreach (var cur in frame.PickCursors)
                {
                    sb.AppendLine("---PCUR");
                    sb.AppendLine($"N={cur.NetworkNumber}");
                    sb.AppendLine($"P={cur.PosX},{cur.PosY}");
                    sb.AppendLine($"V={cur.Visible}");
                }

                sb.AppendLine($"ItemStateCount={frame.ItemStates.Count}");
                foreach (var st in frame.ItemStates)
                {
                    sb.AppendLine("---ISTATE");
                    sb.AppendLine($"P={st.PieceID}");
                    sb.AppendLine($"XY={st.PosX},{st.PosY}");
                    sb.AppendLine($"R={st.RotZ}");
                    sb.AppendLine($"S={st.ScaleX},{st.ScaleY}");
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        public static Recording Load(string path)
        {
            var rec = new Recording();
            RecordingFrame currentFrame = null;
            bool inPhaseSection = false;
            bool inBoxSection = false;
            bool inBoxItemBlock = false;
            bool inItemPickupBlock = false;
            bool inItemPlacedBlock = false;
            bool inItemDestroyBlock = false;
            bool inCursorBlock = false;
            bool inPickCursorBlock = false;
            bool inItemStateBlock = false;

            foreach (string line in File.ReadAllLines(path))
            {
                if (line == "---PHASES")
                {
                    inPhaseSection = true;
                    inBoxSection = false;
                    continue;
                }

                if (line == "---BOX")
                {
                    inBoxSection = true;
                    inPhaseSection = false;
                    inItemPickupBlock = false;
                    inItemPlacedBlock = false;
                    inItemDestroyBlock = false;
                    inBoxItemBlock = false;
                    rec.PartyBoxEvents.Add(new PartyBoxVisibilityEvent());
                    continue;
                }

                if (line == "---BITEM")
                {
                    inBoxItemBlock = true;
                    inBoxSection = false;
                    inPhaseSection = false;
                    inItemPickupBlock = false;
                    inItemPlacedBlock = false;
                    inItemDestroyBlock = false;
                    if (rec.PartyBoxEvents.Count > 0)
                        rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Items.Add(new BoxItemSnapshot());
                    continue;
                }

                if (line == "---IPICKUP")
                {
                    inItemPickupBlock = true;
                    inItemPlacedBlock = false;
                    inItemDestroyBlock = false;
                    inPhaseSection = false;
                    inBoxSection = false;
                    rec.ItemPickupEvents.Add(new ItemPickupEvent());
                    continue;
                }

                if (line == "---IPLACED")
                {
                    inItemPlacedBlock = true;
                    inItemPickupBlock = false;
                    inItemDestroyBlock = false;
                    inPhaseSection = false;
                    inBoxSection = false;
                    rec.ItemPlacedEvents.Add(new ItemPlacedEvent());
                    continue;
                }

                if (line == "---IDESTROY")
                {
                    inItemDestroyBlock = true;
                    inItemPickupBlock = false;
                    inItemPlacedBlock = false;
                    inPhaseSection = false;
                    inBoxSection = false;
                    rec.ItemDestroyedEvents.Add(new ItemDestroyedEvent());
                    continue;
                }

                if (line == "---FRAME")
                {
                    inPhaseSection = false;
                    inBoxSection = false;
                    inItemPickupBlock = false;
                    inItemPlacedBlock = false;
                    inItemDestroyBlock = false;
                    inCursorBlock = false;
                    inPickCursorBlock = false;
                    inItemStateBlock = false;
                    currentFrame = new RecordingFrame();
                    rec.Frames.Add(currentFrame);
                    continue;
                }

                if (line == "---CHAR")
                {
                    inCursorBlock = false;
                    inPickCursorBlock = false;
                    inItemStateBlock = false;
                    continue;
                }

                if (line == "---CUR")
                {
                    inCursorBlock = true;
                    inPickCursorBlock = false;
                    inItemStateBlock = false;
                    if (currentFrame != null)
                        currentFrame.Cursors.Add(new CursorSnapshot());
                    continue;
                }

                if (line == "---PCUR")
                {
                    inPickCursorBlock = true;
                    inCursorBlock = false;
                    inItemStateBlock = false;
                    if (currentFrame != null)
                        currentFrame.PickCursors.Add(new PickCursorSnapshot());
                    continue;
                }

                if (line == "---ISTATE")
                {
                    inItemStateBlock = true;
                    inPickCursorBlock = false;
                    inCursorBlock = false;
                    if (currentFrame != null)
                        currentFrame.ItemStates.Add(new ItemStateSnapshot());
                    continue;
                }

                if (line == "---PLAYER")
                    continue;

                if (line == "---PIECE")
                    continue;

                if (line == "---SND")
                {
                    // Default SourceKind=Character so old recordings (no SK field) round-trip correctly
                    rec.SoundEvents.Add(new SoundEvent { SourceKind = SoundSourceKind.Character });
                    continue;
                }

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
                                Plugin.Logger.LogError("[RecordingIO] Failed to decode snapshot base64 data");
                                rec.SnapshotBytes = null;
                            }
                            break;
                        case "SnapshotSize":
                            break;
                        case "SoundCount":
                            break;
                        case "PhaseEventCount":
                            break;
                        case "CursorCount":
                            break;
                        case "PickCursorCount":
                            break;
                        case "T":
                            if (inPhaseSection)
                            {
                                var pe = new PhaseEvent();
                                float.TryParse(val, out pe.Time);
                                rec.PhaseEvents.Add(pe);
                            }
                            else if (inBoxSection && rec.PartyBoxEvents.Count > 0)
                                float.TryParse(val, out rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Time);
                            else if (inItemPickupBlock && rec.ItemPickupEvents.Count > 0)
                                float.TryParse(val, out rec.ItemPickupEvents[rec.ItemPickupEvents.Count - 1].Time);
                            else if (inItemPlacedBlock && rec.ItemPlacedEvents.Count > 0)
                                float.TryParse(val, out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].Time);
                            else if (inItemDestroyBlock && rec.ItemDestroyedEvents.Count > 0)
                                float.TryParse(val, out rec.ItemDestroyedEvents[rec.ItemDestroyedEvents.Count - 1].Time);
                            break;
                        case "O":
                            if (inBoxSection && rec.PartyBoxEvents.Count > 0)
                                bool.TryParse(val, out rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Opened);
                            break;
                        case "E":
                            if (inBoxSection && rec.PartyBoxEvents.Count > 0)
                                bool.TryParse(val, out rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].IsExtraBox);
                            break;
                        case "F":
                            if (inBoxSection && rec.PartyBoxEvents.Count > 0)
                                float.TryParse(val, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].FlapsOpenTime);
                            break;
                        case "ItemCount":
                            // informational only — item list is built by ---BITEM blocks
                            break;
                        case "S":
                            if (inItemPlacedBlock && rec.ItemPlacedEvents.Count > 0)
                            {
                                var sc = val.Split(',');
                                if (sc.Length == 2)
                                {
                                    float.TryParse(sc[0], out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].ScaleX);
                                    float.TryParse(sc[1], out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].ScaleY);
                                }
                            }
                            break;
                        case "C":
                            if (inItemPickupBlock && rec.ItemPickupEvents.Count > 0)
                                int.TryParse(val, out rec.ItemPickupEvents[rec.ItemPickupEvents.Count - 1].CursorNetNum);
                            break;
                        case "B":
                            if (inBoxItemBlock && rec.PartyBoxEvents.Count > 0)
                            {
                                var boxItems = rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Items;
                                if (boxItems.Count > 0)
                                    int.TryParse(val, out boxItems[boxItems.Count - 1].BlockIndex);
                            }
                            else if (inItemPickupBlock && rec.ItemPickupEvents.Count > 0)
                                int.TryParse(val, out rec.ItemPickupEvents[rec.ItemPickupEvents.Count - 1].BlockIndex);
                            break;
                        case "X":
                            if (inBoxItemBlock && rec.PartyBoxEvents.Count > 0)
                            {
                                var boxItems = rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Items;
                                if (boxItems.Count > 0)
                                    float.TryParse(val, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out boxItems[boxItems.Count - 1].LocalX);
                            }
                            break;
                        case "Y":
                            if (inBoxItemBlock && rec.PartyBoxEvents.Count > 0)
                            {
                                var boxItems = rec.PartyBoxEvents[rec.PartyBoxEvents.Count - 1].Items;
                                if (boxItems.Count > 0)
                                    float.TryParse(val, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out boxItems[boxItems.Count - 1].LocalY);
                            }
                            break;
                        case "P":
                            if (inPhaseSection && rec.PhaseEvents.Count > 0)
                                rec.PhaseEvents[rec.PhaseEvents.Count - 1].Phase = val;
                            else if (inItemPickupBlock && rec.ItemPickupEvents.Count > 0)
                                int.TryParse(val, out rec.ItemPickupEvents[rec.ItemPickupEvents.Count - 1].PieceID);
                            else if (inItemPlacedBlock && rec.ItemPlacedEvents.Count > 0)
                                int.TryParse(val, out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].PieceID);
                            else if (inItemDestroyBlock && rec.ItemDestroyedEvents.Count > 0)
                                int.TryParse(val, out rec.ItemDestroyedEvents[rec.ItemDestroyedEvents.Count - 1].PieceID);
                            break;
                        case "XY":
                            if (inItemPlacedBlock && rec.ItemPlacedEvents.Count > 0)
                            {
                                var xy = val.Split(',');
                                if (xy.Length == 2)
                                {
                                    float.TryParse(xy[0], out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].PosX);
                                    float.TryParse(xy[1], out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].PosY);
                                }
                            }
                            break;
                        case "R":
                            if (inItemPlacedBlock && rec.ItemPlacedEvents.Count > 0)
                                float.TryParse(val, out rec.ItemPlacedEvents[rec.ItemPlacedEvents.Count - 1].RotZ);
                            break;
                        case "PartyBoxEventCount":
                        case "ItemPickupCount":
                        case "ItemPlacedCount":
                        case "ItemDestroyedCount":
                            break;
                        case "ST":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                float.TryParse(val, out snd.Time);
                            }
                            break;
                        case "SK":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                if (byte.TryParse(val, out byte kindByte))
                                    snd.SourceKind = (SoundSourceKind)kindByte;
                            }
                            break;
                        case "SN":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                int.TryParse(val, out snd.SourceID);
                            }
                            break;
                        case "SE":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                snd.EventName = val;
                            }
                            break;
                        case "SZ":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                bool.TryParse(val, out snd.IsZombie);
                            }
                            break;
                        case "SG":
                            if (rec.SoundEvents.Count > 0)
                            {
                                var snd = rec.SoundEvents[rec.SoundEvents.Count - 1];
                                bool.TryParse(val, out snd.IsGhost);
                            }
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
                    else if (inItemStateBlock)
                    {
                        if (currentFrame.ItemStates.Count > 0)
                        {
                            var state = currentFrame.ItemStates[currentFrame.ItemStates.Count - 1];
                            switch (key)
                            {
                                case "P": int.TryParse(val, out state.PieceID); break;
                                case "XY":
                                    var xy = val.Split(',');
                                    if (xy.Length == 2) { float.TryParse(xy[0], out state.PosX); float.TryParse(xy[1], out state.PosY); }
                                    break;
                                case "R": float.TryParse(val, out state.RotZ); break;
                                case "S":
                                    var sc = val.Split(',');
                                    if (sc.Length == 2) { float.TryParse(sc[0], out state.ScaleX); float.TryParse(sc[1], out state.ScaleY); }
                                    break;
                            }
                        }
                    }
                    else if (inPickCursorBlock)
                    {
                        // Pick cursor snapshot fields — block started by ---PCUR
                        if (currentFrame.PickCursors.Count > 0)
                        {
                            int last = currentFrame.PickCursors.Count - 1;
                            var cur = currentFrame.PickCursors[last];
                            switch (key)
                            {
                                case "N":
                                    int.TryParse(val, out cur.NetworkNumber);
                                    break;
                                case "P":
                                    var pp = val.Split(',');
                                    if (pp.Length == 2) { float.TryParse(pp[0], out cur.PosX); float.TryParse(pp[1], out cur.PosY); }
                                    break;
                                case "V":
                                    bool.TryParse(val, out cur.Visible);
                                    break;
                            }
                            currentFrame.PickCursors[last] = cur;
                        }
                    }
                    else if (inCursorBlock)
                    {
                        // Cursor snapshot fields — block started by ---CUR
                        if (currentFrame.Cursors.Count > 0)
                        {
                            int last = currentFrame.Cursors.Count - 1;
                            var cur = currentFrame.Cursors[last];
                            switch (key)
                            {
                                case "N":
                                    int.TryParse(val, out cur.NetworkNumber);
                                    break;
                                case "P":
                                    var cp = val.Split(',');
                                    if (cp.Length == 2) { float.TryParse(cp[0], out cur.PosX); float.TryParse(cp[1], out cur.PosY); }
                                    break;
                                case "V":
                                    bool.TryParse(val, out cur.Visible);
                                    break;
                            }
                            currentFrame.Cursors[last] = cur;
                        }
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
                        int last = currentFrame.Characters.Count - 1;
                        var snap = currentFrame.Characters[last];
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
                        currentFrame.Characters[last] = snap;
                    }
                }
            }

            bool hasCursors = rec.Frames.Count > 0 && rec.Frames[0].Cursors.Count > 0;
            bool hasPickCursors = rec.Frames.Count > 0 && rec.Frames[0].PickCursors.Count > 0;
            Plugin.Logger.LogInfo($"[RecordingIO] Loaded: Name={rec.Name} " +
                                  $"Frames={rec.Frames.Count} " +
                                  $"Players={rec.Metadata?.Players.Count ?? 0} " +
                                  $"PhaseEvents={rec.PhaseEvents.Count} " +
                                  $"BoxEvents={rec.PartyBoxEvents.Count} " +
                                  $"Pickups={rec.ItemPickupEvents.Count} " +
                                  $"Placed={rec.ItemPlacedEvents.Count} " +
                                  $"Destroyed={rec.ItemDestroyedEvents.Count} " +
                                  $"Cursors={hasCursors} PickCursors={hasPickCursors} " +
                                  $"SceneName={rec.Metadata?.SceneName ?? "none"} " +
                                  $"HasSnapshot={rec.SnapshotBytes != null} " +
                                  $"SnapshotBytes={rec.SnapshotBytes?.Length ?? 0}");

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
