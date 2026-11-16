using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Diesel smoke for the Wombat.
    ///
    /// Each Wombat gets a few clones of the game's own exhaust particle system
    /// (one per puff texture), so they keep the game's renderer setup and its
    /// size/colour-over-life curves. We never touch individual particles (an
    /// earlier version rewrote them every frame with GetParticles/SetParticles
    /// on systems built from scratch, and that crashed the game natively); we
    /// only emit puffs with EmitParams, which has been reliable since 0.4.
    ///
    /// The look comes from how puffs are emitted:
    ///   - a dense, dark core leaves the pipe as a jet and is left behind in
    ///     world space as the truck drives on (the trail piles up into a cloud),
    ///   - bigger, softer billow puffs with sideways drift give it volume,
    ///   - negative gravity makes it rise, and the puff textures spin at random.
    /// Emission follows load: a light haze at idle, a heavy stream under throttle,
    /// and a dense black slug when the throttle is stabbed (more when lugging).
    /// Holding the smoke screen key lays down big, slow, long-lived clouds.
    /// </summary>
    internal static class CoalSmoke
    {
        // Physical-ish base values; the menu settings scale these.
        private const float BaseStreamSize = 0.45f;   // m, puff size at the pipe
        private const float BaseStreamLife = 4f;      // s
        private const float BaseScreenSize = 2.2f;
        private const float BaseScreenLife = 16f;
        private const float RedlineRpm = 4600f;       // OM617

        private const float DecaySeconds = 0.7f;   // puff fade time
        private const float TipInGain = 1.8f;      // puff per unit of throttle rise
        private const float SustainedFloor = 0.25f; // puff held at near-full throttle
        private const int MaxPerFrame = 120;       // guards against frame-time spikes
        private const int MaxPerSystem = 3000;

        private sealed class State
        {
            public CarAudioManager Cam;
            public Transform Root, Pipe;
            public List<ParticleSystem> Systems;
            public int Next;
            public Vector3 LastPos, Vel;
            public bool HavePos;
            public float LastThrottle, Puff, StreamAccum, ScreenAccum;
            public float AppliedRise = float.NaN;
            public ParticleSystem StockSys;
            public Renderer StockRenderer;
            public Color StockColor;
            public float AppliedTint = -2f;
            public int AppliedHide = -1;
        }

        private static readonly Dictionary<System.IntPtr, State> States = new();

        public static void Tick(ExhaustParticlesManager mgr)
        {
            if (!CrashGuard.Enter("smoke")) return;
            try { TickInner(mgr); }
            finally { CrashGuard.Exit("smoke"); }
        }

        private static void TickInner(ExhaustParticlesManager mgr)
        {
            var st = GetState(mgr);
            if (st == null) return;
            var stock = mgr.exhaustParticleSystem?.noDamageParticleSystem;
            if (stock == null) return;

            var pr = Profile.For(st.Cam);
            ApplyStockLook(st, stock, pr);
            if (st.Systems == null && !CreateSystems(st, stock)) return;
            if (st.Systems.Count == 0) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused

            // Buoyancy: hot exhaust rises (negative gravity on our clones).
            if (!Mathf.Approximately(st.AppliedRise, pr.Rise))
            {
                st.AppliedRise = pr.Rise;
                foreach (var ps in st.Systems)
                {
                    var main = ps.main;
                    main.gravityModifierMultiplier = -0.035f * pr.Rise;
                }
            }

            // Truck velocity from the pipe's motion.
            var pos = st.Pipe.position;
            if (st.HavePos)
            {
                var v = (pos - st.LastPos) / dt;
                if (v.sqrMagnitude < 80f * 80f) st.Vel = Vector3.Lerp(st.Vel, v, 1f - Mathf.Exp(-dt * 12f));
            }
            var prevPos = st.HavePos ? st.LastPos : pos;
            st.LastPos = pos;
            st.HavePos = true;

            EngineState.Read(st.Cam, out float rpm, out float thr);
            float rise = thr - st.LastThrottle;
            st.LastThrottle = thr;
            if (rise > 0f) st.Puff = Mathf.Min(1f, st.Puff + rise * TipInGain);
            if (thr > 0.85f) st.Puff = Mathf.Max(st.Puff, SustainedFloor);
            st.Puff *= Mathf.Exp(-dt / DecaySeconds);

            if (!IsGameExhaustRunning(mgr)) { st.StreamAccum = st.ScreenAccum = 0f; return; }

            // A naturally aspirated diesel smokes most when lugged: full throttle, low revs.
            float lug = 1f + 0.6f * (1f - Mathf.Clamp01(rpm / RedlineRpm));
            float rate = pr.IdleRate + thr * lug * pr.ThrottleRate + st.Puff * lug * pr.PuffRate;
            st.StreamAccum += rate * Mathf.Max(0f, pr.CoalAmount) * dt;
            float load = Mathf.Clamp01(thr * 0.7f + st.Puff);
            EmitStream(st, pr, prevPos, pos, load);

            if (pr.SmokeScreenActive)
            {
                st.ScreenAccum += pr.ScreenRate * dt;
                EmitScreen(st, pr, prevPos, pos);
            }
            else st.ScreenAccum = 0f;
        }

        // ---------- emission ----------

        private static Vector3 JetDir(State st)
        {
            // Out the back, angled slightly down and outboard like a side-exit tailpipe.
            var d = -st.Root.forward + st.Root.right * 0.15f - st.Root.up * 0.12f;
            return d.normalized;
        }

        private static Color32 SmokeColor(Profile pr, float alpha, float lighten)
        {
            float g = Mathf.Lerp(0.42f, 0.05f, Mathf.Clamp01(pr.Darkness)) + lighten;
            byte gb = (byte)(Mathf.Clamp01(g) * 255f);
            return new Color32(gb, gb, (byte)(Mathf.Clamp01(g * 0.96f) * 255f), (byte)(Mathf.Clamp01(alpha) * 255f));
        }

        private static void EmitStream(State st, Profile pr, Vector3 from, Vector3 to, float load)
        {
            int n = Take(ref st.StreamAccum);
            if (n == 0) return;
            float jet = pr.JetSpeed * (0.45f + 0.55f * load);
            float size = BaseStreamSize * pr.Size * (1f + 0.6f * load);
            float life = BaseStreamLife * pr.Linger;
            float dens = Mathf.Clamp01(pr.Density * (0.55f + 0.45f * load));
            var dir = JetDir(st);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(from, to, Random.value) + Random.insideUnitSphere * 0.05f;
                // The smoke keeps a little of the truck's speed; the rest of the trail is left behind.
                var v = st.Vel * 0.25f + dir * jet * Random.Range(0.7f, 1.2f);
                bool billow = (i & 3) == 3;
                if (billow)
                {
                    // Bigger, softer, slower puffs that drift sideways: the volume of the cloud.
                    v = v * 0.5f + Random.insideUnitSphere * (0.6f + 0.9f * pr.Billow);
                    Emit(st, p, v, size * pr.Spread * Random.Range(1.6f, 2.6f), life * Random.Range(1.1f, 1.5f),
                         SmokeColor(pr, dens * 0.55f, 0.08f));
                }
                else
                {
                    v += Random.insideUnitSphere * (0.3f + 0.4f * pr.Billow);
                    Emit(st, p, v, size * Mathf.Lerp(1f, pr.Spread, 0.5f) * Random.Range(0.7f, 1.3f),
                         life * Random.Range(0.7f, 1.1f), SmokeColor(pr, dens, 0f));
                }
            }
        }

        private static void EmitScreen(State st, Profile pr, Vector3 from, Vector3 to)
        {
            int n = Take(ref st.ScreenAccum);
            if (n == 0) return;
            float size = BaseScreenSize * pr.ScreenSize;
            float life = BaseScreenLife * pr.ScreenLinger;
            var dir = JetDir(st);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(from, to, Random.value) + Random.insideUnitSphere * 0.3f;
                var v = dir * pr.JetSpeed * 0.3f * Random.Range(0.5f, 1.2f) + Random.insideUnitSphere * (0.8f + 0.6f * pr.Billow);
                Emit(st, p, v, size * Random.Range(0.7f, 1.4f), life * Random.Range(0.7f, 1.3f),
                     SmokeColor(pr, Mathf.Clamp01(pr.Density * 0.8f + 0.1f), 0.06f));
            }
        }

        private static int Take(ref float accum)
        {
            int n = (int)accum;
            if (n <= 0) return 0;
            accum -= n;
            return Mathf.Min(n, MaxPerFrame);
        }

        private static void Emit(State st, Vector3 pos, Vector3 vel, float size, float life, Color32 color)
        {
            var ps = st.Systems[st.Next];
            st.Next = (st.Next + 1) % st.Systems.Count;
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-20f, 20f),
            };
            ps.Emit(ep, 1);
        }

        // ---------- setup ----------

        /// <summary>Clones the game's exhaust system once per puff material.</summary>
        private static bool CreateSystems(State st, ParticleSystem stock)
        {
            st.Systems = new List<ParticleSystem>();
            var stockRenderer = stock.GetComponent<ParticleSystemRenderer>();
            var mats = SmokeAssets.MaterialsFor(stockRenderer?.sharedMaterial);
            if (mats.Count == 0)
            {
                WombatGDMod.Log.Warning("No exhaust material found; coal smoke disabled for this truck.");
                return false;
            }

            for (int i = 0; i < mats.Count; i++)
            {
                var obj = Object.Instantiate(stock.gameObject, st.Pipe, false);
                var go = obj?.TryCast<GameObject>();
                if (go == null) continue;
                go.name = $"WombatGD_Coal{i}";
                var ps = go.GetComponent<ParticleSystem>();
                if (ps == null) { Object.Destroy(go); continue; }
                var emission = ps.emission;
                emission.enabled = false; // we emit manually
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = MaxPerSystem;

                var r = go.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.sharedMaterial = mats[i];
                    r.enabled = true; // the stock renderer may already be hidden
                }
                ps.Play();
                st.Systems.Add(ps);
            }
            WombatGDMod.Log.Msg($"Coal smoke ready: {st.Systems.Count} puff systems (cloned from the game's exhaust).");
            return st.Systems.Count > 0;
        }

        /// <summary>Hides or tints the game's own exhaust, restoring it when the settings change back.</summary>
        private static void ApplyStockLook(State st, ParticleSystem stock, Profile pr)
        {
            if (st.StockSys == null)
            {
                st.StockSys = stock;
                st.StockRenderer = stock.GetComponent<ParticleSystemRenderer>();
                st.StockColor = stock.main.startColor.color;
            }

            int hide = pr.HideStockExhaust ? 1 : 0;
            if (hide != st.AppliedHide && st.StockRenderer != null)
            {
                st.StockRenderer.enabled = hide == 0;
                st.AppliedHide = hide;
            }

            float want = pr.SootyExhaust ? Mathf.Clamp01(pr.Darkness) : -1f;
            if (Mathf.Approximately(want, st.AppliedTint)) return;
            st.AppliedTint = want;
            var main = stock.main;
            if (want < 0f)
            {
                main.startColor = new ParticleSystem.MinMaxGradient(st.StockColor);
                return;
            }
            float g = Mathf.Lerp(0.55f, 0.12f, want);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(g, g, g, Mathf.Max(st.StockColor.a, 0.85f)));
        }

        private static bool IsGameExhaustRunning(ExhaustParticlesManager mgr)
        {
            var active = mgr.exhaustParticleSystem.ActiveParticleSystem;
            return active != null && active.isEmitting;
        }

        internal static bool IsLocalPlayer(CarAudioManager cam)
        {
            var who = cam.emitterParameterValue;
            return string.IsNullOrEmpty(who) || who == "Player";
        }

        private static State GetState(ExhaustParticlesManager mgr)
        {
            var key = mgr.Pointer;
            if (States.TryGetValue(key, out var st)) return st;
            var root = mgr.transform.root;
            var cam = root.GetComponentInChildren<CarAudioManager>();
            // Non-Wombats are cached as null so we only search once per exhaust.
            st = WombatGDMod.IsWombat(cam) ? new State { Cam = cam, Root = root, Pipe = mgr.transform } : null;
            States[key] = st;
            return st;
        }

        public static void Forget(ExhaustParticlesManager mgr) => States.Remove(mgr.Pointer);
    }

    [HarmonyPatch(typeof(ExhaustParticlesManager), nameof(ExhaustParticlesManager.Update))]
    internal static class ExhaustUpdatePatch
    {
        private static void Postfix(ExhaustParticlesManager __instance) => CoalSmoke.Tick(__instance);
    }

    [HarmonyPatch(typeof(ExhaustParticlesManager), nameof(ExhaustParticlesManager.OnDestroy))]
    internal static class ExhaustDestroyPatch
    {
        private static void Prefix(ExhaustParticlesManager __instance) => CoalSmoke.Forget(__instance);
    }
}
