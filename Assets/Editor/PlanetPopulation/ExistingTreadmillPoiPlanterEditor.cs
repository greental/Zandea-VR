using System.Collections.Generic;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(ExistingTreadmillPoiPlanter))]
public sealed class ExistingTreadmillPoiPlanterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var planter = (ExistingTreadmillPoiPlanter)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Authored sites: {(planter.sites == null ? 0 : planter.sites.Count)}", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Find the authored layouts in Hierarchy: Caroms > PlanetGeneratedStack > Caroms Terrain Treadmill + Scavenger POIs > Scavenger POIs (Scene Preview). They are hidden during Play mode, then cloned as streamed POIs. Edits made to these objects are used at runtime.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Replace Authored Layout From Template") &&
                EditorUtility.DisplayDialog("Replace authored POIs?", "This removes edits made to the visible POI objects and rebuilds them from the procedural template.", "Replace", "Cancel"))
                ExistingTreadmillPoiPreview.Rebuild(planter);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Regrade Sites and Replace Authored Layout") &&
                EditorUtility.DisplayDialog("Replace authored POIs?", "This relocates the sites and removes edits made to the visible POI objects.", "Regrade", "Cancel"))
                if (ExistingTreadmillPoiTerrainPreparation.Prepare(planter)) ExistingTreadmillPoiPreview.Rebuild(planter);
        var preview = planter.transform.Find("Scavenger POIs (Scene Preview)");
        if (preview)
            for (int i = 0; i < preview.childCount; i++)
            {
                var site = preview.GetChild(i);
                if (GUILayout.Button("Select and Frame " + site.name))
                {
                    Selection.activeGameObject = site.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
        EditorGUILayout.HelpBox("Each site requires at most 5 m height change across a 30 m radius. Accepted ground is gently graded before the preview is rebuilt.", MessageType.Info);
    }

    static void Bake(ExistingTreadmillPoiPlanter planter)
    {
        var treadmill = planter.GetComponent<PlanetTerrainTreadmill>();
        var tileSet = treadmill ? treadmill.AuthoredTileSet : null;
        if (!tileSet || tileSet.authoredTiles == null || tileSet.authoredTiles.Count == 0)
        {
            EditorUtility.DisplayDialog("Plant POIs", "Assign a baked Terrain Tile Set to this working treadmill first.", "OK");
            return;
        }
        var result = new List<ExistingTreadmillPoiPlanter.Site>();
        try
        {
            for (int i = 0; i < tileSet.authoredTiles.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Plant POIs", $"Inspecting tile {i + 1}/{tileSet.authoredTiles.Count}", i / (float)tileSet.authoredTiles.Count);
                var tile = tileSet.authoredTiles[i];
                if (tile?.terrainData == null) continue;
                int tileSeed = unchecked(planter.seed * 73856093 ^ tile.coord.x * 19349663 ^ tile.coord.z * 83492791);
                var random = new System.Random(tileSeed);
                if (random.NextDouble() > planter.tileChance) continue;
                var data = tile.terrainData;
                float tileSizeX = data.size.x, tileSizeZ = data.size.z;
                float margin = planter.siteRadius * 1.8f;
                if (tileSizeX <= margin * 2 || tileSizeZ <= margin * 2) continue;
                Vector2 best = default;
                float bestSlope = float.PositiveInfinity, bestDifference = float.PositiveInfinity;
                float bestHeight = 0f;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    float x = Mathf.Lerp(margin, tileSizeX - margin, (float)random.NextDouble());
                    float z = Mathf.Lerp(margin, tileSizeZ - margin, (float)random.NextDouble());
                    float center = data.GetInterpolatedHeight(x / tileSizeX, z / tileSizeZ);
                    float slope = 0f, difference = 0f;
                    for (int sample = 0; sample < 9; sample++)
                    {
                        float sx = x + (sample % 3 - 1) * planter.siteRadius;
                        float sz = z + (sample / 3 - 1) * planter.siteRadius;
                        float u = sx / tileSizeX, v = sz / tileSizeZ;
                        slope = Mathf.Max(slope, data.GetSteepness(u, v));
                        difference = Mathf.Max(difference, Mathf.Abs(data.GetInterpolatedHeight(u, v) - center));
                    }
                    if (slope > planter.maximumSiteSlope ||
                        difference > (planter.flattenWhenNeeded ? planter.maximumFlattenHeightDifference : 1.5f)) continue;
                    if (slope + difference * 2f >= bestSlope + bestDifference * 2f) continue;
                    best = new Vector2(x, z);
                    bestSlope = slope;
                    bestDifference = difference;
                    bestHeight = center;
                }
                if (float.IsPositiveInfinity(bestSlope)) continue;
                if (planter.flattenWhenNeeded && bestDifference > 1.5f)
                {
                    Undo.RegisterCompleteObjectUndo(data, "Smooth POI footprint");
                    FlattenFootprint(data, best, planter.siteRadius, bestHeight);
                    EditorUtility.SetDirty(data);
                    bestHeight = data.GetInterpolatedHeight(best.x / tileSizeX, best.y / tileSizeZ);
                }
                result.Add(new ExistingTreadmillPoiPlanter.Site
                {
                    tile = tile.coord,
                    localPosition = best,
                    normalizedHeight = bestHeight / Mathf.Max(.001f, data.size.y),
                    seed = tileSeed
                });
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        Undo.RecordObject(planter, "Plant POIs on treadmill");
        planter.sites = result;
        EditorUtility.SetDirty(planter);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(planter.gameObject.scene);
        Debug.Log($"Planted {result.Count} POI sites on {tileSet.authoredTiles.Count} existing baked tiles. Treadmill generation was not run.", planter);
    }

    static void FlattenFootprint(TerrainData data, Vector2 center, float radius, float heightMeters)
    {
        int resolution = data.heightmapResolution;
        float samplesX = (resolution - 1f) / data.size.x;
        float samplesZ = (resolution - 1f) / data.size.z;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - radius * 1.5f) * samplesX), 0, resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((center.y - radius * 1.5f) * samplesZ), 0, resolution - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + radius * 1.5f) * samplesX), 0, resolution - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((center.y + radius * 1.5f) * samplesZ), 0, resolution - 1);
        var heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        float target = Mathf.Clamp01(heightMeters / data.size.y);
        for (int z = 0; z < heights.GetLength(0); z++)
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                float px = (x + x0) / samplesX, pz = (z + z0) / samplesZ;
                float distance = Vector2.Distance(new Vector2(px, pz), center);
                float blend = 1f - Mathf.SmoothStep(radius * .75f, radius * 1.5f, distance);
                heights[z, x] = Mathf.Lerp(heights[z, x], target, blend);
            }
        data.SetHeightsDelayLOD(x0, z0, heights);
        data.SyncHeightmap();
    }
}
