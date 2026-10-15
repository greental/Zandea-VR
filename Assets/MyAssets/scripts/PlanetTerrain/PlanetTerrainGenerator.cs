using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    [Serializable]
    public sealed class PlanetTerrainGenerationSettings
    {
        public float tileSizeMeters = 1024f;
        public float maxHeightMeters = 420f;
        public int heightmapResolution = 129;
        public int alphamapResolution = 256;
        public TerrainLayer[] terrainLayers;

        public int SamplesPerTile => Mathf.Max(1, heightmapResolution - 1);
        public float MetersPerSample => tileSizeMeters / SamplesPerTile;
    }

    public readonly struct PlanetTerrainTileBuildResult
    {
        public readonly float[,] heights;
        public readonly float[,,] alphamaps;
        public readonly double generationMilliseconds;

        public PlanetTerrainTileBuildResult(float[,] heights, float[,,] alphamaps, double generationMilliseconds)
        {
            this.heights = heights;
            this.alphamaps = alphamaps;
            this.generationMilliseconds = generationMilliseconds;
        }
    }

    /// <summary>
    /// Shared tile generation service used by both runtime fallback generation and editor-time baking.
    /// </summary>
    public static class PlanetTerrainGenerator
    {
        public static TerrainData CreateTerrainData(PlanetTerrainGenerationSettings settings)
        {
            TerrainLayer[] layers = CompactTerrainLayers(settings.terrainLayers);
            var data = new TerrainData
            {
                heightmapResolution = settings.heightmapResolution,
                size = new Vector3(settings.tileSizeMeters, settings.maxHeightMeters, settings.tileSizeMeters),
                alphamapResolution = Mathf.Clamp(settings.alphamapResolution, 16, 2048)
            };

            if (layers.Length > 0)
                data.terrainLayers = layers;

            return data;
        }

        public static PlanetTerrainTileBuildResult BuildHeights(
            PlanetTileCoord coord,
            PlanetTerrainGenerationSettings settings,
            IPlanetSurfaceCoordinateMapper coordinateMapper,
            PlanetHeightProvider heightProvider)
        {
            return BuildTile(coord, settings, coordinateMapper, heightProvider);
        }

        public static PlanetTerrainTileBuildResult BuildTile(
            PlanetTileCoord coord,
            PlanetTerrainGenerationSettings settings,
            IPlanetSurfaceCoordinateMapper coordinateMapper,
            PlanetHeightProvider heightProvider)
        {
            Stopwatch sw = Stopwatch.StartNew();
            int res = settings.heightmapResolution;
            int samplesPerTile = settings.SamplesPerTile;
            float metersPerSample = settings.MetersPerSample;
            var heights = new float[res, res];

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    PlanetSamplePoint sample = coordinateMapper.MapHeightSample(coord, x, z, samplesPerTile, metersPerSample);
                    heights[z, x] = heightProvider.SampleHeight01(sample);
                }
            }

            float[,,] alphamaps = BuildAlphamaps(coord, settings, coordinateMapper, heightProvider, heights);

            sw.Stop();
            return new PlanetTerrainTileBuildResult(heights, alphamaps, sw.Elapsed.TotalMilliseconds);
        }

        public static void ApplyHeights(Terrain terrain, TerrainCollider collider, TerrainData data, float[,] heights)
        {
            data.SetHeightsDelayLOD(0, 0, heights);
            data.SyncHeightmap();
            if (collider != null)
                collider.terrainData = data;
        }

        public static void ApplyAlphamaps(TerrainData data, float[,,] alphamaps)
        {
            if (data == null || alphamaps == null)
                return;

            if (data.terrainLayers == null || data.terrainLayers.Length != alphamaps.GetLength(2))
                Debug.LogWarning($"[PlanetTerrainGenerator] TerrainData '{data.name}' has {data.terrainLayers?.Length ?? 0} TerrainLayer(s), but alphamap has {alphamaps.GetLength(2)} layer(s). Alphamap painting may not display until matching Terrain Layers are assigned.");

            data.SetAlphamaps(0, 0, alphamaps);
            data.SetBaseMapDirty();
        }

        public static string SummarizeAlphamapCoverage(float[,,] alphamaps)
        {
            if (alphamaps == null)
                return "no alphamaps generated";

            int width = alphamaps.GetLength(1);
            int height = alphamaps.GetLength(0);
            int layers = alphamaps.GetLength(2);
            float sampleCount = Mathf.Max(1, width * height);
            string summary = $"{width}x{height}, {layers} layer(s)";
            for (int layer = 0; layer < layers; layer++)
            {
                double total = 0.0;
                float max = 0f;
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float value = alphamaps[z, x, layer];
                        total += value;
                        if (value > max)
                            max = value;
                    }
                }

                summary += $", L{layer}:avg={(total / sampleCount):0.000}/max={max:0.000}";
            }

            return summary;
        }

        public static void ApplyTileData(Terrain terrain, TerrainCollider collider, TerrainData data, PlanetTerrainTileBuildResult build)
        {
            ApplyHeights(terrain, collider, data, build.heights);
            ApplyAlphamaps(data, build.alphamaps);
        }

        static float[,,] BuildAlphamaps(
            PlanetTileCoord coord,
            PlanetTerrainGenerationSettings settings,
            IPlanetSurfaceCoordinateMapper coordinateMapper,
            PlanetHeightProvider heightProvider,
            float[,] heights)
        {
            TerrainLayer[] layers = CompactTerrainLayers(settings.terrainLayers);
            if (layers.Length == 0)
                return null;

            int layerCount = layers.Length;
            int alphaRes = Mathf.Clamp(settings.alphamapResolution, 16, 2048);
            var maps = new float[alphaRes, alphaRes, layerCount];

            for (int z = 0; z < alphaRes; z++)
            {
                for (int x = 0; x < alphaRes; x++)
                {
                    float u = alphaRes > 1 ? x / (float)(alphaRes - 1) : 0f;
                    float v = alphaRes > 1 ? z / (float)(alphaRes - 1) : 0f;
                    PlanetSamplePoint sample = coordinateMapper.MapHeightSample(
                        coord,
                        Mathf.RoundToInt(u * settings.SamplesPerTile),
                        Mathf.RoundToInt(v * settings.SamplesPerTile),
                        settings.SamplesPerTile,
                        settings.MetersPerSample);

                    float height01 = heightProvider.SampleHeight01(sample);
                    float slope01 = EstimateSlope01(sample, settings, heightProvider);
                    float variation = Mathf.PerlinNoise((float)sample.x / 650f + 41.7f, (float)sample.z / 650f - 19.3f);
                    float macroVariation = Mathf.PerlinNoise((float)sample.x / 2600f - 11.2f, (float)sample.z / 2600f + 5.8f);

                    float total = 0f;
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        float w = EvaluateLayerWeight(layer, layerCount, height01, slope01, variation, macroVariation);
                        maps[z, x, layer] = w;
                        total += w;
                    }

                    if (total <= 0.0001f)
                    {
                        maps[z, x, 0] = 1f;
                    }
                    else
                    {
                        for (int layer = 0; layer < layerCount; layer++)
                            maps[z, x, layer] /= total;
                    }
                }
            }

            return maps;
        }

        public static TerrainLayer[] CompactTerrainLayers(TerrainLayer[] layers)
        {
            if (layers == null || layers.Length == 0)
                return Array.Empty<TerrainLayer>();

            int count = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null)
                    count++;
            }

            if (count == layers.Length)
                return layers;

            var compact = new TerrainLayer[count];
            int write = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null)
                    compact[write++] = layers[i];
            }

            return compact;
        }

        static float EstimateSlope01(PlanetSamplePoint sample, PlanetTerrainGenerationSettings settings, PlanetHeightProvider heightProvider)
        {
            float step = Mathf.Max(2f, settings.MetersPerSample * 2f);
            float hL = heightProvider.SampleHeight01(new PlanetSamplePoint(sample.x - step, sample.z));
            float hR = heightProvider.SampleHeight01(new PlanetSamplePoint(sample.x + step, sample.z));
            float hD = heightProvider.SampleHeight01(new PlanetSamplePoint(sample.x, sample.z - step));
            float hU = heightProvider.SampleHeight01(new PlanetSamplePoint(sample.x, sample.z + step));
            float dhdx = (hR - hL) * settings.maxHeightMeters / (2f * step);
            float dhdz = (hU - hD) * settings.maxHeightMeters / (2f * step);
            float slopeDegrees = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz)) * Mathf.Rad2Deg;
            return Mathf.Clamp01(slopeDegrees / 55f);
        }

        static float EvaluateLayerWeight(int layer, int layerCount, float height01, float slope01, float variation, float macroVariation)
        {
            float jitter = (variation - 0.5f) * 0.12f;
            float macroJitter = (macroVariation - 0.5f) * 0.12f;
            if (layerCount == 1)
                return 1f;

            if (layerCount == 2)
            {
                if (layer == 0)
                    return Mathf.Clamp01(1.2f - height01 * 1.6f - slope01 * 0.65f + jitter);
                return Mathf.Clamp01(height01 * 1.35f + slope01 * 1.25f - 0.28f - jitter);
            }

            if (layerCount == 3)
            {
                if (layer == 0)
                    return Mathf.Clamp01((1f - SmoothRange(0.20f, 0.46f, height01)) * 3.0f + jitter);
                if (layer == 1)
                    return Bell(height01, 0.46f + macroJitter, 0.28f) * Mathf.Clamp01(1.25f - slope01 * 0.45f);
                return Mathf.Clamp01(SmoothRange(0.50f, 0.78f, height01) * 2.1f + slope01 * 2.6f - 0.35f - jitter);
            }

            // Layer convention for quick visual testing, in configured order:
            // 0 low valleys/basin floor, 1 foothills, 2 steep rock/cliffs, 3 high alpine, 4 peaks/snow/scree.
            if (layer == 0)
                return Mathf.Clamp01((1f - SmoothRange(0.18f, 0.36f, height01)) * 3.2f - slope01 * 0.8f + jitter);
            if (layer == 1)
                return Bell(height01, 0.38f + macroJitter, 0.24f) * 2.3f * Mathf.Clamp01(1.15f - slope01 * 0.45f + jitter);
            if (layer == 2)
                return Mathf.Clamp01(slope01 * 4.2f - 0.42f + SmoothRange(0.46f, 0.70f, height01) * 0.9f + Mathf.Abs(jitter));
            if (layer == 3)
                return Bell(height01, 0.66f + macroJitter, 0.18f) * 2.5f * Mathf.Clamp01(1.1f - slope01 * 0.25f + jitter);
            if (layer == 4)
                return Mathf.Clamp01(SmoothRange(0.72f + macroJitter, 0.90f, height01) * 4.0f + slope01 * 0.55f - jitter);

            float bandCenter = layerCount <= 1 ? 0f : layer / (float)(layerCount - 1);
            return Bell(height01, bandCenter, 0.18f) * 1.8f * Mathf.Clamp01(1f - slope01 * 0.15f + jitter);
        }

        static float Bell(float value, float center, float halfWidth)
        {
            halfWidth = Mathf.Max(0.0001f, halfWidth);
            return Mathf.Clamp01(1f - Mathf.Abs(value - center) / halfWidth);
        }

        static float SmoothRange(float min, float max, float value)
        {
            float t = Mathf.InverseLerp(min, max, value);
            return t * t * (3f - 2f * t);
        }
    }
}