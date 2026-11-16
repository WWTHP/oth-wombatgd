using System.Collections.Generic;
using System.IO;
using MelonLoader.Utils;
using UnityEngine;

namespace WombatGD
{
    /// <summary>
    /// Loads the billowy puff textures (UserData/WombatGD/Smoke/puffN.rgba) and
    /// builds one smoke material per puff from the game's own exhaust material,
    /// so the smoke keeps the game's particle shader (soft particles, fog, etc.).
    /// </summary>
    internal static class SmokeAssets
    {
        private const int TexSize = 256;
        public static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "WombatGD", "Smoke");

        private static List<Texture2D> _textures;
        private static readonly Dictionary<System.IntPtr, List<Material>> Materials = new();

        public static List<Texture2D> Textures
        {
            get
            {
                if (_textures != null) return _textures;
                _textures = new List<Texture2D>();
                for (int i = 0; i < 8; i++)
                {
                    var path = Path.Combine(Dir, $"puff{i}.rgba");
                    if (!File.Exists(path)) break;
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        if (bytes.Length != TexSize * TexSize * 4)
                        {
                            WombatGDMod.Log.Warning($"{path}: unexpected size {bytes.Length}, skipped.");
                            continue;
                        }
                        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false); // data holds one level only
                        tex.LoadRawTextureData(bytes);
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.Apply(false, true);
                        tex.name = $"WombatGD_puff{i}";
                        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        _textures.Add(tex);
                    }
                    catch (System.Exception ex)
                    {
                        WombatGDMod.Log.Warning($"Could not load {path}: {ex.Message}");
                    }
                }
                WombatGDMod.Log.Msg($"Loaded {_textures.Count} smoke puff textures from {Dir}.");
                return _textures;
            }
        }

        /// <summary>One material per puff texture, derived from <paramref name="stock"/>.</summary>
        public static List<Material> MaterialsFor(Material stock)
        {
            if (stock == null) return new List<Material>();
            if (Materials.TryGetValue(stock.Pointer, out var list)) return list;
            list = new List<Material>();
            var texes = Textures;
            WombatGDMod.Log.Msg($"Stock exhaust material '{stock.name}', shader '{stock.shader?.name}'.");
            if (texes.Count == 0)
            {
                list.Add(stock); // fall back to the game's own smoke sprite
            }
            else
            {
                foreach (var tex in texes)
                {
                    var m = new Material(stock) { name = "WombatGD_" + tex.name };
                    m.mainTexture = tex;
                    foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                        if (m.HasProperty(prop)) m.SetTexture(prop, tex);
                    foreach (var prop in new[] { "_BaseColor", "_Color", "_TintColor" })
                        if (m.HasProperty(prop)) m.SetColor(prop, Color.white);
                    m.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    list.Add(m);
                }
            }
            Materials[stock.Pointer] = list;
            return list;
        }
    }
}
