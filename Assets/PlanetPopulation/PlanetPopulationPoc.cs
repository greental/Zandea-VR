using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScavengerNPC;

namespace MyAssets.PlanetTerrain
{
    public enum PlanetBiomeDistribution { Random, Percentages }

    [Serializable]
    public sealed class PlanetBiomeShare
    {
        public PlanetBiomeProfile biome;
        public bool automatic = true;
        [Range(0f, 100f)] public float percentage;
    }

    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(PlanetTerrainTreadmill))]
    public sealed class PlanetPopulationPoc : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
    {
        public int planetSeed = 42;
        public PlanetBiomeProfile[] allowedBiomes;
        public PlanetBiomeDistribution biomeDistribution;
        [HideInInspector] public List<PlanetBiomeShare> biomeShares = new List<PlanetBiomeShare>();
        [Min(1)] public int bakeRadiusTiles = 2;
        [Range(1f, 4f)] public float terrainReliefMultiplier = 2f;
        [Min(33)] public int bakeHeightmapResolution = 257;
        [Min(16)] public int bakeAlphamapResolution = 128;
        [InspectorName("Override Baked Layer Finish")]
        public bool makeBakedLayersMatte = true;
        [Range(0f, 1f)] public float bakedLayerSmoothness = 0f;
        [Tooltip("Region spacing in planet meters, independent of tile boundaries.")]
        [Min(50)] public float biomeRegionMeters = 750;
        [Tooltip("Transition width, limited to half the region spacing.")]
        [Min(1)] public float biomeBlendMeters = 250;
        public bool paintBiomesOnGenerate;
        [HideInInspector] public bool balanceBiomeCoverage;
        [HideInInspector] public int biomeLayoutMinX, biomeLayoutMinZ, biomeLayoutTilesX, biomeLayoutTilesZ;
        [HideInInspector] public float biomeLayoutTileMeters;
        [HideInInspector] public List<PlanetBiomeTileMap> biomeMaps = new List<PlanetBiomeTileMap>();
        public bool populatePlanet = true;
        [Range(1, 9)] public int outpostSpacing = 3;
        [Min(0)] public float minimumSeparationMeters = 300;
        [Min(0)] public float smallWeight = 65, mediumWeight = 25, largeWeight = 10;
        [Min(10)] public float siteRadius = 75;
        [Min(1)] public float siteFalloff = 65;
        [Range(0, 2)] public float siteVariationMeters = 1;
        [Min(100)] public float activationDistance = 700;
        [Min(100)] public float deactivationDistance = 950;
        public OutpostFactionTheme faction;
        public bool showGizmos = true;
        public bool logGeneration;

        PlanetTerrainTreadmill treadmill;
        float maxHeightMeters;
        readonly Dictionary<uint, GameObject> active = new Dictionary<uint, GameObject>();
        readonly Dictionary<uint, NavMeshDataInstance> navMeshes = new Dictionary<uint, NavMeshDataInstance>();
        readonly HashSet<uint> wanted = new HashSet<uint>();
        readonly List<uint> removal = new List<uint>();
        readonly Dictionary<uint, float> referenceHeights = new Dictionary<uint, float>();
        List<SiteInfluence> tileBakeSites;
        readonly BiomeRegion[] cachedRegions = new BiomeRegion[25];
        readonly float[] regionDistances = new float[25];
        BiomeRegion[] balancedRegions;
        BiomeRegion[] weightedSites;
        float[] weightedBiases;
        float[] weightedScratch;
        float weightedTemperature;
        int balancedColumns, balancedRows;
        float[] biomeWeights;
        int cachedRegionX = int.MinValue, cachedRegionZ = int.MinValue;
        float nextUpdate;

        readonly struct BiomeRegion
        {
            public readonly float x, z;
            public readonly int biomeIndex;
            public BiomeRegion(float x, float z, int biomeIndex)
            { this.x = x; this.z = z; this.biomeIndex = biomeIndex; }
        }

        readonly struct SiteInfluence
        {
            public readonly PlanetPoiDescriptor poi;
            public readonly float reference, cosine, sine;
            public SiteInfluence(PlanetPoiDescriptor poi, float reference, float angle)
            { this.poi = poi; this.reference = reference; cosine = Mathf.Cos(angle); sine = Mathf.Sin(angle); }
        }

        void Awake()
        {
            treadmill = GetComponent<PlanetTerrainTreadmill>();
            maxHeightMeters = treadmill.GenerationSettings.maxHeightMeters;
        }

        void OnValidate()
        {
            cachedRegionX = cachedRegionZ = int.MinValue;
            balancedRegions = null;
            weightedSites = null;
        }

        public void BindForBake(PlanetTerrainTreadmill source)
        {
            treadmill = source;
            maxHeightMeters = source.GenerationSettings.maxHeightMeters;
            referenceHeights.Clear();
            cachedRegionX = cachedRegionZ = int.MinValue;
            balancedRegions = null;
            weightedSites = null;
        }

        public void EndBake()
        {
            tileBakeSites = null;
        }

        public void BeginTileBake(PlanetTileCoord coord)
        {
            tileBakeSites = new List<SiteInfluence>();
            cachedRegionX = cachedRegionZ = int.MinValue;
            if (!populatePlanet) return;
            double tileSize = treadmill.TileSizeMeters;
            double reach = siteRadius + siteFalloff;
            double cell = tileSize * Math.Sqrt(outpostSpacing);
            int minX = (int)Math.Floor((coord.x * tileSize - reach) / cell) - 1;
            int maxX = (int)Math.Floor(((coord.x + 1) * tileSize + reach) / cell) + 1;
            int minZ = (int)Math.Floor((coord.z * tileSize - reach) / cell) - 1;
            int maxZ = (int)Math.Floor(((coord.z + 1) * tileSize + reach) / cell) + 1;
            for (int z = minZ; z <= maxZ; z++) for (int x = minX; x <= maxX; x++)
            {
                if (!TryPoi(x, z, out var poi)) continue;
                if (poi.x < coord.x * tileSize - reach || poi.x > (coord.x + 1) * tileSize + reach ||
                    poi.z < coord.z * tileSize - reach || poi.z > (coord.z + 1) * tileSize + reach) continue;
                float angle = Unit(Hash(planetSeed, x, z, 81)) * 6.28318f;
                tileBakeSites.Add(new SiteInfluence(poi, ReferenceHeight(poi), angle));
            }
        }

        public float SampleBakedHeight(PlanetSamplePoint point) => ModifyHeight(point, 0f);

        public TerrainLayer[] CollectTerrainLayers()
        {
            var layers = new List<TerrainLayer>();
            if (allowedBiomes != null) foreach (var biome in allowedBiomes)
                if (biome && biome.terrainLayers != null) foreach (var layer in biome.terrainLayers)
                    if (layer && !layers.Contains(layer)) layers.Add(layer);
            return layers.ToArray();
        }

        public float[,,] PaintTile(PlanetTileCoord coord, int resolution, TerrainLayer[] layers, TerrainData terrainData, PlanetBiomeTileMap authoredMap = null)
        {
            var maps = new float[resolution, resolution, layers.Length];
            if (layers.Length == 0) return maps;
            float tileSize = treadmill.TileSizeMeters;
            for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
            {
                var point = new PlanetSamplePoint((coord.x + (x + .5) / resolution) * tileSize,
                    (coord.z + (z + .5) / resolution) * tileSize);
                float u = (x + .5f) / resolution, v = (z + .5f) / resolution;
                float heightMeters = terrainData.GetInterpolatedHeight(u, v);
                float slope = Mathf.Clamp01(terrainData.GetSteepness(u, v) / 45f);
                var profiles = authoredMap ? authoredMap.biomes : allowedBiomes;
                EnsureWeightBuffer(profiles.Length);
                if (authoredMap) authoredMap.Sample(u, v, biomeWeights);
                else SampleBiomeWeights(point, biomeWeights);
                for (int b = 0; b < profiles.Length; b++)
                    AddLayerWeights(profiles[b], biomeWeights[b], point, heightMeters, slope, layers, maps, z, x);
                float total = 0;
                for (int i = 0; i < layers.Length; i++) total += maps[z, x, i];
                if (total <= .0001f) maps[z, x, 0] = 1;
                else for (int i = 0; i < layers.Length; i++) maps[z, x, i] /= total;
            }
            return maps;
        }

        void AddLayerWeights(PlanetBiomeProfile biome, float weight, PlanetSamplePoint point, float heightMeters, float slope,
            TerrainLayer[] layers, float[,,] maps, int z, int x)
        {
            if (!biome || biome.terrainLayers == null || weight <= 0) return;
            int count = biome.terrainLayers.Length;
            float relief = Mathf.Max(10f, biome.reliefMeters * terrainReliefMultiplier);
            float relativeHeight = (heightMeters - .42f * maxHeightMeters) / (2f * relief);
            float variation = (Mathf.PerlinNoise((float)point.x / 190f + planetSeed * .17f,
                (float)point.z / 190f - planetSeed * .13f) - .5f) * .45f;
            float position = Mathf.Clamp01(.5f + relativeHeight + variation + slope * .3f) * Mathf.Max(0, count - 1);
            for (int j = 0; j < count; j++)
            {
                int index = Array.IndexOf(layers, biome.terrainLayers[j]);
                if (index < 0) continue;
                float bandWeight = Mathf.Max(0f, 1f - Mathf.Abs(position - j));
                maps[z, x, index] += weight * bandWeight;
            }
        }

        void OnDestroy()
        {
            foreach (var instance in navMeshes.Values) instance.Remove();
            navMeshes.Clear();
        }

        public void OnOriginShift(Vector3 shift)
        {
            // FloatingOriginManager already moves root GameObjects before notifying listeners.
        }

        static uint Hash(int seed, int x, int z, uint salt)
        {
            unchecked
            {
                uint h = (uint)seed ^ salt;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu;
                return h ^ (h >> 16);
            }
        }
        static float Unit(uint h) => (h & 0xffffffu) / 16777216f;
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        void EnsureWeightBuffer(int count)
        {
            if (biomeWeights == null || biomeWeights.Length != count) biomeWeights = new float[count];
        }

        public bool TryGetRequestedBiomeShares(out float[] shares, out string error)
        {
            int count = allowedBiomes?.Length ?? 0;
            shares = new float[count];
            error = null;
            if (count == 0) { error = "Select at least one biome."; return false; }
            int automaticCount = 0;
            float fixedTotal = 0;
            for (int i = 0; i < count; i++)
            {
                PlanetBiomeShare entry = biomeShares?.Find(item => item != null && item.biome == allowedBiomes[i]);
                if (entry == null || entry.automatic) { automaticCount++; continue; }
                shares[i] = Mathf.Clamp(entry.percentage, 0f, 100f);
                fixedTotal += shares[i];
            }
            if (fixedTotal > 100.001f)
            {
                error = $"Fixed biome percentages total {fixedTotal:0.##}%. Reduce them to 100% or less.";
                return false;
            }
            if (automaticCount == 0 && Mathf.Abs(fixedTotal - 100f) > .01f)
            {
                error = $"Fixed biome percentages total {fixedTotal:0.##}%. They must total 100%.";
                return false;
            }
            float remainder = automaticCount > 0 ? Mathf.Max(0f, 100f - fixedTotal) / automaticCount : 0;
            for (int i = 0; i < count; i++)
            {
                PlanetBiomeShare entry = biomeShares?.Find(item => item != null && item.biome == allowedBiomes[i]);
                if (entry == null || entry.automatic) shares[i] = remainder;
            }
            return true;
        }

        float BiomeHeight(PlanetSamplePoint p, float natural)
        {
            if (allowedBiomes == null || allowedBiomes.Length == 0) return natural;
            EnsureWeightBuffer(allowedBiomes.Length);
            SampleBiomeWeights(p, biomeWeights);
            float height = 0;
            for (int i = 0; i < allowedBiomes.Length; i++)
                if (biomeWeights[i] > 0) height += biomeWeights[i] * EvaluateBiome(allowedBiomes[i], p, natural);
            return height;
        }

        // A compact, normalized partition over ALL nearby regions. Choosing only the two
        // closest regions causes discontinuities when second/third place exchange.
        // Planet coordinates keep this field independent of tiles and floating origin.
        public void SampleBiomeWeights(PlanetSamplePoint p, float[] weights)
        {
            if (allowedBiomes == null || weights.Length != allowedBiomes.Length)
                throw new ArgumentException("One weight is required for each allowed biome.");
            Array.Clear(weights, 0, weights.Length);
            if (weights.Length == 0) return;
            bool balanced = balanceBiomeCoverage && biomeLayoutTilesX > 0 && biomeLayoutTilesZ > 0 && biomeLayoutTileMeters > 0;
            if (balanced && biomeDistribution == PlanetBiomeDistribution.Percentages)
            {
                SamplePercentageWeights(p, weights);
                return;
            }
            if (balanced) EnsureBalancedRegions();
            float requested = Mathf.Max(50f, biomeRegionMeters);
            float cellX = balanced ? biomeLayoutTilesX * biomeLayoutTileMeters / balancedColumns : requested;
            float cellZ = balanced ? biomeLayoutTilesZ * biomeLayoutTileMeters / balancedRows : requested;
            double originX = balanced ? biomeLayoutMinX * (double)biomeLayoutTileMeters : 0;
            double originZ = balanced ? biomeLayoutMinZ * (double)biomeLayoutTileMeters : 0;
            int cx = (int)Math.Floor((p.x - originX) / cellX), cz = (int)Math.Floor((p.z - originZ) / cellZ);
            if (balanced)
            {
                cx = Mathf.Clamp(cx, 0, balancedColumns - 1);
                cz = Mathf.Clamp(cz, 0, balancedRows - 1);
            }
            if (cx != cachedRegionX || cz != cachedRegionZ)
            {
                int index = 0;
                for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (balanced)
                    {
                        cachedRegions[index++] = x >= 0 && x < balancedColumns && z >= 0 && z < balancedRows
                            ? balancedRegions[z * balancedColumns + x] : new BiomeRegion(0, 0, -1);
                        continue;
                    }
                    uint h = Hash(planetSeed, x, z, 0x52454749);
                    float px = (x + .5f + (Unit(h) - .5f) * .7f) * requested;
                    float pz = (z + .5f + (Unit(Hash(planetSeed, x, z, 15)) - .5f) * .7f) * requested;
                    cachedRegions[index++] = new BiomeRegion(px, pz, (int)(h % (uint)weights.Length));
                }
                cachedRegionX = cx; cachedRegionZ = cz;
            }
            float nearest = float.MaxValue;
            for (int i = 0; i < cachedRegions.Length; i++)
            {
                var region = cachedRegions[i];
                if (region.biomeIndex < 0) { regionDistances[i] = float.MaxValue; continue; }
                double dx = region.x - p.x, dz = region.z - p.z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                regionDistances[i] = distance;
                nearest = Mathf.Min(nearest, distance);
            }
            float blend = Mathf.Clamp(biomeBlendMeters, 1f, Mathf.Min(cellX, cellZ) * .5f);
            float total = 0;
            for (int i = 0; i < cachedRegions.Length; i++)
            {
                if (cachedRegions[i].biomeIndex < 0) continue;
                float w = 1f - Smooth((regionDistances[i] - nearest) / blend);
                weights[cachedRegions[i].biomeIndex] += w;
                total += w;
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
        }

        void EnsureBalancedRegions()
        {
            if (balancedRegions != null) return;
            int biomeCount = allowedBiomes.Length;
            float width = biomeLayoutTilesX * biomeLayoutTileMeters;
            float height = biomeLayoutTilesZ * biomeLayoutTileMeters;
            float requested = Mathf.Max(50f, biomeRegionMeters);
            balancedColumns = Mathf.Max(1, Mathf.CeilToInt(width / requested),
                Mathf.CeilToInt(Mathf.Sqrt(biomeCount * width / height)));
            balancedRows = Mathf.Max(1, Mathf.CeilToInt(height / requested),
                Mathf.CeilToInt(biomeCount / (float)balancedColumns));
            int count = balancedColumns * balancedRows;
            var slots = new int[count];
            var profiles = new int[biomeCount];
            for (int i = 0; i < count; i++) slots[i] = i;
            for (int i = 0; i < biomeCount; i++) profiles[i] = i;
            // Every selected profile gets a seed before a profile is repeated.
            for (int i = count - 1; i > 0; i--)
            {
                int j = (int)(Hash(planetSeed, i, count, 0x534c4f54) % (uint)(i + 1));
                int value = slots[i]; slots[i] = slots[j]; slots[j] = value;
            }
            for (int i = biomeCount - 1; i > 0; i--)
            {
                int j = (int)(Hash(planetSeed, i, biomeCount, 0x50524f46) % (uint)(i + 1));
                int value = profiles[i]; profiles[i] = profiles[j]; profiles[j] = value;
            }
            balancedRegions = new BiomeRegion[count];
            float stepX = width / balancedColumns, stepZ = height / balancedRows;
            for (int i = 0; i < count; i++)
            {
                int slot = slots[i], x = slot % balancedColumns, z = slot / balancedColumns;
                float px = (float)(biomeLayoutMinX * (double)biomeLayoutTileMeters) +
                    (x + .5f + (Unit(Hash(planetSeed, x, z, 91)) - .5f) * .3f) * stepX;
                float pz = (float)(biomeLayoutMinZ * (double)biomeLayoutTileMeters) +
                    (z + .5f + (Unit(Hash(planetSeed, x, z, 92)) - .5f) * .3f) * stepZ;
                balancedRegions[slot] = new BiomeRegion(px, pz, profiles[i % biomeCount]);
            }
        }

        void EnsureWeightedSites()
        {
            if (weightedSites != null) return;
            EnsureBalancedRegions();
            if (!TryGetRequestedBiomeShares(out float[] shares, out string error))
                throw new InvalidOperationException(error);
            var sites = new List<BiomeRegion>();
            var targets = new List<float>();
            var seen = new bool[shares.Length];
            foreach (var region in balancedRegions)
            {
                int index = region.biomeIndex;
                if (seen[index] || shares[index] <= .00001f) continue;
                seen[index] = true;
                sites.Add(region);
                targets.Add(shares[index] / 100f);
            }
            if (sites.Count == 0) throw new InvalidOperationException("At least one biome needs a positive percentage.");
            weightedSites = sites.ToArray();
            weightedBiases = new float[sites.Count];
            weightedScratch = new float[sites.Count];
            if (sites.Count == 1) return;

            float width = biomeLayoutTilesX * biomeLayoutTileMeters;
            float height = biomeLayoutTilesZ * biomeLayoutTileMeters;
            float spacing = Mathf.Min(width / balancedColumns, height / balancedRows);
            float scaleSquared = spacing * spacing;
            weightedTemperature = Mathf.Clamp(biomeBlendMeters / (2.2f * spacing), .08f, .8f);
            const int grid = 64;
            int samples = grid * grid;
            var baseLogits = new float[samples, sites.Count];
            for (int z = 0; z < grid; z++) for (int x = 0; x < grid; x++)
            {
                double px = (biomeLayoutMinX + (x + .5) * biomeLayoutTilesX / grid) * biomeLayoutTileMeters;
                double pz = (biomeLayoutMinZ + (z + .5) * biomeLayoutTilesZ / grid) * biomeLayoutTileMeters;
                for (int i = 0; i < sites.Count; i++)
                {
                    double dx = px - sites[i].x, dz = pz - sites[i].z;
                    baseLogits[z * grid + x, i] = (float)(-(dx * dx + dz * dz) / scaleSquared / weightedTemperature);
                }
            }
            var measured = new float[sites.Count];
            for (int iteration = 0; iteration < 120; iteration++)
            {
                Array.Clear(measured, 0, measured.Length);
                for (int sample = 0; sample < samples; sample++)
                {
                    float max = float.NegativeInfinity;
                    for (int i = 0; i < sites.Count; i++)
                        max = Mathf.Max(max, baseLogits[sample, i] + weightedBiases[i]);
                    float sum = 0;
                    for (int i = 0; i < sites.Count; i++)
                    {
                        weightedScratch[i] = Mathf.Exp(baseLogits[sample, i] + weightedBiases[i] - max);
                        sum += weightedScratch[i];
                    }
                    for (int i = 0; i < sites.Count; i++) measured[i] += weightedScratch[i] / sum;
                }
                float worst = 0, averageBias = 0;
                for (int i = 0; i < sites.Count; i++)
                {
                    measured[i] /= samples;
                    worst = Mathf.Max(worst, Mathf.Abs(measured[i] - targets[i]));
                    weightedBiases[i] += .8f * Mathf.Log(targets[i] / Mathf.Max(.000001f, measured[i]));
                    averageBias += weightedBiases[i];
                }
                averageBias /= sites.Count;
                for (int i = 0; i < sites.Count; i++) weightedBiases[i] -= averageBias;
                if (worst < .002f) break;
            }
        }

        void SamplePercentageWeights(PlanetSamplePoint p, float[] weights)
        {
            EnsureWeightedSites();
            if (weightedSites.Length == 1)
            {
                weights[weightedSites[0].biomeIndex] = 1f;
                return;
            }
            float spacing = Mathf.Min(biomeLayoutTilesX * biomeLayoutTileMeters / balancedColumns,
                biomeLayoutTilesZ * biomeLayoutTileMeters / balancedRows);
            double scaleSquared = spacing * (double)spacing;
            float max = float.NegativeInfinity;
            for (int i = 0; i < weightedSites.Length; i++)
            {
                double dx = p.x - weightedSites[i].x, dz = p.z - weightedSites[i].z;
                float logit = (float)(-(dx * dx + dz * dz) / scaleSquared / weightedTemperature) + weightedBiases[i];
                weightedScratch[i] = logit;
                max = Mathf.Max(max, logit);
            }
            float total = 0;
            for (int i = 0; i < weightedSites.Length; i++)
            {
                weightedScratch[i] = Mathf.Exp(weightedScratch[i] - max);
                total += weightedScratch[i];
            }
            for (int i = 0; i < weightedSites.Length; i++)
                weights[weightedSites[i].biomeIndex] = weightedScratch[i] / total;
        }

        float EvaluateBiome(PlanetBiomeProfile biome, PlanetSamplePoint p, float natural)
        {
            if (!biome) return natural;
            float x = (float)p.x, z = (float)p.z;
            if (biome.terrainShape == PlanetBiomeTerrainShape.SourceTerrain)
            {
                float source = SmoothedSourceHeight(biome, p);
                return Mathf.Clamp01(.42f + (source - .42f) * biome.sourceReliefMeters / maxHeightMeters);
            }
            if (biome.terrainShape == PlanetBiomeTerrainShape.MoonCraters)
            {
                float background = biome.sourceTerrain != null ? biome.sourceTerrain.SampleHeight01(p) : .42f;
                float craterMeters = MoonImpactRelief(x, z);
                return Mathf.Clamp01(.42f + ((background - .42f) * biome.sourceReliefMeters + craterMeters) / maxHeightMeters);
            }
            if (biome.terrainShape == PlanetBiomeTerrainShape.RockyHighlands)
            {
                float source = SmoothedSourceHeight(biome, p);
                float elevation = (source - .42f) * biome.sourceReliefMeters;
                float level = (elevation + 350f) / 110f;
                float whole = Mathf.Floor(level);
                float shelf = 110f * (whole + Smooth((level - whole - .18f) / .64f)) - 350f;
                float roughness = (Mathf.PerlinNoise(x / 180f + 17f, z / 180f - 29f) - .5f) * biome.detailMeters;
                return Mathf.Clamp01(.42f + (shelf + roughness) / maxHeightMeters);
            }
            float broad = Mathf.PerlinNoise((x + planetSeed * 13f) / biome.broadScale, (z - planetSeed * 7f) / biome.broadScale) * 2 - 1;
            float detail = Mathf.PerlinNoise((x - planetSeed * 19f) / biome.detailScale, (z + planetSeed * 17f) / biome.detailScale) * 2 - 1;
            float ridge = 1 - Mathf.Abs(detail);
            float shape = broad * biome.reliefMeters + detail * biome.detailMeters;
            if (biome.terrainShape == PlanetBiomeTerrainShape.RollingHills)
            {
                float bigHills = RoundHillField(x, z, Mathf.Max(480f, biome.broadScale * .42f), 0x48494c4cu);
                float smallHills = RoundHillField(x, z, Mathf.Max(210f, biome.broadScale * .17f), 0x534d414cu);
                shape = (bigHills * .65f + smallHills * .3f + broad * .12f) * biome.reliefMeters
                    + detail * biome.detailMeters * .5f;
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.DuneFields)
            {
                float dunes = SandDuneField(x, z, biome.detailScale);
                shape = broad * biome.reliefMeters * .12f + dunes * biome.reliefMeters
                    + detail * biome.detailMeters * .12f;
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.CombDesert)
            {
                float comb = DuneWaveField(x, z, biome.detailScale);
                shape = broad * biome.reliefMeters * .2f + comb * biome.reliefMeters
                    + detail * biome.detailMeters * .3f;
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.OpenPlains)
            {
                float longSwell = Mathf.PerlinNoise((x + 1271f) / (biome.broadScale * .55f),
                    (z - 941f) / (biome.broadScale * .55f)) * 2f - 1f;
                float channels = Mathf.Abs(Mathf.PerlinNoise(x / 800f + 31f, z / 800f - 47f) - .5f);
                shape = (broad * .7f + longSwell * .55f) * biome.reliefMeters
                    + detail * biome.detailMeters - (1f - Smooth(channels / .13f)) * biome.reliefMeters * .18f;
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.Karst)
            {
                float towers = KarstTowerField(x, z, 540f, 0x4b415253u, .88f);
                float smallerTowers = KarstTowerField(x + 127f, z - 93f, 310f, 0x54544f57u, .43f);
                float valleyFloor = Mathf.PerlinNoise(x / 540f + 59f, z / 540f - 37f) * 2f - 1f;
                shape = (Mathf.Max(towers, smallerTowers * .62f) + valleyFloor * .055f
                    + broad * .045f) * biome.reliefMeters + MountainGroundRelief(biome, p, 1f);
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.SaltFlats)
            {
                float plateauNoise = Mathf.PerlinNoise((x + 1831f) / biome.broadScale,
                    (z - 491f) / biome.broadScale);
                float plateaus = Smooth((plateauNoise - .34f) / .16f) * .65f
                    + Smooth((plateauNoise - .55f) / .15f) * .65f
                    + Smooth((plateauNoise - .73f) / .13f) * .35f;
                float smallerMesa = Mathf.PerlinNoise((x - 531f) / (biome.broadScale * .35f),
                    (z + 177f) / (biome.broadScale * .35f));
                plateaus += Smooth((smallerMesa - .59f) / .19f) * .28f;
                shape = plateaus * biome.reliefMeters + broad * biome.reliefMeters * .08f
                    + detail * biome.detailMeters * 1.7f;
            }
            else if (biome.terrainShape == PlanetBiomeTerrainShape.DesertBadlands)
            {
                float mesas = BadlandsMesaField(x, z, 1050f, 0x4d455341u);
                float buttes = BadlandsMesaField(x, z, 510f, 0x42555454u);
                float erosion = Mathf.PerlinNoise(x / 420f + 23f, z / 420f - 39f);
                float sandstoneRidges = Smooth((erosion - .48f) / .25f) * .32f;
                float valleyNoise = Mathf.PerlinNoise(x / 730f - 31f, z / 730f + 73f);
                float lowValleys = Smooth((.54f - valleyNoise) / .22f) * .31f;
                shape = (mesas + buttes * .43f + sandstoneRidges + broad * .12f - lowValleys)
                    * biome.reliefMeters + MountainGroundRelief(biome, p, .78f);
            }
            shape += ridge * biome.ridgeStrength * biome.reliefMeters;
            if (biome.duneStrength > 0 && biome.terrainShape != PlanetBiomeTerrainShape.DuneFields &&
                biome.terrainShape != PlanetBiomeTerrainShape.CombDesert)
                shape += DuneRelief(x, z, biome.detailScale) * biome.duneStrength * biome.reliefMeters * .55f;
            if (biome.canyonStrength > 0)
            {
                float cut = CanyonCut(x, z, biome.broadScale) * .5f;
                cut += (CanyonCut(x + 8f, z, biome.broadScale) + CanyonCut(x - 8f, z, biome.broadScale)
                    + CanyonCut(x, z + 8f, biome.broadScale) + CanyonCut(x, z - 8f, biome.broadScale)) * .125f;
                shape -= cut * biome.canyonStrength * biome.reliefMeters * 1.35f;
            }
            // Gentle strata variation without quantizing the heightmap into steps.
            shape += Mathf.Sin(shape * (Mathf.PI * 2f / 28f)) * biome.terraceStrength * 2f;
            shape *= terrainReliefMultiplier;
            return Mathf.Clamp01(.42f + shape / maxHeightMeters);
        }

        public float SampleBiomePreviewHeight01(PlanetBiomeProfile biome, PlanetSamplePoint point) =>
            EvaluateBiome(biome, point, 0f);

        static float SmoothedSourceHeight(PlanetBiomeProfile biome, PlanetSamplePoint point)
        {
            if (biome.sourceTerrain == null) return .42f;
            const double radius = 8;
            float center = biome.sourceTerrain.SampleHeight01(point) * .5f;
            float neighbors = biome.sourceTerrain.SampleHeight01(new PlanetSamplePoint(point.x + radius, point.z))
                + biome.sourceTerrain.SampleHeight01(new PlanetSamplePoint(point.x - radius, point.z))
                + biome.sourceTerrain.SampleHeight01(new PlanetSamplePoint(point.x, point.z + radius))
                + biome.sourceTerrain.SampleHeight01(new PlanetSamplePoint(point.x, point.z - radius));
            return center + neighbors * .125f;
        }

        static float MountainGroundRelief(PlanetBiomeProfile biome, PlanetSamplePoint point, float strength)
        {
            float x = (float)point.x, z = (float)point.z;
            float mountain = SmoothedSourceHeight(biome, point) - .42f;
            float outcrops = Mathf.PerlinNoise(x / 115f - 53f, z / 115f + 29f) * 2f - 1f;
            float fine = Mathf.PerlinNoise(x / 48f + 19f, z / 48f - 71f) * 2f - 1f;
            return (mountain * biome.reliefMeters * .58f
                + outcrops * biome.detailMeters * 1.7f
                + fine * biome.detailMeters * .38f) * strength;
        }

        float DuneWaveField(float x, float z, float spacing)
        {
            float warp = (Mathf.PerlinNoise(x / 1050f + 41f, z / 1050f - 23f) - .5f) * 490f;
            warp += (Mathf.PerlinNoise(x / 430f - 71f, z / 430f + 19f) - .5f) * 145f;
            float wavelength = Mathf.Max(155f, spacing * 1.7f);
            float phase = (x * .78f + z * .63f + warp) * (Mathf.PI * 2f / wavelength);
            float cap = Mathf.Pow(.5f + .5f * Mathf.Sin(phase), 1.55f);
            float shoulder = Mathf.Pow(.5f + .5f * Mathf.Sin(phase * .5f + 1.3f), 1.5f);
            return (cap - .42f) * 1.25f + (shoulder - .42f) * .28f;
        }

        float SandDuneField(float x, float z, float spacing)
        {
            float cell = Mathf.Max(185f, spacing * 1.55f);
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float relief = 0f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                uint salt = 0x53414e44u;
                float px = (gx + .5f + (Unit(Hash(planetSeed, gx, gz, salt)) - .5f) * .64f) * cell;
                float pz = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 1)) - .5f) * .64f) * cell;
                float angle = .65f + (Unit(Hash(planetSeed, gx, gz, salt + 2)) - .5f) * 1.25f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float ox = x - px, oz = z - pz;
                float across = ox * ca + oz * sa;
                float along = -ox * sa + oz * ca;
                float span = cell * (.67f + Unit(Hash(planetSeed, gx, gz, salt + 3)) * .35f);
                float width = cell * (.25f + Unit(Hash(planetSeed, gx, gz, salt + 4)) * .12f);
                float bend = (Unit(Hash(planetSeed, gx, gz, salt + 5)) - .5f) * .85f;
                float curved = (across + bend * along * along / span) / width;
                float length = along / span;
                float cap = Mathf.Exp(-curved * curved * .85f - length * length * length * length * 1.25f);
                relief += cap * (.7f + Unit(Hash(planetSeed, gx, gz, salt + 6)) * .3f);
            }
            return (Mathf.Min(relief, 1.75f) - .52f) * .45f;
        }

        float BadlandsMesaField(float x, float z, float cell, uint salt)
        {
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float result = 0f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                float px = (gx + .5f + (Unit(Hash(planetSeed, gx, gz, salt)) - .5f) * .55f) * cell;
                float pz = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 1)) - .5f) * .55f) * cell;
                float angle = Unit(Hash(planetSeed, gx, gz, salt + 2)) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float ox = x - px, oz = z - pz;
                float rx = cell * (.24f + Unit(Hash(planetSeed, gx, gz, salt + 3)) * .16f);
                float rz = cell * (.19f + Unit(Hash(planetSeed, gx, gz, salt + 4)) * .14f);
                float u = (ox * ca - oz * sa) / rx, v = (ox * sa + oz * ca) / rz;
                float r = Mathf.Sqrt(u * u + v * v);
                float erodedEdge = (Mathf.PerlinNoise(x / 115f + 71f, z / 115f - 17f) - .5f) * .3f;
                erodedEdge += (Mathf.PerlinNoise(x / 58f - 43f, z / 58f + 61f) - .5f) * .075f;
                float wall = 1f - Smooth((r + erodedEdge - .62f) / .43f);
                float cap = .76f + Unit(Hash(planetSeed, gx, gz, salt + 5)) * .48f;
                float outcrops = (Mathf.PerlinNoise(x / 95f - 91f, z / 95f + 37f) - .5f) * .22f;
                float brokenTop = (Mathf.PerlinNoise(x / 180f + 19f, z / 180f - 23f) - .5f) * .14f;
                result = Mathf.Max(result, wall * (cap + outcrops + brokenTop));
            }
            return result;
        }

        float KarstTowerField(float x, float z, float cell, uint salt, float chance)
        {
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float result = 0f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                if (Unit(Hash(planetSeed, gx, gz, salt)) > chance) continue;
                float px = (gx + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 1)) - .5f) * .5f) * cell;
                float pz = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 2)) - .5f) * .5f) * cell;
                float angle = Unit(Hash(planetSeed, gx, gz, salt + 3)) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                float ox = x - px, oz = z - pz;
                float rx = cell * (.17f + Unit(Hash(planetSeed, gx, gz, salt + 4)) * .1f);
                float rz = cell * (.19f + Unit(Hash(planetSeed, gx, gz, salt + 5)) * .15f);
                float u = (ox * ca - oz * sa) / rx, v = (ox * sa + oz * ca) / rz;
                float rockWarp = (Mathf.PerlinNoise(x / 115f + 17f, z / 115f - 31f) - .5f) * .27f;
                float r = Mathf.Sqrt(u * u + v * v) + rockWarp;
                float wall = 1f - Smooth((r - .35f) / .85f);
                float ragged = (Mathf.PerlinNoise(x / 87f - 53f, z / 87f + 29f) - .5f) * .17f;
                float peak = .7f + Unit(Hash(planetSeed, gx, gz, salt + 6)) * .75f;
                result = Mathf.Max(result, wall * (peak + ragged));
            }
            return result;
        }

        float RoundHillField(float x, float z, float cell, uint salt)
        {
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float result = 0f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                float px = (gx + .5f + (Unit(Hash(planetSeed, gx, gz, salt)) - .5f) * .7f) * cell;
                float pz = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 1)) - .5f) * .7f) * cell;
                float radius = cell * (.42f + Unit(Hash(planetSeed, gx, gz, salt + 2)) * .18f);
                float u = (x - px) / (radius * 1.4f), v = (z - pz) / (radius * 1.4f);
                float dome = Mathf.Max(0f, 1f - u * u - v * v);
                result += dome * dome *
                    (.55f + Unit(Hash(planetSeed, gx, gz, salt + 3)) * .65f);
            }
            return Mathf.Min(result, 1.8f) - .45f;
        }

        float MoonImpactRelief(float x, float z)
        {
            float total = 0f;
            for (int band = 0; band < 3; band++)
            {
                float cell = band == 0 ? 1050f : band == 1 ? 470f : 205f;
                float chance = band == 0 ? .78f : band == 1 ? .62f : .42f;
                int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gz = cz + dz;
                    uint salt = (uint)(0x4d4f4f4e + band * 41);
                    if (Unit(Hash(planetSeed, gx, gz, salt)) > chance) continue;
                    float px = (gx + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 1)) - .5f) * .65f) * cell;
                    float pz = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, salt + 2)) - .5f) * .65f) * cell;
                    float radius = cell * (.23f + Unit(Hash(planetSeed, gx, gz, salt + 3)) * .15f);
                    float angle = Unit(Hash(planetSeed, gx, gz, salt + 4)) * Mathf.PI * 2f;
                    float aspect = 1f + Unit(Hash(planetSeed, gx, gz, salt + 5)) * .5f;
                    float ox = x - px, oz = z - pz;
                    float along = (ox * Mathf.Cos(angle) + oz * Mathf.Sin(angle)) / (radius * aspect);
                    float across = (-ox * Mathf.Sin(angle) + oz * Mathf.Cos(angle)) / radius;
                    float distance = Mathf.Sqrt(along * along + across * across);
                    if (distance > 1.45f) continue;
                    float depth = radius * .12f;
                    float rim = radius * .023f;
                    float bowl = -depth * (1f - Smooth(distance / .9f));
                    float rimDistance = (distance - 1f) / .22f;
                    float raisedRim = rim * Mathf.Exp(-rimDistance * rimDistance * 1.8f);
                    total += bowl + raisedRim * (1f + along * .12f);
                }
            }
            return Mathf.Max(-85f, total);
        }

        float DuneRelief(float x, float z, float spacing)
        {
            // Jittered, locally rotated crescent dunes. Each crest has a finite span,
            // so it ends naturally instead of continuing as a planet-wide sine ridge.
            float cell = Mathf.Max(80f, spacing * 2.2f);
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float relief = 0f;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                uint h = Hash(planetSeed, gx, gz, 0x44554e45);
                float centerX = (gx + .5f + (Unit(h) - .5f) * .55f) * cell;
                float centerZ = (gz + .5f + (Unit(Hash(planetSeed, gx, gz, 71)) - .5f) * .55f) * cell;
                float angle = .45f + Unit(Hash(planetSeed, gx, gz, 72)) * 1.25f;
                float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
                float offsetX = x - centerX, offsetZ = z - centerZ;
                float u = offsetX * cosine + offsetZ * sine;
                float v = -offsetX * sine + offsetZ * cosine;
                float span = cell * (.38f + Unit(Hash(planetSeed, gx, gz, 73)) * .17f);
                float width = cell * (.16f + Unit(Hash(planetSeed, gx, gz, 74)) * .08f);
                float curved = (u + .42f * v * v / span) / width;
                float sideways = v / span;
                float crest = Mathf.Exp(-curved * curved * 1.4f - sideways * sideways * sideways * sideways * 1.7f);
                relief += crest * (.55f + Unit(Hash(planetSeed, gx, gz, 75)) * .45f);
            }
            return Mathf.Min(relief, 1.2f);
        }

        float CanyonCut(float x, float z, float scale)
        {
            scale = Mathf.Max(450f, scale);
            float sx = planetSeed * 3.71f, sz = planetSeed * 5.93f;
            float warpX = (Mathf.PerlinNoise(x / (scale * .65f) + sx, z / (scale * .65f) + 19f) - .5f) * scale * .32f;
            float warpZ = (Mathf.PerlinNoise(x / (scale * .65f) - 37f, z / (scale * .65f) + sz) - .5f) * scale * .32f;
            float u = x + warpX, v = z + warpZ;
            // Level contours of two warped fields form connected, curving networks.
            // Their union creates tributaries that split and rejoin without tile seams.
            float trunk = Mathf.Abs(Mathf.PerlinNoise(u / scale + sx, v / scale + sz) - .5f);
            float branch = Mathf.Abs(Mathf.PerlinNoise(u / (scale * .48f) - sx, v / (scale * .48f) + sz) - .5f);
            float trunkCut = 1f - Smooth(trunk / .085f);
            float branchCut = (1f - Smooth(branch / .048f)) * .66f;
            return Mathf.Max(trunkCut, branchCut);
        }

        float ModifyHeight(PlanetSamplePoint p, float natural)
        {
            float biome = BiomeHeight(p, natural);
            if (!populatePlanet) return biome;
            if (tileBakeSites != null)
            {
                float baked = biome;
                for (int i = 0; i < tileBakeSites.Count; i++)
                    baked = ApplySite(p, baked, tileBakeSites[i]);
                return Mathf.Clamp01(baked);
            }
            float reach = siteRadius + siteFalloff;
            float size = treadmill.TileSizeMeters * Mathf.Sqrt(outpostSpacing);
            int cx = Mathf.FloorToInt((float)(p.x / size)), cz = Mathf.FloorToInt((float)(p.z / size));
            int ring = Mathf.CeilToInt(reach / size) + 1;
            float result = biome;
            for (int z = cz - ring; z <= cz + ring; z++) for (int x = cx - ring; x <= cx + ring; x++)
            {
                if (!TryPoi(x, z, out var poi)) continue;
                float angle = Unit(Hash(planetSeed, x, z, 81)) * 6.28318f;
                result = ApplySite(p, result, new SiteInfluence(poi, ReferenceHeight(poi), angle));
            }
            return Mathf.Clamp01(result);
        }

        float ApplySite(PlanetSamplePoint p, float height, SiteInfluence site)
        {
            float dx = (float)(p.x - site.poi.x), dz = (float)(p.z - site.poi.z);
            float u = (dx * site.cosine + dz * site.sine) / siteRadius;
            float v = (-dx * site.sine + dz * site.cosine) / (siteRadius * .82f);
            float warped = Mathf.Sqrt(u * u + v * v) + .055f * Mathf.Sin(u * 7 + v * 5);
            float distance = warped * siteRadius;
            if (distance > siteRadius + siteFalloff) return height;
            float micro = (Mathf.PerlinNoise((float)p.x / 35f + 11, (float)p.z / 35f + 17) - .5f) * 2f * siteVariationMeters / maxHeightMeters;
            float weight = 1 - Smooth((distance - siteRadius) / siteFalloff);
            return Mathf.Lerp(height, site.reference + micro, weight);
        }

        float ReferenceHeight(PlanetPoiDescriptor poi)
        {
            if (referenceHeights.TryGetValue(poi.seed, out float cached)) return cached;
            float sum = 0;
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 1.256637f;
                var p = new PlanetSamplePoint(poi.x + Math.Cos(angle) * 25, poi.z + Math.Sin(angle) * 25);
                sum += BiomeHeight(p, 0f);
            }
            float value = sum / 5f;
            referenceHeights[poi.seed] = value;
            return value;
        }

        bool RawCandidate(int x, int z, out PlanetPoiDescriptor poi)
        {
            float size = treadmill.TileSizeMeters * Mathf.Sqrt(outpostSpacing);
            uint h = Hash(planetSeed, x, z, 0x504f4921);
            double px = (x + .05 + Unit(h) * .9) * size;
            double pz = (z + .05 + Unit(Hash(planetSeed, x, z, 22)) * .9) * size;
            float total = smallWeight + mediumWeight + largeWeight;
            float roll = Unit(Hash(planetSeed, x, z, 23)) * Mathf.Max(.001f, total);
            var tier = roll < smallWeight ? OutpostTier.Small : roll < smallWeight + mediumWeight ? OutpostTier.Medium : OutpostTier.Large;
            poi = new PlanetPoiDescriptor(px, pz, h, tier);
            return true;
        }

        bool TryPoi(int x, int z, out PlanetPoiDescriptor poi)
        {
            RawCandidate(x, z, out poi);
            float size = treadmill.TileSizeMeters * Mathf.Sqrt(outpostSpacing);
            int ring = Mathf.CeilToInt(minimumSeparationMeters / size) + 1;
            for (int dz = -ring; dz <= ring; dz++) for (int dx = -ring; dx <= ring; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                RawCandidate(x + dx, z + dz, out var other);
                double vx = poi.x - other.x, vz = poi.z - other.z;
                if (vx * vx + vz * vz >= minimumSeparationMeters * minimumSeparationMeters) continue;
                if (other.seed < poi.seed || other.seed == poi.seed && (dx < 0 || dx == 0 && dz < 0)) return false;
            }
            return true;
        }

        void Update()
        {
            if (Time.time < nextUpdate || !treadmill.Target) return;
            nextUpdate = Time.time + 1;
            wanted.Clear();
            if (populatePlanet)
            {
                Vector3 player = treadmill.Target.position;
                var center = treadmill.CenterTile;
                float size = treadmill.TileSizeMeters * Mathf.Sqrt(outpostSpacing);
                int cx = Mathf.FloorToInt((center.x + .5f) * treadmill.TileSizeMeters / size);
                int cz = Mathf.FloorToInt((center.z + .5f) * treadmill.TileSizeMeters / size);
                int ring = Mathf.CeilToInt((activationDistance + treadmill.TileSizeMeters) / size) + 1;
                for (int z = cz - ring; z <= cz + ring; z++) for (int x = cx - ring; x <= cx + ring; x++)
                {
                    if (!TryPoi(x, z, out var poi)) continue;
                    if (treadmill.AuthoredTileSet != null)
                    {
                        int tx = Mathf.FloorToInt((float)(poi.x / treadmill.TileSizeMeters));
                        int tz = Mathf.FloorToInt((float)(poi.z / treadmill.TileSizeMeters));
                        if (!treadmill.AuthoredTileSet.TryGetTerrainData(new PlanetTileCoord(tx, tz), out _)) continue;
                    }
                    Vector3 world = WorldPoint(poi.x, poi.z);
                    float distance = Vector2.Distance(new Vector2(player.x, player.z), new Vector2(world.x, world.z));
                    if (distance > activationDistance) continue;
                    wanted.Add(poi.seed);
                    if (!active.ContainsKey(poi.seed)) active.Add(poi.seed, BuildRuntime(poi, world));
                }
            }
            removal.Clear();
            foreach (var pair in active)
            {
                if (wanted.Contains(pair.Key) || !pair.Value) continue;
                Vector3 player = treadmill.Target.position;
                float distance = Vector2.Distance(new Vector2(player.x, player.z), new Vector2(pair.Value.transform.position.x, pair.Value.transform.position.z));
                if (!populatePlanet || distance > deactivationDistance) removal.Add(pair.Key);
            }
            foreach (uint key in removal)
            {
                if (navMeshes.TryGetValue(key, out var instance)) { instance.Remove(); navMeshes.Remove(key); }
                if (active[key]) Destroy(active[key]);
                active.Remove(key);
            }
        }

        Vector3 WorldPoint(double x, double z)
        {
            float size = treadmill.TileSizeMeters;
            int tx = Mathf.FloorToInt((float)(x / size)), tz = Mathf.FloorToInt((float)(z / size));
            Vector3 origin = treadmill.GetTileWorldPosition(new PlanetTileCoord(tx, tz));
            return origin + new Vector3((float)(x - tx * (double)size), 0, (float)(z - tz * (double)size));
        }

        float Ground(Vector3 position)
        {
            foreach (Transform child in treadmill.transform)
            {
                Terrain terrain = child.GetComponent<Terrain>();
                if (!terrain || !terrain.enabled || !terrain.terrainData) continue;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (position.x >= origin.x && position.x <= origin.x + size.x &&
                    position.z >= origin.z && position.z <= origin.z + size.z)
                    return terrain.SampleHeight(position) + origin.y;
            }
            return position.y;
        }

        GameObject BuildRuntime(PlanetPoiDescriptor poi, Vector3 world)
        {
            world.y = Ground(world);
            var root = new GameObject($"Scavenger Outpost {poi.seed} ({poi.tier})");
            root.transform.position = world;
            int buildings = 2 + (int)(Hash(planetSeed, (int)poi.seed, 0, 31) % 2);
            float phase = Unit(Hash(planetSeed, (int)poi.seed, 0, 32)) * 6.28318f;
            for (int i = 0; i < buildings; i++)
            {
                float angle = phase + i * 6.28318f / buildings + (Unit(Hash(planetSeed, (int)poi.seed, i, 33)) - .5f) * .35f;
                GameObject prefab = ChooseBuilding(poi.seed, i);
                OutpostModule module = prefab ? prefab.GetComponent<OutpostModule>() : null;
                float footprint = module ? Mathf.Max(module.footprint.x, module.footprint.y) : 10f;
                float radius = 23 + footprint * .5f + (module ? module.clearance : 3f) + Unit(Hash(planetSeed, (int)poi.seed, i, 34)) * 8;
                Vector3 pos = world + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                pos.y = Ground(pos);
                GameObject building;
                if (prefab) building = Instantiate(prefab, pos, Quaternion.identity, root.transform);
                else
                {
                    building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    building.name = "Placeholder Scavenger Shack";
                    building.transform.SetParent(root.transform);
                    building.transform.position = pos + Vector3.up * 3;
                    building.transform.localScale = new Vector3(10, 6, 8);
                }
                Vector3 front = module ? module.localEntranceDirection : Vector3.forward;
                if (front.sqrMagnitude < .01f) front = Vector3.forward;
                Quaternion facing = Quaternion.LookRotation((world - pos).normalized, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(front, Vector3.up));
                if (module && !module.rotateFreely)
                    facing = Quaternion.Euler(0, Mathf.Round(facing.eulerAngles.y / 90f) * 90f, 0);
                building.transform.rotation = facing;
            }
            bool navMeshReady = BuildOutpostNavMesh(root, poi.seed, world);
            var patrol = new Transform[4];
            for (int i = 0; i < patrol.Length; i++)
            {
                float angle = phase + i * Mathf.PI * .5f;
                Vector3 pos = world + new Vector3(Mathf.Cos(angle) * 13, 0, Mathf.Sin(angle) * 13);
                pos.y = Ground(pos);
                if (SampleGroundNavMesh(pos, out var hit)) pos = hit.position;
                var marker = new GameObject($"Patrol {i}").transform;
                marker.SetParent(root.transform); marker.position = pos; patrol[i] = marker;
            }
            int count = 2 + (int)(Hash(planetSeed, (int)poi.seed, 0, 35) % 7);
            if (navMeshReady && faction && faction.npcPrefab)
                for (int i = 0; i < count; i++)
                {
                    float spawnAngle = phase + i * 6.28318f / count;
                    float spawnRadius = 9f + Unit(Hash(planetSeed, (int)poi.seed, i, 48)) * 8f;
                    Vector3 pos = world + new Vector3(Mathf.Cos(spawnAngle) * spawnRadius, 0, Mathf.Sin(spawnAngle) * spawnRadius);
                    pos.y = Ground(pos);
                    if (!SampleGroundNavMesh(pos, out var hit)) continue;
                    var npc = Instantiate(faction.npcPrefab, hit.position, Quaternion.identity, root.transform);
                    npc.patrolPoints = patrol;
                    int role = i < 3 ? i : (int)(Hash(planetSeed, (int)poi.seed, i, 49) % 4);
                    if (role == 1) npc.idleAction = ScavengerEnemy.Action.Squatting;
                    else if (role == 2)
                    {
                        npc.idleAction = ScavengerEnemy.Action.Sitting;
                        var seat = new GameObject($"Seat {i}").transform;
                        seat.SetParent(root.transform);
                        seat.position = hit.position;
                        seat.rotation = Quaternion.LookRotation((world - hit.position).normalized, Vector3.up);
                        npc.seat = seat;
                        var stool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        stool.name = "Placeholder Seat";
                        stool.transform.SetParent(root.transform);
                        stool.transform.position = hit.position + Vector3.up * .22f - seat.forward * .22f;
                        stool.transform.localScale = new Vector3(.65f, .22f, .65f);
                        var stoolCollider = stool.GetComponent<Collider>();
                        if (stoolCollider) stoolCollider.enabled = false;
                    }
                    else if (role == 3) npc.idleAction = ScavengerEnemy.Action.Standing;
                    else npc.idleAction = ScavengerEnemy.Action.Patrol;
                }
            if (logGeneration) Debug.Log($"Planet POI generated: ({poi.x:0}, {poi.z:0}), seed {poi.seed}, tier {poi.tier}, buildings {buildings}, NPCs {count}, NavMesh {navMeshReady}", this);
            return root;
        }

        bool SampleGroundNavMesh(Vector3 position, out NavMeshHit hit)
        {
            if (NavMesh.SamplePosition(position + Vector3.up * .5f, out hit, 2f, NavMesh.AllAreas))
                return Mathf.Abs(hit.position.y - Ground(position)) <= 1.5f;
            return false;
        }

        bool BuildOutpostNavMesh(GameObject root, uint seed, Vector3 center)
        {
            if (NavMesh.GetSettingsCount() == 0) return false;
            var sources = new List<NavMeshBuildSource>();
            float half = 90f;
            foreach (Transform child in treadmill.transform)
            {
                var terrain = child.GetComponent<Terrain>();
                if (!terrain || !terrain.enabled || !terrain.terrainData) continue;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (origin.x > center.x + half || origin.x + size.x < center.x - half ||
                    origin.z > center.z + half || origin.z + size.z < center.z - half) continue;
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Terrain,
                    sourceObject = terrain.terrainData,
                    transform = terrain.transform.localToWorldMatrix,
                    area = 0
                });
            }
            if (sources.Count == 0) return false;
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (collider is BoxCollider box)
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center),
                        size = box.size,
                        area = 0
                    });
                else if (collider is MeshCollider mesh && mesh.sharedMesh)
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mesh.sharedMesh,
                        transform = mesh.transform.localToWorldMatrix,
                        area = 0
                    });
            }
            int agentType = faction && faction.npcPrefab && faction.npcPrefab.TryGetComponent<NavMeshAgent>(out var agent)
                ? agent.agentTypeID : NavMesh.GetSettingsByIndex(0).agentTypeID;
            var settings = NavMesh.GetSettingsByID(agentType);
            var bounds = new Bounds(center + Vector3.up * 2f, new Vector3(180f, 24f, 180f));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (!data) return false;
            navMeshes.Add(seed, NavMesh.AddNavMeshData(data));
            return true;
        }

        GameObject ChooseBuilding(uint seed, int slot)
        {
            if (!faction || faction.smallBuildings == null) return null;
            float total = 0;
            foreach (var prefab in faction.smallBuildings)
            {
                if (!prefab) continue;
                var module = prefab.GetComponent<OutpostModule>();
                total += module ? Mathf.Max(0, module.placementWeight) : 1;
            }
            if (total <= 0) return null;
            float pick = Unit(Hash(planetSeed, (int)seed, slot, 44)) * total;
            foreach (var prefab in faction.smallBuildings)
            {
                if (!prefab) continue;
                var module = prefab.GetComponent<OutpostModule>();
                pick -= module ? Mathf.Max(0, module.placementWeight) : 1;
                if (pick < 0) return prefab;
            }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            if (!showGizmos || !Application.isPlaying || treadmill == null) return;
            Gizmos.color = Color.yellow;
            foreach (var pair in active) if (pair.Value)
            {
                Gizmos.DrawWireSphere(pair.Value.transform.position, siteRadius);
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(pair.Value.transform.position, minimumSeparationMeters * .5f);
                Gizmos.color = Color.yellow;
            }
        }
    }
}

