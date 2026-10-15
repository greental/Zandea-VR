#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PlanetTerrainTreadmill))]
public sealed class PlanetTerrainTreadmillEditor : Editor
{
    const string RootFolder = "Assets/MyAssets/Terrain/PlanetTerrain";
    const string TileSetFolder = RootFolder + "/TileSets";
    const string GeneratedRootFolder = RootFolder + "/Generated";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var treadmill = (PlanetTerrainTreadmill)target;
        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Planet Terrain Authoring", EditorStyles.boldLabel);

        var authoredSet = treadmill.AuthoredTileSet;
        if (authoredSet && authoredSet.wrapCoordinates)
        {
            int missing = 0, wrongSize = 0;
            foreach (var coord in BuildWrappedCoords(authoredSet))
            {
                if (!authoredSet.TryGetTerrainData(coord, out var data)) { missing++; continue; }
                if (Mathf.Abs(data.size.x - treadmill.TileSizeMeters) > .01f ||
                    Mathf.Abs(data.size.z - treadmill.TileSizeMeters) > .01f) wrongSize++;
            }
            if (missing > 0 || wrongSize > 0)
                EditorGUILayout.HelpBox($"Wrapped tile set is incomplete or mis-sized: {missing} missing tile(s), {wrongSize} tile(s) with size different from {treadmill.TileSizeMeters:0.##} m. Generate Wrapped Planet Tile Set again to repair coverage and spacing.", MessageType.Error);
        }

        if (!treadmill.GetComponent<ExistingTreadmillPoiPlanter>())
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Add Existing Terrain POI Planter"))
                {
                    var planter = Undo.AddComponent<ExistingTreadmillPoiPlanter>(treadmill.gameObject);
                    var recipe = treadmill.GetComponent<PlanetPopulationPoc>();
                    planter.faction = recipe && recipe.faction ? recipe.faction :
                        AssetDatabase.LoadAssetAtPath<OutpostFactionTheme>("Assets/MyAssets/Terrain/PlanetTerrain/PopulationPOC/FrontierRaiders.asset");
                    if (recipe)
                    {
                        Undo.RecordObject(recipe, "Use existing terrain POI planter");
                        recipe.enabled = false;
                        EditorUtility.SetDirty(recipe);
                    }
                    EditorUtility.SetDirty(planter);
                }

        var population = treadmill.GetComponent<PlanetPopulationPoc>();
        var biomeTileSet = treadmill.AuthoredTileSet;
        string biomeTileSetPath = biomeTileSet ? AssetDatabase.GetAssetPath(biomeTileSet) : null;
        if (population && !string.IsNullOrEmpty(biomeTileSetPath) &&
            biomeTileSetPath.StartsWith("Assets/PlanetPopulation/Generated/"))
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Repaint Generated Biome Terrain"))
                    PlanetPopulationPocEditor.RepaintExistingBiomes(population, biomeTileSet, biomeTileSetPath);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Connect to Planet Approach"))
                    PlanetPopulationPocEditor.ConnectToPlanetApproach(treadmill);
        }

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generate Wrapped Planet Tile Set"))
                GenerateWrappedPlanet(treadmill, confirmOverwrite: true);

            if (GUILayout.Button("Paint / Debug Wrapped Planet Terrain"))
                PaintExistingWrappedPlanet(treadmill);

            if (GUILayout.Button("Make Existing Terrain Matte"))
                MakeExistingTerrainMatte(treadmill);

            if (GUILayout.Button("Bake Proxy Sphere Thumbnail POC"))
                PlanetProxyTerrainThumbnailBaker.BakeFromTreadmill(treadmill);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Legacy / Debug", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Generate Initial 3×3"))
                GenerateInitial3x3(treadmill, confirmOverwrite: true);

            if (GUILayout.Button("Paint / Debug Existing 3×3 Terrain"))
                PaintExisting3x3Terrain(treadmill);

            if (GUILayout.Button("Apply Screenshot Height Defaults"))
                ApplyScreenshotHeightDefaults();

            if (GUILayout.Button("Validate Height Seams"))
                PlanetTerrainSeamUtility.ValidateHeightSeams(treadmill.AuthoredTileSet, showDialog: true);

            if (GUILayout.Button("Repair Height Seams"))
            {
                if (EditorUtility.DisplayDialog(
                        "Repair Height Seams",
                        "This averages shared heightmap edges between authored neighbor tiles. Continue?",
                        "Repair",
                        "Cancel"))
                {
                    PlanetTerrainSeamUtility.RepairHeightSeams(treadmill.AuthoredTileSet);
                    PlanetTerrainSeamUtility.ValidateHeightSeams(treadmill.AuthoredTileSet, showDialog: true);
                }
            }
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Authoring actions are disabled while entering or running Play Mode.", MessageType.Info);
    }

    static void MakeExistingTerrainMatte(PlanetTerrainTreadmill treadmill)
    {
        var tileSet = treadmill.AuthoredTileSet;
        if (!tileSet || tileSet.generationSettings?.terrainLayers == null) return;
        string tileSetPath = AssetDatabase.GetAssetPath(tileSet);
        if (string.IsNullOrEmpty(tileSetPath)) return;
        string folder = System.IO.Path.GetDirectoryName(tileSetPath).Replace('\\', '/') + "/" + tileSet.name + "_MatteLayers";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
        var source = tileSet.generationSettings.terrainLayers;
        var matte = new TerrainLayer[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (!source[i]) continue;
            string path = $"{folder}/Layer_{i}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!layer)
            {
                layer = Object.Instantiate(source[i]);
                layer.name = source[i].name + " Matte";
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.maskMapTexture = null;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            var serialized = new SerializedObject(layer);
            var smoothnessSource = serialized.FindProperty("m_SmoothnessSource");
            if (smoothnessSource != null)
            {
                smoothnessSource.intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(layer);
            matte[i] = layer;
        }
        Undo.RecordObject(tileSet, "Make treadmill terrain matte");
        tileSet.generationSettings.terrainLayers = matte;
        EditorUtility.SetDirty(tileSet);
        if (tileSet.authoredTiles != null)
            foreach (var tile in tileSet.authoredTiles)
                if (tile?.terrainData != null)
                {
                    tile.terrainData.terrainLayers = matte;
                    tile.terrainData.SetBaseMapDirty();
                    EditorUtility.SetDirty(tile.terrainData);
                }
        var serializedTreadmill = new SerializedObject(treadmill);
        var terrainLayers = serializedTreadmill.FindProperty("terrainLayers");
        terrainLayers.arraySize = matte.Length;
        for (int i = 0; i < matte.Length; i++)
            terrainLayers.GetArrayElementAtIndex(i).objectReferenceValue = matte[i];
        serializedTreadmill.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true)) terrain.Flush();
        SceneView.RepaintAll();
        Debug.Log($"Created matte layer copies for {tileSet.authoredTiles?.Count ?? 0} existing treadmill tiles. Height and paint data were preserved.", treadmill);
    }

    static void GenerateWrappedPlanet(PlanetTerrainTreadmill treadmill, bool confirmOverwrite)
    {
        EnsureFolders(treadmill);
        PlanetTerrainTileSet tileSet = EnsureTileSet(treadmill);
        var coords = BuildWrappedCoords(tileSet);
        if (confirmOverwrite && AnyTerrainDataExists(treadmill, coords))
        {
            bool ok = EditorUtility.DisplayDialog(
                "Generate Wrapped Planet Tile Set",
                $"This will generate/overwrite {coords.Count} baked colored TerrainData asset(s) for this treadmill only, in:\n\n{TerrainDataFolder(treadmill)}\n\nOther planet/moon treadmill folders are not touched. Continue?",
                "Generate / Overwrite",
                "Cancel");
            if (!ok)
                return;
        }

        var settings = GetCompactGenerationSettings(treadmill);
        int originalResolution = treadmill.GenerationSettings.heightmapResolution;
        var treadmillSettings = new SerializedObject(treadmill);
        treadmillSettings.FindProperty("heightmapResolution").intValue = settings.heightmapResolution;
        treadmillSettings.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[PlanetTerrainAuthoring] Baking wrapped {tileSet.wrapWidth}×{tileSet.wrapHeight} planet with {CountAssignedLayers(settings.terrainLayers)} configured TerrainLayer(s): {DescribeLayers(settings.terrainLayers)}", treadmill);
        string stagingFolder = TerrainDataFolder(treadmill) + "/_BakeStaging_" + System.Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder(TerrainDataFolder(treadmill), System.IO.Path.GetFileName(stagingFolder));
        var staged = new Dictionary<PlanetTileCoord, TerrainData>();

        var sceneTerrains = new Dictionary<PlanetTileCoord, Terrain>();
        int generated = 0;
        bool committed = false;
        try
        {
            for (int i = 0; i < coords.Count; i++)
            {
                PlanetTileCoord coord = coords[i];
                if (EditorUtility.DisplayCancelableProgressBar("Generate Wrapped Planet Terrain", $"Generating tile {coord} ({i + 1}/{coords.Count})", (i + 1) / (float)coords.Count))
                {
                    Debug.Log("[PlanetTerrainAuthoring] Wrapped bake canceled; previous tile set retained.", treadmill);
                    return;
                }

                TerrainData data = treadmill.CreateGeneratedTerrainData(coord);
                data.name = $"PT_Tile_x{coord.x}_z{coord.z}";
                data.size = new Vector3(settings.tileSizeMeters, settings.maxHeightMeters, settings.tileSizeMeters);
                AssetDatabase.CreateAsset(data, $"{stagingFolder}/Tile_{coord.x}_{coord.z}.asset");
                staged.Add(coord, data);
            }

            Undo.RegisterCompleteObjectUndo(treadmill.gameObject, "Generate Wrapped Planet Terrain");
            Undo.RecordObject(tileSet, "Generate Wrapped Planet Terrain Tile Set");
            tileSet.wrapCoordinates = true;
            tileSet.generationSettings = settings;
            tileSet.authoredTiles.Clear();
            for (int i = 0; i < coords.Count; i++)
            {
                PlanetTileCoord coord = coords[i];
                TerrainData data = staged[coord];

                string assetPath = TerrainDataPath(treadmill, coord);
                TerrainData existing = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(data, existing);
                    existing.terrainLayers = settings.terrainLayers;
                    existing.size = new Vector3(settings.tileSizeMeters, settings.maxHeightMeters, settings.tileSizeMeters);
                    existing.SetBaseMapDirty();
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(data), assetPath))
                        throw new System.IO.IOException("Could not save wrapped terrain tile " + assetPath);
                }

                TerrainData savedData = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
                if (savedData != null)
                {
                    savedData.terrainLayers = settings.terrainLayers;
                    savedData.size = new Vector3(settings.tileSizeMeters, settings.maxHeightMeters, settings.tileSizeMeters);
                    savedData.SetBaseMapDirty();
                    EditorUtility.SetDirty(savedData);
                    tileSet.SetTerrainData(coord, savedData);

                    Terrain terrain = CreateOrUpdateSceneTerrain(treadmill, coord, savedData);
                    sceneTerrains[coord] = terrain;
                    generated++;
                }
            }
            committed = generated == coords.Count;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.DeleteAsset(stagingFolder);
            if (!committed)
            {
                var restoreSettings = new SerializedObject(treadmill);
                restoreSettings.FindProperty("heightmapResolution").intValue = originalResolution;
                restoreSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (!committed) throw new System.InvalidOperationException($"Wrapped bake saved only {generated}/{coords.Count} tiles; tile set is incomplete.");

        RebuildSceneTerrainNeighbors(sceneTerrains, tileSet);
        PlanetTerrainSeamUtility.RepairHeightSeams(tileSet);
        PlanetTerrainSeamUtility.BlendAlphamapSeams(tileSet);
        PlanetTerrainSeamUtility.ValidateHeightSeams(tileSet, showDialog: false);

        EditorUtility.SetDirty(tileSet);
        EditorUtility.SetDirty(treadmill);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(treadmill.gameObject.scene);

        Debug.Log($"[PlanetTerrainAuthoring] Generated {generated}/{coords.Count} wrapped baked terrain tile(s) and updated '{AssetDatabase.GetAssetPath(tileSet)}'. Runtime traversal should now use baked colored wrapped tiles instead of live generation.", treadmill);
    }

    static void PaintExistingWrappedPlanet(PlanetTerrainTreadmill treadmill)
    {
        PlanetTerrainTileSet tileSet = treadmill.AuthoredTileSet;
        if (tileSet == null)
        {
            EditorUtility.DisplayDialog("Paint Wrapped Planet Terrain", "No Authored Tile Set is assigned on this PlanetTerrainTreadmill.", "OK");
            return;
        }

        tileSet.generationSettings = GetCompactGenerationSettings(treadmill);
        TerrainLayer[] layers = tileSet.generationSettings.terrainLayers;
        int layerCount = CountAssignedLayers(layers);
        if (layerCount == 0)
        {
            EditorUtility.DisplayDialog("Paint Wrapped Planet Terrain", "No Terrain Layers are configured on the treadmill or authored tile set.", "OK");
            return;
        }

        var coords = BuildWrappedCoords(tileSet);
        Debug.Log($"[PlanetTerrainPaint] Repainting wrapped {tileSet.wrapWidth}×{tileSet.wrapHeight} planet with {layerCount} TerrainLayer(s): {DescribeLayers(layers)}", treadmill);

        int painted = 0;
        var sceneTerrains = new Dictionary<PlanetTileCoord, Terrain>();
        try
        {
            for (int i = 0; i < coords.Count; i++)
            {
                PlanetTileCoord coord = coords[i];
                if (EditorUtility.DisplayCancelableProgressBar("Paint Wrapped Planet Terrain", $"Painting tile {coord} ({i + 1}/{coords.Count})", (i + 1) / (float)coords.Count))
                    break;

                TerrainData data = null;
                if (tileSet.TryGetTerrainData(coord, out TerrainData authoredData))
                    data = authoredData;

                if (data == null)
                    data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath(treadmill, coord));

                if (data == null)
                {
                    Debug.LogWarning($"[PlanetTerrainPaint] Tile {coord}: no TerrainData found in tile set or at '{TerrainDataPath(treadmill, coord)}'. Generate Wrapped Planet Tile Set first.", treadmill);
                    continue;
                }

                Undo.RecordObject(data, "Paint Wrapped Planet Alphamaps");
                data.terrainLayers = layers;
                float[,,] alphamaps = treadmill.BuildGeneratedAlphamaps(coord);
                PlanetTerrainGenerator.ApplyAlphamaps(data, alphamaps);
                data.SetBaseMapDirty();
                EditorUtility.SetDirty(data);

                Terrain terrain = CreateOrUpdateSceneTerrain(treadmill, coord, data);
                LogTerrainRenderState(coord, terrain);
                sceneTerrains[coord] = terrain;
                painted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        RebuildSceneTerrainNeighbors(sceneTerrains, tileSet);
        PlanetTerrainSeamUtility.BlendAlphamapSeams(tileSet);
        EditorUtility.SetDirty(tileSet);
        EditorUtility.SetDirty(treadmill);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(treadmill.gameObject.scene);

        Debug.Log($"[PlanetTerrainPaint] Painted {painted}/{coords.Count} wrapped authored terrain tile(s).", treadmill);
    }

    static void GenerateInitial3x3(PlanetTerrainTreadmill treadmill, bool confirmOverwrite)
    {
        EnsureFolders(treadmill);

        PlanetTerrainTileSet tileSet = EnsureTileSet(treadmill);

        var coords = BuildInitial3x3Coords();
        if (confirmOverwrite && AnyTerrainDataExists(treadmill, coords))
        {
            bool ok = EditorUtility.DisplayDialog(
                "Generate Initial 3×3",
                $"One or more generated TerrainData assets already exist for this treadmill in:\n\n{TerrainDataFolder(treadmill)}\n\nThis will overwrite only this treadmill's initial 3×3 region. Continue?",
                "Generate / Overwrite",
                "Cancel");
            if (!ok)
                return;
        }

        Undo.RegisterCompleteObjectUndo(treadmill.gameObject, "Generate Planet Terrain 3x3");
        tileSet.generationSettings = GetCompactGenerationSettings(treadmill);
        Debug.Log($"[PlanetTerrainAuthoring] Baking with {CountAssignedLayers(tileSet.generationSettings.terrainLayers)} configured TerrainLayer(s): {DescribeLayers(tileSet.generationSettings.terrainLayers)}", treadmill);

        var sceneTerrains = new Dictionary<PlanetTileCoord, Terrain>();
        foreach (PlanetTileCoord coord in coords)
        {
            TerrainData data = treadmill.CreateGeneratedTerrainData(coord);
            data.name = $"PT_Tile_x{coord.x}_z{coord.z}";
            Debug.Log($"[PlanetTerrainAuthoring] Tile {coord} alphamap coverage: {PlanetTerrainGenerator.SummarizeAlphamapCoverage(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight))}", treadmill);

            string assetPath = TerrainDataPath(treadmill, coord);
            TerrainData existing = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                existing.terrainLayers = tileSet.generationSettings.terrainLayers;
                existing.SetBaseMapDirty();
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(data, assetPath);
                EditorUtility.SetDirty(data);
            }

            TerrainData savedData = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
            if (savedData != null)
            {
                savedData.terrainLayers = tileSet.generationSettings.terrainLayers;
                savedData.SetBaseMapDirty();
                EditorUtility.SetDirty(savedData);
            }
            tileSet.SetTerrainData(coord, savedData);

            Terrain terrain = CreateOrUpdateSceneTerrain(treadmill, coord, savedData);
            sceneTerrains[coord] = terrain;
        }

        RebuildSceneTerrainNeighbors(sceneTerrains);

        EditorUtility.SetDirty(tileSet);
        EditorUtility.SetDirty(treadmill);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(treadmill.gameObject.scene);

        Debug.Log($"[PlanetTerrainAuthoring] Generated initial 3×3 authored terrain and updated '{AssetDatabase.GetAssetPath(tileSet)}'.", treadmill);
    }

    static void PaintExisting3x3Terrain(PlanetTerrainTreadmill treadmill)
    {
        PlanetTerrainTileSet tileSet = treadmill.AuthoredTileSet;
        if (tileSet == null)
        {
            EditorUtility.DisplayDialog("Paint Planet Terrain", "No Authored Tile Set is assigned on this PlanetTerrainTreadmill.", "OK");
            return;
        }

        tileSet.generationSettings = GetCompactGenerationSettings(treadmill);
        TerrainLayer[] layers = tileSet.generationSettings.terrainLayers;
        int layerCount = CountAssignedLayers(layers);
        if (layerCount == 0)
        {
            EditorUtility.DisplayDialog("Paint Planet Terrain", "No Terrain Layers are configured on the treadmill or authored tile set.", "OK");
            return;
        }

        Debug.Log($"[PlanetTerrainPaint] Repainting existing initial 3×3 with {layerCount} TerrainLayer(s): {DescribeLayers(layers)}", treadmill);

        int painted = 0;
        var sceneTerrains = new Dictionary<PlanetTileCoord, Terrain>();
        foreach (PlanetTileCoord coord in BuildInitial3x3Coords())
        {
            TerrainData data = null;
            if (tileSet.TryGetTerrainData(coord, out TerrainData authoredData))
                data = authoredData;

            if (data == null)
                data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath(treadmill, coord));

            if (data == null)
            {
                Debug.LogWarning($"[PlanetTerrainPaint] Tile {coord}: no TerrainData found in tile set or at '{TerrainDataPath(treadmill, coord)}'. Generate Initial 3×3 first.", treadmill);
                continue;
            }

            Undo.RecordObject(data, "Paint Planet Terrain Alphamaps");
            data.terrainLayers = layers;
            Debug.Log($"[PlanetTerrainPaint] Tile {coord}: before paint data='{data.name}', size={data.size}, heightmap={data.heightmapResolution}, alphamap={data.alphamapWidth}x{data.alphamapHeight}x{data.alphamapLayers}, runtime layers={DescribeLayers(data.terrainLayers)}", data);

            float[,,] alphamaps = treadmill.BuildGeneratedAlphamaps(coord);
            PlanetTerrainGenerator.ApplyAlphamaps(data, alphamaps);
            data.SetBaseMapDirty();
            EditorUtility.SetDirty(data);

            float[,,] stored = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            Debug.Log($"[PlanetTerrainPaint] Tile {coord}: after paint coverage: {PlanetTerrainGenerator.SummarizeAlphamapCoverage(stored)}", data);

            Terrain terrain = CreateOrUpdateSceneTerrain(treadmill, coord, data);
            LogTerrainRenderState(coord, terrain);
            sceneTerrains[coord] = terrain;
            painted++;
        }

        RebuildSceneTerrainNeighbors(sceneTerrains);
        EditorUtility.SetDirty(tileSet);
        EditorUtility.SetDirty(treadmill);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(treadmill.gameObject.scene);

        Debug.Log($"[PlanetTerrainPaint] Painted {painted} existing authored terrain tile(s). If coverage is multi-layer here but the scene is still one color, inspect the Terrain material/shader/render-pipeline settings logged above.", treadmill);
    }

    void ApplyScreenshotHeightDefaults()
    {
        serializedObject.Update();
        SerializedProperty heightProvider = serializedObject.FindProperty("heightProvider");
        if (heightProvider == null)
        {
            Debug.LogWarning("[PlanetTerrainAuthoring] Could not find serialized heightProvider property.", target);
            return;
        }

        SetRelativeInt(heightProvider, "seed", 42);
        SetRelativeFloat(heightProvider, "baseHeight01", 0.28f);
        SetRelativeFloat(heightProvider, "mountainAmplitude01", 0.62f);
        SetRelativeFloat(heightProvider, "macroScaleMeters", 1400f);
        SetRelativeFloat(heightProvider, "mountainScaleMeters", 4300f);
        SetRelativeFloat(heightProvider, "ridgeScaleMeters", 520f);
        SetRelativeFloat(heightProvider, "canyonScaleMeters", 7600f);
        SetRelativeFloat(heightProvider, "valleyScaleMeters", 6800f);
        SetRelativeFloat(heightProvider, "detailScaleMeters", 260f);
        serializedObject.ApplyModifiedProperties();

        Debug.Log("[PlanetTerrainAuthoring] Applied screenshot height defaults: seed=42, base=0.28, mountainAmplitude=0.62, macro=1400, mountain=4300, ridge=520, canyon=7600, valley=6800, detail=260.", target);
    }

    static void SetRelativeFloat(SerializedProperty parent, string relativeName, float value)
    {
        SerializedProperty property = parent.FindPropertyRelative(relativeName);
        if (property != null)
            property.floatValue = value;
    }

    static void SetRelativeInt(SerializedProperty parent, string relativeName, int value)
    {
        SerializedProperty property = parent.FindPropertyRelative(relativeName);
        if (property != null)
            property.intValue = value;
    }

    static List<PlanetTileCoord> BuildInitial3x3Coords()
    {
        var coords = new List<PlanetTileCoord>(9);
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
                coords.Add(new PlanetTileCoord(x, z));
        }
        return coords;
    }

    static List<PlanetTileCoord> BuildWrappedCoords(PlanetTerrainTileSet tileSet)
    {
        int width = Mathf.Max(1, tileSet != null ? tileSet.wrapWidth : 1);
        int height = Mathf.Max(1, tileSet != null ? tileSet.wrapHeight : 1);
        var coords = new List<PlanetTileCoord>(width * height);
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
                coords.Add(new PlanetTileCoord(x, z));
        }
        return coords;
    }

    static PlanetTerrainTileSet EnsureTileSet(PlanetTerrainTreadmill treadmill)
    {
        PlanetTerrainTileSet tileSet = treadmill.AuthoredTileSet;
        string tileSetPath = TileSetPath(treadmill);
        if (tileSet != null && AssetDatabase.GetAssetPath(tileSet) == tileSetPath)
            return tileSet;

        PlanetTerrainTileSet scopedTileSet = AssetDatabase.LoadAssetAtPath<PlanetTerrainTileSet>(tileSetPath);
        if (scopedTileSet == null)
        {
            scopedTileSet = ScriptableObject.CreateInstance<PlanetTerrainTileSet>();
            if (tileSet != null)
            {
                scopedTileSet.wrapWidth = tileSet.wrapWidth;
                scopedTileSet.wrapHeight = tileSet.wrapHeight;
                scopedTileSet.wrapCoordinates = tileSet.wrapCoordinates;
                scopedTileSet.generationSettings = tileSet.generationSettings;
            }

            AssetDatabase.CreateAsset(scopedTileSet, tileSetPath);
        }

        Undo.RecordObject(treadmill, "Assign Planet Terrain Tile Set");
        treadmill.SetAuthoredTileSet(scopedTileSet);
        EditorUtility.SetDirty(treadmill);
        return scopedTileSet;
    }

    static PlanetTerrainGenerationSettings GetCompactGenerationSettings(PlanetTerrainTreadmill treadmill)
    {
        PlanetTerrainGenerationSettings settings = treadmill.GenerationSettings;
        int requested = Mathf.Clamp(settings.heightmapResolution, 33, 4097);
        settings.heightmapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(requested - 1) + 1, 33, 4097);
        settings.terrainLayers = PlanetTerrainGenerator.CompactTerrainLayers(settings.terrainLayers);
        return settings;
    }

    static Terrain CreateOrUpdateSceneTerrain(PlanetTerrainTreadmill treadmill, PlanetTileCoord coord, TerrainData data)
    {
        string name = $"PT_AuthoredTile_x{coord.x}_z{coord.z}";
        Transform existing = treadmill.transform.Find(name);
        GameObject go;
        if (existing != null)
            go = existing.gameObject;
        else
        {
            go = Terrain.CreateTerrainGameObject(data);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create Authored Planet Terrain Tile");
            go.transform.SetParent(treadmill.transform, true);
        }

        go.transform.position = treadmill.GetTileWorldPosition(coord);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        Terrain terrain = go.GetComponent<Terrain>();
        TerrainCollider collider = go.GetComponent<TerrainCollider>();
        terrain.terrainData = null;
        terrain.terrainData = data;
        terrain.Flush();
        if (collider != null)
            collider.terrainData = data;
        EditorUtility.SetDirty(go);
        return terrain;
    }

    static void RebuildSceneTerrainNeighbors(Dictionary<PlanetTileCoord, Terrain> terrains)
    {
        RebuildSceneTerrainNeighbors(terrains, null);
    }

    static void RebuildSceneTerrainNeighbors(Dictionary<PlanetTileCoord, Terrain> terrains, PlanetTerrainTileSet tileSet)
    {
        foreach (var kv in terrains)
        {
            PlanetTileCoord c = kv.Key;
            Terrain terrain = kv.Value;
            PlanetTileCoord leftCoord = tileSet != null ? tileSet.WestOf(c) : new PlanetTileCoord(c.x - 1, c.z);
            PlanetTileCoord topCoord = tileSet != null ? tileSet.NorthOf(c) : new PlanetTileCoord(c.x, c.z + 1);
            PlanetTileCoord rightCoord = tileSet != null ? tileSet.EastOf(c) : new PlanetTileCoord(c.x + 1, c.z);
            PlanetTileCoord bottomCoord = tileSet != null ? tileSet.SouthOf(c) : new PlanetTileCoord(c.x, c.z - 1);
            terrains.TryGetValue(leftCoord, out Terrain left);
            terrains.TryGetValue(topCoord, out Terrain top);
            terrains.TryGetValue(rightCoord, out Terrain right);
            terrains.TryGetValue(bottomCoord, out Terrain bottom);
            terrain.SetNeighbors(left, top, right, bottom);
            EditorUtility.SetDirty(terrain);
        }
    }

    static bool AnyTerrainDataExists(PlanetTerrainTreadmill treadmill, IEnumerable<PlanetTileCoord> coords)
    {
        foreach (PlanetTileCoord coord in coords)
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath(treadmill, coord)) != null)
                return true;
        }
        return false;
    }

    static string TileSetPath(PlanetTerrainTreadmill treadmill) => $"{TileSetFolder}/{TreadmillAssetScopeName(treadmill)}_TileSet.asset";

    static string TerrainDataFolder(PlanetTerrainTreadmill treadmill) => $"{GeneratedRootFolder}/{TreadmillAssetScopeName(treadmill)}/TerrainData";

    static string TerrainDataPath(PlanetTerrainTreadmill treadmill, PlanetTileCoord coord) => $"{TerrainDataFolder(treadmill)}/PT_Tile_x{coord.x}_z{coord.z}.asset";

    static string TreadmillAssetScopeName(PlanetTerrainTreadmill treadmill)
    {
        if (treadmill == null)
            return "PlanetTerrainTreadmill";

        string baseName = treadmill.gameObject.name;
        PlanetSystemRig rig = treadmill.GetComponentInParent<PlanetSystemRig>();
        if (rig != null)
            baseName = rig.gameObject.name + "_" + baseName;
        else if (treadmill.transform.parent != null)
            baseName = treadmill.transform.parent.name + "_" + baseName;

        string safeName = SanitizeAssetName(baseName);
        if (string.IsNullOrEmpty(safeName))
            safeName = "PlanetTerrainTreadmill";

        GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(treadmill.gameObject);
        string objectId = globalObjectId.targetObjectId.ToString("x");
        if (!string.IsNullOrEmpty(objectId) && objectId != "0")
            safeName += "_" + objectId;

        return safeName;
    }

    static string SanitizeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }

        return builder.ToString().Trim('_');
    }

    static void LogTerrainRenderState(PlanetTileCoord coord, Terrain terrain)
    {
        if (terrain == null)
            return;

        TerrainData data = terrain.terrainData;
        Material material = terrain.materialTemplate;
        string materialText = material != null ? $"'{material.name}' shader='{material.shader?.name ?? "null"}'" : "default terrain material";
        Debug.Log($"[PlanetTerrainPaint] Tile {coord}: scene Terrain='{terrain.name}', data='{data?.name ?? "null"}', layers={DescribeLayers(data?.terrainLayers)}, material={materialText}, drawInstanced={terrain.drawInstanced}, basemapDistance={terrain.basemapDistance:0.0}", terrain);
    }

    static string DescribeLayers(TerrainLayer[] layers)
    {
        if (layers == null || layers.Length == 0)
            return "none";

        string text = string.Empty;
        for (int i = 0; i < layers.Length; i++)
        {
            if (i > 0)
                text += ", ";
            text += layers[i] != null ? $"L{i}='{layers[i].name}'" : $"L{i}=null";
        }
        return text;
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

    static void EnsureFolders()
    {
        EnsureFolder("Assets/MyAssets", "Terrain");
        EnsureFolder("Assets/MyAssets/Terrain", "PlanetTerrain");
        EnsureFolder(RootFolder, "TileSets");
        EnsureFolder(RootFolder, "Generated");
    }

    static void EnsureFolders(PlanetTerrainTreadmill treadmill)
    {
        EnsureFolders();
        string scopeName = TreadmillAssetScopeName(treadmill);
        EnsureFolder(GeneratedRootFolder, scopeName);
        EnsureFolder($"{GeneratedRootFolder}/{scopeName}", "TerrainData");
    }

    static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }
}

public static class PlanetTerrainSeamUtility
{
    const float HeightToleranceMeters = 0.01f;
    const int HeightBlendSamples = 8;
    const int AlphamapBlendTexels = 24;

    public static void ValidateHeightSeams(PlanetTerrainTileSet tileSet, bool showDialog)
    {
        if (tileSet == null)
        {
            EditorUtility.DisplayDialog("Planet Terrain Seams", "No authored tile set assigned.", "OK");
            return;
        }

        int checkedEdges = 0;
        int brokenEdges = 0;
        float worstDelta = 0f;

        foreach (AuthoredTerrainTileRecord record in tileSet.authoredTiles)
        {
            if (record == null || record.terrainData == null)
                continue;

            CheckPair(tileSet, record.coord, tileSet.EastOf(record.coord), eastWest: true, ref checkedEdges, ref brokenEdges, ref worstDelta);
            CheckPair(tileSet, record.coord, tileSet.NorthOf(record.coord), eastWest: false, ref checkedEdges, ref brokenEdges, ref worstDelta);
        }

        string msg = $"Checked {checkedEdges} authored neighbor edge(s). Broken: {brokenEdges}. Worst delta: {worstDelta:0.000} m.";
        Debug.Log("[PlanetTerrainSeams] " + msg, tileSet);
        if (showDialog)
            EditorUtility.DisplayDialog("Planet Terrain Height Seams", msg, "OK");
    }

    public static void RepairHeightSeams(PlanetTerrainTileSet tileSet)
    {
        if (tileSet == null)
            return;

        foreach (AuthoredTerrainTileRecord record in tileSet.authoredTiles)
        {
            if (record == null || record.terrainData == null)
                continue;

            RepairPair(tileSet, record.coord, tileSet.EastOf(record.coord), eastWest: true);
            RepairPair(tileSet, record.coord, tileSet.NorthOf(record.coord), eastWest: false);
        }

        AssetDatabase.SaveAssets();
    }

    public static void BlendAlphamapSeams(PlanetTerrainTileSet tileSet)
    {
        if (tileSet == null)
            return;

        foreach (AuthoredTerrainTileRecord record in tileSet.authoredTiles)
        {
            if (record == null || record.terrainData == null)
                continue;

            BlendAlphamapPair(tileSet, record.coord, tileSet.EastOf(record.coord), eastWest: true);
            BlendAlphamapPair(tileSet, record.coord, tileSet.NorthOf(record.coord), eastWest: false);
        }

        AssetDatabase.SaveAssets();
    }

    static void CheckPair(PlanetTerrainTileSet tileSet, PlanetTileCoord aCoord, PlanetTileCoord bCoord, bool eastWest, ref int checkedEdges, ref int brokenEdges, ref float worstDelta)
    {
        if (!tileSet.TryGetTerrainData(aCoord, out TerrainData a) || !tileSet.TryGetTerrainData(bCoord, out TerrainData b))
            return;
        if (a.heightmapResolution != b.heightmapResolution)
            return;

        checkedEdges++;
        int res = a.heightmapResolution;
        float[,] ah = a.GetHeights(0, 0, res, res);
        float[,] bh = b.GetHeights(0, 0, res, res);
        float maxHeight = Mathf.Max(a.size.y, b.size.y);
        float edgeWorst = 0f;
        for (int i = 0; i < res; i++)
        {
            float av = eastWest ? ah[i, res - 1] : ah[res - 1, i];
            float bv = eastWest ? bh[i, 0] : bh[0, i];
            edgeWorst = Mathf.Max(edgeWorst, Mathf.Abs(av - bv) * maxHeight);
        }

        worstDelta = Mathf.Max(worstDelta, edgeWorst);
        if (edgeWorst > HeightToleranceMeters)
            brokenEdges++;
    }

    static void RepairPair(PlanetTerrainTileSet tileSet, PlanetTileCoord aCoord, PlanetTileCoord bCoord, bool eastWest)
    {
        if (!tileSet.TryGetTerrainData(aCoord, out TerrainData a) || !tileSet.TryGetTerrainData(bCoord, out TerrainData b))
            return;
        if (a.heightmapResolution != b.heightmapResolution)
            return;

        int res = a.heightmapResolution;
        int blendSamples = Mathf.Clamp(HeightBlendSamples, 1, res / 2);
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

        Undo.RegisterCompleteObjectUndo(a, "Repair Planet Terrain Height Seam");
        if (!sameTerrainData)
            Undo.RegisterCompleteObjectUndo(b, "Repair Planet Terrain Height Seam");
        a.SetHeights(0, 0, ah);
        if (!sameTerrainData)
            b.SetHeights(0, 0, bh);
        EditorUtility.SetDirty(a);
        if (!sameTerrainData)
            EditorUtility.SetDirty(b);
    }

    static void BlendAlphamapPair(PlanetTerrainTileSet tileSet, PlanetTileCoord aCoord, PlanetTileCoord bCoord, bool eastWest)
    {
        if (!tileSet.TryGetTerrainData(aCoord, out TerrainData a) || !tileSet.TryGetTerrainData(bCoord, out TerrainData b))
            return;

        if (a.alphamapWidth != b.alphamapWidth || a.alphamapHeight != b.alphamapHeight || a.alphamapLayers != b.alphamapLayers)
            return;

        int width = a.alphamapWidth;
        int height = a.alphamapHeight;
        int layers = a.alphamapLayers;
        if (width <= 0 || height <= 0 || layers <= 0)
            return;

        int blendTexels = Mathf.Clamp(AlphamapBlendTexels, 1, (eastWest ? width : height) / 2);
        float[,,] am = a.GetAlphamaps(0, 0, width, height);
        bool sameTerrainData = ReferenceEquals(a, b);
        float[,,] bm = sameTerrainData ? am : b.GetAlphamaps(0, 0, width, height);

        int span = eastWest ? height : width;
        for (int i = 0; i < span; i++)
        {
            for (int band = 0; band < blendTexels; band++)
            {
                float t = blendTexels <= 1 ? 0f : band / (float)(blendTexels - 1);
                float edgeWeight = 1f - Smooth01(t);

                int ax = eastWest ? width - 1 - band : i;
                int az = eastWest ? i : height - 1 - band;
                int bx = eastWest ? band : i;
                int bz = eastWest ? i : band;

                for (int layer = 0; layer < layers; layer++)
                {
                    float avg = (am[az, ax, layer] + bm[bz, bx, layer]) * 0.5f;
                    am[az, ax, layer] = Mathf.Lerp(am[az, ax, layer], avg, edgeWeight);
                    bm[bz, bx, layer] = Mathf.Lerp(bm[bz, bx, layer], avg, edgeWeight);
                }

                NormalizeAlphamapTexel(am, az, ax, layers);
                NormalizeAlphamapTexel(bm, bz, bx, layers);
            }
        }

        Undo.RegisterCompleteObjectUndo(a, "Blend Planet Terrain Alphamap Seam");
        if (!sameTerrainData)
            Undo.RegisterCompleteObjectUndo(b, "Blend Planet Terrain Alphamap Seam");
        a.SetAlphamaps(0, 0, am);
        if (!sameTerrainData)
            b.SetAlphamaps(0, 0, bm);
        a.SetBaseMapDirty();
        if (!sameTerrainData)
            b.SetBaseMapDirty();
        EditorUtility.SetDirty(a);
        if (!sameTerrainData)
            EditorUtility.SetDirty(b);
    }

    static void NormalizeAlphamapTexel(float[,,] maps, int z, int x, int layers)
    {
        float total = 0f;
        for (int layer = 0; layer < layers; layer++)
            total += maps[z, x, layer];

        if (total <= 0.0001f)
        {
            maps[z, x, 0] = 1f;
            for (int layer = 1; layer < layers; layer++)
                maps[z, x, layer] = 0f;
            return;
        }

        for (int layer = 0; layer < layers; layer++)
            maps[z, x, layer] /= total;
    }

    static float Smooth01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }
}
#endif
