using System;
using MyAssets.PlanetTerrain;
using UnityEngine;

namespace Zandea.POI
{
    public static class OutpostTerrain
    {
        public static bool Sample(PlanetTerrainTreadmill t, Vector2 stable, out float height, out float slope)
        {
            float size = t.TileSizeMeters;
            var coord = new PlanetTileCoord(Mathf.FloorToInt(stable.x / size), Mathf.FloorToInt(stable.y / size));
            var uv = new Vector2(stable.x / size - coord.x, stable.y / size - coord.z);
            if (t.AuthoredTileSet && t.AuthoredTileSet.TryGetTerrainData(coord, out var data, out _))
            {
                height = data.GetInterpolatedHeight(uv.x, uv.y);
                slope = data.GetSteepness(uv.x, uv.y);
                return true;
            }
            height = slope = 0;
            return false;
        }

        public static bool FindSite(PlanetTerrainTreadmill t, BanditOutpostDefinition d, Vector2 start, out Vector2 site, out float height)
        {
            site = default; height = 0;
            float best = float.PositiveInfinity;
            float minimumRange = float.PositiveInfinity, minimumSlope = 90;
            int sampled = 0;
            // Fixed search order and stable coordinates: no approach-dependent choices.
            for (int z = -32; z <= 32; z++)
                for (int x = -32; x <= 32; x++)
                {
                    var p = start + new Vector2(x, z) * 32;
                    if (Vector2.Distance(p, start) < 130) continue;
                    float low = float.PositiveInfinity, high = float.NegativeInfinity, sum = 0;
                    bool valid = true;
                    float worstSlope = 0;
                    var rotation = Quaternion.Euler(0, d.gateQuarterTurns * 90, 0);
                    for (int iz = -7; iz <= 4; iz++)
                        for (int ix = -4; ix <= 4; ix++)
                        {
                            var delta = rotation * new Vector3(ix * (d.dimensions.x / 8 + 1), 0, iz * (d.dimensions.y / 8 + 1));
                            if (!Sample(t, p + new Vector2(delta.x, delta.z), out float h, out float slope) || slope > d.maximumSiteSlope) valid = false;
                            low = Mathf.Min(low, h); high = Mathf.Max(high, h); sum += h;
                            worstSlope = Mathf.Max(worstSlope, slope);
                        }
                    sampled++;
                    if (high - low < minimumRange) { minimumRange = high - low; minimumSlope = worstSlope; }
                    if (!valid || high - low > d.maximumHeightRange) continue;
                    float score = high - low + Vector2.Distance(p, start) * .003f;
                    if (score >= best) continue;
                    float average = sum / 108;
                    var world = t.GetTileWorldPosition(new PlanetTileCoord(0, 0)) + new Vector3(p.x, average + 3, p.y);
                    foreach (var collider in Physics.OverlapBox(world, new Vector3(d.dimensions.x / 2 + 3, 5, d.dimensions.y / 2 + 3), rotation, ~0, QueryTriggerInteraction.Ignore))
                        if (!(collider is TerrainCollider)) { valid = false; break; }
                    if (!valid) continue;
                    best = score; site = p; height = average;
                }
            Debug.Log($"[Zandea Site] Start={start}, treadmill={t.name}, set={t.AuthoredTileSet}, candidates={sampled}, lowest range={minimumRange}, slope there={minimumSlope}, selected={site}, height={height}");
            return !float.IsPositiveInfinity(best);
        }

        public static float Weight(BanditOutpostDefinition d, Vector2 local)
        {
            // Rectangular plateau includes foundations, with a continuous blend in stable space.
            Vector2 outside = new Vector2(Mathf.Max(0, Mathf.Abs(local.x) - d.dimensions.x / 2 - 3),
                Mathf.Max(0, Mathf.Abs(local.y) - d.dimensions.y / 2 - 3));
            float plateau = 1 - Mathf.SmoothStep(0, 1, outside.magnitude / d.blendDistance);
            var gate = new Vector2(0, -d.dimensions.y / 2);
            float along = Mathf.Clamp01((gate.y - local.y) / d.approachLength);
            float lateral = Mathf.Max(0, Mathf.Abs(local.x) - d.pathWidth / 2);
            float approach = local.y < gate.y && local.y > gate.y - d.approachLength
                ? (1 - Mathf.SmoothStep(0, 1, along)) * (1 - Mathf.SmoothStep(0, 1, lateral / d.blendDistance)) : 0;
            return Mathf.Max(plateau, approach);
        }
        public static void Stamp(PlanetTerrainTreadmill t, BanditOutpostDefinition d, Vector2 site, float height, PlanetTileCoord tile, TerrainData data, Quaternion? rotation = null)
        {
            float reach = d.dimensions.magnitude / 2 + d.blendDistance + d.approachLength;
            Vector2 tileOrigin = new Vector2(tile.x, tile.z) * t.TileSizeMeters;
            if (site.x + reach < tileOrigin.x || site.y + reach < tileOrigin.y ||
                site.x - reach > tileOrigin.x + data.size.x || site.y - reach > tileOrigin.y + data.size.z) return;
            int n = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, n, n);
            var inverse = Quaternion.Inverse(rotation ?? Quaternion.Euler(0, d.gateQuarterTurns * 90, 0));
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = tileOrigin + new Vector2(x * data.size.x / (n - 1), z * data.size.z / (n - 1)) - site;
                    var local = inverse * new Vector3(p.x, 0, p.y);
                    heights[z, x] = Mathf.Lerp(heights[z, x], height / data.size.y, Weight(d, new Vector2(local.x, local.z)));
                }
            data.SetHeights(0, 0, heights);
        }
    }
}
