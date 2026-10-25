using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JunkerEquipment.Editor
{
    /// <summary>Repeatable model-to-primary-weapon build. Tune the anchors in the saved prefab afterward.</summary>
    [InitializeOnLoad]
    public static class JunkerPrimaryWeaponBuilder
    {
        static JunkerPrimaryWeaponBuilder() { EditorApplication.delayCall += BuildMissingPrefab; }
        static void BuildMissingPrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return;
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(GunPath) || !AssetDatabase.LoadAssetAtPath<GameObject>(ScopePath)) return;
            try { BuildRustFang(); }
            catch (Exception error) { Debug.LogError("RustFang automatic build failed: " + error); }
        }
        const string GunPath = "Assets/MyAssets/mesh/weapons/SniperRifles/RustFang/Rustfang.glb";
        const string ScopePath = "Assets/MyAssets/mesh/weapons/SniperRifles/RustFang/RustFangScope.glb";
        const string PrefabPath = "Assets/MyAssets/prefabs/weapons/RustFang.prefab";
        const string PlayerPath = "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab";
        const string ItemId = "RustFang";

        [MenuItem("Zandea/Junker Equipment/Build RustFang primary rifle")]
        public static void BuildRustFang()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var gun = AssetDatabase.LoadAssetAtPath<GameObject>(GunPath);
            var scope = AssetDatabase.LoadAssetAtPath<GameObject>(ScopePath);
            if (!gun || !scope) throw new InvalidOperationException("Import both RustFang GLBs before building the rifle.");
            GameObject root = null;
            bool loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            try
            {
                root = loaded ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(ItemId);
                var visual = Child(root.transform, "Visual");
                // Both GLBs were exported in the same coordinate frame. -X is the muzzle direction.
                visual.localRotation = Quaternion.Euler(0, 90, 0);
                var gunVisual = Model(gun, visual, "Gun mesh");
                var scopeVisual = Model(scope, visual, "Scope mesh");
                if (!loaded)
                {
                    var raw = BoundsOf(gunVisual);
                    visual.localScale = Vector3.one * (1.15f / Mathf.Max(.001f, raw.size.z));
                    var bounds = BoundsOf(gunVisual);
                    visual.position += root.transform.position - bounds.center;
                }
                var gunBounds = BoundsOf(gunVisual);
                var scopeBounds = BoundsOf(scopeVisual);
                var grip = Child(root.transform, "Grip");
                var muzzle = Child(root.transform, "Muzzle");
                var eyepiece = Child(root.transform, "Scope eyepiece");
                var objective = Child(root.transform, "Scope objective");
                if (!loaded)
                {
                    grip.position = new Vector3(gunBounds.center.x, gunBounds.min.y + .09f, gunBounds.min.z + .3f);
                    muzzle.position = new Vector3(gunBounds.center.x, gunBounds.center.y, gunBounds.max.z + .01f);
                    eyepiece.position = new Vector3(scopeBounds.center.x, scopeBounds.center.y, scopeBounds.min.z + .01f);
                    objective.position = new Vector3(scopeBounds.center.x, scopeBounds.center.y, scopeBounds.max.z - .01f);
                }
                muzzle.rotation = objective.rotation = eyepiece.rotation = root.transform.rotation;

                var kit = GetOrAdd<JunkerPistolKit>(root);
                kit.itemId = ItemId;
                kit.visualRoot = visual;
                kit.gripPose = grip;
                kit.muzzle = muzzle;
                kit.mass = 3.5f;
                kit.fireRate = 1.1f;
                kit.range = 500f;
                kit.damage = 85f;
                kit.recoilTravel = .025f;
                kit.holsterRadius = .38f;
                kit.addReflexDot = false;
                kit.InstallPistolComponents();
                root.GetComponent<JunkerEquipmentItem>().useGripTransformDirectly = true;
                root.GetComponent<JunkerEquipmentItem>().heldCollidersAreTriggers = true;

                var opticsCamera = GetOrAdd<Camera>(Child(root.transform, "Scope camera").gameObject);
                opticsCamera.enabled = false;
                opticsCamera.depth = -10;
                var optics = GetOrAdd<OpticalDevice>(root);
                optics.eyepiece = eyepiece;
                optics.objective = objective;
                optics.opticsCamera = opticsCamera;
                optics.requiredGrab = root.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                optics.immersiveViewing = true;
                optics.showReticle = true;
                optics.textureResolution = 1536;
                optics.viewingShader = Shader.Find("JunkerEquipment/OpticalEyepiece");
                if (!optics.viewingShader) throw new InvalidOperationException("OpticalEyepiece shader is missing.");
                optics.activationDistance = .18f;
                optics.activationAngle = 15f;
                optics.baseFieldOfView = 60f;
                optics.zoomLevels = new[] { 2f, 4f, 8f };
                optics.farClip = 1000f;
                GetOrAdd<JunkerScopeZoom>(root);

                // Avoid attaching the optics camera or anchors to the recoiling visual.
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (root)
                {
                    if (loaded) PrefabUtility.UnloadPrefabContents(root);
                    else UnityEngine.Object.DestroyImmediate(root);
                }
            }
            AssignPrimarySlot();
            AssetDatabase.SaveAssets();
            Debug.Log("Built RustFang and assigned it to JunkerWeapon's primary slot.");
        }

        static void AssignPrimarySlot()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!rifle) throw new InvalidOperationException("RustFang prefab was not saved.");
            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var slot = player.GetComponentsInChildren<JunkerEquipmentSlot>(true)
                    .FirstOrDefault(s => s.legacyAcceptedItemId == "primary");
                if (!slot) throw new InvalidOperationException("JunkerWeapon has no primary slot.");
                slot.radius = .38f;
                if (!slot.approvedItemIds.Contains(ItemId)) slot.approvedItemIds.Add(ItemId);
                var instance = slot.transform.Cast<Transform>().Select(t => t.GetComponent<JunkerEquipmentItem>())
                    .FirstOrDefault(i => i && i.itemId == ItemId);
                if (!instance)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(rifle, slot.transform);
                    go.name = ItemId;
                    instance = go.GetComponent<JunkerEquipmentItem>();
                }
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.rig = player.GetComponentInChildren<JunkerEquipmentRig>(true);
                slot.startingItem = instance;
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
        static GameObject Model(GameObject asset, Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing) return existing.gameObject;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = name;
            return instance;
        }
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var value = go.GetComponent<T>();
            return value ? value : go.AddComponent<T>();
        }
        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException(go.name + " has no renderer.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
