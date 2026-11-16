using System;
using MelonLoader;
using UnityEngine.InputSystem;

namespace WombatGD
{
    /// <summary>All user-tunable settings, stored in UserData/MelonPreferences.cfg [WombatGD].</summary>
    internal static class Settings
    {
        internal static MelonPreferences_Category Cat;

        // General
        internal static MelonPreferences_Entry<bool> MirrorSnorkel;
        internal static MelonPreferences_Entry<bool> PillarAntenna;
        internal static MelonPreferences_Entry<float> AntennaForward;
        internal static MelonPreferences_Entry<float> AntennaHeight;
        internal static MelonPreferences_Entry<float> AntennaSide;
        internal static MelonPreferences_Entry<float> AntennaTilt;
        internal static MelonPreferences_Entry<bool> MultiplayerSync;
        internal static MelonPreferences_Entry<bool> DebugDump;
        internal static MelonPreferences_Entry<string> MenuKey;

        // Engine sound
        internal static MelonPreferences_Entry<string> EngineSoundLabel;
        internal static MelonPreferences_Entry<string> EngineSoundCycleKey;
        internal static MelonPreferences_Entry<bool> DieselLayerEnabled;
        internal static MelonPreferences_Entry<string> DieselSoundSet;
        internal static MelonPreferences_Entry<float> DieselVolume;
        internal static MelonPreferences_Entry<float> StockEngineVolume;
        internal static MelonPreferences_Entry<string> DieselIdleFile;
        internal static MelonPreferences_Entry<string> DieselLoadFile;
        internal static MelonPreferences_Entry<float> DieselIdleRecordedRpm;
        internal static MelonPreferences_Entry<float> DieselLoadRecordedRpm;
        internal static MelonPreferences_Entry<float> DieselPitchScale;

        // Smoke
        internal static MelonPreferences_Entry<float> CoalAmount;
        internal static MelonPreferences_Entry<float> SmokeIdleRate;
        internal static MelonPreferences_Entry<float> SmokeThrottleRate;
        internal static MelonPreferences_Entry<float> SmokePuffRate;
        internal static MelonPreferences_Entry<float> SmokeSize;
        internal static MelonPreferences_Entry<float> SmokeLinger;
        internal static MelonPreferences_Entry<float> SmokeDarkness;
        internal static MelonPreferences_Entry<bool> SootyExhaust;
        internal static MelonPreferences_Entry<bool> HideStockExhaust;
        internal static MelonPreferences_Entry<float> SmokeDensity;
        internal static MelonPreferences_Entry<float> SmokeJetSpeed;
        internal static MelonPreferences_Entry<float> SmokeBillow;
        internal static MelonPreferences_Entry<float> SmokeRise;
        internal static MelonPreferences_Entry<float> SmokeSpread;
        internal static MelonPreferences_Entry<int> ConfigVersion;
        private const int CurrentConfigVersion = 2;

        // Smoke screen
        internal static MelonPreferences_Entry<string> SmokeScreenKey;
        internal static MelonPreferences_Entry<float> SmokeScreenRate;
        internal static MelonPreferences_Entry<float> SmokeScreenSize;
        internal static MelonPreferences_Entry<float> SmokeScreenLinger;

        public static void Init()
        {
            Cat = MelonPreferences.CreateCategory("WombatGD");

            MirrorSnorkel = Cat.CreateEntry("MirrorSnorkel", true,
                description: "Move the Wombat's snorkel to the passenger (right) side, like a 300GD.");
            PillarAntenna = Cat.CreateEntry("PillarAntenna", true,
                description: "Move the antenna from the fender to the passenger A-pillar, like a 300GD.");
            AntennaForward = Cat.CreateEntry("AntennaForward", 0f,
                description: "Nudge the A-pillar antenna forward (+) or back (-), metres.");
            AntennaHeight = Cat.CreateEntry("AntennaHeight", 0f,
                description: "Nudge the A-pillar antenna up (+) or down (-), metres.");
            AntennaSide = Cat.CreateEntry("AntennaSide", 0f,
                description: "Nudge the A-pillar antenna outward (+) or inward (-), metres.");
            AntennaTilt = Cat.CreateEntry("AntennaTilt", 0f,
                description: "Extra backward lean of the A-pillar antenna on top of the pillar's own rake, degrees.");
            MultiplayerSync = Cat.CreateEntry("MultiplayerSync", true,
                description: "Share your Wombat's look and sound with other players running WombatGD (takes effect next map load).");
            DebugDump = Cat.CreateEntry("DebugDump", false,
                description: "Log the Wombat's full object hierarchy (for mod development).");
            MenuKey = Cat.CreateEntry("MenuKey", "F7",
                description: "Key that opens the WombatGD settings menu (Unity Input System key name).");

            EngineSoundLabel = Cat.CreateEntry("EngineSoundLabel", "",
                description: "Stock engine sound set for the Wombat (another vehicle's label). Empty = stock Wombat.");
            EngineSoundCycleKey = Cat.CreateEntry("EngineSoundCycleKey", "F8",
                description: "Key that cycles the stock engine sound set while driving the Wombat.");
            DieselLayerEnabled = Cat.CreateEntry("DieselLayerEnabled", true,
                description: "Play the recorded diesel sounds from UserData/WombatGD/Sounds over the engine.");
            DieselSoundSet = Cat.CreateEntry("DieselSoundSet", "om617",
                description: "Diesel sound set: a bank name (files <set>_on_<rpm>.wav / <set>_off_<rpm>.wav) or \"two files\".");
            DieselVolume = Cat.CreateEntry("DieselVolume", 0.8f,
                description: "Volume of the recorded diesel layer (0-1.5).");
            StockEngineVolume = Cat.CreateEntry("StockEngineVolume", 0.5f,
                description: "Volume of the game's own engine sound under the diesel layer (0-1).");
            DieselIdleFile = Cat.CreateEntry("DieselIdleFile", "idle.wav",
                description: "Idle loop file in UserData/WombatGD/Sounds (wav/ogg/mp3/flac).");
            DieselLoadFile = Cat.CreateEntry("DieselLoadFile", "load.wav",
                description: "Under-load loop file in UserData/WombatGD/Sounds (wav/ogg/mp3/flac).");
            DieselIdleRecordedRpm = Cat.CreateEntry("DieselIdleRecordedRpm", 834f,
                description: "Engine RPM the idle recording was made at (sets pitch tracking).");
            DieselLoadRecordedRpm = Cat.CreateEntry("DieselLoadRecordedRpm", 1170f,
                description: "Engine RPM the load recording was made at (sets pitch tracking).");
            DieselPitchScale = Cat.CreateEntry("DieselPitchScale", 1.0f,
                description: "How strongly the diesel layer's pitch follows RPM (0 = fixed, 1 = natural).");

            CoalAmount = Cat.CreateEntry("CoalAmount", 1.0f,
                description: "Master smoke multiplier. 0 = no extra smoke.");
            SmokeIdleRate = Cat.CreateEntry("SmokeIdleRate", 10f,
                description: "Smoke puffs/sec at idle (light haze).");
            SmokeThrottleRate = Cat.CreateEntry("SmokeThrottleRate", 45f,
                description: "Extra puffs/sec at full throttle (more when lugging at low RPM).");
            SmokePuffRate = Cat.CreateEntry("SmokePuffRate", 160f,
                description: "Extra puffs/sec in the black slug when the throttle is stabbed.");
            SmokeSize = Cat.CreateEntry("SmokeSize", 1.0f,
                description: "Puff size multiplier (1 = 0.35 m at the pipe).");
            SmokeLinger = Cat.CreateEntry("SmokeLinger", 1.0f,
                description: "Smoke lifetime multiplier (1 = about 5 s).");
            SmokeDarkness = Cat.CreateEntry("SmokeDarkness", 0.9f,
                description: "0 = light grey, 1 = jet black.");
            SmokeDensity = Cat.CreateEntry("SmokeDensity", 0.85f,
                description: "Opacity of fresh smoke (0-1).");
            SmokeJetSpeed = Cat.CreateEntry("SmokeJetSpeed", 7f,
                description: "Speed the smoke leaves the pipe at full load, m/s.");
            SmokeBillow = Cat.CreateEntry("SmokeBillow", 1.0f,
                description: "Turbulence: how much the cloud rolls and churns.");
            SmokeRise = Cat.CreateEntry("SmokeRise", 1.0f,
                description: "How strongly hot smoke rises.");
            SmokeSpread = Cat.CreateEntry("SmokeSpread", 1.0f,
                description: "How much each puff expands as it mixes with air.");
            HideStockExhaust = Cat.CreateEntry("HideStockExhaust", true,
                description: "Hide the game's own exhaust sprite (the mod's smoke replaces it).");
            SootyExhaust = Cat.CreateEntry("SootyExhaust", true,
                description: "Tint the game's own exhaust with the smoke darkness (when not hidden).");

            SmokeScreenKey = Cat.CreateEntry("SmokeScreenKey", "F9",
                description: "Hold this key for a heavy, lingering smoke screen (engine must be running).");
            SmokeScreenRate = Cat.CreateEntry("SmokeScreenRate", 90f,
                description: "Smoke screen puffs/sec.");
            SmokeScreenSize = Cat.CreateEntry("SmokeScreenSize", 1.0f,
                description: "Smoke screen puff size multiplier (1 = 1.1 m at the pipe).");
            SmokeScreenLinger = Cat.CreateEntry("SmokeScreenLinger", 1.0f,
                description: "Smoke screen lifetime multiplier (1 = about 20 s).");

            ConfigVersion = Cat.CreateEntry("ConfigVersion", 1,
                description: "Internal: settings layout version.");
            Migrate();
        }

        /// <summary>v0.5 changed what the smoke numbers mean; old saved values would be way off.</summary>
        private static void Migrate()
        {
            if (ConfigVersion.Value >= CurrentConfigVersion) return;
            foreach (var e in new MelonPreferences_Entry[] {
                         CoalAmount, SmokeIdleRate, SmokeThrottleRate, SmokePuffRate, SmokeSize, SmokeLinger,
                         SmokeScreenRate, SmokeScreenSize, SmokeScreenLinger })
                e.ResetToDefault();
            ConfigVersion.Value = CurrentConfigVersion;
            Save();
            WombatGDMod.Log.Msg("Smoke settings reset to the new v0.5 defaults.");
        }

        public static void Save() => Cat.SaveToFile(false);

        public static void ResetToDefaults()
        {
            foreach (var e in Cat.Entries)
                if (e.Identifier != nameof(EngineSoundLabel) && e.Identifier != nameof(ConfigVersion)) e.ResetToDefault();
        }

        /// <summary>True on the frame the key named by <paramref name="entry"/> is pressed.</summary>
        public static bool Pressed(MelonPreferences_Entry<string> entry)
        {
            var k = KeyOf(entry);
            return k != null && k.wasPressedThisFrame;
        }

        public static bool Held(MelonPreferences_Entry<string> entry)
        {
            var k = KeyOf(entry);
            return k != null && k.isPressed;
        }

        private static UnityEngine.InputSystem.Controls.KeyControl KeyOf(MelonPreferences_Entry<string> entry)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(entry.Value, true, out Key key) || key == Key.None) return null;
            return kb[key];
        }
    }
}
