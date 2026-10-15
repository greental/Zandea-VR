using System.Collections.Generic;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PlanetBiomeTreadmill))]
public sealed class PlanetBiomeTreadmillEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var combined = (PlanetBiomeTreadmill)target;
        var treadmill = combined.Treadmill;
        var curve = combined.Curvature;
        var biomes = combined.Biomes;
        if (!treadmill || !curve || !biomes)
        {
            EditorGUILayout.HelpBox("This component needs the terrain treadmill, curvature controller, and biome sampler on the same GameObject.", MessageType.Error);
            return;
        }

        EditorGUILayout.HelpBox("This is the terrain entry point. Biome placement, tile baking, wrap, and visual curvature are configured here. POIs are separate; add ExistingTreadmillPoiPlanter when needed.", MessageType.Info);
        EditorGUILayout.LabelField("Treadmill", EditorStyles.boldLabel);
        Transform previousTarget = treadmill.Target;
        DrawFields(treadmill, "target", "tileSizeMeters", "maxHeightMeters", "tileVerticalOffsetMeters",
            "heightmapResolution", "visibleTileRadius", "heightProvider", "authoredTileSet");
        if (treadmill.Target != previousTarget)
        {
            Undo.RecordObject(curve, "Change biome treadmill curvature anchor");
            curve.Anchor = treadmill.Target;
            EditorUtility.SetDirty(curve);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Curvature", EditorStyles.boldLabel);
        DrawFields(curve, "curvatureEnabled", "planetRadiusMeters", "curvatureStrength", "anchor",
            "fullDetailRadiusMeters", "falloffOuterRadiusMeters", "falloffStrengthMeters",
            "applyMaterialToChildTerrains", "curvedTerrainMaterial");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Biome Treadmill", EditorStyles.boldLabel);
        DrawFields(biomes, "planetSeed", "allowedBiomes", "biomeDistribution", "biomeRegionMeters",
            "biomeBlendMeters", "bakeRadiusTiles", "bakeHeightmapResolution", "bakeAlphamapResolution",
            "terrainReliefMultiplier", "paintBiomesOnGenerate", "makeBakedLayersMatte", "bakedLayerSmoothness");
        DrawShares(biomes);
        DrawLayers(biomes);

        var set = combined.AuthoredTileSet;
        string path = set ? AssetDatabase.GetAssetPath(set) : null;
        bool generated = !string.IsNullOrEmpty(path) && path.StartsWith("Assets/PlanetPopulation/Generated/");
        EditorGUILayout.Space();
        if (set) EditorGUILayout.LabelField("Saved tiles", (set.authoredTiles?.Count ?? 0).ToString());
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generate New Biome Treadmill"))
                GenerateWithSharedLayers(combined);
            if (generated)
            {
                if (GUILayout.Button("Rebuild Biome Distribution and Terrain"))
                    RebuildWithSharedLayers(biomes, set, path);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Rebake Shapes")) PlanetPopulationPocEditor.RebakeExistingShapes(biomes, set);
                    if (GUILayout.Button("Repaint Biomes")) RepaintWithSharedLayers(biomes, set, path);
                }
                if (GUILayout.Button("Consolidate Generated Layer Assets"))
                    PlanetSharedTerrainLayers.ConsolidateGenerated();
                if (GUILayout.Button("Validate Saved Treadmill")) PlanetBiomeValidation.ValidateGenerated(biomes);
            }
            if (!combined.GetComponent<ExistingTreadmillPoiPlanter>() &&
                GUILayout.Button("Add Separate POI Populator"))
                Undo.AddComponent<ExistingTreadmillPoiPlanter>(combined.gameObject);
        }
    }

    static void GenerateWithSharedLayers(PlanetBiomeTreadmill combined)
    {
        var recipe = combined.Biomes;
        GameObject previous = combined.gameObject;
        bool matte = recipe.makeBakedLayersMatte;
        try
        {
            // The legacy baker can paint with source layers directly. Shared finish
            // variants are bound afterward, so no Layer_N assets are created per planet.
            recipe.makeBakedLayersMatte = false;
            PlanetPopulationPocEditor.GenerateNewPlanet(recipe);
        }
        finally { recipe.makeBakedLayersMatte = matte; }
        GameObject created = Selection.activeGameObject;
        if (!created || created == previous) return;
        var population = created.GetComponent<PlanetPopulationPoc>();
        var set = created.GetComponent<PlanetTerrainTreadmill>()?.AuthoredTileSet;
        if (!population || !set) return;
        population.makeBakedLayersMatte = matte;
        EditorUtility.SetDirty(population);
        PlanetSharedTerrainLayers.Bind(population, set);
        PlanetSharedTerrainLayers.RemoveNewNeutralCopies(AssetDatabase.GetAssetPath(set));
        if (!created.GetComponent<PlanetBiomeTreadmill>()) Undo.AddComponent<PlanetBiomeTreadmill>(created);
        EditorSceneManager.MarkSceneDirty(created.scene);
        Selection.activeGameObject = created;
    }

    static void RepaintWithSharedLayers(PlanetPopulationPoc population, PlanetTerrainTileSet set, string path)
    {
        bool matte = population.makeBakedLayersMatte;
        try
        {
            population.makeBakedLayersMatte = false;
            PlanetPopulationPocEditor.RepaintExistingBiomes(population, set, path);
        }
        finally { population.makeBakedLayersMatte = matte; }
        PlanetSharedTerrainLayers.Bind(population, set);
    }

    static void RebuildWithSharedLayers(PlanetPopulationPoc population, PlanetTerrainTileSet set, string path)
    {
        bool matte = population.makeBakedLayersMatte;
        try
        {
            population.makeBakedLayersMatte = false;
            PlanetPopulationPocEditor.BalanceExistingBiomes(population, set, path);
        }
        finally { population.makeBakedLayersMatte = matte; }
        PlanetSharedTerrainLayers.Bind(population, set);
    }

    static void DrawFields(Object owner, params string[] names)
    {
        var serialized = new SerializedObject(owner);
        serialized.Update();
        foreach (string name in names)
        {
            var property = serialized.FindProperty(name);
            if (property != null) EditorGUILayout.PropertyField(property, true);
        }
        serialized.ApplyModifiedProperties();
    }

    static void DrawShares(PlanetPopulationPoc biomes)
    {
        if (biomes.biomeDistribution != PlanetBiomeDistribution.Percentages || biomes.allowedBiomes == null) return;
        biomes.biomeShares ??= new List<PlanetBiomeShare>();
        bool changed = false;
        foreach (var profile in biomes.allowedBiomes)
        {
            if (!profile || biomes.biomeShares.Exists(entry => entry != null && entry.biome == profile)) continue;
            if (!changed) Undo.RecordObject(biomes, "Sync biome percentages");
            biomes.biomeShares.Add(new PlanetBiomeShare { biome = profile, automatic = true });
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(biomes);
        var serialized = new SerializedObject(biomes);
        serialized.Update();
        var shares = serialized.FindProperty("biomeShares");
        EditorGUILayout.LabelField("Biome Area Percentages", EditorStyles.boldLabel);
        foreach (var profile in biomes.allowedBiomes)
        {
            if (!profile) continue;
            for (int i = 0; i < shares.arraySize; i++)
            {
                var entry = shares.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("biome").objectReferenceValue != profile) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(profile.identifier, GUILayout.MinWidth(120));
                    var automatic = entry.FindPropertyRelative("automatic");
                    EditorGUILayout.PropertyField(automatic, new GUIContent("Auto"), GUILayout.Width(70));
                    if (!automatic.boolValue)
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("percentage"), GUIContent.none, GUILayout.Width(75));
                }
                break;
            }
        }
        serialized.ApplyModifiedProperties();
        if (!biomes.TryGetRequestedBiomeShares(out _, out string error))
            EditorGUILayout.HelpBox(error, MessageType.Error);
    }

    static void DrawLayers(PlanetPopulationPoc biomes)
    {
        if (biomes.allowedBiomes == null) return;
        EditorGUILayout.LabelField("Layers per Biome", EditorStyles.boldLabel);
        var seen = new HashSet<PlanetBiomeProfile>();
        foreach (var profile in biomes.allowedBiomes)
        {
            if (!profile || !seen.Add(profile)) continue;
            var serialized = new SerializedObject(profile);
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("terrainLayers"),
                new GUIContent(profile.identifier), true);
            if (serialized.ApplyModifiedProperties()) EditorUtility.SetDirty(profile);
        }
    }
}
