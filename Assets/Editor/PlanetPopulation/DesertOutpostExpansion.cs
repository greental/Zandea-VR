using System;
using System.IO;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DesertOutpostExpansion
{
    const string RequestPath = "Temp/DesertOutpostExpansion.request";
    const string FrameRequestPath = "Temp/DesertOutpostFrame.request";
    const string PrefabFolder = "Assets/MyAssets/prefabs/buildings/desertOutpost/";
    const string PreviewName = "Scavenger POIs (Scene Preview)";
    static double nextAttempt;

    static DesertOutpostExpansion()
    {
        if (File.Exists(RequestPath)) EditorApplication.update += RunRequested;
        if (File.Exists(FrameRequestPath)) EditorApplication.update += RunFrameRequested;
    }

    static void RunFrameRequested()
    {
        if (!File.Exists(FrameRequestPath)) { EditorApplication.update -= RunFrameRequested; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!FrameLarge("Large Scavenger Market Depot")) return;
        File.Delete(FrameRequestPath);
        EditorApplication.update -= RunFrameRequested;
    }

    [MenuItem("Tools/Koombar/Desert Outpost/Frame Large Market Depot")]
    public static void MenuFrameMarket() => FrameLarge("Large Scavenger Market Depot");

    [MenuItem("Tools/Koombar/Desert Outpost/Frame Large Fuel Yard")]
    public static void MenuFrameFuel() => FrameLarge("Large Scavenger Fuel Yard");

    static bool FrameLarge(string prefix)
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null || SceneManager.GetActiveScene().name != "PlanetPopulation_POC") return false;
        var planter = UnityEngine.Object.FindFirstObjectByType<ExistingTreadmillPoiPlanter>();
        var preview = planter ? planter.transform.Find(PreviewName) : null;
        if (!preview) return false;
        foreach (Transform child in preview)
        {
            if (!child.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            Selection.activeGameObject = child.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            return true;
        }
        return false;
    }

    static void RunRequested()
    {
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.timeSinceStartup < nextAttempt) return;
        nextAttempt = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (Build())
        {
            File.Delete(RequestPath);
            EditorApplication.update -= RunRequested;
        }
    }

    [MenuItem("Tools/Koombar/Desert Outpost/Detail Small And Add Large POIs")]
    public static void MenuBuild() => Build();

    public static bool Build()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null) return false;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "PlanetPopulation_POC")
            return false;
        var planter = UnityEngine.Object.FindFirstObjectByType<ExistingTreadmillPoiPlanter>();
        if (!planter || planter.gameObject.scene != scene || planter.sites == null || planter.sites.Count < 2)
            return false;
        var treadmill = planter.GetComponent<PlanetTerrainTreadmill>();
        var tileSet = treadmill ? treadmill.AuthoredTileSet : null;
        var preview = planter.transform.Find(PreviewName);
        if (!tileSet || tileSet.authoredTiles == null || !preview || preview.childCount < 2)
            return false;

        if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "Light_A.prefab"))
            DesertOutpostPrefabBuilder.BuildSplitLights();
        bool changed = DetailSmall(planter, treadmill, preview.GetChild(0));
        changed |= AddLarge(planter, treadmill, preview, new PlanetTileCoord(1, 0), 4301, "Large Scavenger Market Depot", false);
        changed |= AddLarge(planter, treadmill, preview, new PlanetTileCoord(1, 1), 4302, "Large Scavenger Fuel Yard", true);
        if (changed)
        {
            EditorUtility.SetDirty(planter);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[DesertOutpostExpansion] Detailed the small POI and authored two large POIs; existing medium placements were preserved.", planter);
        }
        bool firstLarge = false, secondLarge = false;
        foreach (var site in planter.sites)
        {
            if (site == null) continue;
            if (site.seed == 4301) firstLarge = true;
            if (site.seed == 4302) secondLarge = true;
        }
        return firstLarge && secondLarge && preview.GetChild(0).Find("Added Canopies and Camp Details");
    }

    static bool DetailSmall(ExistingTreadmillPoiPlanter planter, PlanetTerrainTreadmill treadmill, Transform small)
    {
        if (small.Find("Added Canopies and Camp Details")) return false;
        var data = FindData(treadmill, planter.sites[0].tile);
        if (!data) return false;
        var details = new GameObject("Added Canopies and Camp Details").transform;
        details.SetParent(small, false);
        Vector2 point = planter.sites[0].localPosition;
        Place(details, data, point, "CanopyYellow2", -6f, -10f, 15f, .75f);
        Place(details, data, point, "CanopyYellow", 18f, 7f, -25f, .6f);
        Place(details, data, point, "Crate", -11f, -11f, 20f, .65f);
        Place(details, data, point, "CrateSmall", -9.7f, -11.3f, -12f, .5f);
        Place(details, data, point, "crateOpen", -5.5f, -11.5f, 80f, .5f);
        Place(details, data, point, "CargoPallet", 18f, 4.5f, 75f, .7f);
        Place(details, data, point, "CrateGray", 19.4f, 4.5f, 20f, .45f);
        Place(details, data, point, "pipe", -9f, -4.5f, 90f, .5f);
        Place(details, data, point, "PipeCurve", -6.8f, -4.5f, 90f, .4f);
        Place(details, data, point, "Light_A", -2.5f, -14f, 0f, .75f);
        Place(details, data, point, "Light_B", 6f, 20f, 180f, .75f);
        return true;
    }

    static bool AddLarge(ExistingTreadmillPoiPlanter planter, PlanetTerrainTreadmill treadmill,
        Transform preview, PlanetTileCoord preferredTile, int seed, string name, bool fuelYard)
    {
        foreach (var existing in planter.sites)
            if (existing != null && existing.seed == seed) return false;
        PlanetTileCoord selectedTile = preferredTile;
        TerrainData data = FindData(treadmill, selectedTile);
        Vector2 point = default;
        float range = float.PositiveInfinity, targetHeight = 0;
        bool found = data && FindFlatPoint(data, out point, out range, out targetHeight);
        foreach (var existing in planter.sites)
            if (existing != null && existing.tile.Equals(selectedTile)) { found = false; break; }
        if (!found)
        {
            float bestScore = float.PositiveInfinity;
            foreach (var tile in treadmill.AuthoredTileSet.authoredTiles)
            {
                if (tile == null || !tile.terrainData) continue;
                int distance = Mathf.Max(Mathf.Abs(tile.coord.x - preferredTile.x), Mathf.Abs(tile.coord.z - preferredTile.z));
                if (distance > 2) continue;
                bool occupied = false;
                foreach (var existing in planter.sites)
                    if (existing != null && existing.tile.Equals(tile.coord)) { occupied = true; break; }
                if (occupied || !FindFlatPoint(tile.terrainData, out Vector2 candidate, out float candidateRange, out float candidateHeight)) continue;
                float score = candidateRange + distance * .2f;
                if (score >= bestScore) continue;
                bestScore = score;
                selectedTile = tile.coord;
                data = tile.terrainData;
                point = candidate;
                range = candidateRange;
                targetHeight = candidateHeight;
                found = true;
            }
        }
        if (!found)
        {
            Debug.LogWarning($"[DesertOutpostExpansion] No suitable 50 m footprint for {name} on tile {preferredTile.x},{preferredTile.z}.", planter);
            return false;
        }
        Undo.RegisterCompleteObjectUndo(data, "Gently grade large outpost");
        Grade(data, point, targetHeight);
        EditorUtility.SetDirty(data);
        float height = data.GetInterpolatedHeight(point.x / data.size.x, point.y / data.size.z);
        var site = new ExistingTreadmillPoiPlanter.Site
        {
            tile = selectedTile, localPosition = point,
            normalizedHeight = height / Mathf.Max(.001f, data.size.y),
            seed = seed, size = ExistingTreadmillPoiPlanter.Site.SiteSize.Large
        };
        Undo.RecordObject(planter, "Add large scavenger outpost");
        planter.sites.Add(site);
        Vector3 origin = treadmill.GetTileWorldPosition(selectedTile);
        foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true))
            if (terrain.terrainData == data) { origin = terrain.transform.position; break; }
        var siteRoot = new GameObject(name + $" - tile {selectedTile.x},{selectedTile.z}").transform;
        siteRoot.SetParent(preview, false);
        siteRoot.position = origin + new Vector3(point.x, height, point.y);
        if (fuelYard) BuildFuelYard(siteRoot, data, point);
        else BuildMarketDepot(siteRoot, data, point);
        AddNpcs(planter, siteRoot, data, point, fuelYard);
        Debug.Log($"[DesertOutpostExpansion] {name}: tile {selectedTile.x},{selectedTile.z}, 50 m height range {range:F2} m before grading.", planter);
        return true;
    }

    static void BuildMarketDepot(Transform root, TerrainData data, Vector2 p)
    {
        Place(root, data, p, "AdobeMainComplete", -17, 11, 90);
        Place(root, data, p, "AdobeMain", 17, 11, -90);
        Place(root, data, p, "AdobeLarge", -20, -17, 135);
        Place(root, data, p, "AdobeRound", 20, -18, -130);
        Place(root, data, p, "AdobeSmallDome", 3, 30, 180);
        Place(root, data, p, "canopyRed", 0, -8, 15, 1.05f);
        Place(root, data, p, "CanopyYellow2", -29, -1, 70, .8f);
        Place(root, data, p, "canopyGray", 28, -2, -80, .8f);
        Place(root, data, p, "Canopy", 0, 23, 0, .65f);
        Place(root, data, p, "WatchTower", -38, 27, 45);
        Place(root, data, p, "WatchTower", 38, 27, -45);
        Place(root, data, p, "Gate", 0, -41, 0, .9f);
        Place(root, data, p, "GateSide", -8, -40, 0, .8f);
        Place(root, data, p, "GateSide", 8, -40, 180, .8f);
        Walls(root, data, p);
        Place(root, data, p, "Tank", -31, 9, 0, .85f);
        Place(root, data, p, "Moisture_extractor", 33, 6, 180, .85f);
        Place(root, data, p, "AntenaCell", -32, 19, 0, .6f);
        Place(root, data, p, "Antena3", 30, 19, 0, .6f);
        PipeRun(root, data, p, -27, 7, 3);
        PipeRun(root, data, p, 26, 7, 2);
        Clutter(root, data, p, -26, -9);
        Clutter(root, data, p, 24, -8);
        Place(root, data, p, "Light_A", -9, -37, 0, .8f);
        Place(root, data, p, "Light_B", 9, -37, 180, .8f);
        Vehicle(root, data, p, "Pyxol2 Lean", 34, -29, 210);
        Vehicle(root, data, p, "Junk Hover Bike", -33, -28, 130);
    }

    static void BuildFuelYard(Transform root, TerrainData data, Vector2 p)
    {
        Place(root, data, p, "AdobeMain", 0, 20, 180);
        Place(root, data, p, "Adobe2", -23, 12, 90);
        Place(root, data, p, "AdobeRound", 25, 11, -90);
        Place(root, data, p, "AdobeLarge", -17, -18, 35);
        Place(root, data, p, "AdobeSmallDome", 17, -19, -35);
        Place(root, data, p, "CanopyYellow", 3, -3, 30, .85f);
        Place(root, data, p, "canopyGray", -31, -4, 80, .75f);
        Place(root, data, p, "canopyRed", 31, -5, -75, .75f);
        Place(root, data, p, "Canopy", 0, 34, 0, .6f);
        Place(root, data, p, "Tank", -30, 26, 0);
        Place(root, data, p, "Tank", -27, 27, 0);
        Place(root, data, p, "Moisture_extractor", 31, 24, 0);
        Place(root, data, p, "pipes", -25, 18, 0, .5f);
        Place(root, data, p, "pipes", 29, 18, 180, .5f);
        Place(root, data, p, "WatchTower", -39, 28, 45);
        Place(root, data, p, "WatchTower", 39, 28, -45);
        Place(root, data, p, "Gate", 0, -41, 0, .9f);
        Walls(root, data, p);
        PipeRun(root, data, p, -23, 5, 3);
        PipeRun(root, data, p, 24, 6, 3);
        Clutter(root, data, p, -28, -8);
        Clutter(root, data, p, 27, -7);
        Place(root, data, p, "Antena2", 31, 31, 0, .6f);
        Place(root, data, p, "Light_A", -10, -37, 0, .8f);
        Place(root, data, p, "Light_B", 10, -37, 180, .8f);
        Vehicle(root, data, p, "Pyxol2 Lean", 32, -29, 215);
        Vehicle(root, data, p, "Junk Hover Bike", -32, -29, 125);
    }

    static void Walls(Transform root, TerrainData data, Vector2 p)
    {
        for (int x = -29; x <= 29; x += 8)
            if (Mathf.Abs(x) > 8) Place(root, data, p, "WallSingle", x, 39, 0, .8f);
        for (int z = -25; z <= 23; z += 8)
        {
            Place(root, data, p, "WallSingle2", -43, z, 90, .8f);
            if (z != -9) Place(root, data, p, "WallSingle2", 43, z, 90, .8f);
        }
        Place(root, data, p, "WallSingle", -30, -41, 0, .8f);
        Place(root, data, p, "WallSingle", 30, -41, 0, .8f);
    }

    static void PipeRun(Transform root, TerrainData data, Vector2 p, float x, float z, int count)
    {
        for (int i = 0; i < count; i++) Place(root, data, p, "pipe", x, z + i * 2.1f, 90, .55f);
        Place(root, data, p, "PipeCurve", x, z + count * 2.1f, 90, .45f);
    }

    static void Clutter(Transform root, TerrainData data, Vector2 p, float x, float z)
    {
        Place(root, data, p, "CargoPallet", x, z, 25, .85f);
        Place(root, data, p, "Crate", x + 1.1f, z + .4f, 65, .6f);
        Place(root, data, p, "CrateGray", x - 1.1f, z + 1f, -20, .45f);
        Place(root, data, p, "CrateSmall", x + 1.5f, z + 1.7f, 90, .5f);
        Place(root, data, p, "crateOpen", x - 1f, z - 1.3f, 40, .5f);
    }

    static void AddNpcs(ExistingTreadmillPoiPlanter planter, Transform root, TerrainData data, Vector2 p, bool fuelYard)
    {
        if (!planter.faction || !planter.faction.npcPrefab) return;
        Vector2[] positions = fuelYard
            ? new[] { new Vector2(-12, 3), new Vector2(11, 4), new Vector2(-26, -13), new Vector2(26, -12), new Vector2(0, -25), new Vector2(-12, 27), new Vector2(13, 29), new Vector2(0, 10) }
            : new[] { new Vector2(-5, 5), new Vector2(7, 4), new Vector2(-24, -10), new Vector2(23, -11), new Vector2(0, -26), new Vector2(-30, 17), new Vector2(30, 17), new Vector2(1, 23) };
        foreach (var position in positions)
        {
            Vector3 world = GroundPoint(root, data, p, position.x, position.y);
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(planter.faction.npcPrefab.gameObject, root.gameObject.scene);
            npc.transform.SetParent(root, true);
            npc.transform.position = world;
        }
    }

    static void Vehicle(Transform root, TerrainData data, Vector2 p, string name, float x, float z, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            name == "Pyxol2 Lean" ? "Assets/MyAssets/prefabs/ground vehichles/Pyxol2 Lean.prefab" :
            "Assets/MyAssets/prefabs/ground vehichles/Junk Hover Bike.prefab");
        if (!prefab) return;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
        instance.transform.SetParent(root, true);
        Vector3 ground = GroundPoint(root, data, p, x, z);
        instance.transform.SetPositionAndRotation(ground, Quaternion.Euler(0, yaw, 0));
        ExistingTreadmillPoiPlanter.LiftVehicleToGround(instance, ground.y);
    }

    static void Place(Transform root, TerrainData data, Vector2 p, string prefabName,
        float x, float z, float yaw = 0f, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + prefabName + ".prefab");
        if (!prefab) { Debug.LogWarning("Missing outpost prefab: " + prefabName); return; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
        instance.transform.SetParent(root, true);
        Vector3 ground = GroundPoint(root, data, p, x, z);
        if (prefabName.StartsWith("Wall", StringComparison.Ordinal) || prefabName == "Gate" || prefabName == "GateSide")
        {
            float halfWidth = (prefabName == "Gate" ? 5f : prefabName == "GateSide" ? 2f : 4f) * scale;
            float radians = yaw * Mathf.Deg2Rad;
            float dx = Mathf.Cos(radians) * halfWidth, dz = -Mathf.Sin(radians) * halfWidth;
            ground.y = Mathf.Min(ground.y,
                GroundPoint(root, data, p, x + dx, z + dz).y,
                GroundPoint(root, data, p, x - dx, z - dz).y) - .15f;
        }
        instance.transform.SetPositionAndRotation(ground, Quaternion.Euler(0, yaw, 0));
        instance.transform.localScale = Vector3.one * scale;
    }

    static Vector3 GroundPoint(Transform root, TerrainData data, Vector2 p, float x, float z)
    {
        float y = data.GetInterpolatedHeight((p.x + x) / data.size.x, (p.y + z) / data.size.z);
        float center = data.GetInterpolatedHeight(p.x / data.size.x, p.y / data.size.z);
        return root.position + new Vector3(x, y - center, z);
    }

    static TerrainData FindData(PlanetTerrainTreadmill treadmill, PlanetTileCoord coord)
    {
        if (!treadmill || !treadmill.AuthoredTileSet) return null;
        foreach (var tile in treadmill.AuthoredTileSet.authoredTiles)
            if (tile != null && tile.coord.Equals(coord)) return tile.terrainData;
        return null;
    }

    static bool FindFlatPoint(TerrainData data, out Vector2 best, out float bestRange, out float height)
    {
        best = default;
        bestRange = float.PositiveInfinity;
        height = 0;
        float score = float.PositiveInfinity;
        for (float z = 96; z <= data.size.z - 96; z += 32)
            for (float x = 96; x <= data.size.x - 96; x += 32)
            {
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                for (int iz = -3; iz <= 3; iz++)
                    for (int ix = -3; ix <= 3; ix++)
                    {
                        if (ix * ix + iz * iz > 9) continue;
                        float h = data.GetInterpolatedHeight((x + ix * 16f) / data.size.x, (z + iz * 16f) / data.size.z);
                        low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                    }
                float range = high - low;
                if (range > 5f) continue;
                float candidateScore = range + .0003f * Vector2.Distance(new Vector2(x, z), new Vector2(data.size.x / 2f, data.size.z / 2f));
                if (candidateScore >= score) continue;
                score = candidateScore;
                bestRange = range;
                best = new Vector2(x, z);
                height = data.GetInterpolatedHeight(x / data.size.x, z / data.size.z);
            }
        return !float.IsPositiveInfinity(score);
    }

    static void Grade(TerrainData data, Vector2 point, float targetMeters)
    {
        int resolution = data.heightmapResolution;
        float sx = (resolution - 1f) / data.size.x, sz = (resolution - 1f) / data.size.z;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((point.x - 65f) * sx), 0, resolution - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((point.y - 65f) * sz), 0, resolution - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((point.x + 65f) * sx), 0, resolution - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((point.y + 65f) * sz), 0, resolution - 1);
        var heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        float target = Mathf.Clamp01(targetMeters / Mathf.Max(.001f, data.size.y));
        for (int z = 0; z < heights.GetLength(0); z++)
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                float radius = Vector2.Distance(new Vector2((x + x0) / sx, (z + z0) / sz), point);
                float blend = .78f * (1f - Mathf.SmoothStep(30f, 65f, radius));
                heights[z, x] = Mathf.Lerp(heights[z, x], target, blend);
            }
        data.SetHeightsDelayLOD(x0, z0, heights);
        data.SyncHeightmap();
    }
}
