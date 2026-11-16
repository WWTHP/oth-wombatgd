using System.Collections.Generic;
using Il2Cpp;

namespace WombatGD
{
    /// <summary>
    /// Everything that decides how one Wombat looks and sounds. The local player's
    /// truck uses a profile built from their settings; other players' trucks use
    /// the profile their owner broadcast (see <see cref="NetSync"/>), or fall back
    /// to the local settings when the owner doesn't run the mod.
    /// </summary>
    internal sealed partial class Profile
    {
        /// <summary>The local player's profile, rebuilt from settings once per frame.</summary>
        public static Profile Local { get; private set; } = new Profile();
        private static int _localFrame = -1;

        public static void RefreshLocal()
        {
            int f = UnityEngine.Time.frameCount;
            if (f == _localFrame) return;
            _localFrame = f;
            Local = FromSettings();
            Fallback = FromSettings();
            Fallback.SmokeScreenActive = false; // our F9 key only ever drives our own truck
        }

        /// <summary>Used for other players' trucks when their owner doesn't run the mod.</summary>
        private static Profile Fallback = new Profile();

        private static readonly Dictionary<uint, Profile> Remote = new();

        /// <summary>The profile that applies to the truck owning <paramref name="cam"/>.</summary>
        public static Profile For(CarAudioManager cam)
        {
            RefreshLocal();
            if (cam == null || CoalSmoke.IsLocalPlayer(cam)) return Local;
            uint id = NetSync.VehicleId(cam);
            return id != 0 && Remote.TryGetValue(id, out var p) ? p : Fallback;
        }

        public static bool SetRemote(uint vehicleId, Profile p)
        {
            bool snorkelChanged = !Remote.TryGetValue(vehicleId, out var old) || old.MirrorSnorkel != p.MirrorSnorkel
                                  || old.PillarAntenna != p.PillarAntenna
                                  || old.EngineSoundLabel != p.EngineSoundLabel;
            Remote[vehicleId] = p;
            return snorkelChanged;
        }

        public static void ClearRemote() => Remote.Clear();

        public static Profile FromSettings() => new Profile
        {
            MirrorSnorkel = Settings.MirrorSnorkel.Value,
            PillarAntenna = Settings.PillarAntenna.Value,
            DieselLayerEnabled = Settings.DieselLayerEnabled.Value,
            HideStockExhaust = Settings.HideStockExhaust.Value,
            SootyExhaust = Settings.SootyExhaust.Value,
            SmokeScreenActive = Settings.Held(Settings.SmokeScreenKey) && !ConfigMenu.IsOpen,
            EngineSoundLabel = Settings.EngineSoundLabel.Value ?? "",
            DieselSoundSet = Settings.DieselSoundSet.Value ?? "",
            DieselVolume = Settings.DieselVolume.Value,
            StockEngineVolume = Settings.StockEngineVolume.Value,
            CoalAmount = Settings.CoalAmount.Value,
            IdleRate = Settings.SmokeIdleRate.Value,
            ThrottleRate = Settings.SmokeThrottleRate.Value,
            PuffRate = Settings.SmokePuffRate.Value,
            Size = Settings.SmokeSize.Value,
            Linger = Settings.SmokeLinger.Value,
            Darkness = Settings.SmokeDarkness.Value,
            Density = Settings.SmokeDensity.Value,
            JetSpeed = Settings.SmokeJetSpeed.Value,
            Billow = Settings.SmokeBillow.Value,
            Rise = Settings.SmokeRise.Value,
            Spread = Settings.SmokeSpread.Value,
            ScreenRate = Settings.SmokeScreenRate.Value,
            ScreenSize = Settings.SmokeScreenSize.Value,
            ScreenLinger = Settings.SmokeScreenLinger.Value,
        };
    }
}
