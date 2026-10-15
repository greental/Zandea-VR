using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    // Shared per-color textures keep sclera and pupil pixels from the imported eye maps intact.
    internal static class IrisTexturePalette
    {
        static readonly Dictionary<uint, Texture2D> cache = new Dictionary<uint, Texture2D>();
        static Color32[][] originals;
        static bool attemptedLoad;

        public static Texture2D Get(Color color, bool rightEye, bool cornea = false)
        {
            // Asset imports can briefly precede Resources registration in Edit Mode.
            if (!attemptedLoad || originals == null) Load();
            if (originals == null) return null;
            Color32 c = color;
            // Eight levels per channel keep the shared NPC texture cache bounded.
            uint key = (uint)((c.r / 32) << 9 | (c.g / 32) << 6 | (c.b / 32) << 3 | (rightEye ? 2 : 0) | (cornea ? 1 : 0));
            if (cache.TryGetValue(key, out var existing)) return existing;
            int side = (rightEye ? 0 : 1) + (cornea ? 2 : 0);
            var source = originals[side];
            var output = new Color32[source.Length];
            Color tint = new Color((c.r / 32 * 32 + 16) / 255f, (c.g / 32 * 32 + 16) / 255f, (c.b / 32 * 32 + 16) / 255f);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    int index = y * 256 + x;
                    var original = source[index];
                    float radius = Mathf.Sqrt((x - 128f) * (x - 128f) + (y - 128f) * (y - 128f));
                    float outer = 1f - Mathf.SmoothStep(30f, 35f, radius);
                    float inner = Mathf.SmoothStep(12f, 17f, radius);
                    float mask = outer * inner;
                    float luminance = (.2126f * original.r + .7152f * original.g + .0722f * original.b) / 255f;
                    Color recolored = tint * Mathf.Clamp01(luminance * 1.7f);
                    output[index] = Color32.Lerp(original, recolored, mask);
                }
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, true)
            { name = "Iris tint " + key, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(output); texture.Apply(true, true);
            cache[key] = texture;
            return texture;
        }

        static void Load()
        {
            attemptedLoad = true;
            originals = new Color32[4][];
            string[] names = { "CharacterCreator/EyeBaseR", "CharacterCreator/EyeBaseL",
                "CharacterCreator/CorneaBaseR", "CharacterCreator/CorneaBaseL" };
            for (int i = 0; i < names.Length; i++)
            {
                var bytes = Resources.Load<TextAsset>(names[i]);
                if (!bytes) { originals = null; Debug.LogWarning("Missing iris source " + names[i]); return; }
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                image.LoadImage(bytes.bytes, false);
                originals[i] = image.GetPixels32();
                if (Application.isPlaying) Object.Destroy(image); else Object.DestroyImmediate(image);
            }
        }
    }
}
