using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SmartTerrainPainterWindow : EditorWindow
{
    private const int PreviewResolution = 256;
    private const string DefaultCustomPresetPath = "Assets/SmartTerrainPainter";
    private const string LayerBindingKeyPrefix = "SmartTerrainPainter.LayerBinding";

    private readonly List<PresetEntry> presetEntries = new List<PresetEntry>();
    private readonly List<BiomePreset> builtInPresets = new List<BiomePreset>();

    private Terrain targetTerrain;
    private int selectedPresetIndex;
    private TerrainPainterUtility.PaintMode paintMode = TerrainPainterUtility.PaintMode.WholeTerrain;
    private float brushRadius = 120f;

    // Active painting controls (can diverge from preset defaults while iterating).
    private float noiseScale = 0.015f;
    private float globalNoiseStrength = 0.35f;
    private float blendSmoothness = 0.25f;
    private float heightInfluence = 1f;
    private float slopeInfluence = 1f;
    private bool canyonCliffBoost;
    private int smoothingPasses = 2;
    private bool enforceDrySurface = true;
    private float drySurfaceSmoothness = 0f;

    private Vector2 scroll;
    private Texture2D slopePreview;
    private Texture2D heightPreview;
    private Texture2D biomeMaskPreview;
    private bool autoApplyPresetValues = true;

    [MenuItem("Tools/Smart Terrain Painter")]
    public static void Open()
    {
        GetWindow<SmartTerrainPainterWindow>("Smart Terrain Painter");
    }

    private void OnEnable()
    {
        CreateBuiltInPresets();
        RefreshPresetEntries();
        TryAutoDetectTerrain();
        ApplySelectedPresetDefaults();
        RestoreRuleLayerBindings(GetSelectedPreset(), targetTerrain);
    }

    private void OnDisable()
    {
        DestroyPreviewTextures();
        DestroyBuiltInPresets();
    }

    private void OnGUI()
    {
        TryAutoDetectTerrain();
        RefreshPresetEntriesIfNeeded();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawTerrainSection();
        EditorGUILayout.Space(8f);
        DrawPresetSection();
        EditorGUILayout.Space(8f);
        DrawPaintControlsSection();
        EditorGUILayout.Space(8f);
        DrawActionsSection();
        EditorGUILayout.Space(8f);
        DrawValidationSection();
        EditorGUILayout.Space(8f);
        DrawPreviewSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTerrainSection()
    {
        EditorGUILayout.LabelField("Terrain", EditorStyles.boldLabel);
        Terrain nextTerrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", targetTerrain, typeof(Terrain), true);
        if (nextTerrain != targetTerrain)
        {
            targetTerrain = nextTerrain;
            RestoreRuleLayerBindings(GetSelectedPreset(), targetTerrain);
            AutoAssignMissingRuleLayers(GetSelectedPreset(), targetTerrain);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Active Terrain"))
            {
                targetTerrain = TerrainPainterUtility.GetActiveTerrain();
                RestoreRuleLayerBindings(GetSelectedPreset(), targetTerrain);
                AutoAssignMissingRuleLayers(GetSelectedPreset(), targetTerrain);
            }

            if (GUILayout.Button("Refresh Presets"))
            {
                RefreshPresetEntries();
            }
        }
    }

    private void DrawPresetSection()
    {
        EditorGUILayout.LabelField("Biome Preset", EditorStyles.boldLabel);

        string[] names = presetEntries.Select(entry => entry.displayName).ToArray();
        if (names.Length == 0)
        {
            EditorGUILayout.HelpBox("No biome presets found. Use the built-ins or create a ScriptableObject preset.", MessageType.Warning);
            if (GUILayout.Button("Rebuild Built-In Presets"))
            {
                CreateBuiltInPresets();
                RefreshPresetEntries();
            }

            return;
        }

        int nextIndex = EditorGUILayout.Popup("Preset", Mathf.Clamp(selectedPresetIndex, 0, names.Length - 1), names);
        if (nextIndex != selectedPresetIndex)
        {
            selectedPresetIndex = nextIndex;
            ApplySelectedPresetDefaults();
        }

        BiomePreset selectedPreset = GetSelectedPreset();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(selectedPreset != null ? selectedPreset.biomeName : "None", EditorStyles.boldLabel);
            if (selectedPreset != null && !string.IsNullOrWhiteSpace(selectedPreset.description))
            {
                EditorGUILayout.LabelField(selectedPreset.description, EditorStyles.wordWrappedLabel);
            }
        }

        autoApplyPresetValues = EditorGUILayout.ToggleLeft("Auto Apply Preset Defaults When Changed", autoApplyPresetValues);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply Preset Defaults"))
            {
                ApplySelectedPresetDefaults();
            }

            if (GUILayout.Button("Auto Map Layers From Terrain"))
            {
                AutoAssignMissingRuleLayers(GetSelectedPreset(), targetTerrain, true);
                SaveRuleLayerBindings(GetSelectedPreset(), targetTerrain);
            }

            if (GUILayout.Button("Save Preset As Asset"))
            {
                SaveSelectedPresetAsAsset();
            }
        }

        DrawRuleList(selectedPreset);
    }

    private void DrawRuleList(BiomePreset preset)
    {
        if (preset == null || preset.rules == null)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Biome Rules", EditorStyles.boldLabel);
        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            if (rule == null)
            {
                continue;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{i + 1}. {rule.ruleName}", EditorStyles.boldLabel);
                TerrainLayer nextLayer = (TerrainLayer)EditorGUILayout.ObjectField("Terrain Layer", rule.terrainLayer, typeof(TerrainLayer), false);
                if (nextLayer != rule.terrainLayer)
                {
                    rule.terrainLayer = nextLayer;
                    SaveRuleLayerBindings(preset, targetTerrain);
                }
                EditorGUILayout.LabelField($"Height: {rule.minHeight:0.00} - {rule.maxHeight:0.00}");
                EditorGUILayout.LabelField($"Slope: {rule.minSlope:0.0} - {rule.maxSlope:0.0} degrees");
                EditorGUILayout.LabelField($"Noise Strength: {rule.noiseStrength:0.00}   Blend: {rule.blendStrength:0.00}");
                if (!string.IsNullOrWhiteSpace(rule.layerHint))
                {
                    EditorGUILayout.LabelField($"Layer Hint: {rule.layerHint}");
                }
            }
        }
    }

    private void DrawPaintControlsSection()
    {
        EditorGUILayout.LabelField("Painting Controls", EditorStyles.boldLabel);
        noiseScale = EditorGUILayout.Slider("Noise Scale", noiseScale, 0.0001f, 0.2f);
        globalNoiseStrength = EditorGUILayout.Slider("Global Noise Strength", globalNoiseStrength, 0f, 2f);
        blendSmoothness = EditorGUILayout.Slider("Blend Smoothness", blendSmoothness, 0f, 1f);
        heightInfluence = EditorGUILayout.Slider("Height Influence", heightInfluence, 0f, 2f);
        slopeInfluence = EditorGUILayout.Slider("Slope Influence", slopeInfluence, 0f, 2f);
        smoothingPasses = EditorGUILayout.IntSlider("Smoothing Passes", smoothingPasses, 0, 6);
        brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 4f, 1000f);
        paintMode = (TerrainPainterUtility.PaintMode)EditorGUILayout.EnumPopup("Paint Mode", paintMode);
        canyonCliffBoost = EditorGUILayout.Toggle("Canyon Cliff Boost", canyonCliffBoost);
        enforceDrySurface = EditorGUILayout.Toggle("Enforce Dry Surface", enforceDrySurface);
        drySurfaceSmoothness = EditorGUILayout.Slider("Dry Surface Smoothness", drySurfaceSmoothness, 0f, 1f);
    }

    private void DrawActionsSection()
    {
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        bool isValid = TerrainPainterUtility.ValidateTerrainAndPreset(
            targetTerrain,
            GetSelectedPreset(),
            out string warning,
            out _);

        EditorGUI.BeginDisabledGroup(!isValid);
        if (GUILayout.Button("Auto Paint Terrain", GUILayout.Height(28f)))
        {
            AutoAssignMissingRuleLayers(GetSelectedPreset(), targetTerrain);
            TerrainPainterUtility.PaintWholeTerrain(targetTerrain, GetSelectedPreset(), BuildPaintSettings(), true);
            targetTerrain.Flush();
        }

        if (GUILayout.Button("Paint Selected Area", GUILayout.Height(24f)))
        {
            PaintSelectedArea();
            targetTerrain.Flush();
        }

        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Reset Terrain"))
        {
            TerrainPainterUtility.ResetTerrain(targetTerrain);
            targetTerrain.Flush();
        }

        if (GUILayout.Button("Apply Dry Surface Settings"))
        {
            TerrainPainterUtility.ApplyDrySurfaceOverrides(targetTerrain, BuildPaintSettings());
            targetTerrain.Flush();
        }

        if (GUILayout.Button("Preview Rules"))
        {
            GeneratePreviews();
        }

        if (!isValid)
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }

    private void DrawValidationSection()
    {
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

        BiomePreset preset = GetSelectedPreset();
        if (targetTerrain == null)
        {
            EditorGUILayout.HelpBox("No Terrain detected. Select a Terrain or press 'Use Active Terrain'.", MessageType.Warning);
            return;
        }

        if (preset == null)
        {
            EditorGUILayout.HelpBox("No preset selected.", MessageType.Warning);
            return;
        }

        AutoAssignMissingRuleLayers(preset, targetTerrain);

        if (TerrainPainterUtility.ValidateTerrainAndPreset(targetTerrain, preset, out string warning, out _))
        {
            EditorGUILayout.HelpBox("Terrain and biome rules are valid.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }

    private void DrawPreviewSection()
    {
        EditorGUILayout.LabelField("Preview & Debug", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Previews are generated in this window only. They are lightweight debug maps for slope, height, and dominant biome rule.",
            MessageType.None);

        if (slopePreview == null || heightPreview == null || biomeMaskPreview == null)
        {
            EditorGUILayout.HelpBox("Press 'Preview Rules' to generate heatmaps.", MessageType.Info);
            return;
        }

        DrawPreviewTexture("Slope Heatmap", slopePreview);
        DrawPreviewTexture("Height Heatmap", heightPreview);
        DrawPreviewTexture("Biome Mask", biomeMaskPreview);
    }

    private void DrawPreviewTexture(string label, Texture2D tex)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(PreviewResolution, PreviewResolution, GUILayout.ExpandWidth(false));
        EditorGUI.DrawPreviewTexture(rect, tex);
    }

    private void PaintSelectedArea()
    {
        BiomePreset preset = GetSelectedPreset();
        if (targetTerrain == null || preset == null)
        {
            return;
        }

        AutoAssignMissingRuleLayers(preset, targetTerrain);
        TerrainPainterUtility.PaintSettings settings = BuildPaintSettings();

        switch (paintMode)
        {
            case TerrainPainterUtility.PaintMode.WholeTerrain:
                TerrainPainterUtility.PaintWholeTerrain(targetTerrain, preset, settings, true);
                break;

            case TerrainPainterUtility.PaintMode.BrushArea:
                if (!TerrainPainterUtility.TryGetBrushBounds(targetTerrain, brushRadius, out Bounds brushBounds))
                {
                    EditorUtility.DisplayDialog(
                        "Smart Terrain Painter",
                        "Brush Area requires either a selected object in the scene or an active Scene view camera pivot.",
                        "OK");
                    return;
                }

                TerrainPainterUtility.PaintBounds(targetTerrain, preset, settings, brushBounds, true);
                break;

            case TerrainPainterUtility.PaintMode.SelectedBounds:
                if (!TerrainPainterUtility.TryGetSelectionBounds(targetTerrain, out Bounds selectedBounds))
                {
                    EditorUtility.DisplayDialog(
                        "Smart Terrain Painter",
                        "Selected Bounds mode requires a selected scene object.",
                        "OK");
                    return;
                }

                TerrainPainterUtility.PaintBounds(targetTerrain, preset, settings, selectedBounds, true);
                break;
        }
    }

    private void GeneratePreviews()
    {
        DestroyPreviewTextures();

        if (targetTerrain == null)
        {
            return;
        }

        BiomePreset preset = GetSelectedPreset();
        if (preset != null)
        {
            AutoAssignMissingRuleLayers(preset, targetTerrain);
        }

        TerrainPainterUtility.PaintSettings settings = BuildPaintSettings();
        slopePreview = TerrainPreviewGenerator.GenerateSlopeHeatmap(targetTerrain, PreviewResolution);
        heightPreview = TerrainPreviewGenerator.GenerateHeightHeatmap(targetTerrain, PreviewResolution);
        biomeMaskPreview = TerrainPreviewGenerator.GenerateBiomeMask(targetTerrain, preset, settings, PreviewResolution);
    }

    private TerrainPainterUtility.PaintSettings BuildPaintSettings()
    {
        return new TerrainPainterUtility.PaintSettings
        {
            noiseScale = noiseScale,
            globalNoiseStrength = globalNoiseStrength,
            blendSmoothness = blendSmoothness,
            heightInfluence = heightInfluence,
            slopeInfluence = slopeInfluence,
            canyonCliffBoost = canyonCliffBoost,
            smoothingPasses = smoothingPasses,
            enforceDrySurface = enforceDrySurface,
            drySurfaceSmoothness = drySurfaceSmoothness
        };
    }

    private void TryAutoDetectTerrain()
    {
        if (targetTerrain == null)
        {
            targetTerrain = TerrainPainterUtility.GetActiveTerrain();
            RestoreRuleLayerBindings(GetSelectedPreset(), targetTerrain);
            AutoAssignMissingRuleLayers(GetSelectedPreset(), targetTerrain);
        }
    }

    private void SaveSelectedPresetAsAsset()
    {
        BiomePreset selected = GetSelectedPreset();
        if (selected == null)
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder(DefaultCustomPresetPath))
        {
            AssetDatabase.CreateFolder("Assets", "SmartTerrainPainter");
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Biome Preset",
            $"{selected.biomeName}_Preset",
            "asset",
            "Choose where to save the biome preset.",
            DefaultCustomPresetPath);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        BiomePreset copy = CreateInstance<BiomePreset>();
        CopyPresetValues(selected, copy);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RefreshPresetEntries();
    }

    private void RefreshPresetEntriesIfNeeded()
    {
        if (presetEntries.Count == 0)
        {
            RefreshPresetEntries();
        }
    }

    private void RefreshPresetEntries()
    {
        if (builtInPresets.Count == 0)
        {
            CreateBuiltInPresets();
        }

        BiomePreset previouslySelected = GetSelectedPreset();
        presetEntries.Clear();

        foreach (BiomePreset builtIn in builtInPresets)
        {
            presetEntries.Add(new PresetEntry($"Built-In / {builtIn.biomeName}", builtIn, true));
        }

        string[] guids = AssetDatabase.FindAssets("t:BiomePreset");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BiomePreset loaded = AssetDatabase.LoadAssetAtPath<BiomePreset>(path);
            if (loaded == null)
            {
                continue;
            }

            if (presetEntries.Any(e => e.preset == loaded))
            {
                continue;
            }

            presetEntries.Add(new PresetEntry($"Asset / {loaded.biomeName}", loaded, false));
        }

        if (presetEntries.Count == 0)
        {
            selectedPresetIndex = 0;
            return;
        }

        if (previouslySelected != null)
        {
            int idx = presetEntries.FindIndex(e => e.preset == previouslySelected);
            selectedPresetIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            selectedPresetIndex = Mathf.Clamp(selectedPresetIndex, 0, presetEntries.Count - 1);
        }

        if (autoApplyPresetValues)
        {
            ApplySelectedPresetDefaults();
        }
    }

    private BiomePreset GetSelectedPreset()
    {
        if (presetEntries.Count == 0)
        {
            return null;
        }

        selectedPresetIndex = Mathf.Clamp(selectedPresetIndex, 0, presetEntries.Count - 1);
        return presetEntries[selectedPresetIndex].preset;
    }

    private void ApplySelectedPresetDefaults()
    {
        BiomePreset preset = GetSelectedPreset();
        if (preset == null)
        {
            return;
        }

        noiseScale = preset.noiseScale;
        globalNoiseStrength = preset.globalNoiseStrength;
        blendSmoothness = preset.blendSmoothness;
        heightInfluence = preset.heightInfluence;
        slopeInfluence = preset.slopeInfluence;
        canyonCliffBoost = preset.canyonCliffBoost;
        smoothingPasses = 2;
        enforceDrySurface = preset.enforceDrySurface;
        drySurfaceSmoothness = preset.drySurfaceSmoothness;
        RestoreRuleLayerBindings(preset, targetTerrain);
        AutoAssignMissingRuleLayers(preset, targetTerrain);
        SaveRuleLayerBindings(preset, targetTerrain);
    }

    private static void CopyPresetValues(BiomePreset src, BiomePreset dst)
    {
        dst.biomeName = src.biomeName;
        dst.description = src.description;
        dst.noiseScale = src.noiseScale;
        dst.globalNoiseStrength = src.globalNoiseStrength;
        dst.blendSmoothness = src.blendSmoothness;
        dst.heightInfluence = src.heightInfluence;
        dst.slopeInfluence = src.slopeInfluence;
        dst.canyonCliffBoost = src.canyonCliffBoost;
        dst.enforceDrySurface = src.enforceDrySurface;
        dst.drySurfaceSmoothness = src.drySurfaceSmoothness;
        dst.rules = new List<BiomeRule>();

        if (src.rules == null)
        {
            return;
        }

        foreach (BiomeRule rule in src.rules)
        {
            if (rule == null)
            {
                continue;
            }

            dst.rules.Add(new BiomeRule
            {
                ruleName = rule.ruleName,
                layerHint = rule.layerHint,
                terrainLayer = rule.terrainLayer,
                minHeight = rule.minHeight,
                maxHeight = rule.maxHeight,
                minSlope = rule.minSlope,
                maxSlope = rule.maxSlope,
                noiseStrength = rule.noiseStrength,
                blendStrength = rule.blendStrength
            });
        }
    }

    private void CreateBuiltInPresets()
    {
        DestroyBuiltInPresets();
        builtInPresets.Clear();

        builtInPresets.Add(CreateRedCanyonPreset());
        builtInPresets.Add(CreateDesertBasinPreset());
        builtInPresets.Add(CreateRockyBadlandsPreset());
        builtInPresets.Add(CreateOasisPreset());
    }

    private void DestroyBuiltInPresets()
    {
        for (int i = 0; i < builtInPresets.Count; i++)
        {
            if (builtInPresets[i] != null)
            {
                DestroyImmediate(builtInPresets[i]);
            }
        }
    }

    private void DestroyPreviewTextures()
    {
        DestroyImmediateIfNotNull(slopePreview);
        DestroyImmediateIfNotNull(heightPreview);
        DestroyImmediateIfNotNull(biomeMaskPreview);
        slopePreview = null;
        heightPreview = null;
        biomeMaskPreview = null;
    }

    private static void DestroyImmediateIfNotNull(Object obj)
    {
        if (obj != null)
        {
            DestroyImmediate(obj);
        }
    }

    private static BiomePreset CreatePreset(string biomeName, string description)
    {
        BiomePreset preset = CreateInstance<BiomePreset>();
        preset.hideFlags = HideFlags.HideAndDontSave;
        preset.biomeName = biomeName;
        preset.description = description;
        return preset;
    }

    private static BiomeRule Rule(
        string ruleName,
        string layerHint,
        float minHeight,
        float maxHeight,
        float minSlope,
        float maxSlope,
        float noiseStrength,
        float blendStrength)
    {
        return new BiomeRule
        {
            ruleName = ruleName,
            layerHint = layerHint,
            minHeight = minHeight,
            maxHeight = maxHeight,
            minSlope = minSlope,
            maxSlope = maxSlope,
            noiseStrength = noiseStrength,
            blendStrength = blendStrength
        };
    }

    private static BiomePreset CreateRedCanyonPreset()
    {
        BiomePreset preset = CreatePreset(
            "Red Canyon",
            "Stylized-realistic canyon blend with sand plateaus, layered rock on medium slopes, and dark cliffs.");

        preset.noiseScale = 0.012f;
        preset.globalNoiseStrength = 0.45f;
        preset.blendSmoothness = 0.30f;
        preset.heightInfluence = 1.15f;
        preset.slopeInfluence = 1.15f;
        preset.canyonCliffBoost = true;
        preset.enforceDrySurface = true;
        preset.drySurfaceSmoothness = 0f;

        preset.rules = new List<BiomeRule>
        {
            Rule("Red Sand Plateau", "Red Sand", 0.05f, 0.95f, 0f, 16f, 0.18f, 1.00f),
            Rule("Pebble Scatter Mix", "Pebble Scatter", 0.10f, 0.85f, 0f, 18f, 0.65f, 0.25f),
            Rule("Layered Canyon Rock", "Layered Canyon Rock", 0.08f, 1.00f, 15f, 42f, 0.25f, 0.95f),
            Rule("Dark Cliff Rock", "Dark Cliff Rock", 0.12f, 1.00f, 40f, 90f, 0.10f, 1.20f),
            Rule("High Rock Exposure", "Layered Canyon Rock", 0.70f, 1.00f, 8f, 65f, 0.22f, 0.35f)
        };
        return preset;
    }

    private static BiomePreset CreateDesertBasinPreset()
    {
        BiomePreset preset = CreatePreset(
            "Desert Basin",
            "Fine sand plains with dune breakups, gravel on slopes, and dry salt in low basins.");

        preset.noiseScale = 0.010f;
        preset.globalNoiseStrength = 0.50f;
        preset.blendSmoothness = 0.25f;
        preset.heightInfluence = 1.10f;
        preset.slopeInfluence = 1.00f;
        preset.canyonCliffBoost = false;
        preset.enforceDrySurface = true;
        preset.drySurfaceSmoothness = 0f;

        preset.rules = new List<BiomeRule>
        {
            Rule("Fine Sand Flats", "Fine Sand", 0.08f, 1.00f, 0f, 12f, 0.15f, 1.00f),
            Rule("Wind Dune Sand", "Wind Dune Sand", 0.18f, 0.88f, 0f, 20f, 0.90f, 0.75f),
            Rule("Slope Gravel", "Gravel", 0.00f, 1.00f, 14f, 55f, 0.20f, 1.00f),
            Rule("Dry Salt Patches", "Dry Salt", 0.00f, 0.25f, 0f, 10f, 0.75f, 0.60f)
        };
        return preset;
    }

    private static BiomePreset CreateRockyBadlandsPreset()
    {
        BiomePreset preset = CreatePreset(
            "Rocky Badlands",
            "Dusty flats with broken rock transitions, hard exposed patches, and cliff stone on steep faces.");

        preset.noiseScale = 0.016f;
        preset.globalNoiseStrength = 0.42f;
        preset.blendSmoothness = 0.28f;
        preset.heightInfluence = 0.95f;
        preset.slopeInfluence = 1.20f;
        preset.canyonCliffBoost = true;
        preset.enforceDrySurface = true;
        preset.drySurfaceSmoothness = 0f;

        preset.rules = new List<BiomeRule>
        {
            Rule("Dust Sand Flats", "Dust Sand", 0.00f, 1.00f, 0f, 12f, 0.12f, 1.00f),
            Rule("Broken Rock Slopes", "Broken Rock", 0.08f, 1.00f, 12f, 38f, 0.20f, 0.95f),
            Rule("Cliff Stone Steep", "Cliff Stone", 0.10f, 1.00f, 36f, 90f, 0.12f, 1.15f),
            Rule("Hard Rock Exposure", "Hard Rock", 0.15f, 1.00f, 6f, 50f, 0.95f, 0.45f)
        };
        return preset;
    }

    private static BiomePreset CreateOasisPreset()
    {
        BiomePreset preset = CreatePreset(
            "Oasis",
            "Wet low-elevation edges with grass flats, dirt slopes, and stone on steep terrain.");

        preset.noiseScale = 0.013f;
        preset.globalNoiseStrength = 0.30f;
        preset.blendSmoothness = 0.35f;
        preset.heightInfluence = 1.25f;
        preset.slopeInfluence = 1.05f;
        preset.canyonCliffBoost = false;
        preset.enforceDrySurface = false;
        preset.drySurfaceSmoothness = 0.05f;

        preset.rules = new List<BiomeRule>
        {
            Rule("Wet Sand Lowlands", "Wet Sand", 0.00f, 0.22f, 0f, 20f, 0.35f, 1.05f),
            Rule("Grass Flats", "Grass", 0.05f, 0.75f, 0f, 16f, 0.20f, 1.00f),
            Rule("Dirt Slopes", "Dirt", 0.06f, 1.00f, 12f, 40f, 0.20f, 0.90f),
            Rule("Stone Steep", "Stone", 0.10f, 1.00f, 35f, 90f, 0.10f, 1.10f)
        };
        return preset;
    }

    private static void AutoAssignMissingRuleLayers(BiomePreset preset, Terrain terrain, bool overwriteExisting = false)
    {
        if (preset == null || terrain == null || terrain.terrainData == null || preset.rules == null)
        {
            return;
        }

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        foreach (BiomeRule rule in preset.rules)
        {
            if (rule == null)
            {
                continue;
            }

            if (!overwriteExisting && rule.terrainLayer != null)
            {
                continue;
            }

            TerrainLayer byHint = FindLayerByHint(terrainLayers, rule.layerHint);
            if (byHint != null)
            {
                rule.terrainLayer = byHint;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rule.ruleName))
            {
                TerrainLayer byRuleName = FindLayerByHint(terrainLayers, rule.ruleName);
                if (byRuleName != null)
                {
                    rule.terrainLayer = byRuleName;
                }
            }
        }
    }

    private static TerrainLayer FindLayerByHint(TerrainLayer[] terrainLayers, string hint)
    {
        if (terrainLayers == null || terrainLayers.Length == 0 || string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        string normalizedHint = hint.Trim().ToLowerInvariant();
        for (int i = 0; i < terrainLayers.Length; i++)
        {
            TerrainLayer layer = terrainLayers[i];
            if (layer == null)
            {
                continue;
            }

            if (layer.name.ToLowerInvariant().Contains(normalizedHint))
            {
                return layer;
            }
        }

        return null;
    }

    private static void SaveRuleLayerBindings(BiomePreset preset, Terrain terrain)
    {
        if (preset == null || terrain == null || terrain.terrainData == null || preset.rules == null)
        {
            return;
        }

        string terrainKey = BuildTerrainKey(terrain);
        if (string.IsNullOrEmpty(terrainKey))
        {
            return;
        }

        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            if (rule == null) continue;

            string key = BuildRuleBindingKey(preset, rule, terrainKey, i);
            if (rule.terrainLayer == null)
            {
                EditorPrefs.DeleteKey(key);
                continue;
            }

            string layerPath = AssetDatabase.GetAssetPath(rule.terrainLayer);
            if (string.IsNullOrWhiteSpace(layerPath))
            {
                EditorPrefs.DeleteKey(key);
                continue;
            }

            EditorPrefs.SetString(key, layerPath);
        }
    }

    private static void RestoreRuleLayerBindings(BiomePreset preset, Terrain terrain)
    {
        if (preset == null || terrain == null || terrain.terrainData == null || preset.rules == null)
        {
            return;
        }

        string terrainKey = BuildTerrainKey(terrain);
        if (string.IsNullOrEmpty(terrainKey))
        {
            return;
        }

        for (int i = 0; i < preset.rules.Count; i++)
        {
            BiomeRule rule = preset.rules[i];
            if (rule == null) continue;

            string key = BuildRuleBindingKey(preset, rule, terrainKey, i);
            string layerPath = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrWhiteSpace(layerPath)) continue;

            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer != null)
            {
                rule.terrainLayer = layer;
            }
        }
    }

    private static string BuildTerrainKey(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null) return string.Empty;
        string terrainDataPath = AssetDatabase.GetAssetPath(terrain.terrainData);
        if (!string.IsNullOrWhiteSpace(terrainDataPath)) return terrainDataPath;
        return terrain.terrainData.name;
    }

    private static string BuildRuleBindingKey(BiomePreset preset, BiomeRule rule, string terrainKey, int ruleIndex)
    {
        string presetKey = string.IsNullOrWhiteSpace(preset.biomeName) ? "Preset" : preset.biomeName;
        string ruleKey = !string.IsNullOrWhiteSpace(rule.ruleName) ? rule.ruleName : $"Rule{ruleIndex}";
        return $"{LayerBindingKeyPrefix}|{terrainKey}|{presetKey}|{ruleKey}";
    }

    private struct PresetEntry
    {
        public string displayName;
        public BiomePreset preset;
        public bool isBuiltIn;

        public PresetEntry(string displayName, BiomePreset preset, bool isBuiltIn)
        {
            this.displayName = displayName;
            this.preset = preset;
            this.isBuiltIn = isBuiltIn;
        }
    }
}
