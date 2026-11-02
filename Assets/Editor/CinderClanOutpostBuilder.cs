using System;
using System.IO;
using System.Text;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEngine;

public static class CinderClanOutpostBuilder
{
    const string Source = "Assets/MyAssets/mesh/buildings/CinderClan";
    const string Output = "Assets/MyAssets/prefabs/buildings/cinderClan";
    const string Request = "Temp/CinderClanBuild.request";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        if (File.Exists(Request)) EditorApplication.delayCall += RunRequested;
    }

    static void RunRequested()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunRequested;
            return;
        }
        File.Delete(Request);
        Build();
    }

    [MenuItem("Zandea/Koombar/Cinder Clan/Build Outpost Prefabs")]
    public static void Build()
    {
        EnsureFolder(Output);
        var files = Directory.GetFiles(Source, "*.glb", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var report = new StringBuilder("Source,Prefab,Category,Width,Height,Depth,Triangles\n");
        int built = 0;
        foreach (string file in files)
        {
            string path = file.Replace('\\', '/');
            try
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model) { Debug.LogError("Cinder Clan model import failed: " + path); continue; }
                string raw = Path.GetFileNameWithoutExtension(file);
                string name = Clean(raw);
                string category = Category(raw);
                var root = new GameObject(name);
                try
                {
                    var visual = PrefabUtility.InstantiatePrefab(model) as GameObject;
                    if (!visual) visual = UnityEngine.Object.Instantiate(model);
                    visual.name = "Visual";
                    visual.transform.SetParent(root.transform, false);
                    if (!BoundsOf(visual, out var bounds)) { Debug.LogError("Cinder Clan model has no renderer: " + path); continue; }
                    float measure = category == "Tower" || category == "Column" ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
                    visual.transform.localScale = Vector3.one * (TargetSize(category) / Mathf.Max(.01f, measure));
                    if (!BoundsOf(visual, out bounds)) continue;
                    visual.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                    if (!BoundsOf(visual, out bounds)) continue;
                    AddCollision(root, category, bounds.size);
                    if (category == "Building" || category == "Tower")
                    {
                        var module = root.AddComponent<OutpostModule>();
                        module.footprint = new Vector2(bounds.size.x, bounds.size.z);
                        module.clearance = 3f;
                    }
                    string prefabPath = Output + "/" + name + ".prefab";
                    if (!PrefabUtility.SaveAsPrefabAsset(root, prefabPath)) throw new IOException("Could not save " + prefabPath);
                    int triangles = 0;
                    foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh) triangles += filter.sharedMesh.triangles.Length / 3;
                    report.AppendLine($"{Csv(raw)},{Csv(name)},{category},{bounds.size.x:F2},{bounds.size.y:F2},{bounds.size.z:F2},{triangles}");
                    built++;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            catch (Exception e) { Debug.LogException(e); }
        }
        string reportPath = Output + "/Dimensions.csv";
        File.WriteAllText(reportPath, report.ToString());
        AssetDatabase.ImportAsset(reportPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CinderClanOutpostBuilder] Built {built}/{files.Length} prefabs at {Output}");
    }

    static string Category(string value)
    {
        string lower = value.ToLowerInvariant();
        if (lower.Contains("door") || value.Contains("דלת") || value.Contains("פתח")) return "Gate";
        if (lower.Contains("wall") || lower.Contains("frame") || lower.Contains("window")) return "Wall";
        if (value.Contains("מגדל") || value.Contains("צריח")) return "Tower";
        if (value.Contains("שער") || value.Contains("פתח מקושת")) return "Gate";
        if (value.Contains("מדרגות")) return "Stairs";
        if (value.Contains("סככת")) return "Canopy";
        if (value.Contains("קיר") || value.Contains("חומה")) return "Wall";
        if (value.Contains("עמוד") || value.Contains("תושבת")) return "Column";
        if (value.Contains("בית") || value.Contains("מבנה") || value.Contains("גג")) return "Building";
        return "Prop";
    }

    static float TargetSize(string category)
    {
        switch (category)
        {
            case "Building": return 10f;
            case "Tower": return 12f;
            case "Gate": return 8f;
            case "Wall": return 6f;
            case "Canopy": return 8f;
            case "Stairs": return 6f;
            case "Column": return 4f;
            default: return 3f;
        }
    }

    static void AddCollision(GameObject root, string category, Vector3 size)
    {
        if (category == "Gate")
        {
            float post = Mathf.Clamp(size.x * .15f, .25f, 1f);
            Box(root, "Left Post", new Vector3(-size.x / 2 + post / 2, size.y / 2, 0), new Vector3(post, size.y, size.z));
            Box(root, "Right Post", new Vector3(size.x / 2 - post / 2, size.y / 2, 0), new Vector3(post, size.y, size.z));
            Box(root, "Lintel", new Vector3(0, size.y * .9f, 0), new Vector3(size.x, size.y * .2f, size.z));
        }
        else if (category == "Canopy")
        {
            Box(root, "Roof", new Vector3(0, size.y * .94f, 0), new Vector3(size.x, size.y * .12f, size.z));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box(root, "Post", new Vector3(x * size.x * .45f, size.y * .5f, z * size.z * .45f), new Vector3(.25f, size.y, .25f));
        }
        else if (category == "Stairs")
        {
            for (int i = 0; i < 4; i++)
                Box(root, "Step", new Vector3(0, size.y * (i + 1) / 8f, -size.z / 2 + size.z * (i + .5f) / 4f), new Vector3(size.x, size.y * (i + 1) / 4f, size.z / 4f));
        }
        else Box(root, "Collision", new Vector3(0, size.y / 2, 0), size);
    }

    static void Box(GameObject root, string name, Vector3 center, Vector3 size)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        var collider = child.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }

    static bool BoundsOf(GameObject root, out Bounds result)
    {
        result = default;
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!found) { result = renderer.bounds; found = true; }
            else result.Encapsulate(renderer.bounds);
        }
        return found;
    }

    static string Clean(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value) result.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return result.ToString().Trim('_');
    }

    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
