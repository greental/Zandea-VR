using System.Collections.Generic;
using MyAssets.PlanetTerrain;
using MyAssets.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PlanetPopulationPoc))]
public sealed class PlanetPopulationPocEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        var biomeList = serializedObject.FindProperty("allowedBiomes");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Biome Slot"))
            {
                int index = biomeList.arraySize;
                biomeList.InsertArrayElementAtIndex(index);
                biomeList.GetArrayElementAtIndex(index).objectReferenceValue = null;
            }
            using (new EditorGUI.DisabledScope(biomeList.arraySize == 0))
                if (GUILayout.Button("Remove Last Biome"))
                {
                    int last = biomeList.arraySize - 1;
                    biomeList.GetArrayElementAtIndex(last).objectReferenceValue = null;
                    biomeList.DeleteArrayElementAtIndex(last);
                }
        }
        serializedObject.ApplyModifiedProperties();

        var population = (PlanetPopulationPoc)target;
        if (population.biomeDistribution == PlanetBiomeDistribution.Percentages)
        {
            SyncBiomeShares(population);
            serializedObject.Update();
            var shares = serializedObject.FindProperty("biomeShares");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Biome Area Percentages", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Turn Auto off to fix a biome's percentage. Auto biomes share the remaining area equally. Percentages refer to the whole saved tile set, including tiles outside the visible window.", MessageType.Info);
            for (int i = 0; i < shares.arraySize; i++)
            {
                var entry = shares.GetArrayElementAtIndex(i);
                var profile = population.allowedBiomes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(profile ? profile.identifier : "Missing Biome", GUILayout.MinWidth(120));
                    var automatic = entry.FindPropertyRelative("automatic");
                    EditorGUILayout.PropertyField(automatic, new GUIContent("Auto"), GUILayout.Width(70));
                    if (!automatic.boolValue)
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("percentage"), GUIContent.none, GUILayout.Width(75));
                }
            }
            serializedObject.ApplyModifiedProperties();
            if (population.TryGetRequestedBiomeShares(out var percentages, out string percentageError))
            {
                for (int i = 0; i < percentages.Length; i++)
                    EditorGUILayout.LabelField($"{population.allowedBiomes[i].identifier}: {percentages[i]:0.##}%");
            }
            else EditorGUILayout.HelpBox(percentageError, MessageType.Error);
        }
        else EditorGUILayout.HelpBox("Random distributes the selected biomes with the planet seed. Balanced coverage includes every selected biome on finite tile sets.", MessageType.Info);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Selected Biome Terrain Layers", EditorStyles.boldLabel);
        var seen = new HashSet<PlanetBiomeProfile>();
        if (population.allowedBiomes != null)
            foreach (var biome in population.allowedBiomes)
            {
                if (!biome || !seen.Add(biome)) continue;
                var biomeObject = new SerializedObject(biome);
                biomeObject.Update();
                EditorGUILayout.PropertyField(biomeObject.FindProperty("terrainLayers"), new GUIContent(biome.identifier + " Layers"), true);
                if (biomeObject.ApplyModifiedProperties()) EditorUtility.SetDirty(biome);
            }

        EditorGUILayout.HelpBox("Add or remove entries in Allowed Biomes above. Biomes are authored before painting. Enable Paint Biomes On Generate only when every selected biome has layers.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Generate New Planet Treadmill"))
                GenerateNewPlanet(population);

        var existingSet = population.GetComponent<PlanetTerrainTreadmill>()?.AuthoredTileSet;
        string existingPath = existingSet ? AssetDatabase.GetAssetPath(existingSet) : null;
        if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith("Assets/PlanetPopulation/Generated/"))
        {
            if (population.balanceBiomeCoverage)
                EditorGUILayout.HelpBox("Balanced biome coverage is enabled for this saved tile set. Rebuild after changing the selected biome list or placement settings.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("This tile set uses random region assignment. Large region spacing can omit selected biomes. Use Balance All Biomes and Rebuild Terrain to distribute every selected biome across the saved area.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Connect Generated Treadmill to Planet Approach"))
                    ConnectToPlanetApproach(population.GetComponent<PlanetTerrainTreadmill>());
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Repaint Generated Biome Terrain"))
                    RepaintExistingBiomes(population, existingSet, existingPath);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Rebake Generated Biome Shapes"))
                    RebakeExistingShapes(population, existingSet);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Rebuild Biome Distribution and Terrain"))
                    BalanceExistingBiomes(population, existingSet, existingPath);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Apply Smoothness to Existing Baked Terrain"))
                    ApplyFinishToExisting(population, existingSet, existingPath);
            EditorGUILayout.HelpBox("Rebake Shapes updates saved heights. Repaint updates TerrainLayer weights. Apply Smoothness updates layer finish. Biome placement stays on the saved maps.", MessageType.Info);
        }
    }

    static void SyncBiomeShares(PlanetPopulationPoc population)
    {
        if (population.allowedBiomes == null) return;
        var previous = population.biomeShares ?? new List<PlanetBiomeShare>();
        bool matches = previous.Count == population.allowedBiomes.Length;
        if (matches)
            for (int i = 0; i < previous.Count; i++)
                if (previous[i] == null || previous[i].biome != population.allowedBiomes[i]) { matches = false; break; }
        if (matches) return;
        Undo.RecordObject(population, "Sync biome percentages");
        var updated = new List<PlanetBiomeShare>();
        foreach (var biome in population.allowedBiomes)
        {
            var old = previous.Find(item => item != null && item.biome == biome);
            updated.Add(old ?? new PlanetBiomeShare { biome = biome, automatic = true });
        }
        population.biomeShares = updated;
        EditorUtility.SetDirty(population);
    }

    static bool ValidateDistribution(PlanetPopulationPoc population, string title)
    {
        if (population.biomeDistribution != PlanetBiomeDistribution.Percentages) return true;
        if (population.TryGetRequestedBiomeShares(out _, out string error)) return true;
        EditorUtility.DisplayDialog(title, error, "OK");
        return false;
    }

    internal static void ConfigureCurvature(PlanetTerrainTreadmill treadmill)
    {
        var curve = treadmill.GetComponent<PlanetTerrainCurvatureController>();
        if (!curve) curve = Undo.AddComponent<PlanetTerrainCurvatureController>(treadmill.gameObject);
        var serialized = new SerializedObject(curve);
        serialized.FindProperty("curvatureEnabled").boolValue = true;
        serialized.FindProperty("planetRadiusMeters").floatValue = 60000f;
        serialized.FindProperty("curvatureStrength").floatValue = 1f;
        serialized.FindProperty("anchor").objectReferenceValue = treadmill.Target;
        serialized.FindProperty("fullDetailRadiusMeters").floatValue = 1900f;
        serialized.FindProperty("falloffOuterRadiusMeters").floatValue = 6000f;
        serialized.FindProperty("falloffStrengthMeters").floatValue = 3000f;
        serialized.FindProperty("applyMaterialToChildTerrains").boolValue = true;
        serialized.FindProperty("curvedTerrainMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/MyAssets/shaders/Terrain/CurvedTerrain/M_CurvedTerrainLit.mat");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        curve.enabled = false;
        curve.enabled = true;
        EditorUtility.SetDirty(curve);
    }

    static void ConfigureBalancedLayout(PlanetPopulationPoc population, PlanetTerrainTileSet tileSet, float tileMeters)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var tile in tileSet.authoredTiles)
        {
            if (tile?.terrainData == null) continue;
            minX = Mathf.Min(minX, tile.coord.x); maxX = Mathf.Max(maxX, tile.coord.x);
            minZ = Mathf.Min(minZ, tile.coord.z); maxZ = Mathf.Max(maxZ, tile.coord.z);
        }
        if (minX == int.MaxValue) throw new System.InvalidOperationException("The tile set has no authored terrain.");
        population.balanceBiomeCoverage = true;
        population.biomeLayoutMinX = minX;
        population.biomeLayoutMinZ = minZ;
        population.biomeLayoutTilesX = maxX - minX + 1;
        population.biomeLayoutTilesZ = maxZ - minZ + 1;
        population.biomeLayoutTileMeters = tileMeters;
    }

    internal static void BalanceExistingBiomes(PlanetPopulationPoc population, PlanetTerrainTileSet tileSet, string tileSetPath)
    {
        if (!population || !tileSet || tileSet.authoredTiles == null || tileSet.authoredTiles.Count == 0) return;
        if (population.allowedBiomes == null || population.allowedBiomes.Length == 0) return;
        if (!ValidateDistribution(population, "Rebuild Biome Distribution")) return;
        foreach (var biome in population.allowedBiomes)
            if (!biome || biome.terrainLayers == null || biome.terrainLayers.Length == 0 ||
                System.Array.Exists(biome.terrainLayers, layer => !layer))
            {
                EditorUtility.DisplayDialog("Balance Biomes", "Each selected biome needs valid TerrainLayers before rebuilding painted terrain.", "OK");
                return;
            }
        var treadmill = population.GetComponent<PlanetTerrainTreadmill>();
        Undo.RecordObject(population, "Balance planet biomes");
        ConfigureBalancedLayout(population, tileSet, treadmill.TileSizeMeters);
        population.BindForBake(treadmill);
        population.biomeMaps ??= new List<PlanetBiomeTileMap>();
        try
        {
            for (int i = 0; i < tileSet.authoredTiles.Count; i++)
            {
                var tile = tileSet.authoredTiles[i];
                if (tile?.terrainData == null) continue;
                EditorUtility.DisplayProgressBar("Balance Planet Biomes",
                    $"Authoring biome map {i + 1}/{tileSet.authoredTiles.Count}", i / (float)tileSet.authoredTiles.Count);
                var map = population.biomeMaps.Find(item => item && item.coord.Equals(tile.coord));
                if (!map)
                {
                    string folder = System.IO.Path.GetDirectoryName(tileSetPath).Replace('\\', '/');
                    string path = $"{folder}/Biomes_{tile.coord.x}_{tile.coord.z}.asset";
                    map = AssetDatabase.LoadAssetAtPath<PlanetBiomeTileMap>(path);
                    if (!map)
                    {
                        map = ScriptableObject.CreateInstance<PlanetBiomeTileMap>();
                        AssetDatabase.CreateAsset(map, path);
                    }
                    population.biomeMaps.Add(map);
                }
                Undo.RecordObject(map, "Balance planet biomes");
                int resolution = map.resolution > 1 ? map.resolution : tile.terrainData.alphamapResolution + 1;
                var baked = BakeBiomeMap(population, tile.coord, resolution, treadmill.TileSizeMeters);
                map.coord = tile.coord;
                map.resolution = baked.resolution;
                map.biomes = baked.biomes;
                map.weights = baked.weights;
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(map);
            }
        }
        finally { population.EndBake(); EditorUtility.ClearProgressBar(); }
        if (population.GetComponent<PlanetBiomeWrapRuntime>())
            PlanetBiomeWrapBaker.StitchBiomes(population, tileSet);
        EditorUtility.SetDirty(population);
        AssetDatabase.SaveAssets();
        RebakeExistingShapes(population, tileSet, false);
        RepaintExistingBiomes(population, tileSet, tileSetPath);
        Debug.Log($"Balanced all {population.allowedBiomes.Length} biomes across {tileSet.authoredTiles.Count} saved tiles.", population);
    }

    internal static void RebakeExistingShapes(PlanetPopulationPoc population, PlanetTerrainTileSet tileSet, bool bakeProxy = true)
    {
        var treadmill = population.GetComponent<PlanetTerrainTreadmill>();
        if (!treadmill || !tileSet || tileSet.authoredTiles == null) return;
        EnsureBiomeHeightRange(treadmill, tileSet);
        population.BindForBake(treadmill);
        try
        {
            for (int i = 0; i < tileSet.authoredTiles.Count; i++)
            {
                var tile = tileSet.authoredTiles[i];
                if (tile?.terrainData == null) continue;
                EditorUtility.DisplayProgressBar("Rebake Generated Biome Shapes",
                    $"Baking tile {i + 1}/{tileSet.authoredTiles.Count}", i / (float)tileSet.authoredTiles.Count);
                population.BeginTileBake(tile.coord);
                var data = tile.terrainData;
                var size = data.size;
                size.y = treadmill.GenerationSettings.maxHeightMeters;
                data.size = size;
                int resolution = data.heightmapResolution;
                float step = treadmill.TileSizeMeters / (resolution - 1);
                var heights = new float[resolution, resolution];
                for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
                    heights[z, x] = population.SampleBakedHeight(treadmill.CoordinateMapper.MapHeightSample(
                        tile.coord, x, z, resolution - 1, step));
                Undo.RegisterCompleteObjectUndo(data, "Rebake generated biome shapes");
                data.SetHeights(0, 0, heights);
                EditorUtility.SetDirty(data);
            }
        }
        finally
        {
            population.EndBake();
            EditorUtility.ClearProgressBar();
        }
        if (population.GetComponent<PlanetBiomeWrapRuntime>())
            PlanetBiomeWrapBaker.StitchTerrain(tileSet);
        AssetDatabase.SaveAssets();
        foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true)) terrain.Flush();
        if (bakeProxy) PlanetProxyTerrainThumbnailBaker.BakeFromTreadmill(treadmill);
        SceneView.RepaintAll();
        Debug.Log($"Rebaked {tileSet.authoredTiles.Count} biome tile heights; biome maps and paint retained.", population);
    }

    static void EnsureBiomeHeightRange(PlanetTerrainTreadmill treadmill, PlanetTerrainTileSet tileSet)
    {
        const float minimumHeightMeters = 1600f;
        var serialized = new SerializedObject(treadmill);
        var maxHeight = serialized.FindProperty("maxHeightMeters");
        if (maxHeight.floatValue < minimumHeightMeters)
        {
            maxHeight.floatValue = minimumHeightMeters;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(treadmill);
        }
        if (tileSet && tileSet.generationSettings != null)
        {
            tileSet.generationSettings.maxHeightMeters = treadmill.GenerationSettings.maxHeightMeters;
            EditorUtility.SetDirty(tileSet);
        }
    }

    internal static void ConnectToPlanetApproach(PlanetTerrainTreadmill generated)
    {
        if (!generated) return;
        var rigs = Object.FindObjectsByType<PlanetSystemRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var rig in rigs)
        {
            var serializedRig = new SerializedObject(rig);
            var rigRoot = serializedRig.FindProperty("generatedRoot")?.objectReferenceValue as Transform;
            bool matches = rigRoot && generated.transform.IsChildOf(rigRoot);
            if (!matches) continue;
            serializedRig.FindProperty("treadmill").objectReferenceValue = generated;
            serializedRig.ApplyModifiedProperties();
            var transition = rig.GetComponent<PlanetTerrainTransitionController>();
            if (transition)
            {
                var serializedTransition = new SerializedObject(transition);
                serializedTransition.FindProperty("treadmill").objectReferenceValue = generated;
                serializedTransition.ApplyModifiedProperties();
                EditorUtility.SetDirty(transition);
            }
            EditorUtility.SetDirty(rig);
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            Debug.Log($"Connected '{generated.name}' to '{rig.name}' planet approach.", generated);
            return;
        }
        Debug.LogWarning("No matching PlanetSystemRig found for generated treadmill. Assign it to the rig's Treadmill and Terrain Transition Controller fields.", generated);
    }

    internal static void RepaintExistingBiomes(PlanetPopulationPoc population, PlanetTerrainTileSet tileSet, string tileSetPath)
    {
        if (population.allowedBiomes == null || population.allowedBiomes.Length == 0)
        {
            EditorUtility.DisplayDialog("Repaint Biomes", "Select at least one biome.", "OK");
            return;
        }
        foreach (var biome in population.allowedBiomes)
        {
            if (!biome || biome.terrainLayers == null || biome.terrainLayers.Length == 0 ||
                System.Array.Exists(biome.terrainLayers, layer => !layer))
            {
                EditorUtility.DisplayDialog("Repaint Biomes", "Each selected biome needs valid TerrainLayers.", "OK");
                return;
            }
        }
        var treadmill = population.GetComponent<PlanetTerrainTreadmill>();
        if (!treadmill || tileSet.authoredTiles == null || tileSet.authoredTiles.Count == 0) return;
        foreach (var map in population.biomeMaps)
            if (map && (map.biomes.Length != population.allowedBiomes.Length ||
                !System.Array.TrueForAll(map.biomes, b => System.Array.IndexOf(population.allowedBiomes, b) >= 0)))
            {
                EditorUtility.DisplayDialog("Repaint Biomes", "The biome list differs from the authored terrain. Generate a new treadmill to change biome membership.", "OK");
                return;
            }
        var sources = population.CollectTerrainLayers();
        var paintedLayers = new TerrainLayer[sources.Length];
        string folder = System.IO.Path.GetDirectoryName(tileSetPath).Replace('\\', '/');
        for (int i = 0; i < sources.Length; i++)
            paintedLayers[i] = population.makeBakedLayersMatte
                ? CreateOrUpdateFinishLayer(sources[i], $"{folder}/Layer_{i}.terrainlayer", population.bakedLayerSmoothness)
                : sources[i];
        population.BindForBake(treadmill);
        int painted = 0;
        try
        {
            for (int i = 0; i < tileSet.authoredTiles.Count; i++)
            {
                var tile = tileSet.authoredTiles[i];
                if (tile?.terrainData == null) continue;
                EditorUtility.DisplayProgressBar("Repaint Generated Biomes", $"Painting tile {i + 1}/{tileSet.authoredTiles.Count}",
                    i / (float)tileSet.authoredTiles.Count);
                var data = tile.terrainData;
                var authoredMap = population.biomeMaps.Find(map => map && map.coord.Equals(tile.coord));
                var maps = PlanetBiomeSmartPainter.PaintTile(population, tile.coord,
                    data.alphamapResolution, sources, data, authoredMap);
                Undo.RegisterCompleteObjectUndo(data, "Repaint generated biome terrain");
                data.terrainLayers = paintedLayers;
                PlanetTerrainGenerator.ApplyAlphamaps(data, maps);
                EditorUtility.SetDirty(data);
                painted++;
            }
        }
        finally
        {
            population.EndBake();
            EditorUtility.ClearProgressBar();
        }
        if (population.GetComponent<PlanetBiomeWrapRuntime>())
            PlanetBiomeWrapBaker.StitchPaint(tileSet);
        Undo.RecordObject(tileSet, "Repaint generated biome terrain");
        tileSet.generationSettings.terrainLayers = paintedLayers;
        EditorUtility.SetDirty(tileSet);
        var settings = new SerializedObject(treadmill);
        var property = settings.FindProperty("terrainLayers");
        property.arraySize = paintedLayers.Length;
        for (int i = 0; i < paintedLayers.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = paintedLayers[i];
        settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true)) terrain.Flush();
        SceneView.RepaintAll();
        PlanetProxyTerrainThumbnailBaker.BakeFromTreadmill(treadmill);
        Debug.Log($"Repainted {painted} generated biome tiles without changing heights.", population);
    }

    static TerrainLayer CreateOrUpdateFinishLayer(TerrainLayer source, string assetPath, float smoothness)
    {
        TerrainLayer copy = AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath);
        if (!copy)
        {
            copy = Object.Instantiate(source);
            copy.name = "POC Finish " + source.name;
            AssetDatabase.CreateAsset(copy, assetPath);
        }
        else Undo.RecordObject(copy, "Change POC Terrain Smoothness");
        copy.maskMapTexture = null;
        copy.metallic = 0f;
        copy.smoothness = Mathf.Clamp01(smoothness);
        // Imported layers can read gloss from diffuse alpha even when Smoothness is zero.
        var serializedLayer = new SerializedObject(copy);
        var smoothnessSource = serializedLayer.FindProperty("m_SmoothnessSource");
        if (smoothnessSource != null)
        {
            smoothnessSource.intValue = 0; // Constant, rather than diffuse alpha.
            serializedLayer.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(copy);
        return copy;
    }

    static void ApplyFinishToExisting(PlanetPopulationPoc population, PlanetTerrainTileSet tileSet, string tileSetPath)
    {
        string folder = System.IO.Path.GetDirectoryName(tileSetPath).Replace('\\', '/');
        var oldLayers = tileSet.generationSettings.terrainLayers;
        if (oldLayers == null || oldLayers.Length == 0) return;
        var finishedLayers = new TerrainLayer[oldLayers.Length];
        for (int i = 0; i < oldLayers.Length; i++)
        {
            if (!oldLayers[i]) continue;
            finishedLayers[i] = CreateOrUpdateFinishLayer(oldLayers[i], $"{folder}/Layer_{i}.terrainlayer", population.bakedLayerSmoothness);
        }
        Undo.RecordObject(tileSet, "Change POC Terrain Smoothness");
        tileSet.generationSettings.terrainLayers = finishedLayers;
        EditorUtility.SetDirty(tileSet);
        if (tileSet.authoredTiles != null)
            foreach (var tile in tileSet.authoredTiles)
            {
                if (tile?.terrainData == null) continue;
                tile.terrainData.terrainLayers = finishedLayers;
                tile.terrainData.SetBaseMapDirty();
                EditorUtility.SetDirty(tile.terrainData);
            }
        var treadmill = population.GetComponent<PlanetTerrainTreadmill>();
        var treadmillSettings = new SerializedObject(treadmill);
        var property = treadmillSettings.FindProperty("terrainLayers");
        property.arraySize = finishedLayers.Length;
        for (int i = 0; i < finishedLayers.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = finishedLayers[i];
        treadmillSettings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        foreach (Terrain terrain in treadmill.GetComponentsInChildren<Terrain>(true)) terrain.Flush();
        SceneView.RepaintAll();
        Debug.Log($"Applied smoothness {population.bakedLayerSmoothness:0.00} to {tileSet.authoredTiles?.Count ?? 0} baked POC terrain tiles without rebaking.", population);
    }

    internal static PlanetBiomeTileMap BakeBiomeMap(PlanetPopulationPoc population, PlanetTileCoord coord, int resolution, float tileSize)
    {
        var map = ScriptableObject.CreateInstance<PlanetBiomeTileMap>();
        map.coord = coord;
        map.resolution = resolution;
        map.biomes = (PlanetBiomeProfile[])population.allowedBiomes.Clone();
        map.weights = new float[resolution * resolution * map.biomes.Length];
        var sample = new float[map.biomes.Length];
        for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
        {
            population.SampleBiomeWeights(new PlanetSamplePoint((coord.x + x / (double)(resolution - 1)) * tileSize,
                (coord.z + z / (double)(resolution - 1)) * tileSize), sample);
            System.Array.Copy(sample, 0, map.weights, (z * resolution + x) * sample.Length, sample.Length);
        }
        return map;
    }

    internal static void GenerateNewPlanet(PlanetPopulationPoc recipe)
    {
        if (!ValidateDistribution(recipe, "Generate Planet")) return;
        if (recipe.allowedBiomes == null || recipe.allowedBiomes.Length == 0)
        {
            EditorUtility.DisplayDialog("Generate Planet", "Add at least one biome to Allowed Biomes.", "OK");
            return;
        }
        foreach (var biome in recipe.allowedBiomes)
        {
            if (!biome || (recipe.paintBiomesOnGenerate && (biome.terrainLayers == null || biome.terrainLayers.Length == 0)))
            {
                EditorUtility.DisplayDialog("Generate Planet", "Select valid biome profiles. Painting also requires layers for every biome.", "OK");
                return;
            }
            if (recipe.paintBiomesOnGenerate) foreach (var layer in biome.terrainLayers)
                if (!layer)
                {
                    EditorUtility.DisplayDialog("Generate Planet", $"Biome {biome.name} contains an empty TerrainLayer slot.", "OK");
                    return;
                }
        }

        var source = recipe.GetComponent<PlanetTerrainTreadmill>();
        if (!source)
        {
            Debug.LogError("The planet recipe needs a PlanetTerrainTreadmill for tile size, height scale and target.", recipe);
            return;
        }
        var layers = recipe.paintBiomesOnGenerate ? recipe.CollectTerrainLayers() : System.Array.Empty<TerrainLayer>();
        string rootFolder = "Assets/PlanetPopulation/Generated";
        if (!AssetDatabase.IsValidFolder(rootFolder)) AssetDatabase.CreateFolder("Assets/PlanetPopulation", "Generated");
        string folderName = $"Planet_{recipe.planetSeed}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
        AssetDatabase.CreateFolder(rootFolder, folderName);
        string folder = rootFolder + "/" + folderName;

        var root = new GameObject("Generated Planet Treadmill " + recipe.planetSeed);
        Undo.RegisterCreatedObjectUndo(root, "Generate Planet Treadmill");
        root.transform.SetParent(source.transform.parent, false);
        root.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        root.transform.localScale = source.transform.localScale;
        var treadmill = root.AddComponent<PlanetTerrainTreadmill>();
        EditorUtility.CopySerialized(source, treadmill);
        root.AddComponent<PlanetBiomeWrapRuntime>();
        ConfigureCurvature(treadmill);
        var population = root.AddComponent<PlanetPopulationPoc>();
        EditorUtility.CopySerialized(recipe, population);
        population.biomeMaps = new List<PlanetBiomeTileMap>();
        root.SetActive(false);
        var tileSet = ScriptableObject.CreateInstance<PlanetTerrainTileSet>();
        tileSet.wrapCoordinates = false;
        int visibleRadius = new SerializedObject(source).FindProperty("visibleTileRadius").intValue;
        int radius = Mathf.Max(1, recipe.bakeRadiusTiles, visibleRadius);
        tileSet.wrapWidth = tileSet.wrapHeight = radius * 2 + 1;
        population.balanceBiomeCoverage = true;
        population.biomeLayoutMinX = population.biomeLayoutMinZ = -radius;
        population.biomeLayoutTilesX = population.biomeLayoutTilesZ = radius * 2 + 1;
        population.biomeLayoutTileMeters = treadmill.TileSizeMeters;
        AssetDatabase.CreateAsset(tileSet, folder + "/PlanetTileSet.asset");
        int heightResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(32, recipe.bakeHeightmapResolution - 1)) + 1, 33, 4097);
        int alphaResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(recipe.bakeAlphamapResolution), 16, 2048);
        // A neutral base displays authored geometry without borrowing another biome's paint.
        var bakedLayers = new TerrainLayer[Mathf.Max(1, layers.Length)];
        if (layers.Length == 0)
        {
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            texture.Apply();
            AssetDatabase.CreateAsset(texture, folder + "/UnpaintedTexture.asset");
            bakedLayers[0] = new TerrainLayer { diffuseTexture = texture, smoothness = 0 };
            AssetDatabase.CreateAsset(bakedLayers[0], folder + "/Unpainted.terrainlayer");
        }
        for (int i = 0; i < layers.Length; i++)
        {
            if (!recipe.makeBakedLayersMatte) { bakedLayers[i] = layers[i]; continue; }
            bakedLayers[i] = CreateOrUpdateFinishLayer(layers[i], $"{folder}/Layer_{i}.terrainlayer", recipe.bakedLayerSmoothness);
        }
        var treadmillSettings = new SerializedObject(treadmill);
        treadmillSettings.FindProperty("maxHeightMeters").floatValue =
            Mathf.Max(1600f, treadmillSettings.FindProperty("maxHeightMeters").floatValue);
        treadmillSettings.FindProperty("heightmapResolution").intValue = heightResolution;
        var terrainLayersProperty = treadmillSettings.FindProperty("terrainLayers");
        terrainLayersProperty.arraySize = bakedLayers.Length;
        for (int i = 0; i < bakedLayers.Length; i++)
            terrainLayersProperty.GetArrayElementAtIndex(i).objectReferenceValue = bakedLayers[i];
        treadmillSettings.FindProperty("authoredTileSet").objectReferenceValue = tileSet;
        treadmillSettings.FindProperty("target").objectReferenceValue = source.Target;
        treadmillSettings.FindProperty("generateMissingRuntimeTiles").boolValue = false;
        treadmillSettings.ApplyModifiedPropertiesWithoutUndo();
        tileSet.generationSettings = treadmill.GenerationSettings;
        tileSet.generationSettings.alphamapResolution = alphaResolution;

        var terrains = new Dictionary<PlanetTileCoord, Terrain>();
        population.BindForBake(treadmill);
        int side = radius * 2 + 1;
        int count = side * side;
        int complete = 0;
        bool succeeded = false;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int z = -radius; z <= radius; z++) for (int x = -radius; x <= radius; x++)
            {
                var coord = new PlanetTileCoord(x, z);
                if (EditorUtility.DisplayCancelableProgressBar("Generate Planet Treadmill", $"Baking tile {coord} ({complete + 1}/{count})", complete / (float)count))
                    throw new System.OperationCanceledException("Planet bake canceled by user.");
                population.BeginTileBake(coord);
                var settings = tileSet.generationSettings;
                TerrainData data = PlanetTerrainGenerator.CreateTerrainData(settings);
                data.name = $"PlanetTile_{x}_{z}";
                var heights = new float[settings.heightmapResolution, settings.heightmapResolution];
                for (int sampleZ = 0; sampleZ < settings.heightmapResolution; sampleZ++)
                    for (int sampleX = 0; sampleX < settings.heightmapResolution; sampleX++)
                    {
                        var point = treadmill.CoordinateMapper.MapHeightSample(coord, sampleX, sampleZ,
                            settings.SamplesPerTile, settings.MetersPerSample);
                        heights[sampleZ, sampleX] = population.SampleBakedHeight(point);
                    }
                data.SetHeights(0, 0, heights);
                var biomeMap = BakeBiomeMap(population, coord, alphaResolution + 1, treadmill.TileSizeMeters);
                AssetDatabase.CreateAsset(biomeMap, $"{folder}/Biomes_{x}_{z}.asset");
                population.biomeMaps.Add(biomeMap);
                if (recipe.paintBiomesOnGenerate)
                    PlanetTerrainGenerator.ApplyAlphamaps(data,
                        PlanetBiomeSmartPainter.PaintTile(population, coord, alphaResolution, layers, data, biomeMap));
                else
                {
                    var neutral = new float[alphaResolution, alphaResolution, 1];
                    for (int az = 0; az < alphaResolution; az++) for (int ax = 0; ax < alphaResolution; ax++) neutral[az, ax, 0] = 1;
                    PlanetTerrainGenerator.ApplyAlphamaps(data, neutral);
                }
                AssetDatabase.CreateAsset(data, $"{folder}/Tile_{x}_{z}.asset");
                tileSet.SetTerrainData(coord, data);
                GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = $"PT_AuthoredTile_x{x}_z{z}";
                terrainObject.transform.SetParent(root.transform, false);
                terrainObject.transform.position = treadmill.GetTileWorldPosition(coord);
                terrains.Add(coord, terrainObject.GetComponent<Terrain>());
                complete++;
            }
            succeeded = true;
        }
        catch (System.Exception error)
        {
            Debug.LogException(error, recipe);
        }
        finally
        {
            population.EndBake();
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        if (!succeeded)
        {
            Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(folder);
            return;
        }
        PlanetBiomeWrapBaker.StitchTerrainAndBiomes(population, tileSet);
        if (recipe.paintBiomesOnGenerate)
        {
            population.BindForBake(treadmill);
            try
            {
                foreach (var tile in tileSet.authoredTiles)
                {
                    var map = population.biomeMaps.Find(item => item && item.coord.Equals(tile.coord));
                    PlanetTerrainGenerator.ApplyAlphamaps(tile.terrainData,
                        PlanetBiomeSmartPainter.PaintTile(population, tile.coord,
                            alphaResolution, layers, tile.terrainData, map));
                    EditorUtility.SetDirty(tile.terrainData);
                }
            }
            finally { population.EndBake(); }
        }
        PlanetBiomeWrapBaker.StitchPaint(tileSet);
        foreach (var pair in terrains)
        {
            var c = pair.Key;
            Terrain Find(int x, int z) => terrains.TryGetValue(new PlanetTileCoord(x, z), out var t) ? t : null;
            pair.Value.SetNeighbors(Find(c.x - 1, c.z), Find(c.x, c.z + 1), Find(c.x + 1, c.z), Find(c.x, c.z - 1));
        }
        EditorUtility.SetDirty(tileSet);
        EditorUtility.SetDirty(treadmill);
        EditorUtility.SetDirty(population);
        AssetDatabase.SaveAssets();
        if (source.transform.parent != null)
            foreach (Transform sibling in source.transform.parent)
                if (sibling != root.transform && sibling.name.StartsWith("Generated Planet Treadmill "))
                    sibling.gameObject.SetActive(false);
        Undo.RecordObject(source.gameObject, "Replace POC Terrain Treadmill");
        source.gameObject.SetActive(false);
        root.SetActive(true);
        ConnectToPlanetApproach(treadmill);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        PlanetProxyTerrainThumbnailBaker.BakeFromTreadmill(treadmill);
        Debug.Log($"Generated a new {side}x{side} terrain treadmill with {recipe.allowedBiomes.Length} selected biomes and {layers.Length} TerrainLayers. TerrainData saved in {folder}.", root);
    }
}
