using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class ArmorShowcaseSceneBuilder
    {
        const string ScenePath = "Assets/CharacterCreator/ImportedArmor/ArmorFittingsFixed.unity";
        const string Items = "Assets/CharacterCreator/ImportedDownloads/Items";
        const string Weapons = "Assets/MeleeCombat/ImportedArmorWeapons";
        const string BanditPath = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        const string CatalogPath = "Assets/CharacterCreator/CosmeticCatalog.asset";
        static readonly Color[] SlotColors =
        {
            new(.24f,.18f,.12f), new(.18f,.13f,.09f), new(.20f,.22f,.25f), new(.20f,.18f,.14f),
            new(.16f,.19f,.15f), new(.13f,.16f,.19f), new(.18f,.14f,.12f), new(.14f,.16f,.20f)
        };

        [InitializeOnLoadMethod]
        static void BuildFixedSceneOnce()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) return;
                try { Build(); }
                catch (Exception error) { Debug.LogException(error); }
            };
        }

        [MenuItem("Zandea/Character Creator/Build Imported Armor Fittings Scene")]
        public static void Build()
        {
            var banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (!banditPrefab || !catalog) throw new InvalidOperationException("Bandit prefab or cosmetic catalog is missing");
            var definitions = AssetDatabase.FindAssets("t:AppearanceItemDefinition", new[] { Items })
                .Select(guid => AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(item => item && (item.name.StartsWith("bandit_", StringComparison.Ordinal) ||
                    item.name.StartsWith("paladin_", StringComparison.Ordinal)))
                .OrderBy(item => item.slot).ThenBy(item => item.name).ToArray();
            var weaponPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Weapons })
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(prefab => prefab && prefab.GetComponent<PhysicalMeleeWeapon>()).OrderBy(prefab => prefab.name).ToArray();
            if (definitions.Length != 29 || weaponPrefabs.Length != 3)
                throw new InvalidOperationException($"Expected 29 wearables and 3 weapons, found {definitions.Length} and {weaponPrefabs.Length}");

            for (int sceneIndex = SceneManager.sceneCount - 1; sceneIndex >= 0; sceneIndex--)
            {
                var loadedFittings = SceneManager.GetSceneAt(sceneIndex);
                if (!string.Equals(loadedFittings.path, ScenePath, StringComparison.OrdinalIgnoreCase)) continue;
                EditorSceneManager.CloseScene(loadedFittings, true);
            }
            // This is a generated fitting scene. Removing the old clean asset avoids
            // Unity retaining an unloaded scene handle that refuses overwrite.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            var previous = SceneManager.GetActiveScene();
            var mode = string.IsNullOrEmpty(previous.path) ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);
            try
            {
                BuildEnvironment(definitions.Length + weaponPrefabs.Length);
                int index = 0;
                foreach (var group in definitions.GroupBy(item => item.slot))
                {
                    foreach (var item in group)
                    {
                        CreateBanditStation(banditPrefab, catalog, item, index);
                        index++;
                    }
                }
                foreach (var weaponPrefab in weaponPrefabs)
                {
                    CreateWeaponStation(banditPrefab, catalog, weaponPrefab, index);
                    index++;
                }
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                Debug.Log($"Armor fittings scene built: {definitions.Length} wearable bandits and {weaponPrefabs.Length} weapon stations at {ScenePath}");
            }
            finally
            {
                if (mode == NewSceneMode.Additive && previous.IsValid())
                { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
            }
        }

        static void CreateBanditStation(GameObject prefab, CosmeticCatalog catalog, AppearanceItemDefinition item, int index)
        {
            var bandit = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            bandit.name = $"{index + 1:00} {item.slot} - {item.name}";
            bandit.transform.position = GridPosition(index);
            bandit.transform.rotation = Quaternion.identity;
            FreezeBandit(bandit);
            var cosmetics = bandit.GetComponent<CharacterCosmeticsController>();
            var appearance = bandit.GetComponent<CharacterAppearanceController>();
            if (!cosmetics || !appearance) throw new InvalidOperationException("Bandit lacks appearance components");
            cosmetics.catalog = catalog;
            cosmetics.headwear = cosmetics.torso = cosmetics.legs = cosmetics.feet = cosmetics.hands = cosmetics.outerArmor = null;
            if (item.slot == AppearanceSlot.Beard)
            {
                int variant = Array.IndexOf(catalog.fullBeardVariants, item);
                if (variant < 0) throw new InvalidOperationException(item.name + " is absent from full beard variants");
                var data = appearance.Data; data.hairStyle = HairStyle.Bald; data.beardStyle = BeardStyle.FullBeard; data.beardVariant = variant;
                appearance.Apply(data);
            }
            else
            {
                Assign(cosmetics, item);
                var data = appearance.Data; data.hairStyle = item.slot == AppearanceSlot.Headwear ? HairStyle.Bald : data.hairStyle;
                data.beardStyle = BeardStyle.CleanShaven; appearance.Apply(data);
            }
            cosmetics.Refresh();
            // Applying cosmetics can rebuild body renderers. Re-apply the authored
            // fitting pose afterwards so every station is consistently in T-pose.
            FreezeBandit(bandit);
            var spawned = bandit.GetComponentsInChildren<AppearanceSpawnedItem>(true).FirstOrDefault(marker => marker.definition == item);
            if (!spawned) throw new InvalidOperationException("Could not equip " + item.name);
            var entry = spawned.gameObject.AddComponent<ArmorShowcaseEntry>();
            entry.appearanceItem = item;
            entry.editingNote = "Edit this object's local Position, Rotation, and Scale. Capture Imported Armor Fittings saves the changes to " + item.prefab.name + ".";
            if (item.skinned)
            {
                var adjustment = spawned.gameObject.AddComponent<SkinnedArmorFitAdjustment>();
                adjustment.InitializeBaseline();
                entry.editingNote += " Skinned fitting preview is active: ordinary Transform edits are converted into visible mesh adjustments.";
            }
        }

        static void CreateWeaponStation(GameObject prefab, CosmeticCatalog catalog, GameObject weaponPrefab, int index)
        {
            var bandit = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            bandit.name = $"{index + 1:00} Weapon - {weaponPrefab.name}";
            bandit.transform.position = GridPosition(index);
            bandit.transform.rotation = Quaternion.identity;
            FreezeBandit(bandit);
            var cosmetics = bandit.GetComponent<CharacterCosmeticsController>();
            if (cosmetics)
            { cosmetics.catalog = catalog; cosmetics.headwear = cosmetics.torso = cosmetics.legs = cosmetics.feet = cosmetics.hands = cosmetics.outerArmor = null; cosmetics.Refresh(); }
            var hand = bandit.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "CC_Base_R_Hand_081");
            if (!hand) throw new InvalidOperationException("Bandit right hand bone is missing");
            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab);
            weapon.name = "SHOWCASE WEAPON - " + weaponPrefab.name;
            weapon.transform.SetParent(hand, false);
            var physical = weapon.GetComponent<PhysicalMeleeWeapon>();
            physical.enabled = false;
            var body = weapon.GetComponent<Rigidbody>(); if (body) body.isKinematic = true;
            if (physical.grip)
            {
                weapon.transform.localRotation = Quaternion.Inverse(physical.grip.localRotation);
                weapon.transform.localPosition = -(weapon.transform.localRotation * physical.grip.localPosition);
            }
            var entry = weapon.AddComponent<ArmorShowcaseEntry>(); entry.weaponPrefab = weaponPrefab;
            entry.editingNote = "Edit Cleaned visual, Grip, Secondary Grip, tip markers, or collider. Capture Imported Armor Fittings copies those overrides into the weapon prefab.";
        }

        static void FreezeBandit(GameObject root)
        {
            foreach (var brain in root.GetComponentsInChildren<StarvingBandit>(true)) brain.enabled = false;
            foreach (var agent in root.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
                if (!animator.isHuman) continue;
                AlignLimb(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), Vector3.left);
                AlignLimb(animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), animator.GetBoneTransform(HumanBodyBones.LeftHand), Vector3.left);
                AlignLimb(animator.GetBoneTransform(HumanBodyBones.RightUpperArm), animator.GetBoneTransform(HumanBodyBones.RightLowerArm), Vector3.right);
                AlignLimb(animator.GetBoneTransform(HumanBodyBones.RightLowerArm), animator.GetBoneTransform(HumanBodyBones.RightHand), Vector3.right);
            }
        }

        static void AlignLimb(Transform joint, Transform child, Vector3 worldDirection)
        {
            if (!joint || !child) return;
            var current = child.position - joint.position;
            if (current.sqrMagnitude > .000001f) joint.rotation = Quaternion.FromToRotation(current.normalized, worldDirection) * joint.rotation;
        }

        static void Assign(CharacterCosmeticsController cosmetics, AppearanceItemDefinition item)
        {
            switch (item.slot)
            {
                case AppearanceSlot.Headwear: cosmetics.headwear = item; break;
                case AppearanceSlot.Torso: cosmetics.torso = item; break;
                case AppearanceSlot.Legs: cosmetics.legs = item; break;
                case AppearanceSlot.Feet: cosmetics.feet = item; break;
                case AppearanceSlot.Hands: cosmetics.hands = item; break;
                case AppearanceSlot.OuterArmor: cosmetics.outerArmor = item; break;
                default: throw new InvalidOperationException("Unsupported fitting slot " + item.slot);
            }
        }

        static Vector3 GridPosition(int index) => new((index % 6) * 2.35f, 0f, -(index / 6) * 2.75f);

        static void BuildEnvironment(int count)
        {
            var light = new GameObject("Directional Light", typeof(Light)); light.transform.rotation = Quaternion.Euler(38, -28, 0);
            light.GetComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 1.6f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.58f, .60f, .64f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Showcase Floor";
            floor.transform.position = new Vector3(5.9f, -.02f, -6.9f); floor.transform.localScale = new Vector3(1.7f, 1, 1.7f);
            var camera = new GameObject("Overview Camera", typeof(Camera)); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(5.9f, 8.5f, 10.5f); camera.transform.LookAt(new Vector3(5.9f, 1f, -6.2f));
            camera.GetComponent<Camera>().farClipPlane = 80f;
        }

        [MenuItem("Zandea/Character Creator/Capture Imported Armor Fittings")]
        public static void Capture()
        {
            var scene = SceneManager.GetActiveScene();
            bool opened = false;
            try
            {
                if (!HasShowcaseEntries(scene))
                {
                    scene = EditorSceneManager.GetSceneByPath(ScenePath);
                    if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
                }
                int appearances = 0, weapons = 0;
                foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true)))
                {
                    if (entry.appearanceItem) { CaptureAppearance(entry); appearances++; }
                    else if (entry.weaponPrefab) { CaptureWeapon(entry); weapons++; }
                }
                AssetDatabase.SaveAssets();
                Debug.Log($"Captured imported armor fittings: {appearances} appearance prefabs and {weapons} weapon prefabs.");
            }
            finally
            {
                // Never leave a generated fitting scene open additively, including
                // when an individual item fails to capture.
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static bool HasShowcaseEntries(Scene scene) => scene.IsValid() && scene.isLoaded &&
            scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<ArmorShowcaseEntry>(true));

        static void CaptureAppearance(ArmorShowcaseEntry entry)
        {
            string path = AssetDatabase.GetAssetPath(entry.appearanceItem.prefab);
            var adjustment = entry.GetComponent<SkinnedArmorFitAdjustment>();
            if (adjustment)
            {
                var renderers = entry.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var sourceMeshes = adjustment.SourceMeshes;
                if (sourceMeshes == null || sourceMeshes.Length != renderers.Length)
                    throw new InvalidOperationException("Skinned fitting preview is not initialized for " + entry.appearanceItem.name);
                var matrix = adjustment.AdjustmentMatrix;
                for (int i = 0; i < sourceMeshes.Length; i++)
                {
                    var source = sourceMeshes[i];
                    if (!source || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source))) continue;
                    var adjusted = UnityEngine.Object.Instantiate(source);
                    SkinnedArmorFitAdjustment.ApplyMatrix(source, adjusted, matrix);
                    EditorUtility.CopySerialized(adjusted, source);
                    UnityEngine.Object.DestroyImmediate(adjusted);
                    EditorUtility.SetDirty(source);
                }
                adjustment.ResetTransformToBaseline();
                entry.appearanceItem.fitNote = "Skinned fit authored and mesh-baked in " + ScenePath + ".";
                EditorUtility.SetDirty(entry.appearanceItem);
                return;
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try { CopyHierarchy(entry.transform, contents.transform, true); PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            entry.appearanceItem.fitNote = "Fit authored in " + ScenePath + "."; EditorUtility.SetDirty(entry.appearanceItem);
        }

        static void CaptureWeapon(ArmorShowcaseEntry entry)
        {
            string path = AssetDatabase.GetAssetPath(entry.weaponPrefab);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CopyHierarchy(entry.transform, contents.transform, false);
                var sourceBox = entry.GetComponent<BoxCollider>(); var targetBox = contents.GetComponent<BoxCollider>();
                if (sourceBox && targetBox) { targetBox.center = sourceBox.center; targetBox.size = sourceBox.size; }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static void CopyHierarchy(Transform source, Transform target, bool copyRoot)
        {
            if (copyRoot) CopyTransform(source, target);
            int count = Mathf.Min(source.childCount, target.childCount);
            for (int i = 0; i < count; i++)
            {
                var from = source.GetChild(i); var to = target.GetChild(i);
                if (from.name != to.name) continue;
                CopyTransform(from, to); CopyHierarchy(from, to, false);
            }
        }

        static void CopyTransform(Transform source, Transform target)
        { target.localPosition = source.localPosition; target.localRotation = source.localRotation; target.localScale = source.localScale; }

        [MenuItem("Zandea/Character Creator/Validate Imported Armor Fittings Scene")]
        public static void Validate()
        {
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var entries = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true)).ToArray();
                int appearances = entries.Count(entry => entry.appearanceItem); int weapons = entries.Count(entry => entry.weaponPrefab);
                if (appearances != 29 || weapons != 3 || entries.Any(entry => !entry.GetComponentInParent<CharacterCosmeticsController>()))
                    throw new InvalidOperationException($"Fittings scene expected 29 wearable bandits and 3 weapons; found {appearances} and {weapons}");
                Debug.Log("Imported armor fittings scene validation PASS: every new item is visible on or held by a labeled bandit and linked to its source prefab.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Zandea/Character Creator/Capture Imported Armor Fittings Images")]
        public static void CaptureImages()
        {
            const string output = "Assets/CharacterCreator/ImportedArmor/ShowcaseCaptures";
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var camera = scene.GetRootGameObjects().Select(root => root.GetComponent<Camera>()).FirstOrDefault(value => value);
                if (!camera) throw new InvalidOperationException("Overview camera is missing from the fittings scene");
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.085f, .095f, .11f);
                camera.fieldOfView = 51f;
                Render(camera, new Vector3(5.9f, 8.8f, 13.8f), new Vector3(5.9f, 1.05f, -6.4f), output + "/armor_fittings_overview.png");
                camera.fieldOfView = 45f;
                Render(camera, new Vector3(5.9f, 2.65f, 6.4f), new Vector3(5.9f, 1.15f, -1.4f), output + "/armor_fittings_close.png");
                AssetDatabase.Refresh();
                Debug.Log("Armor fittings captures saved to " + output);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static void Render(Camera camera, Vector3 position, Vector3 target, string path)
        {
            const int width = 1920, height = 1080;
            camera.transform.position = position;
            camera.transform.LookAt(target);
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = texture;
            try
            {
                texture.Create(); camera.Render(); RenderTexture.active = texture;
                var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
