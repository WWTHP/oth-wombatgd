using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;

namespace WombatGD
{
    /// <summary>
    /// Survives hard crashes in risky native code. Before the first few runs of a
    /// guarded stage a marker file names it; the marker is removed when the stage
    /// returns. If the game dies inside the stage the marker is still there at the
    /// next launch, so that stage is logged and switched off (persisted in
    /// disabled.txt) until the user re-enables it from the menu.
    /// </summary>
    internal static class CrashGuard
    {
        private const int WatchedRuns = 30; // a stage is trusted after this many clean runs
        private static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "WombatGD");
        private static string MarkerPath(string stage) => Path.Combine(Dir, $"in_progress_{stage}.txt");
        private static string DisabledPath => Path.Combine(Dir, "disabled.txt");

        private static readonly HashSet<string> Disabled = new();
        private static readonly Dictionary<string, int> Runs = new();

        public static IReadOnlyCollection<string> DisabledStages => Disabled;

        public static void Init()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(DisabledPath))
                    foreach (var line in File.ReadAllLines(DisabledPath))
                        if (line.Trim().Length > 0) Disabled.Add(line.Trim());
                foreach (var marker in Directory.GetFiles(Dir, "in_progress_*.txt"))
                {
                    var stage = File.ReadAllText(marker).Trim();
                    File.Delete(marker);
                    if (stage.Length > 0)
                    {
                        WombatGDMod.Log.Warning($"The game closed unexpectedly during WombatGD stage '{stage}' last time. " +
                                                "It is switched off; re-enable it in the F7 menu.");
                        Disabled.Add(stage);
                        Save();
                    }
                }
                foreach (var s in Disabled) WombatGDMod.Log.Warning($"WombatGD stage '{s}' is disabled after a crash.");
            }
            catch (System.Exception ex) { WombatGDMod.Log.Warning("CrashGuard: " + ex.Message); }
        }

        public static bool IsDisabled(string stage) => Disabled.Contains(stage);

        /// <summary>Returns false if the stage is disabled; otherwise marks it as running.</summary>
        public static bool Enter(string stage)
        {
            if (Disabled.Contains(stage)) return false;
            Runs.TryGetValue(stage, out int n);
            if (n < WatchedRuns)
                try { File.WriteAllText(MarkerPath(stage), stage); } catch { }
            return true;
        }

        public static void Exit(string stage)
        {
            Runs.TryGetValue(stage, out int n);
            if (n >= WatchedRuns) return;
            Runs[stage] = n + 1;
            try { File.Delete(MarkerPath(stage)); } catch { }
            if (n + 1 == WatchedRuns) WombatGDMod.Log.Msg($"Stage '{stage}' running normally.");
        }

        /// <summary>Clears a stage's marker for good (e.g. on quit).</summary>
        public static void Clear(string stage)
        {
            Runs[stage] = WatchedRuns;
            try { File.Delete(MarkerPath(stage)); } catch { }
        }

        public static void ReEnableAll()
        {
            Disabled.Clear();
            Save();
        }

        private static void Save()
        {
            try { File.WriteAllLines(DisabledPath, Disabled); } catch { }
        }
    }
}
