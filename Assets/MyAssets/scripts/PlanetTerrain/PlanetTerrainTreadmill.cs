using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    /// <summary>
    /// Integer logical terrain tile coordinate. This is independent of Unity world coordinates.
    /// </summary>
    [Serializable]
    public struct PlanetTileCoord : IEquatable<PlanetTileCoord>
    {
        public int x;
        public int z;

        public PlanetTileCoord(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public bool Equals(PlanetTileCoord other) => x == other.x && z == other.z;
        public override bool Equals(object obj) => obj is PlanetTileCoord other && Equals(other);
        public override int GetHashCode() => (x * 397) ^ z;
        public override string ToString() => $"({x}, {z})";
        public static PlanetTileCoord operator -(PlanetTileCoord a, PlanetTileCoord b) => new PlanetTileCoord(a.x - b.x, a.z - b.z);
    }

    /// <summary>
    /// Stable planet-surface sample point consumed by height generation.
    /// V1 is a flat infinite X/Z plane; later mappers can replace this with cube-sphere data.
    /// </summary>
    public readonly struct PlanetSamplePoint
    {
        public readonly double x;
        public readonly double z;

        public PlanetSamplePoint(double x, double z)
        {
            this.x = x;
            this.z = z;
        }
    }

    /// <summary>
    /// Tiny coordinate mapping abstraction. Keeps treadmill/tile addressing separate from planet sampling.
    /// </summary>
    public interface IPlanetSurfaceCoordinateMapper
    {
        PlanetSamplePoint MapHeightSample(PlanetTileCoord tile, int sampleX, int sampleZ, int samplesPerTile, float metersPerSample);
    }

    /// <summary>
    /// V1 mapper: logical tile/sample coordinates map to an infinite flat local tangent plane.
    /// </summary>
    public sealed class FlatPlanetCoordinateMapper : IPlanetSurfaceCoordinateMapper
    {
        public PlanetSamplePoint MapHeightSample(PlanetTileCoord tile, int sampleX, int sampleZ, int samplesPerTile, float metersPerSample)
        {
            double gx = ((double)tile.x * samplesPerTile + sampleX) * metersPerSample;
            double gz = ((double)tile.z * samplesPerTile + sampleZ) * metersPerSample;
            return new PlanetSamplePoint(gx, gz);
        }
    }

    public enum PlanetTerrainProfile
    {
        Canyon,
        MoonCraters
    }

    /// <summary>
    /// Deterministic v1 height source. All tiles sample this through the mapper, so shared edges match inherently.
    /// </summary>
    [Serializable]
    public sealed class PlanetHeightProvider
    {
        [SerializeField] private PlanetTerrainProfile terrainProfile = PlanetTerrainProfile.Canyon;

        [Header("Canyon")]
        [SerializeField] private int seed = 42;
        [SerializeField] private float baseHeight01 = 0.28f;
        [SerializeField] private float mountainAmplitude01 = 0.62f;
        [SerializeField] private float macroScaleMeters = 1400f;
        [SerializeField] private float mountainScaleMeters = 4300f;
        [SerializeField] private float ridgeScaleMeters = 520f;
        [SerializeField] private float canyonScaleMeters = 7600f;
        [SerializeField] private float valleyScaleMeters = 6800f;
        [SerializeField] private float detailScaleMeters = 260f;
        [SerializeField] private bool keepPrimaryCanyonNearOrigin = true;
        [SerializeField] private float primaryCanyonOriginOffsetMeters = 0f;

        [Header("Moon Craters")]
        [SerializeField] private int moonSeed = 1337;
        [SerializeField] private float moonBaseHeight01 = 0.42f;
        [SerializeField] private float moonCraterDensity = 1f;
        [SerializeField] private Vector2 moonSmallCraterRadiusRangeMeters = new Vector2(40f, 90f);
        [SerializeField] private Vector2 moonMediumCraterRadiusRangeMeters = new Vector2(90f, 220f);
        [SerializeField] private Vector2 moonLargeCraterRadiusRangeMeters = new Vector2(220f, 500f);
        [SerializeField] private float moonCraterDepth01 = 0.16f;
        [SerializeField] private float moonRimHeight01 = 0.055f;
        [SerializeField] private float moonRimWidth = 0.18f;
        [SerializeField] private float moonBackgroundRoughness01 = 0.045f;
        [SerializeField] private float moonMacroElevationVariation01 = 0.16f;
        [SerializeField] private float moonRockyAreaStrength01 = 0.05f;

        public float SampleHeight01(PlanetSamplePoint p)
        {
            switch (terrainProfile)
            {
                case PlanetTerrainProfile.MoonCraters:
                    return SampleMoonCraterHeight01(p);
                case PlanetTerrainProfile.Canyon:
                default:
                    return SampleCanyonHeight01(p);
            }
        }

        float SampleCanyonHeight01(PlanetSamplePoint p)
        {
            float sx = (float)p.x + SeedRange(101, -85000f, 85000f);
            float sz = (float)p.z + SeedRange(102, -85000f, 85000f);

            float continent = FractalNoise(sx, sz, macroScaleMeters, 5, 0.55f);
            float uplift = SmoothRange(0.28f, 0.82f, continent);
            float basin = 1f - SmoothRange(0.22f, 0.72f, continent);

            float mountainNoise = FractalNoise(sx + 1200f, sz - 800f, mountainScaleMeters, 5, 0.57f);
            float mountains = Mathf.Pow(SmoothRange(0.35f, 1f, mountainNoise), 1.35f) * Mathf.Lerp(0.35f, 1.25f, uplift);

            float ridgeRaw = FractalNoise(sx - 211.9f, sz + 503.6f, ridgeScaleMeters, 5, 0.52f);
            float ridges = Mathf.Pow(1f - Mathf.Abs(ridgeRaw * 2f - 1f), 0.82f) * Mathf.Lerp(0.45f, 1.2f, uplift);

            float canyon = CanyonMask(sx, sz);
            float canyonWall = CanyonWallMask(sx, sz);
            float escarpment = EscarpmentMask(sx, sz);
            float valley = ValleyMask(sx, sz);
            float detail = FractalNoise(sx + 311.7f, sz - 97.4f, detailScaleMeters, 3, 0.45f);

            float h = baseHeight01;
            h += (continent - 0.5f) * 0.42f;
            h -= basin * 0.14f;
            h += mountains * mountainAmplitude01;
            h += ridges * 0.28f;
            h += canyonWall * 0.26f;
            h += escarpment * 0.24f;
            h += (detail - 0.5f) * Mathf.Lerp(0.05f, 0.11f, uplift);
            h -= valley * 0.48f;
            h -= canyon * 0.62f;
            h = ApplyCliffTerraces(h, uplift, ridges, canyonWall, escarpment);
            return Mathf.Clamp01(h);
        }

        float SampleMoonCraterHeight01(PlanetSamplePoint p)
        {
            float x = (float)p.x;
            float z = (float)p.z;

            float macro = FractalNoise(x + MoonSeedRange(10, -40000f, 40000f), z + MoonSeedRange(11, -40000f, 40000f), 5600f, 4, 0.56f);
            float basinNoise = FractalNoise(x + MoonSeedRange(12, -50000f, 50000f), z + MoonSeedRange(13, -50000f, 50000f), 9200f, 3, 0.5f);
            float basin = 1f - SmoothRange(0.30f, 0.78f, basinNoise);
            float regolith = FractalNoise(x + 173.1f, z - 421.7f, 620f, 4, 0.52f) - 0.5f;
            float fine = FractalNoise(x - 911.4f, z + 250.2f, 145f, 3, 0.45f) - 0.5f;

            float rockyMask = SmoothRange(0.58f, 0.86f, FractalNoise(x - 2300f, z + 8100f, 2400f, 4, 0.54f));
            float rockyRidgeRaw = FractalNoise(x + 713.7f, z - 1288.3f, 300f, 4, 0.50f);
            float rockyRidges = 1f - Mathf.Abs(rockyRidgeRaw * 2f - 1f);

            float h = moonBaseHeight01;
            h += (macro - 0.5f) * moonMacroElevationVariation01;
            h -= basin * moonMacroElevationVariation01 * 0.72f;
            h += regolith * moonBackgroundRoughness01;
            h += fine * moonBackgroundRoughness01 * 0.42f;
            h += rockyMask * rockyRidges * moonRockyAreaStrength01;

            h += SampleCraterBand(x, z, 0, moonLargeCraterRadiusRangeMeters, 1150f, 0.34f, 1.15f, 1.12f);
            h += SampleCraterBand(x, z, 1, moonMediumCraterRadiusRangeMeters, 510f, 0.56f, 0.78f, 0.96f);
            h += SampleCraterBand(x, z, 2, moonSmallCraterRadiusRangeMeters, 225f, 0.78f, 0.46f, 0.72f);

            return Mathf.Clamp01(h);
        }

        float SampleCraterBand(float x, float z, int band, Vector2 radiusRangeMeters, float cellSizeMeters, float baseChance, float depthScale, float rimScale)
        {
            float density = Mathf.Max(0f, moonCraterDensity);
            if (density <= 0f)
                return 0f;

            float minRadius = Mathf.Max(40f, Mathf.Min(radiusRangeMeters.x, radiusRangeMeters.y));
            float maxRadius = Mathf.Max(minRadius, Mathf.Max(radiusRangeMeters.x, radiusRangeMeters.y));
            float cellSize = Mathf.Max(maxRadius * 2.35f, cellSizeMeters);
            int cellX = Mathf.FloorToInt(x / cellSize);
            int cellZ = Mathf.FloorToInt(z / cellSize);
            float chance = Mathf.Clamp01(baseChance * density);
            float contribution = 0f;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;
                    if (Hash01(cx, cz, band, 0) > chance)
                        continue;

                    float centerX = (cx + Hash01(cx, cz, band, 1)) * cellSize;
                    float centerZ = (cz + Hash01(cx, cz, band, 2)) * cellSize;
                    float radius = Mathf.Lerp(minRadius, maxRadius, Hash01(cx, cz, band, 3));
                    float affectedRadius = radius * (1.65f + Mathf.Clamp01(moonRimWidth) * 0.35f);
                    float offsetX = x - centerX;
                    float offsetZ = z - centerZ;
                    float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                    if (distanceSquared > affectedRadius * affectedRadius)
                        continue;

                    float distance = Mathf.Sqrt(distanceSquared);
                    float depth = moonCraterDepth01 * depthScale * Mathf.Lerp(0.72f, 1.22f, Hash01(cx, cz, band, 4));
                    float rim = moonRimHeight01 * rimScale * Mathf.Lerp(0.72f, 1.25f, Hash01(cx, cz, band, 5));
                    contribution += EvaluateCraterProfile(distance / radius, depth, rim, moonRimWidth);
                }
            }

            return contribution;
        }

        static float EvaluateCraterProfile(float normalizedRadius, float depth, float rimHeight, float rimWidth)
        {
            float r = Mathf.Max(0f, normalizedRadius);
            float bowlEdge = 0.72f;
            float rimOuter = 1f + Mathf.Clamp(rimWidth, 0.06f, 0.45f);
            float ejectaOuter = rimOuter + 0.55f;

            if (r < bowlEdge)
            {
                float t = r / bowlEdge;
                float bowl = 1f - t * t * (3f - 2f * t);
                float floorFlatten = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.22f));
                return -depth * Mathf.Lerp(0.82f, 1f, floorFlatten) * bowl;
            }

            if (r < 1f)
            {
                float t = Mathf.InverseLerp(bowlEdge, 1f, r);
                float innerWall = t * t * (3f - 2f * t);
                return Mathf.Lerp(-depth * 0.18f, rimHeight, innerWall);
            }

            if (r < rimOuter)
            {
                float t = Mathf.InverseLerp(1f, rimOuter, r);
                return rimHeight * (1f - t * t * (3f - 2f * t));
            }

            if (r < ejectaOuter)
            {
                float t = Mathf.InverseLerp(rimOuter, ejectaOuter, r);
                float fade = 1f - t * t * (3f - 2f * t);
                return rimHeight * 0.20f * fade;
            }

            return 0f;
        }

        float MoonSeedRange(int salt, float min, float max)
        {
            return Mathf.Lerp(min, max, MoonSeed01(salt));
        }

        float MoonSeed01(int salt)
        {
            unchecked
            {
                uint h = (uint)moonSeed;
                h ^= (uint)salt * 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        float Hash01(int cellX, int cellZ, int band, int salt)
        {
            unchecked
            {
                uint h = (uint)moonSeed;
                h ^= (uint)cellX * 0x8DA6B343u;
                h ^= (uint)cellZ * 0xD8163841u;
                h ^= (uint)(band + 1) * 0xCB1AB31Fu;
                h ^= (uint)(salt + 1) * 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        float CanyonMask(float x, float z)
        {
            float s = Mathf.Max(1f, canyonScaleMeters);
            CanyonSpace(x, z, out float along, out float across);
            float meander = CanyonMeander(along, across, s);

            float dist = Mathf.Abs(across - meander);
            float broadCut = Mathf.Clamp01(1f - dist / SeedRange(120, 1050f, 2100f));
            float innerGorge = Mathf.Clamp01(1f - dist / SeedRange(121, 260f, 640f));
            float brokenSegments = SmoothRange(0.18f, 0.78f, FractalNoise(along + seed * 13f, across - seed * 17f, s * 0.74f, 3, 0.5f));
            return Mathf.Clamp01((broadCut * SeedRange(122, 0.42f, 0.76f) + innerGorge * SeedRange(123, 0.65f, 1.05f)) * brokenSegments);
        }

        float CanyonWallMask(float x, float z)
        {
            float s = Mathf.Max(1f, canyonScaleMeters);
            CanyonSpace(x, z, out float along, out float across);
            float meander = CanyonMeander(along, across, s);

            float dist = Mathf.Abs(across - meander);
            float leftWall = Bell(dist, SeedRange(130, 260f, 720f), SeedRange(131, 130f, 290f));
            float rightWall = Bell(dist, SeedRange(132, 820f, 1580f), SeedRange(133, 180f, 380f));
            float segment = SmoothRange(0.20f, 0.72f, FractalNoise(along - seed * 31f, across + seed * 7f, s * 0.58f, 3, 0.48f));
            return Mathf.Clamp01(Mathf.Max(leftWall * SeedRange(134, 0.75f, 1.15f), rightWall * SeedRange(135, 0.55f, 1.05f)) * segment);
        }

        float EscarpmentMask(float x, float z)
        {
            SeededSpace(x, z, 200, out float along, out float across);
            float meander = Mathf.Sin((along + SeedRange(201, -18000f, 18000f)) / SeedRange(202, 6400f, 12800f) * Mathf.PI * 2.0f) * SeedRange(203, 900f, 2600f);
            meander += (Mathf.PerlinNoise(along / SeedRange(204, 3800f, 7600f) + SeedRange(205, -40f, 40f), SeedRange(206, -40f, 40f)) - 0.5f) * SeedRange(207, 700f, 1800f);
            float signedDistance = across - meander;
            float stepWidth = SeedRange(208, 160f, 520f);
            float step = SmoothRange(-stepWidth, stepWidth, signedDistance);
            float broken = SmoothRange(0.32f, 0.78f, FractalNoise(along + SeedRange(209, -2500f, 2500f), across + SeedRange(210, -2500f, 2500f), SeedRange(211, 2400f, 5200f), 3, 0.5f));
            return step * broken;
        }

        float ValleyMask(float x, float z)
        {
            float s = Mathf.Max(1f, valleyScaleMeters);
            SeededSpace(x, z, 300, out float along, out float across);
            float warp = (Mathf.PerlinNoise(along / SeedRange(301, 2200f, 4600f) + SeedRange(302, -60f, 60f), across / SeedRange(303, 2200f, 4600f) + SeedRange(304, -60f, 60f)) - 0.5f) * SeedRange(305, 650f, 1800f);
            float center = Mathf.Sin((along + SeedRange(306, -16000f, 16000f)) / s * Mathf.PI * 2.0f) * SeedRange(307, 800f, 2100f);
            center += Mathf.Sin((along + SeedRange(308, -16000f, 16000f)) / (s * SeedRange(309, 0.36f, 0.68f)) * Mathf.PI * 2.0f) * SeedRange(310, 260f, 820f);
            float dist = Mathf.Abs(across + warp - center);
            float broad = Mathf.Clamp01(1f - dist / SeedRange(311, 1150f, 2450f));
            float narrow = Mathf.Clamp01(1f - dist / SeedRange(312, 320f, 760f));
            return Mathf.Clamp01(broad * SeedRange(313, 0.42f, 0.72f) + narrow * SeedRange(314, 0.38f, 0.72f));
        }

        void CanyonSpace(float x, float z, out float along, out float across)
        {
            float angle = SeedRange(110, -Mathf.PI, Mathf.PI);
            float ox = keepPrimaryCanyonNearOrigin ? 0f : SeedRange(111, -65000f, 65000f);
            float oz = keepPrimaryCanyonNearOrigin ? 0f : SeedRange(112, -65000f, 65000f);
            float px = x + ox;
            float pz = z + oz;
            along = px * Mathf.Cos(angle) + pz * Mathf.Sin(angle);
            across = -px * Mathf.Sin(angle) + pz * Mathf.Cos(angle) - primaryCanyonOriginOffsetMeters;
        }

        float CanyonMeander(float along, float across, float scale)
        {
            float meander = Mathf.Sin((along + SeedRange(111, -18000f, 18000f)) / scale * Mathf.PI * 2.0f) * SeedRange(112, 700f, 2100f);
            meander += Mathf.Sin((along + SeedRange(113, -18000f, 18000f)) / (scale * SeedRange(114, 0.34f, 0.62f)) * Mathf.PI * 2.0f) * SeedRange(115, 260f, 880f);
            meander += (Mathf.PerlinNoise(along / SeedRange(116, 3000f, 6200f) + SeedRange(117, -60f, 60f), across / SeedRange(118, 5200f, 9800f) + SeedRange(119, -60f, 60f)) - 0.5f) * SeedRange(124, 850f, 2200f);
            return meander;
        }

        void SeededSpace(float x, float z, int salt, out float along, out float across)
        {
            float angle = SeedRange(salt, -Mathf.PI, Mathf.PI);
            float ox = SeedRange(salt + 1, -65000f, 65000f);
            float oz = SeedRange(salt + 2, -65000f, 65000f);
            float px = x + ox;
            float pz = z + oz;
            along = px * Mathf.Cos(angle) + pz * Mathf.Sin(angle);
            across = -px * Mathf.Sin(angle) + pz * Mathf.Cos(angle);
        }

        float SeedRange(int salt, float min, float max)
        {
            return Mathf.Lerp(min, max, Seed01(salt));
        }

        float Seed01(int salt)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)salt * 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        static float FractalNoise(float x, float z, float scale, int octaves, float persistence)
        {
            float sum = 0f;
            float amp = 1f;
            float norm = 0f;
            float freq = 1f;
            scale = Mathf.Max(1f, scale);
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x / scale * freq, z / scale * freq) * amp;
                norm += amp;
                amp *= persistence;
                freq *= 2.03f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        static float ApplyCliffTerraces(float height, float uplift, float ridges, float canyonWall, float escarpment)
        {
            float cliffInfluence = Mathf.Clamp01(ridges * 0.45f + canyonWall * 0.9f + escarpment * 0.8f + uplift * 0.2f);
            float terraceCount = 9f;
            float terraced = Mathf.Round(height * terraceCount) / terraceCount;
            return Mathf.Lerp(height, terraced, cliffInfluence * 0.32f);
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

    /// <summary>
    /// Physical pooled Unity Terrain tile. Each view owns independent TerrainData for safe recycling.
    /// </summary>
    public sealed class TerrainTileView
    {
        public readonly GameObject gameObject;
        public readonly Terrain terrain;
        public readonly TerrainCollider collider;
        public readonly TerrainData disposableTerrainData;
        public readonly bool isAuthoredSceneTerrain;
        TerrainData instancedTerrainData;

        public PlanetTileCoord LogicalCoord { get; private set; }

        public TerrainTileView(string name, Transform parent, PlanetTerrainGenerationSettings settings)
        {
            disposableTerrainData = PlanetTerrainGenerator.CreateTerrainData(settings);

            gameObject = Terrain.CreateTerrainGameObject(disposableTerrainData);
            gameObject.name = name;
            if (parent != null)
                gameObject.transform.SetParent(parent, false);

            terrain = gameObject.GetComponent<Terrain>();
            collider = gameObject.GetComponent<TerrainCollider>();
            isAuthoredSceneTerrain = false;
            if (collider != null)
                collider.terrainData = disposableTerrainData;
        }

        public TerrainTileView(Terrain existingTerrain, PlanetTerrainGenerationSettings settings)
        {
            gameObject = existingTerrain.gameObject;
            terrain = existingTerrain;
            collider = gameObject.GetComponent<TerrainCollider>();
            disposableTerrainData = PlanetTerrainGenerator.CreateTerrainData(settings);
            isAuthoredSceneTerrain = true;
        }

        public void Assign(PlanetTileCoord coord, Vector3 worldPosition)
        {
            LogicalCoord = coord;
            // Keep authored scene tile names stable so a new surface session can
            // find and hide/reuse them instead of leaving an orphaned visible grid.
            if (!isAuthoredSceneTerrain)
                gameObject.name = $"PlanetTerrain_Tile_{coord.x}_{coord.z}";
            gameObject.transform.position = worldPosition;
            SetVisible(true);
        }

        public void SetVisible(bool visible)
        {
            if (terrain != null)
                terrain.enabled = visible;
            if (collider != null)
                collider.enabled = visible;
        }

        public void ApplyHeights(float[,] heights)
        {
            PlanetTerrainGenerator.ApplyHeights(terrain, collider, disposableTerrainData, heights);
        }

        public void ApplyTileData(PlanetTerrainTileBuildResult build)
        {
            PlanetTerrainGenerator.ApplyTileData(terrain, collider, disposableTerrainData, build);
        }

        public void UseDisposableTerrainData()
        {
            DestroyInstancedTerrainData();
            terrain.terrainData = disposableTerrainData;
            if (collider != null)
                collider.terrainData = disposableTerrainData;
        }

        public void UseTerrainData(TerrainData terrainData)
        {
            DestroyInstancedTerrainData();
            terrain.terrainData = terrainData != null ? terrainData : disposableTerrainData;
            if (collider != null)
                collider.terrainData = terrain.terrainData;
        }

        public void UseInstancedTerrainData(TerrainData terrainData, Action<TerrainData> prepare = null)
        {
            DestroyInstancedTerrainData();
            instancedTerrainData = terrainData != null ? UnityEngine.Object.Instantiate(terrainData) : null;
            if (instancedTerrainData != null)
                instancedTerrainData.name = terrainData.name + "_RuntimeInstance";

            if (instancedTerrainData != null) prepare?.Invoke(instancedTerrainData);

            terrain.terrainData = instancedTerrainData != null ? instancedTerrainData : disposableTerrainData;
            if (collider != null)
                collider.terrainData = terrain.terrainData;
        }

        void DestroyInstancedTerrainData()
        {
            if (instancedTerrainData == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instancedTerrainData);
            else
                UnityEngine.Object.DestroyImmediate(instancedTerrainData);
            instancedTerrainData = null;
        }
    }

    /// <summary>
    /// Unity Terrain treadmill around a ship/player transform.
    /// Prototype scope: flat local tangent plane, deterministic heights, row/column recycling, neighbor rebuilds, and timing logs.
    /// </summary>
    [AddComponentMenu("Koombar/Planet Terrain/Planet Terrain Treadmill")]
    [DisallowMultipleComponent]
    public sealed class PlanetTerrainTreadmill : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
    {
        const int HeightSeamRelaxSamples = 8;
        const string AuthoredTileNamePrefix = "PT_AuthoredTile_x";

        [Header("References")]
        [SerializeField] private Transform target;

        [Header("Terrain tile settings")]
        [SerializeField] private float tileSizeMeters = 1024f;
        [SerializeField] private float maxHeightMeters = 520f;
        [Tooltip("World-space Y offset applied to every visible terrain tile. Increase this to raise the generated/authored treadmill terrain without changing logical tile coordinates.")]
        [SerializeField] private float tileVerticalOffsetMeters = 2220f;
        [SerializeField] private int heightmapResolution = 129;
        [SerializeField] private TerrainLayer[] terrainLayers;

        [Header("Runtime visible window")]
        [Tooltip("Number of terrain tiles kept visible outward from the target tile. 1 = 3x3, 2 = 5x5.")]
        [SerializeField, Min(1)] private int visibleTileRadius = 2;

        [Header("Authored terrain")]
        [SerializeField] private PlanetTerrainTileSet authoredTileSet;
        [SerializeField] private bool generateMissingRuntimeTiles = false;
        [SerializeField] private bool relaxRuntimeHeightSeams = false;

        [Header("Height source")]
        [SerializeField] private PlanetHeightProvider heightProvider = new PlanetHeightProvider();

        [Header("Debug")]
        [SerializeField] private bool logTiming = true;
        [SerializeField] private bool drawGizmos = true;

        private readonly List<TerrainTileView> tiles = new List<TerrainTileView>(25);
        private readonly Dictionary<PlanetTileCoord, TerrainTileView> tileByCoord = new Dictionary<PlanetTileCoord, TerrainTileView>();
        private readonly HashSet<HeightSeamKey> relaxedHeightSeams = new HashSet<HeightSeamKey>();
        private IPlanetSurfaceCoordinateMapper coordinateMapper;
        private PlanetTileCoord centerTile;
        private bool initialized;
        private MyAssets.Planet.FloatingOriginPosition tileFrameOrigin;

        public void OnOriginShift(Vector3 shift)
        {
            tileFrameOrigin.Add(-shift);
        }

        public event Action<PlanetTileCoord, PlanetTileCoord, PlanetTileCoord> CenterTileChanged;
        // Shape private runtime data before assigning it to the visible terrain/collider.
        public event Action<PlanetTileCoord, TerrainData> PrepareTerrainTile;

        public PlanetTileCoord CenterTile => centerTile;
        public Transform Target => target;
        public PlanetTerrainTileSet AuthoredTileSet => authoredTileSet;
        public PlanetTerrainGenerationSettings GenerationSettings => BuildGenerationSettings();
        public IPlanetSurfaceCoordinateMapper CoordinateMapper => coordinateMapper ??= new FlatPlanetCoordinateMapper();
        public PlanetHeightProvider HeightProvider => heightProvider;
        public bool HasAuthoredTileSet => authoredTileSet != null;

        public float TileSizeMeters => tileSizeMeters;

        public void ConfigureTarget(Transform targetTransform)
        {
            target = targetTransform;
        }

        public void SetLocalTileFrameEnabled(bool enabled)
        {
            // Compatibility hook for planet transition code. The known-good 5x5 treadmill uses flat Unity world coordinates only.
        }

        public void ReinitializeFlatSession(Vector3 flatWorldOrigin, Transform targetTransform, PlanetTileCoord startingCenter = default)
        {
            ValidateSettings();
            target = targetTransform;
            // A new surface session has a new local frame. Existing tile identities
            // remain valid, but their positions must be rebuilt in that frame.
            tileFrameOrigin = new MyAssets.Planet.FloatingOriginPosition(
                (double)flatWorldOrigin.x - (double)startingCenter.x * tileSizeMeters,
                flatWorldOrigin.y,
                (double)flatWorldOrigin.z - (double)startingCenter.z * tileSizeMeters);
            PlanetTileCoord currentCenter = WorldToTile(flatWorldOrigin);

            if (!initialized || tiles.Count != VisibleTileWindowSize * VisibleTileWindowSize)
                InitializeGrid(currentCenter);
            else
            {
                // Surface entry replaces the orbital frame. Rebind every view, including
                // tiles that kept the same logical coordinate, so no old placement or
                // TerrainData instance survives the frame swap.
                centerTile = currentCenter;
                tileByCoord.Clear();
                relaxedHeightSeams.Clear();
                int index = 0;
                foreach (PlanetTileCoord coord in BuildVisibleCoords(currentCenter))
                {
                    TerrainTileView tile = tiles[index++];
                    GenerateAndAssign(tile, coord);
                    tileByCoord.Add(coord, tile);
                }
                RebuildTerrainNeighbors();
                SyncAuthoredSceneTerrainVisibility();
                if (relaxRuntimeHeightSeams)
                    RelaxVisibleHeightSeams();
            }
        }

        private void Awake()
        {
            coordinateMapper = new FlatPlanetCoordinateMapper();
            ValidateSettings();
        }

        private void Start()
        {
            if (initialized)
                return;

            if (target == null)
            {
                var rb = FindFirstObjectByType<Rigidbody>();
                if (rb != null)
                    target = rb.transform;
            }

            if (target == null)
            {
                Debug.LogWarning("[PlanetTerrainTreadmill] No target assigned; treadmill will not initialize.", this);
                return;
            }

            centerTile = WorldToTile(target.position);
            InitializeGrid(centerTile);
        }

        private void Update()
        {
            if (!initialized || target == null)
                return;

            PlanetTileCoord current = WorldToTile(target.position);
            if (!current.Equals(centerTile))
                Recenter(current);
        }

        private void ValidateSettings()
        {
            tileSizeMeters = Mathf.Max(1f, tileSizeMeters);
            maxHeightMeters = Mathf.Max(1f, maxHeightMeters);
            heightmapResolution = Mathf.Clamp(heightmapResolution, 33, 4097);
            visibleTileRadius = Mathf.Clamp(visibleTileRadius, 1, 8);

            // Unity Terrain heightmap resolutions should be 2^n + 1. Use the closest lower valid value to avoid surprises.
            int samples = heightmapResolution - 1;
            int pow = Mathf.ClosestPowerOfTwo(samples);
            if (pow != samples)
            {
                int lower = Mathf.NextPowerOfTwo(samples) / 2;
                heightmapResolution = Mathf.Max(33, lower + 1);
                Debug.LogWarning($"[PlanetTerrainTreadmill] Adjusted heightmapResolution to valid 2^n+1 value: {heightmapResolution}.", this);
            }
        }

        private void InitializeGrid(PlanetTileCoord initialCenter)
        {
            Stopwatch sw = Stopwatch.StartNew();
            DestroyGeneratedRuntimeTiles();
            tiles.Clear();
            tileByCoord.Clear();
            relaxedHeightSeams.Clear();
            centerTile = initialCenter;

            foreach (PlanetTileCoord coord in BuildVisibleCoords(initialCenter))
            {
                var view = CreateTileView(coord);
                GenerateAndAssign(view, coord);
                tiles.Add(view);
                tileByCoord[coord] = view;
            }

            RebuildTerrainNeighbors();
            SyncAuthoredSceneTerrainVisibility();
            if (relaxRuntimeHeightSeams)
                RelaxVisibleHeightSeams();
            initialized = true;
            sw.Stop();
            LogTiming($"Initialized {VisibleTileWindowSize}x{VisibleTileWindowSize} terrain grid around {initialCenter} in {sw.Elapsed.TotalMilliseconds:0.00} ms");
        }

        private void DestroyGeneratedRuntimeTiles()
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                TerrainTileView tile = tiles[i];
                if (tile == null || tile.gameObject == null || tile.isAuthoredSceneTerrain)
                    continue;

                if (Application.isPlaying)
                    Destroy(tile.gameObject);
                else
                    DestroyImmediate(tile.gameObject);
            }
        }

        private TerrainTileView CreateTileView(PlanetTileCoord coord)
        {
            Terrain existing = FindAuthoredSceneTerrain(coord);
            if (existing != null)
                return new TerrainTileView(existing, BuildGenerationSettings());

            return new TerrainTileView($"PlanetTerrain_Tile_{coord.x}_{coord.z}", transform, BuildGenerationSettings());
        }

        private Terrain FindAuthoredSceneTerrain(PlanetTileCoord coord)
        {
            string authoredName = GetAuthoredTileName(coord);
            Transform child = transform.Find(authoredName);
            return child != null ? child.GetComponent<Terrain>() : null;
        }

        private static string GetAuthoredTileName(PlanetTileCoord coord) => $"{AuthoredTileNamePrefix}{coord.x}_z{coord.z}";

        private void Recenter(PlanetTileCoord newCenter)
        {
            Stopwatch sw = Stopwatch.StartNew();
            PlanetTileCoord oldCenter = centerTile;
            centerTile = newCenter;

            var needed = new HashSet<PlanetTileCoord>(BuildVisibleCoords(newCenter));

            tileByCoord.Clear();
            var reusable = new Queue<TerrainTileView>();
            foreach (TerrainTileView tile in tiles)
            {
                if (needed.Contains(tile.LogicalCoord))
                    tileByCoord[tile.LogicalCoord] = tile;
                else
                    reusable.Enqueue(tile);
            }
            int recycledTileCount = reusable.Count;

            foreach (PlanetTileCoord coord in needed)
            {
                if (tileByCoord.ContainsKey(coord))
                    continue;

                if (reusable.Count == 0)
                {
                    Debug.LogError("[PlanetTerrainTreadmill] Recycle pool exhausted; rebuilding all tiles is required.", this);
                    return;
                }

                TerrainTileView tile = reusable.Dequeue();
                GenerateAndAssign(tile, coord);
                tileByCoord[coord] = tile;
            }

            RebuildTerrainNeighbors();
            SyncAuthoredSceneTerrainVisibility();
            if (relaxRuntimeHeightSeams)
                RelaxVisibleHeightSeams();
            PlanetTileCoord delta = newCenter - oldCenter;
            CenterTileChanged?.Invoke(oldCenter, newCenter, delta);
            sw.Stop();
            LogTiming($"Recentered from {oldCenter} to {newCenter}; recycled {recycledTileCount} tile(s) in {sw.Elapsed.TotalMilliseconds:0.00} ms");
        }

        private void RelaxVisibleHeightSeams()
        {
            foreach (TerrainTileView tile in tileByCoord.Values)
            {
                PlanetTileCoord c = tile.LogicalCoord;
                if (tileByCoord.TryGetValue(new PlanetTileCoord(c.x + 1, c.z), out TerrainTileView east) && ShouldRelaxHeightSeam(tile, east, eastWest: true))
                    RelaxHeightSeamPair(tile.terrain.terrainData, east.terrain.terrainData, eastWest: true);

                if (tileByCoord.TryGetValue(new PlanetTileCoord(c.x, c.z + 1), out TerrainTileView north) && ShouldRelaxHeightSeam(tile, north, eastWest: false))
                    RelaxHeightSeamPair(tile.terrain.terrainData, north.terrain.terrainData, eastWest: false);
            }
        }

        private bool ShouldRelaxHeightSeam(TerrainTileView a, TerrainTileView b, bool eastWest)
        {
            TerrainData aData = a?.terrain != null ? a.terrain.terrainData : null;
            TerrainData bData = b?.terrain != null ? b.terrain.terrainData : null;
            if (aData == null || bData == null)
                return false;

            return relaxedHeightSeams.Add(new HeightSeamKey(aData.GetInstanceID(), bData.GetInstanceID(), eastWest));
        }

        private void ForgetRelaxedHeightSeams(TerrainData terrainData)
        {
            if (terrainData == null || relaxedHeightSeams.Count == 0)
                return;

            int id = terrainData.GetInstanceID();
            relaxedHeightSeams.RemoveWhere(key => key.Contains(id));
        }

        static void RelaxHeightSeamPair(TerrainData a, TerrainData b, bool eastWest)
        {
            if (a == null || b == null || a.heightmapResolution != b.heightmapResolution)
                return;

            int res = a.heightmapResolution;
            int blendSamples = Mathf.Clamp(HeightSeamRelaxSamples, 1, res / 2);
            float[,] ah = a.GetHeights(0, 0, res, res);
            bool sameTerrainData = ReferenceEquals(a, b);
            float[,] bh = sameTerrainData ? ah : b.GetHeights(0, 0, res, res);

            for (int i = 0; i < res; i++)
            {
                for (int band = 0; band < blendSamples; band++)
                {
                    float t = blendSamples <= 1 ? 0f : band / (float)(blendSamples - 1);
                    float edgeWeight = 1f - Smooth01(t);

                    if (eastWest)
                    {
                        int ax = res - 1 - band;
                        int bx = band;
                        float avg = (ah[i, ax] + bh[i, bx]) * 0.5f;
                        ah[i, ax] = Mathf.Lerp(ah[i, ax], avg, edgeWeight);
                        bh[i, bx] = Mathf.Lerp(bh[i, bx], avg, edgeWeight);
                    }
                    else
                    {
                        int az = res - 1 - band;
                        int bz = band;
                        float avg = (ah[az, i] + bh[bz, i]) * 0.5f;
                        ah[az, i] = Mathf.Lerp(ah[az, i], avg, edgeWeight);
                        bh[bz, i] = Mathf.Lerp(bh[bz, i], avg, edgeWeight);
                    }
                }
            }

            a.SetHeightsDelayLOD(0, 0, ah);
            a.SyncHeightmap();
            if (!sameTerrainData)
            {
                b.SetHeightsDelayLOD(0, 0, bh);
                b.SyncHeightmap();
            }
        }

        static float Smooth01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private void GenerateAndAssign(TerrainTileView tile, PlanetTileCoord coord)
        {
            tile.Assign(coord, TileWorldPosition(coord));

            if (authoredTileSet != null && authoredTileSet.TryGetTerrainData(coord, out TerrainData authoredData, out PlanetTileCoord resolvedCoord))
            {
                tile.UseInstancedTerrainData(authoredData, data => PrepareTerrainTile?.Invoke(coord, data));
                LogTiming($"Tile {coord}: using baked TerrainData '{authoredData.name}' from wrapped coord {resolvedCoord}");
                return;
            }

            if (!generateMissingRuntimeTiles)
            {
                tile.UseDisposableTerrainData();
                tile.SetVisible(false);
                Debug.LogError($"[PlanetTerrainTreadmill] Missing baked terrain for tile {coord}. Bake the wrapped planet tile set or enable Generate Missing Runtime Tiles for debug fallback.", this);
                return;
            }

            PlanetTerrainTileBuildResult build = PlanetTerrainGenerator.BuildTile(coord, BuildRuntimeHeightOnlySettings(), CoordinateMapper, heightProvider);
            tile.UseDisposableTerrainData();
            Stopwatch sw = Stopwatch.StartNew();
            tile.ApplyHeights(build.heights);
            PrepareTerrainTile?.Invoke(coord, tile.disposableTerrainData);
            ForgetRelaxedHeightSeams(tile.disposableTerrainData);
            sw.Stop();
            LogTiming($"Tile {coord}: generated debug fallback height-only {heightmapResolution}x{heightmapResolution} in {build.generationMilliseconds:0.00} ms, applied in {sw.Elapsed.TotalMilliseconds:0.00} ms");
        }

        readonly struct HeightSeamKey : IEquatable<HeightSeamKey>
        {
            readonly int a;
            readonly int b;
            readonly bool eastWest;

            public HeightSeamKey(int firstTerrainDataId, int secondTerrainDataId, bool eastWest)
            {
                if (firstTerrainDataId <= secondTerrainDataId)
                {
                    a = firstTerrainDataId;
                    b = secondTerrainDataId;
                }
                else
                {
                    a = secondTerrainDataId;
                    b = firstTerrainDataId;
                }

                this.eastWest = eastWest;
            }

            public bool Contains(int terrainDataId) => a == terrainDataId || b == terrainDataId;
            public bool Equals(HeightSeamKey other) => a == other.a && b == other.b && eastWest == other.eastWest;
            public override bool Equals(object obj) => obj is HeightSeamKey other && Equals(other);
            public override int GetHashCode() => (((a * 397) ^ b) * 397) ^ eastWest.GetHashCode();
        }

        public TerrainData CreateGeneratedTerrainData(PlanetTileCoord coord)
        {
            PlanetTerrainGenerationSettings settings = BuildGenerationSettings();
            TerrainData data = PlanetTerrainGenerator.CreateTerrainData(settings);
            PlanetTerrainTileBuildResult build = PlanetTerrainGenerator.BuildTile(coord, settings, CoordinateMapper, heightProvider);
            data.SetHeights(0, 0, build.heights);
            PlanetTerrainGenerator.ApplyAlphamaps(data, build.alphamaps);
            return data;
        }

        public float[,,] BuildGeneratedAlphamaps(PlanetTileCoord coord)
        {
            PlanetTerrainGenerationSettings settings = BuildGenerationSettings();
            PlanetTerrainTileBuildResult build = PlanetTerrainGenerator.BuildTile(coord, settings, CoordinateMapper, heightProvider);
            return build.alphamaps;
        }

        public Vector3 GetTileWorldPosition(PlanetTileCoord coord) => TileWorldPosition(coord);
        public float TileVerticalOffsetMeters => tileVerticalOffsetMeters;

        public void SetTileVerticalOffset(float offsetMeters)
        {
            if (Mathf.Approximately(tileVerticalOffsetMeters, offsetMeters))
                return;

            tileVerticalOffsetMeters = offsetMeters;
            RefreshTileWorldPositions();
        }

        /// <summary>
        /// Places altitude-zero in the displayed world frame without changing the X/Z
        /// tile frame. The reveal offset is applied relative to this surface plane.
        /// </summary>
        public void SetTileFrameBaseY(float worldY)
        {
            if (Math.Abs(tileFrameOrigin.y - worldY) < 0.001d)
                return;

            tileFrameOrigin.y = worldY;
            RefreshTileWorldPositions();
        }

        private void RefreshTileWorldPositions()
        {
            foreach (TerrainTileView tile in tiles)
            {
                if (tile != null && tile.gameObject != null)
                    tile.gameObject.transform.position = TileWorldPosition(tile.LogicalCoord);
            }
        }

        private int VisibleTileWindowSize => visibleTileRadius * 2 + 1;

        private IEnumerable<PlanetTileCoord> BuildVisibleCoords(PlanetTileCoord center)
        {
            for (int dz = -visibleTileRadius; dz <= visibleTileRadius; dz++)
            {
                for (int dx = -visibleTileRadius; dx <= visibleTileRadius; dx++)
                    yield return new PlanetTileCoord(center.x + dx, center.z + dz);
            }
        }

        public void SetAuthoredTileSet(PlanetTerrainTileSet tileSet)
        {
            authoredTileSet = tileSet;
        }

        private PlanetTerrainGenerationSettings BuildGenerationSettings()
        {
            TerrainLayer[] resolvedLayers = ResolveTerrainLayers();
            return new PlanetTerrainGenerationSettings
            {
                tileSizeMeters = tileSizeMeters,
                maxHeightMeters = maxHeightMeters,
                heightmapResolution = heightmapResolution,
                alphamapResolution = Mathf.Max(16, Mathf.Min(512, heightmapResolution * 2)),
                terrainLayers = resolvedLayers
            };
        }

        private PlanetTerrainGenerationSettings BuildRuntimeHeightOnlySettings()
        {
            PlanetTerrainGenerationSettings settings = BuildGenerationSettings();
            settings.terrainLayers = null;
            settings.alphamapResolution = 16;
            return settings;
        }

        private TerrainLayer[] ResolveTerrainLayers()
        {
            TerrainLayer[] tileSetLayers = authoredTileSet != null ? authoredTileSet.generationSettings?.terrainLayers : null;
            int componentLayerCount = CountAssignedLayers(terrainLayers);
            int tileSetLayerCount = CountAssignedLayers(tileSetLayers);

            // Prefer the richer authored TileSet layer list when the component has only a partial/stale layer assignment.
            if (tileSetLayerCount > componentLayerCount)
                return tileSetLayers;

            return terrainLayers;
        }

        static int CountAssignedLayers(TerrainLayer[] layers)
        {
            if (layers == null)
                return 0;

            int count = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null)
                    count++;
            }
            return count;
        }

        private void RebuildTerrainNeighbors()
        {
            foreach (TerrainTileView tile in tiles)
            {
                if (tile?.terrain != null)
                    tile.terrain.SetNeighbors(null, null, null, null);
            }

            foreach (TerrainTileView tile in tileByCoord.Values)
            {
                PlanetTileCoord c = tile.LogicalCoord;
                tileByCoord.TryGetValue(new PlanetTileCoord(c.x - 1, c.z), out TerrainTileView left);
                tileByCoord.TryGetValue(new PlanetTileCoord(c.x, c.z + 1), out TerrainTileView top);
                tileByCoord.TryGetValue(new PlanetTileCoord(c.x + 1, c.z), out TerrainTileView right);
                tileByCoord.TryGetValue(new PlanetTileCoord(c.x, c.z - 1), out TerrainTileView bottom);
                tile.terrain.SetNeighbors(left?.terrain, top?.terrain, right?.terrain, bottom?.terrain);
            }
        }

        private void SyncAuthoredSceneTerrainVisibility()
        {
            if (transform == null)
                return;

            var activeTerrains = new HashSet<Terrain>();
            foreach (TerrainTileView tile in tileByCoord.Values)
            {
                if (tile?.terrain != null)
                    activeTerrains.Add(tile.terrain);
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null || !child.name.StartsWith(AuthoredTileNamePrefix, StringComparison.Ordinal))
                    continue;

                Terrain terrain = child.GetComponent<Terrain>();
                if (terrain == null)
                    continue;

                bool isActiveVisibleTile = activeTerrains.Contains(terrain);
                terrain.enabled = isActiveVisibleTile;

                TerrainCollider terrainCollider = child.GetComponent<TerrainCollider>();
                if (terrainCollider != null)
                    terrainCollider.enabled = isActiveVisibleTile;
            }
        }

        public PlanetTileCoord WorldToTile(Vector3 worldPosition)
        {
            return new PlanetTileCoord(
                (int)Math.Floor(((double)worldPosition.x - tileFrameOrigin.x) / tileSizeMeters),
                (int)Math.Floor(((double)worldPosition.z - tileFrameOrigin.z) / tileSizeMeters));
        }

        private Vector3 TileWorldPosition(PlanetTileCoord coord)
        {
            return new Vector3(
                (float)((double)coord.x * tileSizeMeters + tileFrameOrigin.x),
                (float)(tileVerticalOffsetMeters + tileFrameOrigin.y),
                (float)((double)coord.z * tileSizeMeters + tileFrameOrigin.z));
        }

        private void LogTiming(string message)
        {
            if (logTiming)
                Debug.Log("[PlanetTerrainTreadmill] " + message, this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Gizmos.color = Color.cyan;
            foreach (TerrainTileView tile in tileByCoord.Values)
            {
                if (tile == null || tile.gameObject == null)
                    continue;

                Vector3 p = tile.gameObject.transform.position;
                Vector3 center = p + new Vector3(tileSizeMeters * 0.5f, maxHeightMeters * 0.5f, tileSizeMeters * 0.5f);
                Gizmos.DrawWireCube(center, new Vector3(tileSizeMeters, maxHeightMeters, tileSizeMeters));
            }
        }
    }
}
