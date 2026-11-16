using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppFMOD;
using Il2CppFMODUnity;
using MelonLoader.Utils;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Plays the diesel engine layer on top of the Wombat's stock engine sound
    /// using FMOD's core API, positioned at the engine.
    ///
    /// Two kinds of sound set:
    ///   * A bank: files named &lt;set&gt;_on_&lt;rpm&gt;.wav / &lt;set&gt;_off_&lt;rpm&gt;.wav
    ///     (e.g. the synthesized om617 bank). The two loops nearest the current
    ///     RPM are crossfaded, each pitched only slightly, and "on" (fuelled) vs
    ///     "off" (coasting) loops are crossfaded by load.
    ///   * "two files": the older idle + load recording pair, pitched by RPM.
    /// Files live in UserData/WombatGD/Sounds so they can be swapped freely.
    /// </summary>
    internal static class DieselLayer
    {
        public const string TwoFiles = "two files";
        private const float MinDistance = 3f, MaxDistance = 90f;
        private const float IdleRpm = 720f;

        private sealed class Layer
        {
            public Sound Snd;
            public float Rpm;
            public bool On;
        }

        /// <summary>A loaded sound set. Shared by every truck that uses it.</summary>
        private sealed class Bank
        {
            public string Key;
            public bool TwoFiles;
            public readonly List<Layer> Layers = new();
            public float[] Points = System.Array.Empty<float>();
            public bool Ok;
            public string Status = "";
        }

        private sealed class Voice
        {
            public CarAudioManager Cam;
            public Bank Bank;
            public Channel[] Chans;
            public bool HasChannels;
            public bool StockVolumeTouched;
            public float Load, Rpm;
        }

        private static readonly Dictionary<System.IntPtr, Voice> Voices = new();
        private static readonly Dictionary<string, Bank> Banks = new();
        private static ChannelGroup _group;
        private static bool _groupResolved;

        public static string SoundsDir => Path.Combine(MelonEnvironment.UserDataDirectory, "WombatGD", "Sounds");

        /// <summary>Status of the local player's sound set, for the menu.</summary>
        public static string Status
        {
            get
            {
                Profile.RefreshLocal();
                if (!Profile.Local.DieselLayerEnabled) return "off";
                return Banks.TryGetValue(KeyFor(Profile.Local), out var b) ? b.Status : "not loaded yet";
            }
        }

        /// <summary>Sound sets available: banks found in the Sounds folder, plus the two-file mode.</summary>
        public static List<string> AvailableSets()
        {
            var sets = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(SoundsDir))
                    foreach (var f in Directory.GetFiles(SoundsDir, "*_on_*.wav"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        sets.Add(name.Substring(0, name.LastIndexOf("_on_", System.StringComparison.Ordinal)));
                    }
            }
            catch (System.Exception ex) { WombatGDMod.Log.Warning("Could not list sound sets: " + ex.Message); }
            var list = new List<string>(sets) { TwoFiles };
            return list;
        }

        public static void Register(CarAudioManager cam)
        {
            if (!Voices.ContainsKey(cam.Pointer)) Voices[cam.Pointer] = new Voice { Cam = cam };
        }

        public static void Unregister(CarAudioManager cam)
        {
            if (!Voices.TryGetValue(cam.Pointer, out var v)) return;
            StopChannels(v);
            Voices.Remove(cam.Pointer);
        }

        /// <summary>Called every frame from the mod's OnUpdate.</summary>
        public static void Update()
        {
            if (Voices.Count == 0) return;
            if (!CrashGuard.Enter("sound")) return; // switched off after a crash; see the menu
            try { UpdateAll(); }
            finally { CrashGuard.Exit("sound"); }
        }

        private static void UpdateAll()
        {
            bool paused = Time.timeScale == 0f;
            var dead = new List<System.IntPtr>();
            foreach (var kv in Voices)
            {
                var v = kv.Value;
                if (v.Cam == null || v.Cam.WasCollected) { dead.Add(kv.Key); continue; }
                UpdateVoice(v, paused);
            }
            foreach (var k in dead) Voices.Remove(k);
        }

        private static void UpdateVoice(Voice v, bool paused)
        {
            var em = v.Cam.engineAudioEmitter;
            if (em == null) return;
            var inst = em.EventInstance;
            var pr = Profile.For(v.Cam);
            var bank = pr.DieselLayerEnabled ? GetBank(pr) : null;
            bool active = bank != null && bank.Ok;

            // Stock engine volume under the layer
            if (inst.isValid())
            {
                float want = active ? Mathf.Clamp01(pr.StockEngineVolume) : 1f;
                if (active || v.StockVolumeTouched)
                {
                    inst.setVolume(want);
                    v.StockVolumeTouched = active;
                }
            }

            EngineState.Read(v.Cam, out float rpm, out float thr);
            bool running = rpm > 100f;
            if (!active || !running || v.Bank != bank) StopChannels(v);
            if (!active || !running) return;

            if (!v.HasChannels || !AllAlive(v))
            {
                StopChannels(v);
                if (!StartChannels(v, bank)) return;
            }

            // Smooth the inputs a little so gear changes and throttle stabs don't click.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            v.Rpm = v.Rpm <= 0f ? rpm : Mathf.Lerp(v.Rpm, rpm, 1f - Mathf.Exp(-dt / 0.05f));
            // An idling diesel is still fuelled with the pedal up; coasting above idle is not.
            float idleFuel = Mathf.Clamp01(1f - (v.Rpm - IdleRpm) / 450f);
            float load = Mathf.Max(Mathf.Pow(thr, 0.6f), idleFuel);
            v.Load = Mathf.Lerp(v.Load, load, 1f - Mathf.Exp(-dt / 0.08f));

            var pos = em.transform.position.ToFMODVector();
            var vel = Vector3.zero.ToFMODVector();
            float vol = Mathf.Clamp(pr.DieselVolume, 0f, 1.5f);

            for (int i = 0; i < bank.Layers.Count; i++)
            {
                var ch = v.Chans[i];
                ComputeLayer(bank, i, v.Rpm, v.Load, thr, out float w, out float pitch);
                ch.set3DAttributes(ref pos, ref vel);
                ch.setPitch(pitch);
                ch.setVolume(vol * w);
                ch.setPaused(paused);
            }
        }

        /// <summary>Weight and pitch of layer <paramref name="i"/> at this RPM and load.</summary>
        private static void ComputeLayer(Bank bank, int i, float rpm, float load, float thr, out float w, out float pitch)
        {
            var l = bank.Layers[i];
            if (bank.TwoFiles)
            {
                // Two-file mode: idle (Layers[0]) and load (Layers[1]) recordings.
                float idleRec = bank.Layers[0].Rpm, loadRec = bank.Layers[1].Rpm;
                float scale = Mathf.Clamp01(Settings.DieselPitchScale.Value);
                pitch = Mathf.Clamp(Mathf.Lerp(1f, rpm / l.Rpm, scale), 0.5f, 2.5f);
                float rpmT = Mathf.Clamp01((rpm - idleRec) / Mathf.Max(100f, loadRec - idleRec));
                float x = Mathf.Clamp01(0.55f * thr + 0.45f * rpmT);
                w = l.On ? Mathf.Sin(x * Mathf.PI * 0.5f) : Mathf.Cos(x * Mathf.PI * 0.5f);
                return;
            }

            // Bank: equal-power crossfade between the two nearest RPM points...
            var pts = bank.Points;
            float wr = 0f;
            if (rpm <= pts[0]) wr = l.Rpm == pts[0] ? 1f : 0f;
            else if (rpm >= pts[pts.Length - 1]) wr = l.Rpm == pts[pts.Length - 1] ? 1f : 0f;
            else
            {
                for (int k = 0; k < pts.Length - 1; k++)
                {
                    if (rpm < pts[k] || rpm >= pts[k + 1]) continue;
                    float f = (rpm - pts[k]) / (pts[k + 1] - pts[k]);
                    if (l.Rpm == pts[k]) wr = Mathf.Cos(f * Mathf.PI * 0.5f);
                    else if (l.Rpm == pts[k + 1]) wr = Mathf.Sin(f * Mathf.PI * 0.5f);
                    break;
                }
            }
            // ...and between fuelled and coasting loops by load.
            float wl = l.On ? Mathf.Sin(load * Mathf.PI * 0.5f) : Mathf.Cos(load * Mathf.PI * 0.5f);
            w = wr * wl;
            pitch = Mathf.Clamp(rpm / l.Rpm, 0.5f, 2f);
        }

        private static bool StartChannels(Voice v, Bank bank)
        {
            var core = RuntimeManager.CoreSystem;
            var group = GetGroup();
            v.Chans = new Channel[bank.Layers.Count];
            for (int i = 0; i < bank.Layers.Count; i++)
            {
                if (core.playSound(bank.Layers[i].Snd, group, true, out v.Chans[i]) != RESULT.OK)
                {
                    for (int j = 0; j < i; j++) v.Chans[j].stop();
                    return false;
                }
                v.Chans[i].set3DMinMaxDistance(MinDistance, MaxDistance);
                v.Chans[i].setVolume(0f);
                // Start each loop at a random point so the layers don't line up.
                if (bank.Layers[i].Snd.getLength(out uint len, TIMEUNIT.PCM) == RESULT.OK && len > 0)
                    v.Chans[i].setPosition((uint)Random.Range(0, (int)len), TIMEUNIT.PCM);
            }
            v.Bank = bank;
            v.HasChannels = true;
            return true;
        }

        private static void StopChannels(Voice v)
        {
            if (!v.HasChannels) return;
            foreach (var ch in v.Chans) ch.stop();
            v.HasChannels = false;
            v.Bank = null;
        }

        private static bool AllAlive(Voice v)
        {
            foreach (var ch in v.Chans)
                if (ch.isPlaying(out bool playing) != RESULT.OK || !playing) return false;
            return true;
        }

        /// <summary>Forces sound files to be reloaded (e.g. after changing files).</summary>
        public static void ReloadSounds()
        {
            foreach (var v in Voices.Values) StopChannels(v);
            foreach (var b in Banks.Values)
                foreach (var l in b.Layers) l.Snd.release();
            Banks.Clear();
        }

        /// <summary>Two-file mode always uses this computer's own file choices.</summary>
        private static string KeyFor(Profile p) => p.DieselSoundSet != TwoFiles
            ? p.DieselSoundSet
            : $"{TwoFiles}|{Settings.DieselIdleFile.Value}|{Settings.DieselLoadFile.Value}|" +
              $"{Settings.DieselIdleRecordedRpm.Value}|{Settings.DieselLoadRecordedRpm.Value}";

        private static Bank GetBank(Profile p)
        {
            var key = KeyFor(p);
            if (Banks.TryGetValue(key, out var bank)) return bank;
            bank = new Bank { Key = key, TwoFiles = p.DieselSoundSet == TwoFiles };
            Banks[key] = bank;
            Load(bank, p.DieselSoundSet);
            return bank;
        }

        private static void Load(Bank bank, string set)
        {
            Directory.CreateDirectory(SoundsDir);
            var wanted = new List<(string file, float rpm, bool on)>();
            if (!bank.TwoFiles)
            {
                foreach (var f in Directory.GetFiles(SoundsDir, set + "_*_*.wav"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var parts = name.Substring(set.Length + 1).Split('_');
                    if (parts.Length != 2 || (parts[0] != "on" && parts[0] != "off")) continue;
                    if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float r) || r < 100f || r > 12000f) continue;
                    wanted.Add((Path.GetFileName(f), r, parts[0] == "on"));
                }
                if (wanted.Count == 0)
                {
                    bank.Status = $"no '{set}_on_<rpm>.wav' files in Sounds";
                    WombatGDMod.Log.Warning($"Diesel layer: sound set '{set}' not found in {SoundsDir}");
                    return;
                }
            }
            else
            {
                wanted.Add((Settings.DieselIdleFile.Value, Mathf.Max(100f, Settings.DieselIdleRecordedRpm.Value), false));
                wanted.Add((Settings.DieselLoadFile.Value, Mathf.Max(200f, Settings.DieselLoadRecordedRpm.Value), true));
            }

            var core = RuntimeManager.CoreSystem;
            const MODE mode = MODE.LOOP_NORMAL | MODE._3D | MODE.CREATESAMPLE | MODE._3D_LINEARSQUAREROLLOFF;
            foreach (var (file, rpm, on) in wanted)
            {
                var path = Path.Combine(SoundsDir, file);
                Sound snd = default;
                var res = File.Exists(path) ? core.createSound(path, mode, out snd) : RESULT.ERR_FILE_NOTFOUND;
                if (res != RESULT.OK)
                {
                    bank.Status = $"could not load {file} ({res})";
                    WombatGDMod.Log.Error($"Diesel layer: FMOD could not load {path}: {res}");
                    foreach (var l in bank.Layers) l.Snd.release();
                    bank.Layers.Clear();
                    return;
                }
                bank.Layers.Add(new Layer { Snd = snd, Rpm = rpm, On = on });
            }

            var pts = new SortedSet<float>();
            foreach (var l in bank.Layers) pts.Add(l.Rpm);
            bank.Points = new float[pts.Count];
            pts.CopyTo(bank.Points);
            bank.Ok = true;
            bank.Status = bank.TwoFiles
                ? $"playing {wanted[0].file} + {wanted[1].file}"
                : $"{set}: {bank.Layers.Count} loops, {bank.Points[0]:F0}-{bank.Points[bank.Points.Length - 1]:F0} rpm";
            WombatGDMod.Log.Msg("Diesel layer loaded: " + bank.Status);
        }

        /// <summary>
        /// Routes the layer through the game's vehicle/SFX bus when one can be
        /// found, so the game's volume sliders apply; otherwise the master group.
        /// </summary>
        private static ChannelGroup GetGroup()
        {
            if (_groupResolved) return _group;
            _groupResolved = true;
            RuntimeManager.CoreSystem.getMasterChannelGroup(out _group);

            var studio = RuntimeManager.StudioSystem;
            if (studio.getBankList(out var banks) != RESULT.OK || banks == null) return _group;
            Il2CppFMOD.Studio.Bus best = default;
            int bestScore = 0;
            var names = new List<string>();
            foreach (var bank in banks)
            {
                if (bank.getBusList(out var buses) != RESULT.OK || buses == null) continue;
                foreach (var bus in buses)
                {
                    if (bus.getPath(out string path) != RESULT.OK || path == null) continue;
                    names.Add(path);
                    var p = path.ToLowerInvariant();
                    int score = p.Contains("vehicle") ? 3 : p.Contains("sfx") ? 2 : 0;
                    if (p.Contains("engine")) score += 1;
                    if (score > bestScore) { bestScore = score; best = bus; }
                }
            }
            WombatGDMod.Log.Msg("FMOD buses: " + string.Join(", ", names));
            if (bestScore == 0) return _group;

            best.lockChannelGroup();
            studio.flushCommands();
            if (best.getChannelGroup(out var cg) == RESULT.OK)
            {
                _group = cg;
                best.getPath(out string chosen);
                WombatGDMod.Log.Msg($"Diesel layer routed through {chosen}");
            }
            return _group;
        }
    }

    [HarmonyPatch(typeof(CarAudioManager), nameof(CarAudioManager.OnDestroy))]
    internal static class CarAudioManagerDestroyPatch
    {
        private static void Prefix(CarAudioManager __instance)
        {
            DieselLayer.Unregister(__instance);
            NetSync.Forget(__instance);
        }
    }
}
