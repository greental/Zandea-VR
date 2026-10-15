using System.Collections.Generic;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExistingTreadmillPoiTerrainPreparation
{
    const float SearchRadius = 30f;
    const float MaximumRange = 5f;
    const float MinimumSiteSeparation = 120f;

    struct Candidate
    {
        public ExistingTreadmillPoiPlanter.Site site;
        public PlanetTileCoord tile;
        public TerrainData data;
        public Vector2 position;
        public float range;
        public float height;
    }

    public static bool Prepare(ExistingTreadmillPoiPlanter planter)
    {
        if (!planter || EditorApplication.isPlayingOrWillChangePlaymode) return false;
        var treadmill = planter.GetComponent<PlanetTerrainTreadmill>();
        var tileSet = treadmill ? treadmill.AuthoredTileSet : null;
        if (!tileSet || tileSet.authoredTiles == null || planter.sites == null || planter.sites.Count == 0) return false;

        var selected = new List<Candidate>();
        foreach (var site in planter.sites)
        {
            if (site == null) continue;
            if (!FindCandidate(tileSet, site, selected, out var candidate))
            {
                Debug.LogWarning($"No POI footprint with <= {MaximumRange} m height change within {SearchRadius} m was found for site {site.seed}. Terrain was not changed.", planter);
                return false;
            }
            selected.Add(candidate);
        }
        if (selected.Count == 0) return false;

        Undo.RecordObject(planter, "Relocate scavenger POIs to level ground");
        foreach (var candidate in selected)
        {
            Undo.RegisterCompleteObjectUndo(candidate.data, "Grade scavenger POI terrain");
            Grade(candidate.data, candidate.position, candidate.height);
            candidate.site.tile = candidate.tile;
            candidate.site.localPosition = candidate.position;
            candidate.site.normalizedHeight = candidate.data.GetInterpolatedHeight(
                candidate.position.x / candidate.data.size.x,
                candidate.position.y / candidate.data.size.z) / Mathf.Max(.001f, candidate.data.size.y);
            EditorUtility.SetDirty(candidate.data);
            Debug.Log($"POI {candidate.site.seed}: tile {candidate.tile.x},{candidate.tile.z}, 30 m height range {candidate.range:F2} m before gentle grading.", planter);
        }
        planter.terrainPrepared = true;
        EditorUtility.SetDirty(planter);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(planter.gameObject.scene);
        return true;
    }

    static bool FindCandidate(PlanetTerrainTileSet tileSet, ExistingTreadmillPoiPlanter.Site site,
        List<Candidate> selected, out Candidate best)
    {
        best = default;
        for (int neighborhood = 0; neighborhood <= 2; neighborhood++)
        {
            float bestRange = float.PositiveInfinity;
            foreach (var tile in tileSet.authoredTiles)
            {
                if (tile == null || !tile.terrainData) continue;
                int tileDistance = Mathf.Max(Mathf.Abs(tile.coord.x - site.tile.x), Mathf.Abs(tile.coord.z - site.tile.z));
                if (neighborhood == 0 && tileDistance != 0 || neighborhood == 1 && tileDistance != 1 ||
                    neighborhood == 2 && tileDistance < 2) continue;
                TerrainData data = tile.terrainData;
                for (float z = 64f; z <= data.size.z - 64f; z += 64f)
                    for (float x = 64f; x <= data.size.x - 64f; x += 64f)
                    {
                        Vector2 point = new Vector2(x, z);
                        bool separated = true;
                        foreach (var other in selected)
                        {
                            float dx = (tile.coord.x - other.tile.x) * data.size.x + x - other.position.x;
                            float dz = (tile.coord.z - other.tile.z) * data.size.z + z - other.position.y;
                            if (dx * dx + dz * dz < MinimumSiteSeparation * MinimumSiteSeparation)
                            { separated = false; break; }
                        }
                        if (!separated) continue;
                        float range = HeightRange(data, point, out float height);
                        if (range > MaximumRange || range >= bestRange) continue;
                        bestRange = range;
                        best = new Candidate { site = site, tile = tile.coord, data = data,
                            position = point, height = height, range = range };
                    }
            }
            if (best.data) return true;
        }
        return false;
    }

    static float HeightRange(TerrainData data, Vector2 center, out float height)
    {
        height = data.GetInterpolatedHeight(center.x / data.size.x, center.y / data.size.z);
        float low = height, high = height;
        for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
            {
                if (x * x + z * z > 9) continue;
                float sample = data.GetInterpolatedHeight((center.x + x * 10f) / data.size.x,
                    (center.y + z * 10f) / data.size.z);
                low = Mathf.Min(low, sample);
                high = Mathf.Max(high, sample);
            }
        return high - low;
    }

    static void Grade(TerrainData data, Vector2 center, float targetHeight)
    {
        int resolution = data.heightmapResolution;
        float stepX = (resolution - 1f) / data.size.x;
        float stepZ = (resolution - 1f) / data.size.z;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - 45f) * stepX), 0, resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((center.y - 45f) * stepZ), 0, resolution - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + 45f) * stepX), 0, resolution - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((center.y + 45f) * stepZ), 0, resolution - 1);
        var heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        float target = Mathf.Clamp01(targetHeight / data.size.y);
        for (int z = 0; z < heights.GetLength(0); z++)
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                float distance = Vector2.Distance(new Vector2((x + x0) / stepX, (z + z0) / stepZ), center);
                float blend = .72f * (1f - Mathf.SmoothStep(22f, 45f, distance));
                heights[z, x] = Mathf.Lerp(heights[z, x], target, blend);
            }
        data.SetHeightsDelayLOD(x0, z0, heights);
        data.SyncHeightmap();
    }
}
