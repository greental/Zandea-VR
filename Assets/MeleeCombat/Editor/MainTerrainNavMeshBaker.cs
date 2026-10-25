using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class MainTerrainNavMeshBaker
    {
        const string ScenePath = "Assets/Main.unity";
        const string DataPath = "Assets/MeleeCombat/MainTerrainNavMesh.asset";

        [MenuItem("Zandea/Melee Combat/Bake Main terrain NavMesh")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogError("Leave Play mode before baking the main terrain NavMesh."); return; }
            if (SceneManager.GetActiveScene().path != ScenePath)
            { Debug.LogError("Open Assets/Main.unity before baking the main terrain NavMesh."); return; }
            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (!surface) { Debug.LogError("Main Terrain Navigation surface is missing."); return; }
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (!surface.navMeshData)
            { Debug.LogError("No NavMesh was generated. Check that the terrain tiles and their colliders are enabled."); return; }
            var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath);
            if (saved)
            {
                EditorUtility.CopySerialized(surface.navMeshData, saved);
                Object.DestroyImmediate(surface.navMeshData);
            }
            else
            {
                saved = surface.navMeshData;
                AssetDatabase.CreateAsset(saved, DataPath);
            }
            surface.RemoveData();
            surface.navMeshData = saved;
            surface.AddData();
            var group = Object.FindFirstObjectByType<BanditGroup>();
            if (group)
            {
                group.buildLocalNavMesh = false;
                EditorUtility.SetDirty(group);
            }
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
            EditorSceneManager.SaveScene(surface.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Baked and saved main terrain NavMesh: " + DataPath);
        }
    }
}
