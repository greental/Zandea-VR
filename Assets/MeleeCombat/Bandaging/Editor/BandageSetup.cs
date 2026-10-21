#if UNITY_EDITOR
using JunkerBody;
using JunkerEquipment;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.Bandaging.Editor
{
    public static class BandageSetup
    {
        const string Source = "Assets/MyAssets/mesh/tools/bandage_roll.glb";
        const string Folder = "Assets/MeleeCombat/Bandaging";
        const string Prefab = "Assets/MyAssets/prefabs/medical/BandageRoll.prefab";
        const string Cloth = Folder + "/BandageCloth.mat";
        const string Player = "Assets/MyAssets/prefabs/Player/VR player - JunkerWeapon.prefab";
        const string TestScene = "Assets/MeleeCombat/MeleePOC.unity";

        [MenuItem("Kenshi VR/Bandaging/Build prefab and test scene")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (!source) { Debug.LogError("Bandage GLB is not imported: " + Source); return; }
            var cloth = AssetDatabase.LoadAssetAtPath<Material>(Cloth);
            if (!cloth)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                cloth = new Material(shader) { name = "Bandage cloth" };
                var renderer = source.GetComponentInChildren<Renderer>(true);
                if (renderer && renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_BaseColor"))
                    cloth.SetColor("_BaseColor", renderer.sharedMaterial.GetColor("_BaseColor"));
                else cloth.SetColor("_BaseColor", new Color(.82f, .78f, .65f));
                cloth.SetFloat("_Cull", 0f);
                AssetDatabase.CreateAsset(cloth, Cloth);
            }
            var root = new GameObject("Bandage roll");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = "Supplied bandage roll GLB";
                visual.transform.SetParent(root.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = default;
                bool found = false;
                foreach (var renderer in renderers)
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (found && bounds.size.magnitude > .0001f)
                {
                    float scale = .09f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    visual.transform.localScale = Vector3.one * scale;
                    visual.transform.localPosition = -bounds.center * scale;
                }
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(.14f, .10f, .14f);
                var body = root.AddComponent<Rigidbody>();
                body.mass = .12f; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.angularDamping = 3f;
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.throwOnDetach = false;
                var grip = new GameObject("Grip pose").transform;
                grip.SetParent(root.transform, false);
                grip.localPosition = new Vector3(0f, -.018f, 0f);
                grip.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var tip = new GameObject("Loose tip").transform;
                tip.SetParent(root.transform, false);
                tip.localPosition = new Vector3(.045f, 0f, 0f);
                var equipment = root.AddComponent<JunkerEquipmentItem>();
                equipment.itemId = "bandage_roll"; equipment.gripPose = grip;
                equipment.useGripTransformDirectly = true;
                equipment.heldCollidersAreTriggers = true;
                root.AddComponent<VRInventoryItem>();
                var roll = root.AddComponent<BandageRoll>();
                roll.looseTip = tip; roll.clothMaterial = cloth;
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            }
            finally { Object.DestroyImmediate(root); }
            ConfigurePlayer();
            ConfigureTestScene();
            AssetDatabase.SaveAssets();
            Debug.Log("Bandage prefab, player wounds, and MeleePOC test scene configured.");
        }
        static void ConfigurePlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(Player);
            try
            {
                var health = root.GetComponentInChildren<ScavengerPlayerHealth>(true);
                var body = root.GetComponentInChildren<VRBodyController>(true);
                if (!health || !body) { Debug.LogError("Player health or body missing in " + Player); return; }
                var wounds = health.GetComponent<PlayerWounds>();
                if (!wounds) wounds = health.gameObject.AddComponent<PlayerWounds>();
                wounds.body = body;
                PrefabUtility.SaveAsPrefabAsset(root, Player);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static void ConfigureTestScene()
        {
            var scene = EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Additive);
            try
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var existing in go.GetComponentsInChildren<BandageRoll>(true))
                        if (existing.name == "Test bandage roll") return;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                instance.name = "Test bandage roll";
                Vector3 position = new Vector3(0f, 1.15f, .45f);
                foreach (var go in scene.GetRootGameObjects())
                {
                    var body = go.GetComponentInChildren<VRBodyController>(true);
                    if (!body) continue;
                    position = body.transform.position + body.transform.forward * .45f + Vector3.up * 1.15f;
                    break;
                }
                instance.transform.position = position;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
#endif
