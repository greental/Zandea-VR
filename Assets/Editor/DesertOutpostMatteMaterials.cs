using System;
using System.IO;
using System.Text.RegularExpressions;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DesertOutpostMatteMaterials
{
    const string ModelFolder = "Assets/MyAssets/mesh/buildings/DesertOutpost";
    const string PrefabFolder = "Assets/MyAssets/prefabs/buildings/desertOutpost";
    const string MaterialFolder = "Assets/MyAssets/materials/buildings/desertOutpost";
    const string Request = "Temp/DesertOutpostMatte.request";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        if (File.Exists(Request)) EditorApplication.update += RunWhenReady;
    }

    static void RunWhenReady()
    {
        if (!File.Exists(Request)) { EditorApplication.update -= RunWhenReady; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= RunWhenReady;
        File.Delete(Request);
        Apply();
    }

    [MenuItem("Tools/Koombar/Desert Outpost/Make Materials Editable And Matte")]
    public static void Apply()
    {
        EnsureFolder(MaterialFolder);
        int changedPrefabs = 0, changedRenderers = 0;
        foreach (string path in Directory.GetFiles(MaterialFolder, "*.mat"))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path.Replace('\\', '/'));
            if (material) MakeMatte(material);
        }
        foreach (string path in Directory.GetFiles(PrefabFolder, "*.prefab"))
        {
            string assetPath = path.Replace('\\', '/');
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                int count = ApplyToRenderers(root);
                if (count == 0) continue;
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                changedPrefabs++;
                changedRenderers += count;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var planter in UnityEngine.Object.FindObjectsByType<ExistingTreadmillPoiPlanter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!planter.gameObject.scene.IsValid()) continue;
            var preview = planter.transform.Find("Scavenger POIs (Scene Preview)");
            if (!preview) continue;
            int count = ApplyToRenderers(preview.gameObject);
            if (count == 0) continue;
            changedRenderers += count;
            EditorSceneManager.MarkSceneDirty(planter.gameObject.scene);
            EditorSceneManager.SaveScene(planter.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[DesertOutpostMatteMaterials] Updated {changedPrefabs} prefabs and {changedRenderers} renderers. Editable materials: {MaterialFolder}");
    }

    public static void ApplyToPrefab(string prefabPath)
    {
        EnsureFolder(MaterialFolder);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (ApplyToRenderers(root) > 0)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    static int ApplyToRenderers(GameObject root)
    {
        int changed = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool modified = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var matte = MatteFor(materials[i]);
                if (!matte || matte == materials[i]) continue;
                materials[i] = matte;
                modified = true;
            }
            if (!modified) continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
            changed++;
        }
        return changed;
    }

    static Material MatteFor(Material source)
    {
        if (!source) return null;
        string path = AssetDatabase.GetAssetPath(source);
        if (!path.StartsWith(ModelFolder + "/", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("Assets/MyAssets/mesh/buildings/AdobeMainComplete.blend", StringComparison.OrdinalIgnoreCase))
            return source;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId)) return source;
        string model = Path.GetFileNameWithoutExtension(path);
        string fileName = Regex.Replace($"{model}_{source.name}_{localId}", "[^A-Za-z0-9_-]", "_");
        string mattePath = $"{MaterialFolder}/{fileName}.mat";
        var matte = AssetDatabase.LoadAssetAtPath<Material>(mattePath);
        if (matte)
        {
            MakeMatte(matte);
            return matte;
        }
        matte = new Material(source) { name = $"{model} {source.name} Matte", enableInstancing = true };
        MakeMatte(matte);
        AssetDatabase.CreateAsset(matte, mattePath);
        return matte;
    }

    static void MakeMatte(Material matte)
    {
        SetIfPresent(matte, "_Smoothness", 0f);
        SetIfPresent(matte, "_Glossiness", 0f);
        SetIfPresent(matte, "_GlossMapScale", 0f);
        SetIfPresent(matte, "_Metallic", 0f);
        SetIfPresent(matte, "_MetallicFactor", 0f);
        SetIfPresent(matte, "_Roughness", 1f);
        SetIfPresent(matte, "_RoughnessFactor", 1f);
        SetIfPresent(matte, "metallicFactor", 0f);
        SetIfPresent(matte, "roughnessFactor", 1f);
        if (matte.HasProperty("metallicRoughnessTexture")) matte.SetTexture("metallicRoughnessTexture", null);
        EditorUtility.SetDirty(matte);
    }

    static void SetIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string piece in path.Substring("Assets/".Length).Split('/'))
        {
            string next = current + "/" + piece;
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, piece);
            current = next;
        }
    }
}
