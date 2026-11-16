using System;
using System.IO;
using System.Text;

namespace WombatGD
{
    // Pure part of Profile: the data and its network wire format. This file must
    // not reference Unity, Il2Cpp or MelonLoader; it is compiled into tests/.
    // Game-side glue (settings, lookup by car) lives in ../Profile.cs.
    internal sealed partial class Profile
    {
        public const byte WireVersion = 1;

        public bool MirrorSnorkel, PillarAntenna;
        public bool DieselLayerEnabled, HideStockExhaust, SootyExhaust, SmokeScreenActive;
        public string EngineSoundLabel = "", DieselSoundSet = "om617";
        public float DieselVolume, StockEngineVolume;
        public float CoalAmount, IdleRate, ThrottleRate, PuffRate, Size, Linger, Darkness, Density,
                     JetSpeed, Billow, Rise, Spread, ScreenRate, ScreenSize, ScreenLinger;

        // ---------- wire format ----------
        // [version:1][vehicleId:4][flags:1][label:str][soundSet:str][17 x float32]

        public byte[] Serialize(uint vehicleId)
        {
            using var ms = new MemoryStream(128);
            using var w = new BinaryWriter(ms);
            w.Write(WireVersion);
            w.Write(vehicleId);
            byte flags = 0;
            if (MirrorSnorkel) flags |= 1;
            if (DieselLayerEnabled) flags |= 2;
            if (HideStockExhaust) flags |= 4;
            if (SootyExhaust) flags |= 8;
            if (SmokeScreenActive) flags |= 16;
            if (PillarAntenna) flags |= 32; // older WombatGD versions ignore this bit
            w.Write(flags);
            WriteStr(w, EngineSoundLabel);
            WriteStr(w, DieselSoundSet);
            foreach (var f in Floats()) w.Write(f);
            return ms.ToArray();
        }

        private float[] Floats() => new[]
        {
            DieselVolume, StockEngineVolume, CoalAmount, IdleRate, ThrottleRate, PuffRate, Size, Linger,
            Darkness, Density, JetSpeed, Billow, Rise, Spread, ScreenRate, ScreenSize, ScreenLinger,
        };

        // Allowed range for each float, in Floats() order (matches the menu sliders).
        private static readonly (float min, float max)[] Ranges =
        {
            (0f, 1.5f), (0f, 1f), (0f, 4f), (0f, 80f), (0f, 250f), (0f, 500f), (0.3f, 4f), (0.3f, 5f),
            (0f, 1f), (0.1f, 1f), (0f, 20f), (0f, 3f), (0f, 3f), (0.2f, 3f), (0f, 400f), (0.3f, 4f), (0.2f, 4f),
        };

        /// <summary>Parses and validates a profile from the network. Returns null if malformed.</summary>
        public static Profile Deserialize(byte[] data, out uint vehicleId)
        {
            vehicleId = 0;
            if (data == null || data.Length < 6 || data.Length > 512) return null;
            try
            {
                using var r = new BinaryReader(new MemoryStream(data));
                if (r.ReadByte() != WireVersion) return null;
                vehicleId = r.ReadUInt32();
                byte flags = r.ReadByte();
                var p = new Profile
                {
                    MirrorSnorkel = (flags & 1) != 0,
                    DieselLayerEnabled = (flags & 2) != 0,
                    HideStockExhaust = (flags & 4) != 0,
                    SootyExhaust = (flags & 8) != 0,
                    SmokeScreenActive = (flags & 16) != 0,
                    PillarAntenna = (flags & 32) != 0,
                    EngineSoundLabel = ReadStr(r),
                    DieselSoundSet = ReadStr(r),
                };
                var v = new float[Ranges.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    float f = r.ReadSingle();
                    if (float.IsNaN(f) || float.IsInfinity(f) || f < Ranges[i].min || f > Ranges[i].max) return null;
                    v[i] = f;
                }
                if (p.EngineSoundLabel == null || p.DieselSoundSet == null) return null;
                (p.DieselVolume, p.StockEngineVolume, p.CoalAmount, p.IdleRate, p.ThrottleRate, p.PuffRate,
                 p.Size, p.Linger) = (v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
                (p.Darkness, p.Density, p.JetSpeed, p.Billow, p.Rise, p.Spread, p.ScreenRate, p.ScreenSize,
                 p.ScreenLinger) = (v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15], v[16]);
                return p;
            }
            catch (EndOfStreamException) { return null; }
        }

        private static void WriteStr(BinaryWriter w, string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > 32) Array.Resize(ref b, 32);
            w.Write((byte)b.Length);
            w.Write(b);
        }

        /// <summary>Names are limited to 32 chars of letters, digits, space, '_' and '-'.</summary>
        private static string ReadStr(BinaryReader r)
        {
            int n = r.ReadByte();
            if (n > 32) return null;
            var s = Encoding.UTF8.GetString(r.ReadBytes(n));
            foreach (var c in s)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')) return null;
            return s;
        }
    }
}
