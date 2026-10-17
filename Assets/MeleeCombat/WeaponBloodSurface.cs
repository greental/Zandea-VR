using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Keeps blood in the blade's albedo instead of placing geometry beside a moving weapon.
    public sealed class WeaponBloodSurface : MonoBehaviour
    {
        public Renderer[] bladeRenderers;
        [Range(0f, 1f)] public float coverage;
        [Min(0f)] public float gainPerDamage = .012f;
        [Min(0f)] public float decayPerSecond;
        public Color bloodColor = new Color(.28f, .015f, .01f, 1f);

        sealed class Surface
        {
            public Renderer renderer;
            public int colorId, textureId;
            public Color originalColor;
            public Texture originalTexture;
            public Texture2D[] stages;
        }

        readonly List<Surface> surfaces = new();
        readonly MaterialPropertyBlock block = new();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorProperty = Shader.PropertyToID("_Color");
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int GltfDiffuseColor = Shader.PropertyToID("diffuseFactor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int GltfBaseTexture = Shader.PropertyToID("baseColorTexture");
        static readonly int GltfDiffuseTexture = Shader.PropertyToID("diffuseTexture");

        void Awake()
        {
            if (bladeRenderers == null || bladeRenderers.Length == 0)
            {
                var found = new List<Renderer>();
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                    if (renderer && (renderer.name.ToLowerInvariant().Contains("blade") ||
                        renderer.name.ToLowerInvariant().Contains("metal"))) found.Add(renderer);
                if (found.Count == 0)
                    foreach (var renderer in GetComponentsInChildren<Renderer>())
                        if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) found.Add(renderer);
                bladeRenderers = found.ToArray();
            }
            foreach (var renderer in bladeRenderers)
            {
                if (!renderer || !renderer.sharedMaterial) continue;
                var material = renderer.sharedMaterial;
                var surface = new Surface {
                    renderer = renderer,
                    colorId = FirstProperty(material, BaseColor, ColorProperty, GltfBaseColor, GltfDiffuseColor),
                    textureId = FindTextureProperty(material)
                };
                if (surface.colorId != 0) surface.originalColor = material.GetColor(surface.colorId);
                if (surface.textureId != 0)
                {
                    surface.originalTexture = material.GetTexture(surface.textureId);
                    surface.stages = BakeAlbedoStages(surface.originalTexture);
                }
                surfaces.Add(surface);
            }
            Apply();
        }

        static int FirstProperty(Material material, params int[] ids)
        {
            foreach (var id in ids) if (material.HasProperty(id)) return id;
            return 0;
        }

        static int FindTextureProperty(Material material)
        {
            int[] ids = { GltfBaseTexture, GltfDiffuseTexture, BaseMap, MainTex };
            foreach (var id in ids)
                if (material.HasProperty(id) && material.GetTexture(id)) return id;
            return FirstProperty(material, ids);
        }

        public void AddBlood(float damage, Vector3 contact)
        {
            coverage = Mathf.Clamp01(coverage + Mathf.Max(0f, damage) * gainPerDamage);
            Apply();
        }

        void Update()
        {
            if (decayPerSecond <= 0f || coverage <= 0f) return;
            coverage = Mathf.Max(0f, coverage - decayPerSecond * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            int stage = coverage <= .001f ? 0 : coverage < .33f ? 1 : coverage < .7f ? 2 : 3;
            foreach (var surface in surfaces)
            {
                if (!surface.renderer) continue;
                surface.renderer.GetPropertyBlock(block);
                if (surface.textureId != 0 && surface.stages != null)
                    block.SetTexture(surface.textureId, stage == 0 ? surface.originalTexture : surface.stages[stage - 1]);
                if (surface.colorId != 0)
                    block.SetColor(surface.colorId, surface.stages != null ? surface.originalColor :
                        Color.Lerp(surface.originalColor, bloodColor, coverage));
                surface.renderer.SetPropertyBlock(block);
            }
        }

        Texture2D[] BakeAlbedoStages(Texture source)
        {
            int width = source ? Mathf.Clamp(source.width, 64, 512) : 256;
            int height = source ? Mathf.Clamp(source.height, 64, 512) : 256;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source ? source : Texture2D.whiteTexture, target);
                RenderTexture.active = target;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                var original = readable.GetPixels32();
                Destroy(readable);
                var stages = new Texture2D[3];
                float[] density = { .28f, .62f, .92f };
                for (int stage = 0; stage < stages.Length; stage++)
                {
                    var pixels = new Color32[original.Length];
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int index = y * width + x;
                            float u = (x + .5f) / width, v = (y + .5f) / height;
                            float mottling = Mathf.PerlinNoise(u * 13.7f + 2.1f, v * 11.3f + 4.6f);
                            float streaks = Mathf.PerlinNoise(u * 3.1f, v * 26.7f);
                            float mask = Mathf.SmoothStep(0f, 1f,
                                (mottling * .65f + streaks * .35f - (1f - density[stage]) * .7f) * 2.2f);
                            Color basePixel = original[index];
                            Color stained = Color.Lerp(basePixel, bloodColor, mask * (.45f + density[stage] * .5f));
                            stained.a = basePixel.a;
                            pixels[index] = stained;
                        }
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
                    texture.name = name + " blood albedo " + (stage + 1);
                    texture.wrapMode = source ? source.wrapMode : TextureWrapMode.Repeat;
                    texture.SetPixels32(pixels);
                    texture.Apply(true, true);
                    stages[stage] = texture;
                }
                return stages;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        void OnDestroy()
        {
            foreach (var surface in surfaces)
                if (surface.stages != null)
                    foreach (var texture in surface.stages) if (texture) Destroy(texture);
        }
    }
}
