using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppFMOD.Studio;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Swaps the Wombat's engine sound set. The engine FMOD event takes a labeled
    /// "vehicleType" parameter (one label per vehicle); we override the label on
    /// the Wombat's engine emitter only. The cycle key (F8) or the settings menu
    /// steps through labels live and saves the pick.
    /// </summary>
    internal static class DieselAudio
    {
        private const string ParamName = "vehicleType";
        private static CarAudioManager _activeWombat;
        public static CarAudioManager ActiveWombat => _activeWombat;
        private static List<string> _labels;

        public static void OnWombatAudioReady(CarAudioManager cam)
        {
            if (CoalSmoke.IsLocalPlayer(cam)) _activeWombat = cam;
            if (_labels == null) _labels = ReadLabels(cam);
            ApplyLabel(cam, Profile.For(cam).EngineSoundLabel);
        }

        /// <summary>Called every frame from the mod's OnUpdate.</summary>
        public static void Update()
        {
            if (Settings.Pressed(Settings.EngineSoundCycleKey) && !ConfigMenu.IsOpen) Cycle(+1);
        }

        /// <summary>Steps the stock engine sound set forward/back and saves it.</summary>
        public static void Cycle(int dir)
        {
            if (_activeWombat == null || _labels == null || _labels.Count == 0)
            {
                WombatGDMod.Log.Msg("No Wombat engine active; get in the Wombat first.");
                return;
            }
            var current = Settings.EngineSoundLabel.Value;
            if (string.IsNullOrEmpty(current)) current = "Wombat";
            int i = _labels.IndexOf(current);
            if (i < 0) i = 0;
            var next = _labels[((i + dir) % _labels.Count + _labels.Count) % _labels.Count];
            Settings.EngineSoundLabel.Value = next == "Wombat" ? "" : next;
            Settings.Save();
            ApplyLabel(_activeWombat, next);
            WombatGDMod.Log.Msg($"Engine sound -> '{next}' (saved)");
        }

        /// <summary>Sets the engine's vehicle sound label; empty means the stock Wombat set.</summary>
        public static void ApplyLabel(CarAudioManager cam, string label)
        {
            if (string.IsNullOrEmpty(label)) label = "Wombat";
            var em = cam.engineAudioEmitter;
            if (em == null) return;
            var res = em.EventInstance.setParameterByNameWithLabel(ParamName, label);
            if (res != Il2CppFMOD.RESULT.OK)
                WombatGDMod.Log.Warning($"Engine label '{label}' rejected by FMOD: {res}");
        }

        /// <summary>The Wombat whose engine emitter this is, or null.</summary>
        public static CarAudioManager WombatOf(AudioEmitter emitter)
        {
            if (emitter == null) return null;
            var cam = emitter.transform.root.GetComponentInChildren<CarAudioManager>();
            return WombatGDMod.IsWombat(cam)
                && cam.engineAudioEmitter != null
                && cam.engineAudioEmitter.Pointer == emitter.Pointer ? cam : null;
        }

        private static List<string> ReadLabels(CarAudioManager cam)
        {
            var labels = new List<string>();
            var em = cam.engineAudioEmitter;
            if (em == null) return labels;
            var desc = em.EventDescription;
            var sb = new StringBuilder();
            desc.getPath(out string path);
            sb.AppendLine($"Engine event: {path}");
            desc.getParameterDescriptionCount(out int count);
            for (int p = 0; p < count; p++)
            {
                desc.getParameterDescriptionByIndex(p, out PARAMETER_DESCRIPTION pd);
                string name = pd.name;
                sb.AppendLine($"  param '{name}' [{pd.minimum}..{pd.maximum}] type={pd.type}");
                for (int l = 0; ; l++)
                {
                    if (desc.getParameterLabelByIndex(p, l, out string lab) != Il2CppFMOD.RESULT.OK) break;
                    sb.AppendLine($"    label {l}: {lab}");
                    if (name == ParamName) labels.Add(lab);
                }
            }
            WombatGDMod.Log.Msg(sb.ToString());
            return labels;
        }
    }

    /// <summary>Keeps our label when the game (re)applies vehicle labels to the engine event.</summary>
    [HarmonyPatch(typeof(CarAudioManager), nameof(CarAudioManager.SetLabeledParameterIfPresent))]
    internal static class SetLabeledParameterPatch
    {
        private static void Prefix(AudioEmitter audioEmitter, string parameterName, ref string label)
        {
            if (parameterName != "vehicleType") return;
            var cam = DieselAudio.WombatOf(audioEmitter);
            if (cam == null) return;
            var wanted = Profile.For(cam).EngineSoundLabel;
            if (!string.IsNullOrEmpty(wanted)) label = wanted;
        }
    }
}
