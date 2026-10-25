using System;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEngine;

public static class PlanetBiomeValidation
{
    public static void ValidateGenerated(PlanetPopulationPoc population)
    {
        var set = population.GetComponent<PlanetTerrainTreadmill>().AuthoredTileSet;
        Require(set && set.authoredTiles.Count == population.biomeMaps.Count, "Missing biome maps");
        int edges = 0, mixedTiles = 0;
        var coverage = new int[population.allowedBiomes.Length];
        var areaSums = new double[coverage.Length];
        long areaSamples = 0;
        var dominantTiles = new int[coverage.Length];
        var centerWeights = new float[coverage.Length];
        foreach (var tile in set.authoredTiles)
        {
            var map = population.biomeMaps.Find(m => m && m.coord.Equals(tile.coord));
            Require(map && map.weights.Length == map.resolution * map.resolution * map.biomes.Length, "Invalid saved biome map");
            map.Sample(.5f, .5f, centerWeights);
            int strongest = 0;
            for (int b = 1; b < centerWeights.Length; b++)
                if (centerWeights[b] > centerWeights[strongest]) strongest = b;
            if (centerWeights[strongest] > .5f) dominantTiles[strongest]++;
            bool mixed = false;
            for (int s = 0; s < map.resolution * map.resolution; s++)
            {
                float sum = 0;
                int contributors = 0;
                for (int b = 0; b < map.biomes.Length; b++)
                {
                    float w = map.weights[s * map.biomes.Length + b];
                    sum += w;
                    areaSums[b] += w;
                    if (w > .5f) coverage[b]++;
                    if (w > .01f) contributors++;
                }
                Require(Mathf.Abs(sum - 1) < .00001f, "Saved weights are not normalized");
                areaSamples++;
                int x = s % map.resolution, z = s / map.resolution;
                if (x > 0 && z > 0 && x < map.resolution - 1 && z < map.resolution - 1 && contributors > 1) mixed = true;
            }
            if (mixed) mixedTiles++;
            int last = tile.terrainData.heightmapResolution - 1;
            foreach (var offset in new[] { new PlanetTileCoord(1, 0), new PlanetTileCoord(0, 1) })
            {
                var coord = new PlanetTileCoord(tile.coord.x + offset.x, tile.coord.z + offset.z);
                if (!set.TryGetTerrainData(coord, out var other)) continue;
                var otherMap = population.biomeMaps.Find(m => m && m.coord.Equals(coord));
                var a = new float[map.biomes.Length];
                var b = new float[a.Length];
                for (int i = 0; i <= last; i++)
                {
                    float h1 = tile.terrainData.GetHeight(offset.x == 1 ? last : i, offset.z == 1 ? last : i);
                    float h2 = other.GetHeight(offset.x == 1 ? 0 : i, offset.z == 1 ? 0 : i);
                    Require(h1 == h2, "Baked height edge mismatch");
                    float t = i / (float)last;
                    map.Sample(offset.x == 1 ? 1 : t, offset.z == 1 ? 1 : t, a);
                    otherMap.Sample(offset.x == 1 ? 0 : t, offset.z == 1 ? 0 : t, b);
                    for (int j = 0; j < a.Length; j++) Require(a[j] == b[j], "Baked biome edge mismatch");
                }
                edges++;
            }
        }
        Require(mixedTiles > 0, "No transitions within tile interiors");
        if (population.balanceBiomeCoverage)
        {
            float[] requested = null;
            if (population.biomeDistribution == PlanetBiomeDistribution.Percentages)
                Require(population.TryGetRequestedBiomeShares(out requested, out _), "Invalid requested biome percentages");
            for (int b = 0; b < coverage.Length; b++)
            {
                float target = requested != null ? requested[b] / 100f : 1f / coverage.Length;
                if (requested != null)
                    Require(System.Math.Abs(areaSums[b] / areaSamples - target) < .025,
                        $"Biome {population.allowedBiomes[b].name} differs from its requested area percentage");
                if (target < .02f) continue;
                Require(coverage[b] > 0, $"Selected biome {population.allowedBiomes[b].name} has no dominant area");
                if (set.authoredTiles.Count >= coverage.Length)
                    Require(dominantTiles[b] > 0, $"Selected biome {population.allowedBiomes[b].name} has no dominant tile");
            }
        }
        if (population.GetComponent<PlanetBiomeWrapRuntime>()) ValidateFarSideEdges(population, set);
        Debug.Log($"Saved biome treadmill passed: {set.authoredTiles.Count} tiles, {edges} shared height/biome edges, {mixedTiles} tiles with interior transitions, dominant tiles [{string.Join(", ", dominantTiles)}].");
    }

    static void ValidateFarSideEdges(PlanetPopulationPoc population, PlanetTerrainTileSet set)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var tile in set.authoredTiles)
        {
            minX = Mathf.Min(minX, tile.coord.x); maxX = Mathf.Max(maxX, tile.coord.x);
            minZ = Mathf.Min(minZ, tile.coord.z); maxZ = Mathf.Max(maxZ, tile.coord.z);
        }
        var a = new float[population.allowedBiomes.Length];
        var b = new float[a.Length];
        for (int axis = 0; axis < 2; axis++)
        {
            int start = axis == 0 ? minZ : minX, end = axis == 0 ? maxZ : maxX;
            for (int cross = start; cross <= end; cross++)
            {
                var lowCoord = axis == 0 ? new PlanetTileCoord(minX, cross) : new PlanetTileCoord(cross, minZ);
                var highCoord = axis == 0 ? new PlanetTileCoord(maxX, cross) : new PlanetTileCoord(cross, maxZ);
                Require(set.TryGetTerrainData(lowCoord, out var low), "Missing low wrapped tile");
                Require(set.TryGetTerrainData(highCoord, out var high), "Missing high wrapped tile");
                var lowMap = population.biomeMaps.Find(item => item && item.coord.Equals(lowCoord));
                var highMap = population.biomeMaps.Find(item => item && item.coord.Equals(highCoord));
                Require(lowMap && highMap, "Missing wrapped biome map");
                int last = low.heightmapResolution - 1;
                for (int i = 0; i <= last; i++)
                {
                    float h0 = axis == 0 ? low.GetHeight(0, i) : low.GetHeight(i, 0);
                    float h1 = axis == 0 ? high.GetHeight(last, i) : high.GetHeight(i, last);
                    Require(h0 == h1, "Opposite treadmill height edges do not match");
                }
                int mapLast = lowMap.resolution - 1;
                for (int i = 0; i <= mapLast; i++)
                {
                    float t = i / (float)mapLast;
                    lowMap.Sample(axis == 0 ? 0 : t, axis == 0 ? t : 0, a);
                    highMap.Sample(axis == 0 ? 1 : t, axis == 0 ? t : 1, b);
                    for (int j = 0; j < a.Length; j++) Require(a[j] == b[j], "Opposite treadmill biome edges do not match");
                }
                var lowPaint = low.GetAlphamaps(0, 0, low.alphamapResolution, low.alphamapResolution);
                var highPaint = high.GetAlphamaps(0, 0, high.alphamapResolution, high.alphamapResolution);
                int paintLast = low.alphamapResolution - 1;
                for (int i = 0; i <= paintLast; i++) for (int layer = 0; layer < low.alphamapLayers; layer++)
                {
                    float p0 = axis == 0 ? lowPaint[i, 0, layer] : lowPaint[0, i, layer];
                    float p1 = axis == 0 ? highPaint[i, paintLast, layer] : highPaint[paintLast, i, layer];
                    Require(Mathf.Abs(p0 - p1) < .00001f, "Opposite treadmill paint edges do not match");
                }
            }
        }
        var wrapped = PlanetBiomeWrapRuntime.CreateWrappedSet(set);
        try
        {
            int width = maxX - minX + 1, height = maxZ - minZ + 1;
            foreach (var tile in set.authoredTiles)
            {
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                {
                    var repeated = new PlanetTileCoord(tile.coord.x + dx * width, tile.coord.z + dz * height);
                    Require(wrapped.TryGetTerrainData(repeated, out var data) && data == tile.terrainData,
                        "Wrapped runtime lookup did not repeat the authored tile");
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(wrapped); }
        Debug.Log($"Wrapped treadmill passed: opposite X/Z height, biome, and paint edges match across {maxX-minX+1}x{maxZ-minZ+1} tiles.");
    }

    [MenuItem("Zandea/Planet Population/Validate Biome Continuity")]
    public static void Validate()
    {
        var root = new GameObject("Biome validation");
        root.SetActive(false);
        var population = root.AddComponent<PlanetPopulationPoc>();
        var profiles = new PlanetBiomeProfile[8];
        try
        {
            for (int i = 0; i < profiles.Length; i++) profiles[i] = ScriptableObject.CreateInstance<PlanetBiomeProfile>();
            population.allowedBiomes = profiles;
            population.populatePlanet = false;
            population.BindForBake(root.GetComponent<PlanetTerrainTreadmill>());
            var a = new float[8];
            var b = new float[8];
            int mixed = 0;
            float maximumJump = 0;
            for (int z = -120; z <= 120; z++) for (int x = -120; x <= 120; x++)
            {
                var point = new PlanetSamplePoint(x * 31.25, z * 31.25);
                population.SampleBiomeWeights(point, a);
                population.SampleBiomeWeights(new PlanetSamplePoint(point.x + .01, point.z + .01), b);
                float sum = 0;
                int contributors = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    Require(a[i] >= 0 && a[i] <= 1, "Invalid weight");
                    sum += a[i];
                    if (a[i] > .01f) contributors++;
                    maximumJump = Mathf.Max(maximumJump, Mathf.Abs(a[i] - b[i]));
                }
                Require(Mathf.Abs(sum - 1) < .00001f, "Weights are not normalized");
                if (contributors > 1) mixed++;
            }
            Require(mixed > 1000, "Expected transitions inside tiles");
            Require(maximumJump < .001f, "Discontinuous region boundary");
            population.balanceBiomeCoverage = true;
            population.biomeRegionMeters = 7500;
            population.biomeBlendMeters = 2500;
            population.biomeLayoutMinX = population.biomeLayoutMinZ = -5;
            population.biomeLayoutTilesX = population.biomeLayoutTilesZ = 11;
            population.biomeLayoutTileMeters = 1024;
            population.BindForBake(root.GetComponent<PlanetTerrainTreadmill>());
            var represented = new int[8];
            for (int z = -5; z <= 5; z++) for (int x = -5; x <= 5; x++)
            {
                population.SampleBiomeWeights(new PlanetSamplePoint((x + .5) * 1024, (z + .5) * 1024), a);
                for (int i = 0; i < 8; i++) if (a[i] > .5f) represented[i]++;
            }
            for (int i = 0; i < 8; i++) Require(represented[i] > 0, "Balanced layout omitted a selected biome");
            var left = PlanetPopulationPocEditor.BakeBiomeMap(population, new PlanetTileCoord(-1, 0), 65, 1024);
            var right = PlanetPopulationPocEditor.BakeBiomeMap(population, new PlanetTileCoord(0, 0), 65, 1024);
            try
            {
                for (int z = 0; z < 65; z++)
                {
                    left.Sample(1, z / 64f, a);
                    right.Sample(0, z / 64f, b);
                    for (int i = 0; i < 8; i++) Require(a[i] == b[i], "Shared edge mismatch");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(left); UnityEngine.Object.DestroyImmediate(right); }
            var layers = new TerrainLayer[8];
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(1024, 520, 1024) };
            var paintMap = PlanetPopulationPocEditor.BakeBiomeMap(population, new PlanetTileCoord(0, 0), 33, 1024);
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    layers[i] = new TerrainLayer();
                    profiles[i].terrainLayers = new[] { layers[i] };
                }
                population.planetSeed++; // Painting must use saved placement, even after recipe edits.
                var paint = population.PaintTile(new PlanetTileCoord(0, 0), 16, layers, data, paintMap);
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    paintMap.Sample((x + .5f) / 16, (z + .5f) / 16, a);
                    for (int i = 0; i < 8; i++) Require(Mathf.Abs(paint[z, x, i] - a[i]) < .00001f, "Paint escaped authored biome weights");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(paintMap);
                UnityEngine.Object.DestroyImmediate(data);
                foreach (var layer in layers) if (layer) UnityEngine.Object.DestroyImmediate(layer);
            }
            population.biomeDistribution = PlanetBiomeDistribution.Percentages;
            population.biomeShares = new System.Collections.Generic.List<PlanetBiomeShare>();
            for (int i = 0; i < profiles.Length; i++)
                population.biomeShares.Add(new PlanetBiomeShare
                {
                    biome = profiles[i], automatic = i > 1,
                    percentage = i == 0 ? 30f : i == 1 ? 50f : 0f
                });
            population.BindForBake(root.GetComponent<PlanetTerrainTreadmill>());
            var area = new double[profiles.Length];
            for (int z = 0; z < 64; z++) for (int x = 0; x < 64; x++)
            {
                population.SampleBiomeWeights(new PlanetSamplePoint(
                    (-5 + (x + .5) * 11 / 64) * 1024,
                    (-5 + (z + .5) * 11 / 64) * 1024), a);
                for (int i = 0; i < area.Length; i++) area[i] += a[i] / (64 * 64);
            }
            Require(System.Math.Abs(area[0] - .3) < .01 && System.Math.Abs(area[1] - .5) < .01,
                "Percentage layout missed its requested canyon/dune coverage");
            for (int i = 2; i < area.Length; i++)
                Require(System.Math.Abs(area[i] - .2 / 6) < .01, "Auto percentages did not share the remainder");
            Debug.Log($"Biome validation passed: 58,081 samples; {mixed} mixed samples; maximum 1cm weight change {maximumJump}; shared edges identical.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            foreach (var profile in profiles) if (profile) UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
