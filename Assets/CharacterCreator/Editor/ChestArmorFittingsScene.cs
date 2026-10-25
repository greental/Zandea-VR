using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class ChestArmorFittingsScene
    {
        public const string ScenePath = "Assets/Scenes/CharacterCreator/ChestArmorFittings.unity";
        const string BanditPath = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        const string ItemsPath = "Assets/CharacterCreator/ImportedDownloads/Items";
        const string Request = "Temp/BuildChestArmorFittings.request";
        const string Result = "Temp/BuildChestArmorFittings.result";
        const string AuditRequest = "Temp/AuditChestArmorFittings.request";
        const string AuditResult = "Temp/AuditChestArmorFittings.result";
        const string RevealRequest = "Temp/RevealChestArmorFittings.request";
        const string RevealResult = "Temp/RevealChestArmorFittings.result"; // editor repair handshake
        const string CaptureRequest = "Temp/CaptureChestArmorFittings.request";
        const string CaptureResult = "Temp/CaptureChestArmorFittings.result";
        const string MovementRequest = "Temp/AttachChestArmorMovement.request";
        const string MovementResult = "Temp/AttachChestArmorMovement.result"; // one-time fitting migration

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            EditorApplication.update -= TryBuild;
            EditorApplication.update += TryBuild;
        }

        static void TryBuild()
        {
            if (File.Exists(MovementRequest))
            {
                File.Delete(MovementRequest);
                try { File.WriteAllText(MovementResult, AttachRigidChestToSpine()); }
                catch (Exception error) { Debug.LogException(error); File.WriteAllText(MovementResult, error.ToString()); }
                return;
            }
            if (File.Exists(CaptureRequest))
            {
                File.Delete(CaptureRequest);
                try { Capture(); File.WriteAllText(CaptureResult, "Capture completed for " + ScenePath); }
                catch (Exception error) { Debug.LogException(error); File.WriteAllText(CaptureResult, error.ToString()); }
                return;
            }
            if (File.Exists(RevealRequest))
            {
                File.Delete(RevealRequest);
                try { File.WriteAllText(RevealResult, RevealArmor()); }
                catch (Exception error) { Debug.LogException(error); File.WriteAllText(RevealResult, error.ToString()); }
                return;
            }
            if (File.Exists(AuditRequest))
            {
                File.Delete(AuditRequest);
                try { File.WriteAllText(AuditResult, Audit()); }
                catch (Exception error) { File.WriteAllText(AuditResult, error.ToString()); }
                return;
            }
            if (!File.Exists(Request)) { EditorApplication.update -= TryBuild; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= TryBuild;
            File.Delete(Request);
            try { File.WriteAllText(Result, Build()); }
            catch (Exception error) { Debug.LogException(error); File.WriteAllText(Result, error.ToString()); }
        }

        [MenuItem("Zandea/Character Creator/Attach Fitted Chest Plates To Spine")]
        public static string AttachRigidChestToSpine()
        {
            const string spineName = "CC_Base_Spine02_035";
            var names = new HashSet<string>(StringComparer.Ordinal)
                { "bandit_mercenary_chest", "bandit_rib_vest", "paladin_medieval_chest" };
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var entries = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true))
                    .Where(entry => entry.appearanceItem && names.Contains(entry.appearanceItem.name)).ToArray();
                if (entries.Length != names.Count) throw new InvalidOperationException("Expected three fitted rigid chest pieces; found " + entries.Length);
                var bones = new Transform[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    var item = entries[i].appearanceItem;
                    var cosmetics = entries[i].GetComponentInParent<CharacterCosmeticsController>();
                    if (!cosmetics || !cosmetics.skeletonRoot || item.skinned || !item.prefab)
                        throw new InvalidOperationException("Invalid rigid chest setup for " + item.name);
                    bones[i] = cosmetics.skeletonRoot.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(bone => bone.name == spineName && !bone.GetComponentInParent<AppearanceSpawnedItem>());
                    if (!bones[i]) throw new InvalidOperationException("Missing " + spineName + " for " + item.name);
                }
                var report = new StringBuilder();
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    var item = entry.appearanceItem;
                    var before = entry.transform.position;
                    entry.transform.SetParent(bones[i], true);
                    item.attachBoneName = spineName;
                    EditorUtility.SetDirty(item);
                    var path = AssetDatabase.GetAssetPath(item.prefab);
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        Copy(entry.transform, contents.transform);
                        if (!PrefabUtility.SaveAsPrefabAsset(contents, path))
                            throw new IOException("Could not save " + path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    report.AppendLine(item.name + " follows " + spineName + "; world position " + before.ToString("F3") +
                        " -> " + entry.transform.position.ToString("F3"));
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                return report.ToString();
            }
            finally { if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Zandea/Character Creator/Reveal Chest Armor In Fittings Scene")]
        public static string RevealArmor()
        {
            var scene = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .FirstOrDefault(value => value.isLoaded && value.name == "ChestArmorFittings");
            if (!scene.IsValid()) throw new InvalidOperationException("Open ChestArmorFittings first.");
            int replaced = ReplaceCollapsedSkinnedChestEntries(scene);
            int count = 0;
            foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true)))
            {
                if (!entry.appearanceItem || entry.appearanceItem.slot != AppearanceSlot.Torso) continue;
                for (var parent = entry.transform; parent && parent.gameObject.scene == scene; parent = parent.parent)
                    parent.gameObject.SetActive(true);
                foreach (var renderer in entry.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.gameObject.SetActive(true);
                    renderer.enabled = true;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                count++;
            }
            if (count != 7) throw new InvalidOperationException("Expected seven chest armor entries; found " + count);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            SceneView.RepaintAll();
            return "Revealed and saved " + count + " chest armor entries in " + ScenePath + "; replaced " + replaced + " collapsed meshes.";
        }

        static int ReplaceCollapsedSkinnedChestEntries(Scene scene)
        {
            int replaced = 0;
            var entries = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true)).ToArray();
            foreach (var entry in entries)
            {
                var item = entry.appearanceItem;
                if (!item || item.skinned || !entry.GetComponent<SkinnedArmorFitAdjustment>()) continue;
                var cosmetics = entry.GetComponentInParent<CharacterCosmeticsController>();
                if (!cosmetics || !item.prefab) throw new InvalidOperationException("Missing character or prefab for " + item.name);
                var old = entry.transform;
                var position = old.localPosition;
                var rotation = old.localRotation;
                var scale = old.localScale;
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, scene);
                replacement.name = "Appearance Torso - " + item.name;
                replacement.transform.SetParent(cosmetics.transform, false);
                replacement.transform.localPosition = position;
                replacement.transform.localRotation = rotation;
                replacement.transform.localScale = scale;
                var marker = replacement.AddComponent<AppearanceSpawnedItem>();
                marker.slot = AppearanceSlot.Torso;
                marker.definition = item;
                var showcase = replacement.AddComponent<ArmorShowcaseEntry>();
                showcase.appearanceItem = item;
                showcase.editingNote = "Select this armor object and edit Position, Rotation, and Scale. " +
                    "Use Zandea > Character Creator > Capture Chest Armor Fits to save the fit.";
                UnityEngine.Object.DestroyImmediate(entry.gameObject);
                cosmetics.Refresh();
                replaced++;
            }
            return replaced;
        }

        static string Audit()
        {
            var active = SceneManager.GetActiveScene();
            var report = new StringBuilder("Active scene: " + active.path + "\n");
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            {
                if (!scene.name.Contains("ChestArmorFittings")) continue;
                report.AppendLine("Loaded scene: " + scene.path + " dirty=" + scene.isDirty);
                foreach (var entry in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true)))
                {
                    var renderers = entry.GetComponentsInChildren<Renderer>(true);
                    report.Append(entry.appearanceItem ? entry.appearanceItem.name : "MISSING ITEM")
                        .Append(" active=").Append(entry.gameObject.activeInHierarchy)
                        .Append(" visibleRenderers=").Append(renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy))
                        .Append('/').Append(renderers.Length)
                        .Append(" position=").Append(entry.transform.position.ToString("F2"));
                    foreach (var renderer in renderers)
                    {
                        report.Append(" | ").Append(renderer.name).Append(" active=")
                            .Append(renderer.gameObject.activeInHierarchy && renderer.enabled)
                            .Append(" bounds=").Append(renderer.bounds.center.ToString("F2"))
                            .Append(" size=").Append(renderer.bounds.size.ToString("F2"));
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            report.Append(" bones=").Append(skin.bones.Length)
                                .Append(" root=").Append(skin.rootBone ? skin.rootBone.name : "NULL")
                                .Append(" meshVertices=").Append(skin.sharedMesh ? skin.sharedMesh.vertexCount : 0)
                                .Append(" meshBounds=").Append(skin.sharedMesh ? skin.sharedMesh.bounds.ToString() : "NULL")
                                .Append(" localBounds=").Append(skin.localBounds.ToString())
                                .Append(" scale=").Append(skin.transform.lossyScale.ToString("F3"));
                            var baked = new Mesh();
                            skin.BakeMesh(baked);
                            report.Append(" bakedBounds=").Append(baked.bounds.ToString());
                            UnityEngine.Object.DestroyImmediate(baked);
                        }
                    }
                    report.AppendLine();
                }
            }
            return report.ToString();
        }

        [MenuItem("Zandea/Character Creator/Create Chest Armor Fittings Scene")]
        public static string Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                return "Chest armor fitting scene already exists at " + ScenePath;
            var banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPath);
            if (!banditPrefab) throw new InvalidOperationException("Missing " + BanditPath);
            var items = AssetDatabase.FindAssets("t:AppearanceItemDefinition", new[] { ItemsPath })
                .Select(guid => AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(item => item && item.slot == AppearanceSlot.Torso && item.prefab)
                .OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
            if (items.Length != 7) throw new InvalidOperationException("Expected seven chest items; found " + items.Length);

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                BuildEnvironment();
                for (int i = 0; i < items.Length; i++) CreateStation(scene, banditPrefab, items[i], i);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                return "Created " + ScenePath + " with " + items.Length + " editable chest armor stations.";
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void CreateStation(Scene scene, GameObject prefab, AppearanceItemDefinition item, int index)
        {
            var bandit = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            bandit.name = $"{index + 1:00} CHEST - {item.name}";
            bandit.transform.position = new Vector3((index % 4) * 2.8f, 0f, -(index / 4) * 3.2f);
            bandit.transform.rotation = Quaternion.identity;
            var cosmetics = bandit.GetComponent<CharacterCosmeticsController>();
            if (!cosmetics) throw new InvalidOperationException("Bandit lacks cosmetics: " + item.name);
            cosmetics.headwear = cosmetics.legs = cosmetics.feet = cosmetics.hands = cosmetics.outerArmor = null;
            cosmetics.torso = item;
            cosmetics.Refresh();
            Freeze(bandit);
            var marker = bandit.GetComponentsInChildren<AppearanceSpawnedItem>(true)
                .FirstOrDefault(value => value.definition == item);
            if (!marker) throw new InvalidOperationException("Could not equip " + item.name);
            for (var parent = marker.transform; parent && parent.gameObject.scene == scene; parent = parent.parent)
                parent.gameObject.SetActive(true);
            foreach (var renderer in marker.GetComponentsInChildren<Renderer>(true))
            {
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            var entry = marker.gameObject.AddComponent<ArmorShowcaseEntry>();
            entry.appearanceItem = item;
            entry.editingNote = "Select this armor object and edit its Transform Position, Rotation, and Scale. " +
                "Use Zandea > Character Creator > Capture Chest Armor Fits to save the fit to the item.";
            if (item.skinned)
            {
                var adjustment = marker.gameObject.AddComponent<SkinnedArmorFitAdjustment>();
                adjustment.InitializeBaseline();
                entry.editingNote += " Skinned mesh preview follows Transform edits in Edit Mode.";
            }
        }

        static void Freeze(GameObject bandit)
        {
            foreach (var brain in bandit.GetComponentsInChildren<StarvingBandit>(true)) brain.enabled = false;
            foreach (var agent in bandit.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
            foreach (var body in bandit.GetComponentsInChildren<Rigidbody>(true))
            { body.isKinematic = true; body.useGravity = false; }
            foreach (var animator in bandit.GetComponentsInChildren<Animator>(true))
            { animator.enabled = false; animator.applyRootMotion = false; }
        }

        static void BuildEnvironment()
        {
            var light = new GameObject("Fitting Light", typeof(Light));
            light.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            light.GetComponent<Light>().type = LightType.Directional;
            light.GetComponent<Light>().intensity = 1.5f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .58f, .63f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Fitting Floor";
            floor.transform.position = new Vector3(4.2f, -.03f, -1.6f);
            floor.transform.localScale = new Vector3(1.6f, 1f, 1.0f);
            var camera = new GameObject("Chest Armor Overview Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(4.2f, 7.2f, 12f);
            camera.transform.LookAt(new Vector3(4.2f, 1f, -1.6f));
        }

        [MenuItem("Zandea/Character Creator/Capture Chest Armor Fits")]
        public static void Capture()
        {
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            int count = 0;
            try
            {
                var entries = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true))
                    .Where(value => value.appearanceItem && value.appearanceItem.slot == AppearanceSlot.Torso).ToArray();
                if (entries.Length != 7 || entries.Select(value => value.appearanceItem).Distinct().Count() != 7)
                    throw new InvalidOperationException("Expected seven unique chest armor entries; found " + entries.Length);
                foreach (var entry in entries)
                {
                    var item = entry.appearanceItem;
                    string path = AssetDatabase.GetAssetPath(item.prefab);
                    if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Missing prefab for " + item.name);
                    var adjustment = entry.GetComponent<SkinnedArmorFitAdjustment>();
                    if (adjustment)
                    {
                        var renderers = entry.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        var sources = adjustment.SourceMeshes;
                        if (sources == null || sources.Length != renderers.Length)
                            throw new InvalidOperationException("Preview is unavailable for " + item.name);
                        var bakedSources = new HashSet<Mesh>();
                        for (int i = 0; i < sources.Length; i++)
                        {
                            if (!sources[i] || !bakedSources.Add(sources[i])) continue;
                            var sourcePath = AssetDatabase.GetAssetPath(sources[i]);
                            if (string.IsNullOrEmpty(sourcePath))
                                throw new InvalidOperationException("Mesh asset is missing for " + item.name);
                            var baked = UnityEngine.Object.Instantiate(sources[i]);
                            SkinnedArmorFitAdjustment.ApplyMatrix(sources[i], baked, adjustment.AdjustmentMatrix);
                            EditorUtility.CopySerialized(baked, sources[i]);
                            UnityEngine.Object.DestroyImmediate(baked);
                            EditorUtility.SetDirty(sources[i]);
                        }
                        adjustment.ResetTransformToBaseline();
                    }
                    else
                    {
                        var contents = PrefabUtility.LoadPrefabContents(path);
                        try
                        {
                            Copy(entry.transform, contents.transform);
                            foreach (Transform child in entry.transform)
                            {
                                var target = contents.transform.Cast<Transform>().FirstOrDefault(t => t.name == child.name);
                                if (target) Copy(child, target);
                            }
                            PrefabUtility.SaveAsPrefabAsset(contents, path);
                        }
                        finally { PrefabUtility.UnloadPrefabContents(contents); }
                    }
                    item.fitNote = "Fit captured from " + ScenePath + ".";
                    EditorUtility.SetDirty(item);
                    count++;
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save fitting scene");
                Debug.Log("Captured " + count + " chest armor fits from " + ScenePath);
            }
            finally { if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        static void Copy(Transform source, Transform target)
        { target.localPosition = source.localPosition; target.localRotation = source.localRotation; target.localScale = source.localScale; }
    }
}
