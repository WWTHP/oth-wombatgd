using System;
using MelonLoader;
using UnityEngine.InputSystem;

namespace ModName
{
    /// <summary>User settings, stored in UserData/MelonPreferences.cfg [ModName].</summary>
    internal static class Settings
    {
        internal static MelonPreferences_Category Cat;
        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<string> MenuKey;

        public static void Init()
        {
            Cat = MelonPreferences.CreateCategory("ModName");
            Enabled = Cat.CreateEntry("Enabled", true, description: "Turn the mod on or off.");
            // Pick a key no other mod uses: see the key registry in docs/games/<game>/README.md.
            MenuKey = Cat.CreateEntry("MenuKey", "F10",
                description: "Key that opens the menu (Unity Input System key name).");
        }

        public static void Save() => Cat.SaveToFile(false);

        public static bool Pressed(MelonPreferences_Entry<string> entry)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(entry.Value, true, out Key key) || key == Key.None) return false;
            return kb[key].wasPressedThisFrame;
        }
    }
}
