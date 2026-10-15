using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEngine;

// Keeps source models untouched; rerun from the Tools menu after art changes.
public static class DesertOutpostPrefabBuilder
{
    const string SourceFolder = "Assets/MyAssets/mesh/buildings/DesertOutpost";
    const string OutputFolder = "Assets/MyAssets/prefabs/buildings/desertOutpost";
    const string ReportPath = "Assets/MyAssets/prefabs/buildings/desertOutpost/Dimensions.csv";
    const string RequestPath = "Temp/DesertOutpostBuild.request";
    const string SplitLightsRequestPath = "Temp/DesertOutpostSplitLights.request";

    [InitializeOnLoadMethod]
    static void ScheduleRequestedBuild()
    {
        if (File.Exists(RequestPath)) EditorApplication.delayCall += RunRequestedBuild;
        if (File.Exists(SplitLightsRequestPath)) EditorApplication.delayCall += RunRequestedSplitLights;
    }

    static void RunRequestedSplitLights()
    {
        if (!File.Exists(SplitLightsRequestPath)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunRequestedSplitLights;
            return;
        }
        File.Delete(SplitLightsRequestPath);
        BuildSplitLights();
    }

    [MenuItem("Tools/Koombar/Desert Outpost/Build Split Light Prefabs")]
    public static void BuildSplitLights()
    {
        EnsureFolder(OutputFolder);
        var report = new StringBuilder();
        foreach (string name in new[] { "Light_A", "Light_B" })
        {
            AssetDatabase.ImportAsset($"{SourceFolder}/{name}.glb", ImportAssetOptions.ForceSynchronousImport);
            if (BuildOne($"{SourceFolder}/{name}.glb", report))
                DesertOutpostMatteMaterials.ApplyToPrefab($"{OutputFolder}/{name}.prefab");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[DesertOutpostPrefabBuilder] Built Light_A and Light_B prefabs.");
    }

    static void RunRequestedBuild()
    {
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunRequestedBuild;
            return;
        }
        File.Delete(RequestPath);
        BuildAll();
    }

    [MenuItem("Tools/Koombar/Desert Outpost/Build Game Ready Prefabs")]
    public static void BuildAll()
    {
        EnsureFolder("Assets/MyAssets/prefabs/buildings");
        EnsureFolder(OutputFolder);
        var paths = new List<string>(Directory.GetFiles(SourceFolder, "*.glb", SearchOption.TopDirectoryOnly));
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        const string completePath = "Assets/MyAssets/mesh/buildings/AdobeMainComplete.blend";
        if (File.Exists(completePath)) paths.Add(completePath);
        var report = new StringBuilder("Asset,Category,SourceWidth,SourceHeight,SourceDepth,Scale,PrefabWidth,PrefabHeight,PrefabDepth,LOD0Triangles,LOD1Triangles,Collider\n");
        int created = 0;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i].Replace('\\', '/');
                EditorUtility.DisplayProgressBar("Desert Outpost Prefabs", $"Building {Path.GetFileName(path)} ({i + 1}/{paths.Count})", (i + 1f) / paths.Count);
                try
                {
                    if (BuildOne(path, report)) created++;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Debug.LogError("[DesertOutpostPrefabBuilder] Failed: " + path);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        File.WriteAllText(ReportPath, report.ToString());
        AssetDatabase.ImportAsset(ReportPath);
        AssignEmptyScavengerThemeSlots();
        AssetDatabase.SaveAssets();
        DesertOutpostMatteMaterials.Apply();
        Debug.Log($"[DesertOutpostPrefabBuilder] Built {created}/{paths.Count} prefabs at {OutputFolder}. Dimensions: {ReportPath}");
    }

    static bool BuildOne(string sourcePath, StringBuilder report)
    {
        string rawName = Path.GetFileNameWithoutExtension(sourcePath);
        string lodPath = $"{SourceFolder}/Optimized/{rawName}_LOD1.glb";
        AssetDatabase.ImportAsset(lodPath, ImportAssetOptions.ForceSynchronousImport);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        bool usingOptimizedAsPrimary = false;
        if (!source && sourcePath.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
        {
            source = AssetDatabase.LoadAssetAtPath<GameObject>(lodPath);
            usingOptimizedAsPrimary = source;
        }
        if (!source)
        {
            Debug.LogWarning("[DesertOutpostPrefabBuilder] Source model was not imported: " + sourcePath);
            return false;
        }
        string name = CleanName(rawName);
        string category = Category(rawName);
        var root = new GameObject(name);
        try
        {
            var visual = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (!visual) visual = UnityEngine.Object.Instantiate(source);
            visual.name = "LOD0 Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            if (!TryGetBounds(visual, out Bounds sourceBounds))
            {
                Debug.LogWarning("[DesertOutpostPrefabBuilder] No visible mesh: " + sourcePath);
                return false;
            }
            float sourceMeasure = category == "Antenna" || category == "Tower" || category == "Extractor" || category == "Tank" || category == "Light"
                ? sourceBounds.size.y : Mathf.Max(sourceBounds.size.x, sourceBounds.size.z);
            float scale = TargetMeters(rawName, category) / Mathf.Max(.001f, sourceMeasure);
            visual.transform.localScale = Vector3.one * scale;
            if (!TryGetBounds(visual, out Bounds scaledBounds)) return false;
            visual.transform.position += new Vector3(-scaledBounds.center.x, -scaledBounds.min.y, -scaledBounds.center.z);
            if (!TryGetBounds(visual, out Bounds finalBounds)) return false;
            int lod1Triangles = 0;
            var reducedSource = usingOptimizedAsPrimary ? null : AssetDatabase.LoadAssetAtPath<GameObject>(lodPath);
            if (reducedSource)
            {
                var reduced = PrefabUtility.InstantiatePrefab(reducedSource) as GameObject;
                if (reduced)
                {
                    reduced.name = "LOD1 Optimized";
                    reduced.transform.SetParent(root.transform, false);
                    reduced.transform.localPosition = visual.transform.localPosition;
                    reduced.transform.localRotation = visual.transform.localRotation;
                    reduced.transform.localScale = visual.transform.localScale;
                    var lodGroup = root.AddComponent<LODGroup>();
                    lodGroup.SetLODs(new[]
                    {
                        new LOD(.04f, visual.GetComponentsInChildren<Renderer>(true)),
                        new LOD(.00005f, reduced.GetComponentsInChildren<Renderer>(true))
                    });
                    lodGroup.RecalculateBounds();
                    lod1Triangles = TriangleCount(reduced);
                }
            }
            AddColliders(root, category, finalBounds.size);
            if (category == "Building" || category == "Tower")
            {
                var module = root.AddComponent<OutpostModule>();
                module.footprint = new Vector2(finalBounds.size.x, finalBounds.size.z);
                module.clearance = 3f;
            }
            string prefabPath = $"{OutputFolder}/{name}.prefab";
            if (!PrefabUtility.SaveAsPrefabAsset(root, prefabPath))
                throw new IOException("Could not save prefab " + prefabPath);
            int triangles = TriangleCount(visual);
            report.AppendLine($"{Csv(rawName)},{category},{sourceBounds.size.x:F3},{sourceBounds.size.y:F3},{sourceBounds.size.z:F3},{scale:F5},{finalBounds.size.x:F3},{finalBounds.size.y:F3},{finalBounds.size.z:F3},{triangles},{lod1Triangles},{ColliderName(category)}");
            return true;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static int TriangleCount(GameObject model)
    {
        int triangles = 0;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh) triangles += filter.sharedMesh.triangles.Length / 3;
        return triangles;
    }

    static void AssignEmptyScavengerThemeSlots()
    {
        var theme = AssetDatabase.LoadAssetAtPath<OutpostFactionTheme>("Assets/MyAssets/Terrain/PlanetTerrain/PopulationPOC/FrontierRaiders.asset");
        if (!theme) return;
        bool changed = false;
        if (theme.smallBuildings == null || theme.smallBuildings.Length == 0)
        {
            theme.smallBuildings = LoadPrefabs("Adobe", "Adobe2", "AdobeRound", "AdobeSmallDome");
            changed = true;
        }
        if (theme.mediumBuildings == null || theme.mediumBuildings.Length == 0)
        {
            theme.mediumBuildings = LoadPrefabs("AdobeLarge", "AdobeMain");
            changed = true;
        }
        if (theme.largeBuildings == null || theme.largeBuildings.Length == 0)
        {
            theme.largeBuildings = LoadPrefabs("AdobeMainComplete", "AdobeMain");
            changed = true;
        }
        if (theme.props == null || theme.props.Length == 0)
        {
            theme.props = LoadPrefabs("Crate", "CrateSmall", "CargoPallet", "Tank", "Moisture_extractor");
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(theme);
    }

    static GameObject[] LoadPrefabs(params string[] names)
    {
        var found = new List<GameObject>();
        foreach (string name in names)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputFolder}/{name}.prefab");
            if (prefab) found.Add(prefab);
        }
        return found.ToArray();
    }

    static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    static void AddColliders(GameObject root, string category, Vector3 size)
    {
        float x = Mathf.Max(.08f, size.x), y = Mathf.Max(.08f, size.y), z = Mathf.Max(.08f, size.z);
        if (category == "Gate")
        {
            float post = Mathf.Clamp(x * .16f, .3f, 1.2f);
            Box(root, "Left Post", new Vector3(-x * .5f + post * .5f, y * .5f, 0), new Vector3(post, y, z));
            Box(root, "Right Post", new Vector3(x * .5f - post * .5f, y * .5f, 0), new Vector3(post, y, z));
            Box(root, "Top Beam", new Vector3(0, y * .88f, 0), new Vector3(x, y * .24f, z));
        }
        else if (category == "Canopy")
        {
            float thickness = Mathf.Clamp(y * .12f, .12f, .5f);
            Box(root, "Roof", new Vector3(0, y - thickness * .5f, 0), new Vector3(x, thickness, z));
            float post = Mathf.Clamp(Mathf.Min(x, z) * .045f, .12f, .4f);
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    Box(root, "Support", new Vector3(ix * (x * .5f - post), y * .5f, iz * (z * .5f - post)), new Vector3(post, y, post));
        }
        else if (category == "Stairs")
        {
            for (int i = 0; i < 4; i++)
            {
                float depth = z / 4f;
                float height = y * (i + 1f) / 4f;
                Box(root, "Step " + (i + 1), new Vector3(0, height * .5f, -z * .5f + depth * (i + .5f)), new Vector3(x, height, depth));
            }
        }
        else
            Box(root, "Collision", new Vector3(0, y * .5f, 0), new Vector3(x, y, z));
    }

    static void Box(GameObject root, string name, Vector3 center, Vector3 size)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        var collider = child.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }

    static string Category(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("gate")) return "Gate";
        if (lower.Contains("wall")) return "Wall";
        if (lower.Contains("canopy")) return "Canopy";
        if (lower.Contains("stairs")) return "Stairs";
        if (lower.Contains("adobe")) return "Building";
        if (lower.Contains("tower")) return "Tower";
        if (lower.Contains("antena")) return "Antenna";
        if (lower.Contains("extractor")) return "Extractor";
        if (lower.Contains("tank")) return "Tank";
        if (lower.Contains("light")) return "Light";
        if (lower.Contains("pipe")) return "Pipe";
        return "Prop";
    }

    static float TargetMeters(string name, string category)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("adobemain")) return 18f;
        if (lower.Contains("adobelarge")) return 14f;
        if (lower.Contains("adobesmalldome")) return 6f;
        if (category == "Stairs") return 8f;
        if (lower.Contains("adobe")) return 10f;
        if (lower.Contains("cratesmall")) return 1.2f;
        if (lower.Contains("crate")) return 2f;
        if (lower.Contains("cargopallet")) return 2.5f;
        if (lower.Contains("gateside")) return 4f;
        switch (category)
        {
            case "Gate": return 10f;
            case "Wall": return 8f;
            case "Canopy": return 10f;
            case "Stairs": return 8f;
            case "Tower": return 10f;
            case "Antenna": return 8f;
            case "Extractor": return 6f;
            case "Tank": return 4f;
            case "Light": return 3f;
            case "Pipe": return 4f;
            default: return 2f;
        }
    }

    static string ColliderName(string category) => category == "Gate" || category == "Canopy" || category == "Stairs" ? "Compound Boxes" : "Box";
    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    static string CleanName(string value)
    {
        var builder = new StringBuilder();
        foreach (char c in value)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return builder.ToString().Trim('_');
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
