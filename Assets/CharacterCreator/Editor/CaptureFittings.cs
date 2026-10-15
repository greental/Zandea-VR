using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    // One-shot migration of the hand-authored neutral fits in Fittings.unity.
    public static class CaptureFittings
    {
        const string ScenePath = "Assets/Scenes/Fittings.unity";
        const string Items = "Assets/CharacterCreator/ImportedDownloads/Items";
        const string PairFolder = "Assets/CharacterCreator/ImportedDownloads/HandFitted";
        const string Request = "Temp/CaptureFittings.request";
        const string Result = "Temp/CaptureFittings.result";
        const string ValidateRequest = "Temp/ValidateFittings.request";
        const string ValidateResult = "Temp/ValidateFittings.result";

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            if (File.Exists(ValidateRequest))
                EditorApplication.delayCall += () =>
                {
                    if (!File.Exists(ValidateRequest)) return;
                    File.Delete(ValidateRequest);
                    try { File.WriteAllText(ValidateResult, Validate()); }
                    catch (Exception exception) { Debug.LogException(exception); File.WriteAllText(ValidateResult, exception.ToString()); }
                };
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Request)) return;
                File.Delete(Request);
                try { Capture(); File.WriteAllText(Result, "OK"); }
                catch (Exception exception) { Debug.LogException(exception); File.WriteAllText(Result, exception.ToString()); }
            };
        }

        [MenuItem("Tools/Character Creator/Capture Hand-Fitted Gear")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before capturing fits.");
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var characters = new Dictionary<string, GameObject>();
            foreach (var root in scene.GetRootGameObjects()) characters[root.name] = root;
            var fitted = new HashSet<AppearanceItemDefinition>();
            for (int index = 6; index <= 10; index++)
            {
                if (!characters.TryGetValue("CombinedStarvingBandit (" + index + ")", out var character))
                    throw new InvalidOperationException("Missing fitted character " + index);
                foreach (var marker in character.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                {
                    var definition = marker.definition;
                    if (!definition || !definition.prefab || fitted.Contains(definition) ||
                        definition.name == "wrapped_boot") continue;
                    // The beard was hand placed in the headwear slot before its item type changed.
                    CaptureItem(marker.gameObject, definition);
                    fitted.Add(definition);
                }
            }
            var boots = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/wrapped_boot.asset");
            if (!boots) throw new InvalidOperationException("Missing wrapped_boot item definition.");
            CreateBootPair(characters["CombinedStarvingBandit (9)"], boots);
            UpdateCatalog();
            FixBeardCharacter(characters["CombinedStarvingBandit (8)"]);
            AssignBoots(characters["CombinedStarvingBandit (9)"], boots);
            AssignBoots(characters["CombinedStarvingBandit (10)"], boots);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Fittings scene");
            Debug.Log("Captured " + fitted.Count + " unique hand-fitted gear items and one paired boot item.");
        }

        static void CaptureItem(GameObject instance, AppearanceItemDefinition item)
        {
            string path = AssetDatabase.GetAssetPath(item.prefab);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("No prefab asset for " + item.name);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CopyTransform(instance.transform, contents.transform);
                // Rigid imported previews have one visual child. Copy its authored mesh offset.
                if (contents.transform.childCount == 1 && instance.transform.childCount == 1 &&
                    path.StartsWith(Items + "/", StringComparison.Ordinal))
                    CopyTransform(instance.transform.GetChild(0), contents.transform.GetChild(0));
                var fromRenderers = instance.GetComponentsInChildren<Renderer>(true);
                var toRenderers = contents.GetComponentsInChildren<Renderer>(true);
                if (fromRenderers.Length == toRenderers.Length)
                    for (int i = 0; i < fromRenderers.Length; i++)
                        toRenderers[i].sharedMaterials = fromRenderers[i].sharedMaterials;
                else
                    Debug.LogWarning("Skipped material copy for " + item.name +
                        " because its scene instance contains extra nested renderers.");
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                item.attachBoneName = instance.transform.parent.name;
                item.fitNote = "Neutral fit authored in Assets/Scenes/Fittings.unity; capture from " +
                    instance.GetComponentInParent<CharacterCosmeticsController>().name + ".";
                EditorUtility.SetDirty(item);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static void CopyTransform(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        static void CreateBootPair(GameObject character, AppearanceItemDefinition item)
        {
            var singles = new List<Transform>();
            foreach (Transform child in character.transform)
                if (child.name == "wrapped_boot" || child.name == "wrapped_boot (1)") singles.Add(child);
            if (singles.Count == 0 && item.prefab && item.prefab.name == "wrapped_boot_pair") return;
            if (singles.Count != 2) throw new InvalidOperationException("Expected two authored boots on character (9)");
            if (!AssetDatabase.IsValidFolder(PairFolder)) AssetDatabase.CreateFolder("Assets/CharacterCreator/ImportedDownloads", "HandFitted");
            var singlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Items + "/wrapped_boot.prefab");
            if (!singlePrefab) throw new InvalidOperationException("Missing rigid boot preview prefab");
            var pair = new GameObject("wrapped_boot_pair");
            try
            {
                foreach (var source in singles)
                {
                    var boot = (GameObject)PrefabUtility.InstantiatePrefab(singlePrefab);
                    boot.transform.SetParent(pair.transform, false);
                    CopyTransform(source, boot.transform);
                }
                string path = PairFolder + "/wrapped_boot_pair.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(pair, path);
                if (!saved) throw new IOException("Could not save paired boot prefab");
                item.prefab = saved;
                item.attachBoneName = "$CharacterRoot";
                item.skinned = false;
                item.requiredMorphs = Array.Empty<string>();
                item.fitNote = "Two hand-placed rigid boots from Fittings characters (9) and (10); neutral pose fit.";
                EditorUtility.SetDirty(item);
            }
            finally { UnityEngine.Object.DestroyImmediate(pair); }
        }

        static void UpdateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
            if (!catalog) throw new InvalidOperationException("Missing cosmetic catalog");
            var horned = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/horned_hair_beard.asset");
            if (!horned || horned.slot != AppearanceSlot.Beard) throw new InvalidOperationException("Horned item must be Beard");
            var full = new List<AppearanceItemDefinition>(catalog.fullBeardVariants ?? Array.Empty<AppearanceItemDefinition>());
            if (!full.Contains(horned)) full.Add(horned);
            catalog.fullBeardVariants = full.ToArray();
            var wardrobe = new List<AppearanceItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition", new[] { Items }))
            {
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item && item.prefab && item.slot >= AppearanceSlot.Headwear) wardrobe.Add(item);
            }
            wardrobe.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            catalog.wardrobeItems = wardrobe.ToArray();
            EditorUtility.SetDirty(catalog);
        }

        static void FixBeardCharacter(GameObject character)
        {
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            var appearance = character.GetComponent<CharacterAppearanceController>();
            cosmetics.headwear = null;
            var serialized = new SerializedObject(appearance);
            serialized.FindProperty("appearance.beardStyle").enumValueIndex = (int)BeardStyle.FullBeard;
            serialized.FindProperty("appearance.beardVariant").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (var marker in character.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                if (marker.definition && marker.definition.name == "horned_hair_beard")
                { marker.slot = AppearanceSlot.Beard; EditorUtility.SetDirty(marker); }
            EditorUtility.SetDirty(cosmetics);
            EditorUtility.SetDirty(appearance);
        }

        static void AssignBoots(GameObject character, AppearanceItemDefinition boots)
        {
            var cosmetics = character.GetComponent<CharacterCosmeticsController>();
            cosmetics.feet = boots;
            var singles = new List<GameObject>();
            foreach (Transform child in character.transform)
                if (child.name == "wrapped_boot" || child.name == "wrapped_boot (1)") singles.Add(child.gameObject);
            if (singles.Count == 0) { EditorUtility.SetDirty(cosmetics); cosmetics.Refresh(); return; }
            if (singles.Count != 2) throw new InvalidOperationException("Expected two loose boots on " + character.name);
            foreach (var single in singles) UnityEngine.Object.DestroyImmediate(single);
            EditorUtility.SetDirty(cosmetics);
            cosmetics.Refresh();
        }

        [MenuItem("Tools/Character Creator/Validate Hand-Fitted Gear")]
        public static string Validate()
        {
            var scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            int checkedItems = 0;
            var errors = new List<string>();
            try
            {
                var seen = new HashSet<AppearanceItemDefinition>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!root.name.StartsWith("CombinedStarvingBandit (")) continue;
                    foreach (var marker in root.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                    {
                        var item = marker.definition;
                        if (!item || !item.prefab || !seen.Add(item)) continue;
                        if (marker.slot != item.slot) errors.Add(item.name + " marker slot mismatch");
                        if (item.attachBoneName != marker.transform.parent.name && item.attachBoneName != "$CharacterRoot")
                            errors.Add(item.name + " attach bone mismatch");
                        if (!SameTransform(marker.transform, item.prefab.transform))
                            errors.Add(item.name + " root transform mismatch");
                        if (item.prefab.transform.childCount == 1 && marker.transform.childCount == 1 &&
                            AssetDatabase.GetAssetPath(item.prefab).StartsWith(Items + "/", StringComparison.Ordinal) &&
                            !SameTransform(marker.transform.GetChild(0), item.prefab.transform.GetChild(0)))
                            errors.Add(item.name + " visual transform mismatch");
                        checkedItems++;
                    }
                }
                var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
                var horned = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/horned_hair_beard.asset");
                var boots = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/wrapped_boot.asset");
                if (!catalog || catalog.ForBeard(BeardStyle.FullBeard, 1) != horned)
                    errors.Add("Horned beard is unavailable in beard picker");
                if (!boots || !boots.prefab || boots.prefab.transform.childCount != 2 || boots.skinned ||
                    boots.attachBoneName != "$CharacterRoot")
                    errors.Add("Paired rigid boot item is incomplete");
                foreach (int index in new[] { 9, 10 })
                {
                    GameObject character = null;
                    foreach (var root in scene.GetRootGameObjects())
                        if (root.name == "CombinedStarvingBandit (" + index + ")") character = root;
                    if (!character || character.GetComponent<CharacterCosmeticsController>().feet != boots)
                        errors.Add("Character " + index + " lacks paired boot selection");
                }
                string result = "Checked " + checkedItems + " unique scene gear items; " + errors.Count + " errors.\n" +
                    string.Join("\n", errors);
                if (errors.Count == 0) Debug.Log("Hand-fitted gear validation passed: " + result);
                else Debug.LogError("Hand-fitted gear validation failed: " + result);
                return result;
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static bool SameTransform(Transform a, Transform b) =>
            Vector3.Distance(a.localPosition, b.localPosition) < .0001f &&
            Quaternion.Angle(a.localRotation, b.localRotation) < .01f &&
            Vector3.Distance(a.localScale, b.localScale) < .0001f;
    }
}
