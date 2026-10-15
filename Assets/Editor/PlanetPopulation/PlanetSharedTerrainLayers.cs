using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Immutable, content-addressed TerrainLayer variants shared by generated planets.
/// A smoothness change gets a new variant rather than modifying another planet's paint.
/// </summary>
public static class PlanetSharedTerrainLayers
{
    const string Folder = "Assets/PlanetPopulation/SharedTerrainLayers";

    public static TerrainLayer GetFinished(TerrainLayer source, float smoothness)
    {
        if (!source) throw new ArgumentNullException(nameof(source));
        EnsureFolder();
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = "SharedLayer";
        copy.maskMapTexture = null;
        copy.metallic = 0f;
        copy.smoothness = Mathf.Clamp01(smoothness);
        var serialized = new SerializedObject(copy);
        var smoothnessSource = serialized.FindProperty("m_SmoothnessSource");
        if (smoothnessSource != null)
        {
            smoothnessSource.intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        string key = Hash(EditorJsonUtility.ToJson(copy));
        string path = $"{Folder}/Layer_{key}.terrainlayer";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (existing)
        {
            UnityEngine.Object.DestroyImmediate(copy);
            return existing;
        }
        copy.name = $"Shared Biome Layer {key}";
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    public static TerrainLayer GetNeutral()
    {
        EnsureFolder();
        const string layerPath = Folder + "/Neutral.terrainlayer";
        var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (existing) return existing;
        const string texturePath = Folder + "/NeutralTexture.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (!texture)
        {
            texture = new Texture2D(2, 2) { name = "Neutral Biome Texture" };
            texture.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            texture.Apply();
            AssetDatabase.CreateAsset(texture, texturePath);
        }
        var layer = new TerrainLayer { name = "Shared Neutral Biome Layer", diffuseTexture = texture, smoothness = 0f };
        AssetDatabase.CreateAsset(layer, layerPath);
        return layer;
    }

    public static int Bind(PlanetPopulationPoc population, PlanetTerrainTileSet set)
    {
        if (!population || !set || set.generationSettings?.terrainLayers == null) return 0;
        var old = set.generationSettings.terrainLayers;
        var shared = new TerrainLayer[old.Length];
        int replaced = 0;
        for (int i = 0; i < old.Length; i++)
        {
            if (!old[i]) continue;
            string path = AssetDatabase.GetAssetPath(old[i]);
            bool neutral = path.EndsWith("/Unpainted.terrainlayer", StringComparison.OrdinalIgnoreCase) ||
                path == Folder + "/Neutral.terrainlayer";
            shared[i] = neutral ? GetNeutral() : population.makeBakedLayersMatte
                ? GetFinished(old[i], population.bakedLayerSmoothness) : old[i];
            if (shared[i] != old[i]) replaced++;
        }
        if (replaced == 0) return 0;
        Undo.RecordObject(set, "Share generated biome layers");
        set.generationSettings.terrainLayers = shared;
        EditorUtility.SetDirty(set);
        foreach (var tile in set.authoredTiles)
        {
            if (tile?.terrainData == null) continue;
            Undo.RecordObject(tile.terrainData, "Share generated biome layers");
            tile.terrainData.terrainLayers = shared;
            tile.terrainData.SetBaseMapDirty();
            EditorUtility.SetDirty(tile.terrainData);
        }
        var treadmill = population.GetComponent<PlanetTerrainTreadmill>();
        if (treadmill)
        {
            var serialized = new SerializedObject(treadmill);
            var layers = serialized.FindProperty("terrainLayers");
            layers.arraySize = shared.Length;
            for (int i = 0; i < shared.Length; i++)
                layers.GetArrayElementAtIndex(i).objectReferenceValue = shared[i];
            serialized.ApplyModifiedProperties();
            foreach (var terrain in treadmill.GetComponentsInChildren<Terrain>(true)) terrain.Flush();
        }
        AssetDatabase.SaveAssets();
        return replaced;
    }

    public static void RemoveNewNeutralCopies(string tileSetPath)
    {
        string folder = Path.GetDirectoryName(tileSetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder)) return;
        AssetDatabase.DeleteAsset(folder + "/Unpainted.terrainlayer");
        AssetDatabase.DeleteAsset(folder + "/UnpaintedTexture.asset");
    }

    [MenuItem("Tools/Planet Population/Consolidate Generated Terrain Layers")]
    public static void ConsolidateGenerated()
    {
        const string generated = "Assets/PlanetPopulation/Generated";
        var oldPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int changedSets = 0, changedLayers = 0;
        var treadmills = UnityEngine.Object.FindObjectsByType<PlanetTerrainTreadmill>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (string guid in AssetDatabase.FindAssets("t:PlanetTerrainTileSet", new[] { generated }))
        {
            string setPath = AssetDatabase.GUIDToAssetPath(guid);
            var set = AssetDatabase.LoadAssetAtPath<PlanetTerrainTileSet>(setPath);
            var old = set?.generationSettings?.terrainLayers;
            if (old == null || old.Length == 0) continue;
            var shared = (TerrainLayer[])old.Clone();
            int local = 0;
            for (int i = 0; i < old.Length; i++)
            {
                if (!old[i]) continue;
                string path = AssetDatabase.GetAssetPath(old[i]);
                if (!path.StartsWith(generated + "/", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(path);
                if (name == "Unpainted.terrainlayer")
                {
                    shared[i] = GetNeutral();
                    oldPaths.Add(path);
                    oldPaths.Add(Path.GetDirectoryName(path)?.Replace('\\', '/') + "/UnpaintedTexture.asset");
                }
                else if (name.StartsWith("Layer_", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".terrainlayer", StringComparison.OrdinalIgnoreCase))
                {
                    shared[i] = GetFinished(old[i], old[i].smoothness);
                    oldPaths.Add(path);
                }
                if (shared[i] != old[i]) local++;
            }
            if (local == 0) continue;
            changedSets++;
            changedLayers += local;
            set.generationSettings.terrainLayers = shared;
            EditorUtility.SetDirty(set);
            foreach (var tile in set.authoredTiles)
            {
                if (tile?.terrainData == null) continue;
                tile.terrainData.terrainLayers = shared;
                tile.terrainData.SetBaseMapDirty();
                EditorUtility.SetDirty(tile.terrainData);
            }
            foreach (var treadmill in treadmills)
            {
                if (!treadmill || treadmill.AuthoredTileSet != set) continue;
                var serialized = new SerializedObject(treadmill);
                var layers = serialized.FindProperty("terrainLayers");
                layers.arraySize = shared.Length;
                for (int i = 0; i < shared.Length; i++)
                    layers.GetArrayElementAtIndex(i).objectReferenceValue = shared[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        AssetDatabase.SaveAssets();
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded && scene.path.StartsWith("Assets/"))
            EditorSceneManager.SaveScene(scene);

        // Delete only files with no remaining direct references anywhere in Assets.
        // This protects other scenes or prefabs that might still use an old layer.
        int deleted = 0;
        if (oldPaths.Count > 0)
        {
            string[] paths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/") && !oldPaths.Contains(path)).ToArray();
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int batch = 256;
            for (int i = 0; i < paths.Length; i += batch)
                foreach (string dependency in AssetDatabase.GetDependencies(paths.Skip(i).Take(batch).ToArray(), false))
                    dependencies.Add(dependency);
            foreach (string oldPath in oldPaths)
                if (!dependencies.Contains(oldPath) && AssetDatabase.LoadMainAssetAtPath(oldPath) &&
                    AssetDatabase.DeleteAsset(oldPath)) deleted++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Consolidated {changedLayers} layer slots across {changedSets} generated tile sets; deleted {deleted} unused per-planet layer/neutral assets. Shared variants are in {Folder}.");
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/PlanetPopulation", "SharedTerrainLayers");
    }

    static string Hash(string value)
    {
        using var algorithm = SHA256.Create();
        byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(bytes, 0, 10).Replace("-", "").ToLowerInvariant();
    }
}
