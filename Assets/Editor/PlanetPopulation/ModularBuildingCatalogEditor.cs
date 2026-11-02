using System.Text;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ModularBuildingCatalog))]
public sealed class ModularBuildingCatalogEditor : Editor
{
    [MenuItem("Zandea/POI/Modular Buildings/Create Empty Catalog")]
    static void CreateCatalog()
    {
        const string path = "Assets/PlanetPopulation/CinderClanModularBuildingCatalog.asset";
        if (AssetDatabase.LoadAssetAtPath<ModularBuildingCatalog>(path))
        { Selection.activeObject = AssetDatabase.LoadAssetAtPath<ModularBuildingCatalog>(path); return; }
        var catalog = CreateInstance<ModularBuildingCatalog>();
        AssetDatabase.CreateAsset(catalog, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Assign and measure modules manually. Wall length must be exactly 4 or 8 m. Roof entries describe one sloped surface; local +X must follow the ridge and +Z must point from eave to ridge. Set the eave midpoint, horizontal run and rise from the prefab mesh. Uncertain candidates are never assigned automatically.", MessageType.Info);
        if (GUILayout.Button("Report Cinder Clan prefab candidates")) Report();
    }

    [MenuItem("Zandea/POI/Modular Buildings/Report Cinder Clan Candidates")]
    static void Report()
    {
        const string folder = "Assets/MyAssets/prefabs/buildings/cinderClan";
        var output = new StringBuilder("Cinder Clan module candidates (review dimensions and geometry):\n");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) continue;
            string name = prefab.name.ToLowerInvariant();
            if (!name.Contains("wall") && !name.Contains("roof") &&
                !name.Contains("קיר") && !name.Contains("גג")) continue;
            string suggestion = name.Contains("roof") || name.Contains("גג") ? "possible roof; verify it is one slope" :
                name.Contains("door") || name.Contains("פתח") ? "possible door wall; verify clear opening" :
                name.Contains("window") || name.Contains("חלון") ? "possible window wall" : "possible solid wall";
            output.AppendLine($"{path} — {suggestion}");
        }
        Debug.Log(output.ToString());
    }
}

