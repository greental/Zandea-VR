using UnityEditor;
using UnityEngine;
using KenshiVR.CharacterCreator;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class ClothingLootBaker
    {
        const string ItemFolder = "Assets/MeleeCombat/Inventory";
        const string OutputFolder = "Assets/MeleeCombat/Inventory/GeneratedLoot";

        [InitializeOnLoadMethod]
        static void BakeMissingAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                var shirt = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(ItemFolder + "/Rag Shirt.asset");
                var pants = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(ItemFolder + "/Rag Pants.asset");
                if (shirt && pants && (!shirt.worldPrefab || !pants.worldPrefab || shirt.lootBakeVersion < 2 || pants.lootBakeVersion < 2)) BakeBanditClothing();
            };
        }

        [MenuItem("Kenshi VR/Loot/Bake Starving Bandit Clothing Prefabs")]
        public static void BakeBanditClothing()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder(ItemFolder, "GeneratedLoot");
            Bake(AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(ItemFolder + "/Rag Shirt.asset"));
            Bake(AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(ItemFolder + "/Rag Pants.asset"));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        public static void Bake(NpcItemDefinition item)
        {
            if (!item || !item.equippedAppearance || !item.equippedAppearance.prefab)
            { Debug.LogError("Clothing loot needs an appearance prefab", item); return; }
            string prefix = OutputFolder + "/" + item.itemId;
            var source = (GameObject)PrefabUtility.InstantiatePrefab(item.equippedAppearance.prefab);
            if (!source) source = Object.Instantiate(item.equippedAppearance.prefab);
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            source.transform.localScale = Vector3.one;
            var snapshot = ClothingLootFactory.Capture(source.transform, item);
            if (!snapshot.Valid)
            { snapshot.Dispose(); Object.DestroyImmediate(source); Debug.LogError("No clothing meshes to bake", item); return; }
            // Some imported garments (notably scarf_wrap_b) retain centimeter
            // coordinates in their authored prefab. Normalize only those large
            // cached meshes; the pants mesh is already in Unity meters.
            var lootScale = snapshot.Bounds.size.magnitude > 10f ? Vector3.one * .01f : Vector3.one;
            var loot = ClothingLootFactory.Create(item, snapshot, Vector3.zero, Quaternion.identity, lootScale);
            Object.DestroyImmediate(source);
            for (int i = 0; i < loot.transform.childCount; i++)
            {
                var filter = loot.transform.GetChild(i).GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                string path = prefix + "_mesh_" + i + ".asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var saved = Object.Instantiate(filter.sharedMesh);
                saved.name = item.itemId + "_loot_" + i;
                if (old)
                {
                    EditorUtility.CopySerialized(saved, old);
                    Object.DestroyImmediate(saved);
                    EditorUtility.SetDirty(old);
                    filter.sharedMesh = old;
                }
                else { AssetDatabase.CreateAsset(saved, path); filter.sharedMesh = saved; }
            }
            Object.DestroyImmediate(loot.GetComponent<ClothingLootMeshes>());
            if (!loot.GetComponent<NpcWorldItem>()) loot.AddComponent<NpcWorldItem>();
            string prefabPath = prefix + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(loot, prefabPath);
            Object.DestroyImmediate(loot);
            item.worldPrefab = prefab;
            item.preferCachedLoot = !item.equippedAppearance.skinned;
            item.lootBakeVersion = 2;
            EditorUtility.SetDirty(item);
            Debug.Log("Baked loot prefab: " + prefabPath, item);
        }
    }
}
