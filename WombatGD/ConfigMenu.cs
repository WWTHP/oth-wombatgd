using MelonLoader;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// In-game settings window (IMGUI). Toggled with the MenuKey (default F7).
    /// Changes apply live; they are written to MelonPreferences.cfg on Save/Close.
    /// </summary>
    internal static class ConfigMenu
    {
        public static bool IsOpen { get; private set; }

        private const float W = 520f, RowH = 26f, Pad = 12f, LabelW = 190f, ValueW = 60f;
        private static Vector2 _scroll;
        private static float _contentH = 1200f;
        private static float _y;
        private static bool _dirty;
        private static CursorLockMode _prevLock;
        private static bool _prevVisible;

        public static void Update()
        {
            if (Settings.Pressed(Settings.MenuKey)) Toggle();
            if (IsOpen)
            {
                // The game re-locks the cursor; keep it free while the menu is up.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
            }
            else
            {
                if (_dirty) { Settings.Save(); _dirty = false; }
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevVisible;
            }
        }

        public static void Draw()
        {
            if (!IsOpen) return;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float viewH = Mathf.Min(Screen.height / scale - 80f, _contentH + 60f);
            var win = new Rect(40f, 40f, W, viewH);

            GUI.Box(win, "");
            GUI.Box(win, "");
            GUI.Label(new Rect(win.x + Pad, win.y + 8f, W - 2 * Pad, RowH),
                $"<b>WombatGD — 1980 300GD settings</b>   ({Settings.MenuKey.Value} to close)");

            var view = new Rect(win.x, win.y + 38f, W, viewH - 38f - 44f);
            var content = new Rect(0f, 0f, W - 24f, _contentH);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            _y = 0f;

            Section("Snorkel");
            Toggle(Settings.MirrorSnorkel, "Snorkel on passenger side");

            Section("Antenna");
            Toggle(Settings.PillarAntenna, "Antenna on passenger A-pillar");
            if (Settings.PillarAntenna.Value)
            {
                Slider(Settings.AntennaForward, "Forward / back (m)", -0.3f, 0.3f, "F2");
                Slider(Settings.AntennaHeight, "Up / down (m)", -0.3f, 0.3f, "F2");
                Slider(Settings.AntennaSide, "Out / in (m)", -0.2f, 0.2f, "F2");
                Slider(Settings.AntennaTilt, "Extra lean (deg)", -20f, 20f, "F0");
            }

            Section("Engine sound");
            EngineSetRow();
            Toggle(Settings.DieselLayerEnabled, "Diesel layer");
            SoundSetRow();
            Slider(Settings.DieselVolume, "Diesel volume", 0f, 1.5f, "F2");
            Slider(Settings.StockEngineVolume, "Stock engine volume", 0f, 1f, "F2");
            if (Settings.DieselSoundSet.Value == DieselLayer.TwoFiles)
            {
                Slider(Settings.DieselPitchScale, "Pitch follows RPM", 0f, 1f, "F2");
                Slider(Settings.DieselIdleRecordedRpm, "Idle recording RPM", 400f, 1500f, "F0");
                Slider(Settings.DieselLoadRecordedRpm, "Load recording RPM", 1200f, 4500f, "F0");
                FileRow(Settings.DieselIdleFile, "Idle sound file");
                FileRow(Settings.DieselLoadFile, "Load sound file");
            }
            Info("Status: " + DieselLayer.Status);
            if (Button("Reload sound files")) { _files = null; _sets = null; DieselLayer.ReloadSounds(); }

            Section("Exhaust smoke");
            Slider(Settings.CoalAmount, "Master amount", 0f, 4f, "F2");
            Slider(Settings.SmokeIdleRate, "Idle haze", 0f, 80f, "F0");
            Slider(Settings.SmokeThrottleRate, "Under throttle", 0f, 250f, "F0");
            Slider(Settings.SmokePuffRate, "Throttle-stab slug", 0f, 500f, "F0");
            Slider(Settings.SmokeDensity, "Opacity", 0.1f, 1f, "F2");
            Slider(Settings.SmokeDarkness, "Darkness", 0f, 1f, "F2");
            Slider(Settings.SmokeSize, "Puff size", 0.3f, 4f, "F2");
            Slider(Settings.SmokeSpread, "Expansion", 0.2f, 3f, "F2");
            Slider(Settings.SmokeLinger, "Linger", 0.3f, 5f, "F2");
            Slider(Settings.SmokeJetSpeed, "Jet speed (m/s)", 0f, 20f, "F1");
            Slider(Settings.SmokeBillow, "Billow / turbulence", 0f, 3f, "F2");
            Slider(Settings.SmokeRise, "Rise", 0f, 3f, "F2");
            Toggle(Settings.HideStockExhaust, "Hide the game's own exhaust sprite");
            Toggle(Settings.SootyExhaust, "Tint the game's own exhaust (if shown)");

            Section($"Smoke screen (hold {Settings.SmokeScreenKey.Value})");
            Slider(Settings.SmokeScreenRate, "Amount", 0f, 400f, "F0");
            Slider(Settings.SmokeScreenSize, "Size", 0.3f, 4f, "F2");
            Slider(Settings.SmokeScreenLinger, "Linger", 0.2f, 4f, "F2");

            Section("Multiplayer");
            Toggle(Settings.MultiplayerSync, "Share my truck with other WombatGD players");
            Info("Sync: " + NetSync.Status);
            Info("Players running WombatGD see your truck with these settings.");

            if (CrashGuard.DisabledStages.Count > 0)
            {
                Section("Crash protection");
                Info("Switched off after the game crashed in it: " + string.Join(", ", CrashGuard.DisabledStages));
                if (Button("Re-enable (restart the game)")) CrashGuard.ReEnableAll();
            }

            Section("Keys (edit in MelonPreferences.cfg)");
            Info($"Menu {Settings.MenuKey.Value}   Cycle stock sound {Settings.EngineSoundCycleKey.Value}   Smoke screen {Settings.SmokeScreenKey.Value}");

            _contentH = _y + Pad;
            GUI.EndScrollView();

            float by = win.yMax - 36f;
            if (GUI.Button(new Rect(win.x + Pad, by, 110f, 26f), "Save")) { Settings.Save(); _dirty = false; }
            if (GUI.Button(new Rect(win.x + Pad + 120f, by, 140f, 26f), "Reset defaults")) { Settings.ResetToDefaults(); _dirty = true; }
            if (GUI.Button(new Rect(win.xMax - Pad - 110f, by, 110f, 26f), "Close")) Toggle();

            GUI.matrix = Matrix4x4.identity;
        }

        private static void Section(string title)
        {
            _y += 8f;
            GUI.Label(new Rect(Pad, _y, W, RowH), $"<b><color=#f0c060>{title}</color></b>");
            _y += RowH;
        }

        private static void Info(string text)
        {
            GUI.Label(new Rect(Pad + 8f, _y, W - 3 * Pad, RowH), text);
            _y += RowH;
        }

        private static bool Button(string text)
        {
            bool hit = GUI.Button(new Rect(Pad + 8f, _y + 2f, 200f, RowH - 4f), text);
            _y += RowH;
            return hit;
        }

        private static void Toggle(MelonPreferences_Entry<bool> e, string label)
        {
            bool v = GUI.Toggle(new Rect(Pad + 8f, _y, W - 3 * Pad, RowH), e.Value, " " + label);
            if (v != e.Value) { e.Value = v; _dirty = true; }
            _y += RowH;
        }

        private static void Slider(MelonPreferences_Entry<float> e, string label, float min, float max, string fmt)
        {
            GUI.Label(new Rect(Pad + 8f, _y, LabelW, RowH), label);
            float sx = Pad + 8f + LabelW;
            float sw = W - 24f - sx - ValueW - Pad;
            float v = GUI.HorizontalSlider(new Rect(sx, _y + 8f, sw, RowH), e.Value, min, max);
            GUI.Label(new Rect(sx + sw + 8f, _y, ValueW, RowH), e.Value.ToString(fmt));
            if (!Mathf.Approximately(v, e.Value)) { e.Value = v; _dirty = true; }
            _y += RowH;
        }

        private static string[] _files;

        /// <summary>Steps through the audio files in the Sounds folder.</summary>
        private static void FileRow(MelonPreferences_Entry<string> e, string label)
        {
            if (_files == null)
            {
                try
                {
                    var dir = DieselLayer.SoundsDir;
                    _files = System.IO.Directory.Exists(dir)
                        ? System.Array.FindAll(System.IO.Directory.GetFiles(dir), f =>
                            f.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".flac", System.StringComparison.OrdinalIgnoreCase))
                        : System.Array.Empty<string>();
                    for (int i = 0; i < _files.Length; i++) _files[i] = System.IO.Path.GetFileName(_files[i]);
                    System.Array.Sort(_files, System.StringComparer.OrdinalIgnoreCase);
                }
                catch (System.Exception ex)
                {
                    WombatGDMod.Log.Warning("Could not list sound files: " + ex.Message);
                    _files = System.Array.Empty<string>();
                }
            }

            GUI.Label(new Rect(Pad + 8f, _y, LabelW, RowH), label);
            float x = Pad + 8f + LabelW;
            int idx = System.Array.IndexOf(_files, e.Value);
            if (_files.Length > 0 && GUI.Button(new Rect(x, _y + 2f, 30f, RowH - 4f), "<"))
            { e.Value = _files[(idx - 1 + _files.Length) % _files.Length]; _dirty = true; }
            GUI.Label(new Rect(x + 38f, _y, 160f, RowH), e.Value);
            if (_files.Length > 0 && GUI.Button(new Rect(x + 200f, _y + 2f, 30f, RowH - 4f), ">"))
            { e.Value = _files[(idx + 1) % _files.Length]; _dirty = true; }
            _y += RowH;
        }

        private static System.Collections.Generic.List<string> _sets;

        /// <summary>Picks the diesel sound set (synth bank or the two-file recordings).</summary>
        private static void SoundSetRow()
        {
            _sets ??= DieselLayer.AvailableSets();
            GUI.Label(new Rect(Pad + 8f, _y, LabelW, RowH), "Diesel sound set");
            float x = Pad + 8f + LabelW;
            int idx = _sets.IndexOf(Settings.DieselSoundSet.Value);
            if (GUI.Button(new Rect(x, _y + 2f, 30f, RowH - 4f), "<"))
            { Settings.DieselSoundSet.Value = _sets[(idx - 1 + _sets.Count) % _sets.Count]; _dirty = true; }
            GUI.Label(new Rect(x + 38f, _y, 160f, RowH), Settings.DieselSoundSet.Value);
            if (GUI.Button(new Rect(x + 200f, _y + 2f, 30f, RowH - 4f), ">"))
            { Settings.DieselSoundSet.Value = _sets[(idx + 1) % _sets.Count]; _dirty = true; }
            _y += RowH;
        }

        private static void EngineSetRow()
        {
            GUI.Label(new Rect(Pad + 8f, _y, LabelW, RowH), "Stock engine sound set");
            float x = Pad + 8f + LabelW;
            if (GUI.Button(new Rect(x, _y + 2f, 30f, RowH - 4f), "<")) { DieselAudio.Cycle(-1); _dirty = true; }
            var cur = string.IsNullOrEmpty(Settings.EngineSoundLabel.Value) ? "Wombat (stock)" : Settings.EngineSoundLabel.Value;
            GUI.Label(new Rect(x + 38f, _y, 160f, RowH), cur);
            if (GUI.Button(new Rect(x + 200f, _y + 2f, 30f, RowH - 4f), ">")) { DieselAudio.Cycle(+1); _dirty = true; }
            _y += RowH;
        }
    }
}
