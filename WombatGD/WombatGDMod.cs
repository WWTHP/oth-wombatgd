using System.Text;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(WombatGD.WombatGDMod), "WombatGD", "0.5.5", "jims")]
[assembly: MelonGame(null, "over the hill")]

namespace WombatGD
{
    /// <summary>
    /// Makes the Wombat behave more like a 1980 Mercedes 300GD: snorkel and
    /// antenna on the passenger side, recorded diesel engine sound, and black diesel smoke
    /// (with a smoke-screen key). F7 opens the settings menu.
    /// </summary>
    public class WombatGDMod : MelonMod
    {
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Settings.Init();
            CrashGuard.Init();
            NetSync.Init();
            Log.Msg($"WombatGD loaded. {Settings.MenuKey.Value} = settings, " +
                    $"{Settings.SmokeScreenKey.Value} = smoke screen. Sounds: {DieselLayer.SoundsDir}");
        }

        public override void OnUpdate()
        {
            Profile.RefreshLocal();
            ConfigMenu.Update();
            NetSync.Update(DieselAudio.ActiveWombat);
            ApplyLocalChanges();
            DieselAudio.Update();
            DieselLayer.Update();
        }

        public override void OnGUI() => ConfigMenu.Draw();

        public override void OnApplicationQuit() => NetSync.Unhook();

        internal static bool IsWombat(CarAudioManager cam) =>
            cam != null && cam.vehicleType.ToString() == "Wombat";

        internal static void Apply(CarAudioManager cam, string reason)
        {
            if (!IsWombat(cam)) return;
            NetSync.Track(cam);
            SnorkelMirror.Set(cam.transform.root, Profile.For(cam).MirrorSnorkel);
            AntennaMount.Set(cam.transform.root, Profile.For(cam).PillarAntenna);
            DieselAudio.OnWombatAudioReady(cam);
            DieselLayer.Register(cam);
            if (Settings.DebugDump.Value) DumpVehicle(cam, reason);
        }

        private static bool? _lastMirror;
        private static string _lastLabel;
        private static (bool, float, float, float, float) _lastAntenna;

        /// <summary>Snorkel side and engine label are set once, so re-apply them when the menu changes them.</summary>
        private static void ApplyLocalChanges()
        {
            var cam = DieselAudio.ActiveWombat;
            if (cam == null || cam.WasCollected) return;
            var p = Profile.Local;
            var antenna = (p.PillarAntenna, Settings.AntennaForward.Value, Settings.AntennaHeight.Value,
                           Settings.AntennaSide.Value, Settings.AntennaTilt.Value);
            if (_lastMirror == p.MirrorSnorkel && _lastLabel == p.EngineSoundLabel && _lastAntenna == antenna) return;
            bool first = _lastMirror == null;
            _lastMirror = p.MirrorSnorkel;
            _lastLabel = p.EngineSoundLabel;
            _lastAntenna = antenna;
            if (!first) ApplyProfile(cam);
        }

        /// <summary>Re-applies the parts of a profile that are set once rather than every frame.</summary>
        internal static void ApplyProfile(CarAudioManager cam)
        {
            var p = Profile.For(cam);
            SnorkelMirror.Set(cam.transform.root, p.MirrorSnorkel);
            AntennaMount.Set(cam.transform.root, p.PillarAntenna);
            DieselAudio.ApplyLabel(cam, p.EngineSoundLabel);
        }

        internal static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
        {
            var lp = t.localPosition;
            sb.Append(' ', depth * 2)
              .Append(t.name)
              .Append($"  pos=({lp.x:F2},{lp.y:F2},{lp.z:F2}) active={t.gameObject.activeSelf}");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetIl2CppType().Name;
                if (n != "Transform") sb.Append(" [").Append(n).Append(']');
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), sb, depth + 1);
        }

        internal static void DumpVehicle(CarAudioManager cam, string reason)
        {
            var root = cam.transform.root;
            var sb = new StringBuilder();
            sb.AppendLine($"=== Wombat dump ({reason}) root='{root.name}' ===");
            sb.AppendLine($"vehicleTypeParameterName='{CarAudioManager.vehicleTypeParameterName}' vehicleTypeLabel='{cam.vehicleTypeLabel}'");
            sb.AppendLine($"isSnorkelEquipped={cam.isSnorkelEquipped}");
            DumpHierarchy(root, sb, 0);
            Log.Msg(sb.ToString());
        }
    }

    /// <summary>
    /// The snorkel models live under a "Snorkel" container centered on the car's
    /// X axis (SnorkelLevel01/02 sit at x=-0.83, driver side). A -1 X scale on
    /// the container mirrors them to the passenger side.
    /// </summary>
    internal static class SnorkelMirror
    {
        /// <summary>Puts the snorkel on the passenger (<paramref name="mirrored"/>) or stock driver side.</summary>
        public static void Set(Transform root, bool mirrored)
        {
            var container = Find(root);
            if (container == null)
            {
                if (mirrored) WombatGDMod.Log.Warning("Snorkel container not found on Wombat; nothing mirrored.");
                return;
            }
            var s = container.localScale;
            if ((s.x < 0f) == mirrored) return; // already on the wanted side
            container.localScale = new Vector3(mirrored ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
            WombatGDMod.Log.Msg(mirrored ? "Snorkel mirrored to passenger side." : "Snorkel on stock (driver) side.");
        }

        private static Transform Find(Transform t)
        {
            if (t.name == "Snorkel" && t.childCount > 0 && t.GetChild(0).name.StartsWith("SnorkelLevel"))
                return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = Find(t.GetChild(i));
                if (hit != null) return hit;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(CarAudioManager), nameof(CarAudioManager.Start))]
    internal static class CarAudioManagerStartPatch
    {
        private static void Postfix(CarAudioManager __instance) =>
            WombatGDMod.Apply(__instance, "Start");
    }

    [HarmonyPatch(typeof(CarAudioManager), nameof(CarAudioManager.MountedUpgradesChanged))]
    internal static class MountedUpgradesChangedPatch
    {
        private static void Postfix(CarAudioManager __instance) =>
            WombatGDMod.Apply(__instance, "MountedUpgradesChanged");
    }
}
