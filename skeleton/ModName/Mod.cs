using MelonLoader;

[assembly: MelonInfo(typeof(ModName.Mod), "ModName", ModName.Mod.Version, "WWTHP")]
[assembly: MelonGame(null, "over the hill")]

namespace ModName
{
    /// <summary>
    /// Entry point: wires MelonLoader lifecycle callbacks to the mod's subsystems.
    /// Keep logic out of this file. Describe here what the mod does in the game.
    /// </summary>
    public sealed class Mod : MelonMod
    {
        public const string Version = "0.1.0";
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Settings.Init();
            CrashGuard.Init();
            Log.Msg($"ModName {Version} loaded. {Settings.MenuKey.Value} = menu.");
        }

        public override void OnUpdate()
        {
            if (!Settings.Enabled.Value) return;
            if (Settings.Pressed(Settings.MenuKey)) Log.Msg("Menu key pressed.");
        }
    }
}
