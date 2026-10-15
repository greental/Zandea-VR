using System;
using MyAssets.PlanetTerrain;
using UnityEngine;

public static class PlanetBiomeSmartPainter
{
    public static float[,,] PaintTile(PlanetPopulationPoc population, PlanetTileCoord coord, int resolution,
        TerrainLayer[] layers, TerrainData data, PlanetBiomeTileMap map)
    {
        var result = new float[resolution, resolution, layers.Length];
        if (layers.Length == 0) return result;
        var profiles = map ? map.biomes : population.allowedBiomes;
        var profileWeights = new float[profiles.Length];
        var settings = new TerrainPainterUtility.PaintSettings
        {
            noiseScale = .004f,
            globalNoiseStrength = .38f,
            blendSmoothness = .33f,
            heightInfluence = 1f,
            slopeInfluence = 1f
        };
        var rules = new BiomeRule[profiles.Length][];
        var indices = new int[profiles.Length][];
        for (int b = 0; b < profiles.Length; b++)
        {
            var biome = profiles[b];
            int count = biome && biome.terrainLayers != null ? biome.terrainLayers.Length : 0;
            rules[b] = new BiomeRule[count];
            indices[b] = new int[count];
            for (int j = 0; j < count; j++)
            {
                float center = count == 1 ? .5f : j / (float)(count - 1);
                rules[b][j] = new BiomeRule
                {
                    terrainLayer = biome.terrainLayers[j],
                    minHeight = count == 1 ? 0f : Mathf.Max(0f, center - .47f),
                    maxHeight = count == 1 ? 1f : Mathf.Min(1f, center + .47f),
                    minSlope = j == count - 1 && count > 1 ? 12f : 0f,
                    maxSlope = j == 0 && count > 1 ? 62f : 90f,
                    noiseStrength = .4f,
                    blendStrength = 1f
                };
                indices[b][j] = Array.IndexOf(layers, biome.terrainLayers[j]);
            }
        }
        var ruleWeights = new float[profiles.Length][];
        for (int b = 0; b < profiles.Length; b++) ruleWeights[b] = new float[rules[b].Length];
        float tileSize = data.size.x;
        float baseMeters = data.size.y * .42f;
        for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
        {
            float u = (x + .5f) / resolution, v = (z + .5f) / resolution;
            if (map) map.Sample(u, v, profileWeights);
            else population.SampleBiomeWeights(new PlanetSamplePoint((coord.x + u) * tileSize, (coord.z + v) * tileSize), profileWeights);
            float heightMeters = data.GetInterpolatedHeight(u, v);
            float slope = data.GetSteepness(u, v);
            float worldX = (coord.x + u) * tileSize, worldZ = (coord.z + v) * tileSize;
            float noise = Mathf.PerlinNoise(worldX * settings.noiseScale, worldZ * settings.noiseScale);
            for (int b = 0; b < profiles.Length; b++)
            {
                var biome = profiles[b];
                if (!biome || profileWeights[b] <= .00001f || rules[b].Length == 0) continue;
                float relief = biome.terrainShape == PlanetBiomeTerrainShape.SourceTerrain ||
                    biome.terrainShape == PlanetBiomeTerrainShape.MoonCraters ||
                    biome.terrainShape == PlanetBiomeTerrainShape.RockyHighlands
                    ? biome.sourceReliefMeters * .62f : biome.reliefMeters * population.terrainReliefMultiplier;
                var context = new TerrainPainterUtility.RuleContext
                {
                    normalizedHeight = Mathf.Clamp01(.5f + (heightMeters - baseMeters) / (2f * Mathf.Max(20f, relief))),
                    slopeDegrees = slope,
                    noise01 = noise,
                    noiseSigned = noise * 2f - 1f
                };
                settings.canyonCliffBoost = biome.canyonStrength > 0f;
                float total = 0f;
                for (int j = 0; j < rules[b].Length; j++)
                {
                    ruleWeights[b][j] = indices[b][j] < 0 ? 0f :
                        TerrainPainterUtility.EvaluateRuleWeight(rules[b][j], settings, context);
                    total += ruleWeights[b][j];
                }
                if (total <= .00001f)
                {
                    int nearest = Mathf.Clamp(Mathf.RoundToInt(context.normalizedHeight * (rules[b].Length - 1)), 0, rules[b].Length - 1);
                    if (indices[b][nearest] >= 0) result[z, x, indices[b][nearest]] += profileWeights[b];
                }
                else for (int j = 0; j < rules[b].Length; j++)
                    if (indices[b][j] >= 0) result[z, x, indices[b][j]] += profileWeights[b] * ruleWeights[b][j] / total;
            }
            float sum = 0f;
            for (int layer = 0; layer < layers.Length; layer++) sum += result[z, x, layer];
            if (sum <= .00001f) result[z, x, 0] = 1f;
            else for (int layer = 0; layer < layers.Length; layer++) result[z, x, layer] /= sum;
        }
        return result;
    }
}
