using System;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEngine;

public static class PlanetBiomeWrapBaker
{
    public static void StitchTerrainAndBiomes(PlanetPopulationPoc population, PlanetTerrainTileSet set)
    {
        StitchTerrain(set);
        StitchBiomes(population, set);
    }

    public static void StitchTerrain(PlanetTerrainTileSet set)
    {
        Bounds(set, out int minX, out int maxX, out int minZ, out int maxZ);
        for (int z = minZ; z <= maxZ; z++) StitchHeights(Tile(set, minX, z), Tile(set, maxX, z), true);
        for (int x = minX; x <= maxX; x++) StitchHeights(Tile(set, x, minZ), Tile(set, x, maxZ), false);
    }

    public static void StitchBiomes(PlanetPopulationPoc population, PlanetTerrainTileSet set)
    {
        Bounds(set, out int minX, out int maxX, out int minZ, out int maxZ);
        for (int z = minZ; z <= maxZ; z++) StitchMaps(Map(population, minX, z), Map(population, maxX, z), true);
        for (int x = minX; x <= maxX; x++) StitchMaps(Map(population, x, minZ), Map(population, x, maxZ), false);
    }

    public static void StitchPaint(PlanetTerrainTileSet set)
    {
        Bounds(set, out int minX, out int maxX, out int minZ, out int maxZ);
        for (int z = minZ; z <= maxZ; z++) StitchAlphamaps(Tile(set, minX, z), Tile(set, maxX, z), true);
        for (int x = minX; x <= maxX; x++) StitchAlphamaps(Tile(set, x, minZ), Tile(set, x, maxZ), false);
    }

    static void StitchHeights(TerrainData low, TerrainData high, bool xAxis)
    {
        int resolution = low.heightmapResolution;
        if (high.heightmapResolution != resolution) throw new InvalidOperationException("Wrapped height resolutions differ.");
        var a = low.GetHeights(0, 0, resolution, resolution);
        var b = high.GetHeights(0, 0, resolution, resolution);
        int last = resolution - 1, band = Mathf.Min(last, Mathf.RoundToInt(last * .75f));
        for (int cross = 0; cross <= last; cross++)
        {
            float shared = xAxis ? (a[cross, 0] + b[cross, last]) * .5f : (a[0, cross] + b[last, cross]) * .5f;
            for (int distance = 0; distance <= band; distance++)
            {
                float keep = Mathf.SmoothStep(0f, 1f, distance / (float)band);
                if (xAxis)
                {
                    a[cross, distance] = Mathf.Lerp(shared, a[cross, distance], keep);
                    b[cross, last - distance] = Mathf.Lerp(shared, b[cross, last - distance], keep);
                }
                else
                {
                    a[distance, cross] = Mathf.Lerp(shared, a[distance, cross], keep);
                    b[last - distance, cross] = Mathf.Lerp(shared, b[last - distance, cross], keep);
                }
            }
        }
        Undo.RecordObject(low, "Stitch wrapped biome terrain");
        Undo.RecordObject(high, "Stitch wrapped biome terrain");
        low.SetHeights(0, 0, a);
        high.SetHeights(0, 0, b);
        EditorUtility.SetDirty(low); EditorUtility.SetDirty(high);
    }

    static void StitchMaps(PlanetBiomeTileMap low, PlanetBiomeTileMap high, bool xAxis)
    {
        int resolution = low.resolution, count = low.biomes.Length;
        if (high.resolution != resolution || high.biomes.Length != count)
            throw new InvalidOperationException("Wrapped biome map layouts differ.");
        int last = resolution - 1, band = Mathf.Min(last, Mathf.RoundToInt(last * .75f));
        for (int cross = 0; cross <= last; cross++) for (int biome = 0; biome < count; biome++)
        {
            int edgeA = ((xAxis ? cross * resolution : 0) + (xAxis ? 0 : cross)) * count + biome;
            int edgeB = ((xAxis ? cross * resolution : last * resolution) + (xAxis ? last : cross)) * count + biome;
            float shared = (low.weights[edgeA] + high.weights[edgeB]) * .5f;
            for (int distance = 0; distance <= band; distance++)
            {
                float keep = Mathf.SmoothStep(0f, 1f, distance / (float)band);
                int indexA = ((xAxis ? cross * resolution : distance * resolution) + (xAxis ? distance : cross)) * count + biome;
                int indexB = ((xAxis ? cross * resolution : (last - distance) * resolution) + (xAxis ? last - distance : cross)) * count + biome;
                low.weights[indexA] = Mathf.Lerp(shared, low.weights[indexA], keep);
                high.weights[indexB] = Mathf.Lerp(shared, high.weights[indexB], keep);
            }
        }
        EditorUtility.SetDirty(low); EditorUtility.SetDirty(high);
    }

    static void StitchAlphamaps(TerrainData low, TerrainData high, bool xAxis)
    {
        int resolution = low.alphamapResolution;
        if (high.alphamapResolution != resolution || high.alphamapLayers != low.alphamapLayers)
            throw new InvalidOperationException("Wrapped paint layouts differ.");
        var a = low.GetAlphamaps(0, 0, resolution, resolution);
        var b = high.GetAlphamaps(0, 0, resolution, resolution);
        int last = resolution - 1, band = Mathf.Max(1, resolution / 4);
        for (int cross = 0; cross <= last; cross++) for (int layer = 0; layer < low.alphamapLayers; layer++)
        {
            float shared = xAxis ? (a[cross, 0, layer] + b[cross, last, layer]) * .5f
                : (a[0, cross, layer] + b[last, cross, layer]) * .5f;
            for (int distance = 0; distance <= band; distance++)
            {
                float keep = Mathf.SmoothStep(0f, 1f, distance / (float)band);
                if (xAxis)
                {
                    a[cross, distance, layer] = Mathf.Lerp(shared, a[cross, distance, layer], keep);
                    b[cross, last - distance, layer] = Mathf.Lerp(shared, b[cross, last - distance, layer], keep);
                }
                else
                {
                    a[distance, cross, layer] = Mathf.Lerp(shared, a[distance, cross, layer], keep);
                    b[last - distance, cross, layer] = Mathf.Lerp(shared, b[last - distance, cross, layer], keep);
                }
            }
        }
        low.SetAlphamaps(0, 0, a); high.SetAlphamaps(0, 0, b);
        EditorUtility.SetDirty(low); EditorUtility.SetDirty(high);
    }

    static void Bounds(PlanetTerrainTileSet set, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        minX = minZ = int.MaxValue; maxX = maxZ = int.MinValue;
        foreach (var record in set.authoredTiles)
        {
            minX = Mathf.Min(minX, record.coord.x); maxX = Mathf.Max(maxX, record.coord.x);
            minZ = Mathf.Min(minZ, record.coord.z); maxZ = Mathf.Max(maxZ, record.coord.z);
        }
        if (minX == int.MaxValue) throw new InvalidOperationException("Wrapped tile set has no terrain.");
        if (set.authoredTiles.Count != (maxX - minX + 1) * (maxZ - minZ + 1))
            throw new InvalidOperationException("Wrapped biome treadmill needs a complete rectangular tile grid.");
    }

    static TerrainData Tile(PlanetTerrainTileSet set, int x, int z)
    {
        if (!set.TryGetTerrainData(new PlanetTileCoord(x, z), out var data))
            throw new InvalidOperationException($"Missing wrapped terrain tile ({x}, {z}).");
        return data;
    }

    static PlanetBiomeTileMap Map(PlanetPopulationPoc population, int x, int z)
    {
        var map = population.biomeMaps.Find(item => item && item.coord.x == x && item.coord.z == z);
        if (!map) throw new InvalidOperationException($"Missing wrapped biome map ({x}, {z}).");
        return map;
    }
}
