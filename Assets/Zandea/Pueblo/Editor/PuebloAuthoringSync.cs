using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using KenshiVR.MeleeCombat;
using Zandea.POI;

namespace Zandea.Pueblo.Editor
{
    /// <summary>Turns the saved, hand-arranged review settlement into Main's runtime layout.</summary>
    public static class PuebloAuthoringSync
    {
        const string Review = "Assets/Scenes/Zandea/PuebloSettlementReview.unity";
        const string Main = "Assets/Main.unity";
        const string Props = "Assets/MyAssets/prefabs/buildings/props";
        const string Pueblo = "Assets/MyAssets/prefabs/buildings/PuebloBonito";
        const string AuthoredPrefab = Pueblo + "/PuebloAuthoredSettlement.prefab";

        [MenuItem("Zandea/POI/Pueblo/Apply Authored Review to Main _F8")]
        public static void Apply() => Sync(true);

        public static void SyncIfAuthored() => Sync(false);

        static void Sync(bool requireAuthored)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before syncing the authored settlement.");
            if (new DriveInfo("D:\\").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("D: has less than 20 GiB free.");
            var review = SceneManager.GetSceneByPath(Review);
            bool closeReview = !review.isLoaded;
            if (closeReview) review = EditorSceneManager.OpenScene(Review, OpenSceneMode.Additive);
            try
            {
                var poi = review.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true))
                    .FirstOrDefault(p => p.poiId == "zandea.pueblo.settlement.1.review");
                if (!poi) throw new InvalidOperationException("The saved Pueblo review POI is missing.");
                var layout = poi.transform.Find("Layout Preview") ?? poi.transform.Find("Generated Layout Preview");
                if (!layout) throw new InvalidOperationException("The review layout is missing.");
                bool authored = layout.name == "Layout Preview" ||
                    layout.Cast<Transform>().Count(c => c.name.StartsWith("Building - ")) > poi.definition.buildingCount;
                if (!authored)
                {
                    if (requireAuthored) throw new InvalidOperationException("The review scene has no authored layout.");
                    return;
                }
                OrganizePrefabs();
                int adopted = 0;
                foreach (var sceneRoot in review.GetRootGameObjects())
                {
                    if (sceneRoot == poi.gameObject || sceneRoot.GetComponent<Terrain>() ||
                        sceneRoot.GetComponent<Camera>() || sceneRoot.GetComponent<Light>() ||
                        sceneRoot.GetComponent<MyAssets.PlanetTerrain.PlanetTerrainTreadmill>() ||
                        sceneRoot.GetComponentInChildren<StarvingBandit>(true) ||
                        sceneRoot.name.Contains("StarvingBandit")) continue;
                    var relative = sceneRoot.transform.position - layout.position;
                    if (Mathf.Abs(relative.x) > 60 || Mathf.Abs(relative.z) > 60 ||
                        Mathf.Abs(relative.y) > 30) continue;
                    sceneRoot.transform.SetParent(layout, true);
                    adopted++;
                }
                // The author scaled wall height to 90%. Restore only length so adjoining
                // modules meet; retain the hand-set positions, height, and depth.
                int walls = 0;
                foreach (Transform child in layout)
                {
                    if (child.name.StartsWith("Wall - "))
                    {
                        var scale = child.localScale;
                        // South of the gate the author's module centers are 10.7 m
                        // apart; other runs are about 10 m apart.
                        scale.x = child.localPosition.z < -38f && Mathf.Abs(child.localPosition.x) < 40f
                            ? 1.10f : Mathf.Abs(child.localPosition.x) > 40f ? 1.06f : 1.03f;
                        child.localScale = scale;
                        walls++;
                    }
                    else if (child.name.StartsWith("Gate - "))
                    {
                        var scale = child.localScale;
                        scale.x = 1.04f;
                        child.localScale = scale;
                    }
                }
                if (walls < 25) throw new InvalidOperationException("Too few authored wall pieces to close the perimeter.");
                layout.name = "Layout Preview";
                EditorSceneManager.MarkSceneDirty(review);
                EditorSceneManager.SaveScene(review);
                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(layout.gameObject, AuthoredPrefab);
                if (!savedPrefab) throw new InvalidOperationException("Failed to save the authored Pueblo prefab.");
                InstallMain(savedPrefab);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Pueblo] Synced authored review to Main: {adopted} scene-root additions, {walls} length-fitted walls, " +
                    $"{layout.Cast<Transform>().Count(c => c.name.StartsWith("Building - "))} buildings, " +
                    $"{layout.Cast<Transform>().Count(c => c.name.StartsWith("Shade - "))} shades.");
            }
            finally { if (closeReview) EditorSceneManager.CloseScene(review, true); }
        }

        public static void OrganizePrefabs()
        {
            var parent = "Assets/MyAssets/prefabs/buildings";
            if (!AssetDatabase.IsValidFolder(parent)) throw new InvalidOperationException("Missing " + parent);
            if (AssetDatabase.IsValidFolder("Assets/Zandea/Props/Prefabs"))
                MoveFolderContents("Assets/Zandea/Props/Prefabs", Props);
            if (!AssetDatabase.IsValidFolder(Props)) AssetDatabase.CreateFolder(parent, "props");
            if (AssetDatabase.IsValidFolder("Assets/Zandea/Pueblo/Prefabs"))
                MoveFolderContents("Assets/Zandea/Pueblo/Prefabs", Pueblo);
            if (!AssetDatabase.IsValidFolder(Pueblo)) AssetDatabase.CreateFolder(parent, "PuebloBonito");
            foreach (var key in new[] { "basket", "earth_pot", "terracotta_pot", "round_jar", "tall_jar", "feeding_trough" })
            {
                var source = Pueblo + "/Pueblo_" + key + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source))
                    Move(source, Props + "/Pueblo_" + key + ".prefab");
            }
        }

        static void MoveFolderContents(string source, string target)
        {
            if (!AssetDatabase.IsValidFolder(target))
            {
                Move(source, target); // preserves the folder GUID and all prefab GUIDs
                return;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { source }))
            {
                var oldPath = AssetDatabase.GUIDToAssetPath(guid);
                var newPath = target + "/" + Path.GetFileName(oldPath);
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(newPath)) Move(oldPath, newPath);
            }
        }

        static void Move(string source, string target)
        {
            string error = AssetDatabase.MoveAsset(source, target);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"Move {source} to {target}: {error}");
        }

        static void InstallMain(GameObject prefab)
        {
            var main = SceneManager.GetSceneByPath(Main);
            bool closeMain = !main.isLoaded;
            if (closeMain) main = EditorSceneManager.OpenScene(Main, OpenSceneMode.Additive);
            try
            {
                var poi = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true))
                    .FirstOrDefault(p => p.poiId == "zandea.pueblo.settlement.1");
                if (!poi) throw new InvalidOperationException("Main's Pueblo POI is missing.");
                var generated = poi.transform.Find("Generated Layout Preview");
                var authored = poi.transform.Find("Layout Preview");
                if (!authored)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, main);
                    authored = instance.transform;
                    authored.SetParent(poi.transform, false);
                    authored.SetPositionAndRotation(generated ? generated.position : poi.Center,
                        generated ? generated.rotation : poi.Rotation);
                    authored.name = "Layout Preview";
                }
                if (generated) generated.gameObject.SetActive(false);
                EditorSceneManager.MarkSceneDirty(main);
                EditorSceneManager.SaveScene(main);
            }
            finally { if (closeMain) EditorSceneManager.CloseScene(main, true); }
        }
    }
}
