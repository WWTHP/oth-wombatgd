using System.IO;
using System.Text;

namespace ModName
{
    // Core/ = pure C#: no UnityEngine, Il2Cpp*, MelonLoader or HarmonyLib here.
    // These files are also compiled by tests/ModName.Tests, which fails to build if
    // anything game-specific sneaks in.

    /// <summary>
    /// Validation helpers for data from untrusted sources (other players' packets, files
    /// users can replace). Parsers reject bad input; they never coerce it.
    /// </summary>
    internal static class Validate
    {
        /// <summary>True if <paramref name="f"/> is a finite number within [min, max].</summary>
        public static bool InRange(float f, float min, float max) =>
            !float.IsNaN(f) && !float.IsInfinity(f) && f >= min && f <= max;

        /// <summary>Names: 1-<paramref name="maxLength"/> letters, digits, space, '_' or '-'.</summary>
        public static bool IsSafeName(string s, int maxLength = 32)
        {
            if (string.IsNullOrEmpty(s) || s.Length > maxLength) return false;
            foreach (var c in s)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')) return false;
            return true;
        }

        /// <summary>Writes a length-prefixed UTF-8 string, truncated to <paramref name="maxBytes"/> bytes.</summary>
        public static void WriteShortString(BinaryWriter w, string s, int maxBytes = 32)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > maxBytes) System.Array.Resize(ref b, maxBytes);
            w.Write((byte)b.Length);
            w.Write(b);
        }

        /// <summary>
        /// Reads a string written by <see cref="WriteShortString"/>. Returns null if it is too
        /// long, truncated, or not a safe name (empty strings are allowed).
        /// </summary>
        public static string ReadShortString(BinaryReader r, int maxBytes = 32)
        {
            int n = r.ReadByte();
            if (n > maxBytes) return null;
            var bytes = r.ReadBytes(n);
            if (bytes.Length != n) return null;
            var s = Encoding.UTF8.GetString(bytes);
            return s.Length == 0 || IsSafeName(s, maxBytes) ? s : null;
        }
    }
}
