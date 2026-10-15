using System.Collections.Generic;
using UnityEngine;

public static class TerrainPreviewGenerator
{
    public static Texture2D GenerateSlopeHeatmap(Terrain terrain, int resolution)
    {
        return GenerateScalarHeatmap(
            terrain,
            resolution,
            context => context.slopeDegrees / 90f);
    }

    public static Texture2D GenerateHeightHeatmap(Terrain terrain, int resolution)
    {
        return GenerateScalarHeatmap(
            terrain,
            resolution,
            context => context.normalizedHeight);
    }

    public static Texture2D GenerateBiomeMask(
        Terrain terrain,
        BiomePreset preset,
        TerrainPainterUtility.PaintSettings settings,
        int resolution)
    {
        if (terrain == null || terrain.terrainData == null || preset == null || preset.rules == null || preset.rules.Count == 0)
        {
            return CreateSolidTexture(Mathf.Max(8, resolution), new Color(0.15f, 0.15f, 0.15f));
        }

        int size = Mathf.Max(8, resolution);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "BiomeMaskPreview"
        };

        Dictionary<int, Color> ruleColors = BuildRuleColorTable(preset);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                TerrainPainterUtility.RuleContext context = TerrainPainterUtility.BuildRuleContext(terrain, settings, u, v);
                float[] weights = TerrainPainterUtility.EvaluateAllRules(preset, settings, context);
                int maxIndex = FindDominantRule(weights);

                Color color = maxIndex >= 0 && ruleColors.TryGetValue(maxIndex, out Color assigned)
                    ? assigned
                    : Color.black;

                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D GenerateScalarHeatmap(
        Terrain terrain,
        int resolution,
        System.Func<TerrainPainterUtility.RuleContext, float> scalarSelector)
    {
        int size = Mathf.Max(8, resolution);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        if (terrain == null || terrain.terrainData == null)
        {
            Fill(texture, Color.black);
            return texture;
        }

        TerrainPainterUtility.PaintSettings defaultSettings = new TerrainPainterUtility.PaintSettings
        {
            noiseScale = 0.01f,
            globalNoiseStrength = 0f,
            blendSmoothness = 0.5f,
            heightInfluence = 1f,
            slopeInfluence = 1f,
            canyonCliffBoost = false
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                TerrainPainterUtility.RuleContext context = TerrainPainterUtility.BuildRuleContext(terrain, defaultSettings, u, v);
                float value = Mathf.Clamp01(scalarSelector(context));
                pixels[y * size + x] = Color.Lerp(Color.black, Color.white, value);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Dictionary<int, Color> BuildRuleColorTable(BiomePreset preset)
    {
        Dictionary<int, Color> colors = new Dictionary<int, Color>();
        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            int seed = (rule != null && rule.terrainLayer != null)
                ? rule.terrainLayer.name.GetHashCode()
                : i * 73;
            Random.State oldState = Random.state;
            Random.InitState(seed);
            Color color = Color.HSVToRGB(Random.Range(0f, 1f), 0.7f, 0.95f);
            Random.state = oldState;
            colors[i] = color;
        }

        return colors;
    }

    private static int FindDominantRule(float[] weights)
    {
        if (weights == null || weights.Length == 0)
        {
            return -1;
        }

        float best = float.MinValue;
        int index = -1;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] > best)
            {
                best = weights[i];
                index = i;
            }
        }

        return index;
    }

    private static Texture2D CreateSolidTexture(int size, Color color)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Fill(texture, color);
        return texture;
    }

    private static void Fill(Texture2D texture, Color color)
    {
        Color[] pixels = new Color[texture.width * texture.height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
    }
}
