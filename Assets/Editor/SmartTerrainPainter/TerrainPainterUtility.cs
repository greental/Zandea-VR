using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainPainterUtility
{
    public enum PaintMode
    {
        WholeTerrain = 0,
        BrushArea = 1,
        SelectedBounds = 2
    }

    public struct PaintSettings
    {
        public float noiseScale;
        public float globalNoiseStrength;
        public float blendSmoothness;
        public float heightInfluence;
        public float slopeInfluence;
        public bool canyonCliffBoost;
        public int smoothingPasses;
        public bool enforceDrySurface;
        public float drySurfaceSmoothness;
    }

    public struct RuleContext
    {
        public float normalizedHeight;
        public float slopeDegrees;
        public float noise01;
        public float noiseSigned;
    }

    private const float Epsilon = 0.00001f;

    public static Terrain GetActiveTerrain()
    {
        if (Terrain.activeTerrain != null)
        {
            return Terrain.activeTerrain;
        }

        if (Selection.activeGameObject != null)
        {
            Terrain fromSelection = Selection.activeGameObject.GetComponentInParent<Terrain>();
            if (fromSelection != null)
            {
                return fromSelection;
            }
        }

        Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        return terrains.Length > 0 ? terrains[0] : null;
    }

    public static bool ValidateTerrainAndPreset(
        Terrain terrain,
        BiomePreset preset,
        out string warningMessage,
        out Dictionary<TerrainLayer, int> layerIndices)
    {
        warningMessage = string.Empty;
        layerIndices = new Dictionary<TerrainLayer, int>();

        if (terrain == null || terrain.terrainData == null)
        {
            warningMessage = "No active Terrain found.";
            return false;
        }

        if (preset == null)
        {
            warningMessage = "No biome preset selected.";
            return false;
        }

        if (preset.rules == null || preset.rules.Count == 0)
        {
            warningMessage = "Selected biome preset has no rules.";
            return false;
        }

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer layer = terrainLayers[i];
            if (layer != null && !layerIndices.ContainsKey(layer))
            {
                layerIndices.Add(layer, i);
            }
        }

        List<string> missing = new List<string>();
        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            if (rule == null || rule.terrainLayer == null)
            {
                missing.Add($"Rule {i + 1}: TerrainLayer is not assigned.");
                continue;
            }

            if (!layerIndices.ContainsKey(rule.terrainLayer))
            {
                int fallbackIndex = FindLayerIndexByName(terrainLayers, rule.terrainLayer.name);
                if (fallbackIndex >= 0)
                {
                    layerIndices[rule.terrainLayer] = fallbackIndex;
                }
                else
                {
                    missing.Add($"Rule {i + 1}: '{rule.terrainLayer.name}' is not in the Terrain's TerrainLayers list.");
                }
            }
        }

        if (missing.Count > 0)
        {
            warningMessage = string.Join("\n", missing);
            return false;
        }

        return true;
    }

    public static void ResetTerrain(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            return;
        }

        TerrainData data = terrain.terrainData;
        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        int layers = data.alphamapLayers;
        if (layers <= 0)
        {
            return;
        }

        Undo.RegisterCompleteObjectUndo(data, "Reset Terrain Texture Weights");

        float[,,] alphamaps = new float[height, width, layers];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                alphamaps[y, x, 0] = 1f;
                for (int l = 1; l < layers; l++)
                {
                    alphamaps[y, x, l] = 0f;
                }
            }
        }

        data.SetAlphamaps(0, 0, alphamaps);
        EditorUtility.SetDirty(data);
    }

    public static void PaintWholeTerrain(
        Terrain terrain,
        BiomePreset preset,
        PaintSettings settings,
        bool showProgress = true)
    {
        if (!ValidateTerrainAndPreset(terrain, preset, out _, out Dictionary<TerrainLayer, int> layerIndices))
        {
            return;
        }

        ApplyDrySurfaceOverrides(terrain, settings);

        TerrainData data = terrain.terrainData;
        PaintRect(
            terrain,
            preset,
            settings,
            layerIndices,
            0,
            0,
            data.alphamapWidth,
            data.alphamapHeight,
            showProgress,
            "Auto Paint Terrain");
    }

    public static void PaintBounds(
        Terrain terrain,
        BiomePreset preset,
        PaintSettings settings,
        Bounds worldBounds,
        bool showProgress = true)
    {
        if (!ValidateTerrainAndPreset(terrain, preset, out _, out Dictionary<TerrainLayer, int> layerIndices))
        {
            return;
        }

        ApplyDrySurfaceOverrides(terrain, settings);

        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;
        Vector3 size = data.size;

        float minX01 = Mathf.InverseLerp(terrainPos.x, terrainPos.x + size.x, worldBounds.min.x);
        float maxX01 = Mathf.InverseLerp(terrainPos.x, terrainPos.x + size.x, worldBounds.max.x);
        float minZ01 = Mathf.InverseLerp(terrainPos.z, terrainPos.z + size.z, worldBounds.min.z);
        float maxZ01 = Mathf.InverseLerp(terrainPos.z, terrainPos.z + size.z, worldBounds.max.z);

        int startX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(minX01, maxX01) * data.alphamapWidth), 0, data.alphamapWidth - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(minX01, maxX01) * data.alphamapWidth), 0, data.alphamapWidth);
        int startY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(minZ01, maxZ01) * data.alphamapHeight), 0, data.alphamapHeight - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(minZ01, maxZ01) * data.alphamapHeight), 0, data.alphamapHeight);

        int width = Mathf.Max(1, endX - startX);
        int height = Mathf.Max(1, endY - startY);

        PaintRect(
            terrain,
            preset,
            settings,
            layerIndices,
            startX,
            startY,
            width,
            height,
            showProgress,
            "Paint Selected Area");
    }

    public static bool TryGetBrushBounds(Terrain terrain, float brushRadius, out Bounds bounds)
    {
        bounds = default;
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        Vector3 center;
        if (Selection.activeGameObject != null)
        {
            center = Selection.activeGameObject.transform.position;
        }
        else if (SceneView.lastActiveSceneView != null)
        {
            center = SceneView.lastActiveSceneView.pivot;
        }
        else
        {
            return false;
        }

        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;
        float surfaceHeight = terrain.SampleHeight(center) + terrainPos.y;
        center.y = surfaceHeight;

        float clampedRadius = Mathf.Max(1f, brushRadius);
        bounds = new Bounds(
            center,
            new Vector3(clampedRadius * 2f, Mathf.Max(4f, data.size.y), clampedRadius * 2f));
        return true;
    }

    public static bool TryGetSelectionBounds(Terrain terrain, out Bounds bounds)
    {
        bounds = default;
        if (terrain == null)
        {
            return false;
        }

        if (Selection.activeGameObject == null)
        {
            return false;
        }

        Renderer renderer = Selection.activeGameObject.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        Collider collider = Selection.activeGameObject.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        Vector3 pos = Selection.activeGameObject.transform.position;
        bounds = new Bounds(pos, Vector3.one * 8f);
        return true;
    }

    public static float[] EvaluateAllRules(
        BiomePreset preset,
        PaintSettings settings,
        RuleContext context)
    {
        float[] weights = new float[preset.rules.Count];
        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            weights[i] = EvaluateRuleWeight(rule, settings, context);
        }

        return weights;
    }

    private static void PaintRect(
        Terrain terrain,
        BiomePreset preset,
        PaintSettings settings,
        Dictionary<TerrainLayer, int> layerIndices,
        int startX,
        int startY,
        int width,
        int height,
        bool showProgress,
        string undoName)
    {
        TerrainData data = terrain.terrainData;
        int alphaWidth = data.alphamapWidth;
        int alphaHeight = data.alphamapHeight;
        int alphaLayers = data.alphamapLayers;

        startX = Mathf.Clamp(startX, 0, alphaWidth - 1);
        startY = Mathf.Clamp(startY, 0, alphaHeight - 1);
        width = Mathf.Clamp(width, 1, alphaWidth - startX);
        height = Mathf.Clamp(height, 1, alphaHeight - startY);

        Undo.RegisterCompleteObjectUndo(data, undoName);
        float[,,] splat = data.GetAlphamaps(startX, startY, width, height);
        float[] layerWeights = new float[alphaLayers];

        try
        {
            for (int y = 0; y < height; y++)
            {
                if (showProgress && y % 16 == 0)
                {
                    float progress = (float)y / Mathf.Max(1, height - 1);
                    EditorUtility.DisplayProgressBar("Smart Terrain Painter", "Painting terrain textures...", progress);
                }

                int globalY = startY + y;
                float v = (globalY + 0.5f) / alphaHeight;

                for (int x = 0; x < width; x++)
                {
                    int globalX = startX + x;
                    float u = (globalX + 0.5f) / alphaWidth;

                    RuleContext context = BuildRuleContext(terrain, settings, u, v);
                    float[] ruleWeights = EvaluateAllRules(preset, settings, context);

                    Array.Clear(layerWeights, 0, layerWeights.Length);

                    for (int r = 0; r < preset.rules.Count; r++)
                    {
                        BiomeRule rule = preset.rules[r];
                        if (rule == null || rule.terrainLayer == null)
                        {
                            continue;
                        }

                        if (!layerIndices.TryGetValue(rule.terrainLayer, out int layerIndex))
                        {
                            layerIndex = FindLayerIndexByName(data.terrainLayers, rule.terrainLayer.name);
                            if (layerIndex >= 0)
                            {
                                layerIndices[rule.terrainLayer] = layerIndex;
                            }
                        }

                        if (layerIndex < 0 || layerIndex >= alphaLayers)
                        {
                            continue;
                        }

                        layerWeights[layerIndex] += ruleWeights[r];
                    }

                    NormalizeWeights(layerWeights);

                    for (int l = 0; l < alphaLayers; l++)
                    {
                        splat[y, x, l] = layerWeights[l];
                    }
                }
            }
        }
        finally
        {
            if (showProgress)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        if (settings.smoothingPasses > 0)
        {
            SmoothSplatWeights(splat, width, height, alphaLayers, settings.smoothingPasses);
        }

        data.SetAlphamaps(startX, startY, splat);
        EditorUtility.SetDirty(data);
    }

    public static void ApplyDrySurfaceOverrides(Terrain terrain, PaintSettings settings)
    {
        if (!settings.enforceDrySurface || terrain == null || terrain.terrainData == null)
        {
            return;
        }

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        if (terrainLayers == null || terrainLayers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer layer = terrainLayers[i];
            if (layer == null)
            {
                continue;
            }

            Undo.RecordObject(layer, "Apply Dry Surface Override");
            layer.smoothness = settings.drySurfaceSmoothness;
            layer.metallic = 0f;

            SerializedObject serializedLayer = new SerializedObject(layer);
            SerializedProperty smoothnessSource = serializedLayer.FindProperty("m_SmoothnessSource");
            if (smoothnessSource != null)
            {
                // 0 switches away from Diffuse Alpha source in TerrainLayer serialization.
                smoothnessSource.intValue = 0;
                serializedLayer.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(layer);
        }
    }

    public static RuleContext BuildRuleContext(
        Terrain terrain,
        PaintSettings settings,
        float normX,
        float normY)
    {
        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;
        Vector3 size = data.size;

        float worldX = terrainPos.x + (normX * size.x);
        float worldZ = terrainPos.z + (normY * size.z);
        float worldY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainPos.y;

        float normHeight = Mathf.InverseLerp(terrainPos.y, terrainPos.y + size.y, worldY);
        Vector3 normal = data.GetInterpolatedNormal(normX, normY);
        float slope = Vector3.Angle(normal, Vector3.up);

        float scaledX = worldX * Mathf.Max(0.0001f, settings.noiseScale);
        float scaledZ = worldZ * Mathf.Max(0.0001f, settings.noiseScale);
        float noise01 = Mathf.PerlinNoise(scaledX, scaledZ);

        return new RuleContext
        {
            normalizedHeight = Mathf.Clamp01(normHeight),
            slopeDegrees = Mathf.Clamp(slope, 0f, 90f),
            noise01 = noise01,
            noiseSigned = (noise01 * 2f) - 1f
        };
    }

    public static float EvaluateRuleWeight(
        BiomeRule rule,
        PaintSettings settings,
        RuleContext context)
    {
        if (rule == null || rule.terrainLayer == null || rule.blendStrength <= 0f)
        {
            return 0f;
        }

        float heightWeight = GetRangeWeight(
            context.normalizedHeight,
            Mathf.Min(rule.minHeight, rule.maxHeight),
            Mathf.Max(rule.minHeight, rule.maxHeight),
            settings.blendSmoothness);

        float slopeWeight = GetRangeWeight(
            context.slopeDegrees,
            Mathf.Min(rule.minSlope, rule.maxSlope),
            Mathf.Max(rule.minSlope, rule.maxSlope),
            settings.blendSmoothness);

        if (heightWeight <= 0f || slopeWeight <= 0f)
        {
            return 0f;
        }

        float shapedHeight = Mathf.Pow(heightWeight, Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(settings.heightInfluence)));
        float shapedSlope = Mathf.Pow(slopeWeight, Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(settings.slopeInfluence)));

        float noiseFactor = 1f + (context.noiseSigned * rule.noiseStrength * settings.globalNoiseStrength);
        noiseFactor = Mathf.Clamp(noiseFactor, 0.05f, 2.5f);

        float weight = rule.blendStrength * shapedHeight * shapedSlope * noiseFactor;

        if (settings.canyonCliffBoost && context.slopeDegrees > 45f)
        {
            // Favor rock-like high-slope rules for dramatic canyon wall readability.
            float slopeBias = Mathf.InverseLerp(45f, 75f, context.slopeDegrees);
            if (rule.minSlope >= 35f || rule.maxSlope >= 55f)
            {
                weight *= Mathf.Lerp(1f, 2f, slopeBias);
            }
        }

        return Mathf.Max(0f, weight);
    }

    private static float GetRangeWeight(float value, float min, float max, float smoothness01)
    {
        if (max < min)
        {
            float temp = min;
            min = max;
            max = temp;
        }

        if (value < min || value > max)
        {
            return 0f;
        }

        float range = Mathf.Max(Epsilon, max - min);
        float edge = Mathf.Lerp(Epsilon, range * 0.5f, Mathf.Clamp01(smoothness01));
        float inWeight = Mathf.InverseLerp(min, min + edge, value);
        float outWeight = 1f - Mathf.InverseLerp(max - edge, max, value);

        float result = Mathf.Min(inWeight, outWeight);
        return Mathf.Clamp01(result);
    }

    private static void NormalizeWeights(float[] weights)
    {
        float sum = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            sum += Mathf.Max(0f, weights[i]);
        }

        if (sum <= Epsilon)
        {
            if (weights.Length > 0)
            {
                weights[0] = 1f;
            }

            for (int i = 1; i < weights.Length; i++)
            {
                weights[i] = 0f;
            }

            return;
        }

        float inv = 1f / sum;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = Mathf.Max(0f, weights[i]) * inv;
        }
    }

    private static int FindLayerIndexByName(TerrainLayer[] terrainLayers, string layerName)
    {
        if (terrainLayers == null || terrainLayers.Length == 0 || string.IsNullOrWhiteSpace(layerName))
        {
            return -1;
        }

        string target = layerName.Trim();
        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer layer = terrainLayers[i];
            if (layer == null) continue;
            if (string.Equals(layer.name, target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void SmoothSplatWeights(float[,,] splat, int width, int height, int layers, int passes)
    {
        int passCount = Mathf.Clamp(passes, 1, 8);
        float[,,] temp = new float[height, width, layers];

        for (int p = 0; p < passCount; p++)
        {
            for (int y = 0; y < height; y++)
            {
                int yMin = Mathf.Max(0, y - 1);
                int yMax = Mathf.Min(height - 1, y + 1);
                for (int x = 0; x < width; x++)
                {
                    int xMin = Mathf.Max(0, x - 1);
                    int xMax = Mathf.Min(width - 1, x + 1);

                    for (int l = 0; l < layers; l++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int sy = yMin; sy <= yMax; sy++)
                        {
                            for (int sx = xMin; sx <= xMax; sx++)
                            {
                                sum += splat[sy, sx, l];
                                count++;
                            }
                        }

                        temp[y, x, l] = count > 0 ? (sum / count) : splat[y, x, l];
                    }
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int l = 0; l < layers; l++)
                    {
                        float v = Mathf.Max(0f, temp[y, x, l]);
                        splat[y, x, l] = v;
                        sum += v;
                    }

                    if (sum <= Epsilon)
                    {
                        if (layers > 0) splat[y, x, 0] = 1f;
                        for (int l = 1; l < layers; l++) splat[y, x, l] = 0f;
                    }
                    else
                    {
                        float inv = 1f / sum;
                        for (int l = 0; l < layers; l++) splat[y, x, l] *= inv;
                    }
                }
            }
        }
    }
}
