using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [CustomEditor(typeof(ArmorShowcaseEntry))]
    public sealed class ArmorShowcaseEntryEditor : UnityEditor.Editor
    {
        const string CapturedFolder = "Assets/CharacterCreator/CapturedArmor/Items";
        string error;

        public override void OnInspectorGUI()
        {
            var entry = (ArmorShowcaseEntry)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("appearanceItem"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponPrefab"));
            if (entry.weaponPrefab && !entry.appearanceItem)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("editingNote"));
            serializedObject.ApplyModifiedProperties();

            if (entry.weaponPrefab && !entry.appearanceItem) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Capture armor fit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Set Position, Rotation, and Scale in the Transform above. Choose the armor type and attachment bone here, then capture this fit.", MessageType.Info);

            if (entry.appearanceItem)
            {
                var item = entry.appearanceItem;
                var itemObject = new SerializedObject(item);
                itemObject.Update();
                EditorGUILayout.PropertyField(itemObject.FindProperty("slot"), new GUIContent("Armor type"));
                EditorGUILayout.PropertyField(itemObject.FindProperty("attachBoneName"), new GUIContent("Attachment bone"));
                EditorGUILayout.PropertyField(itemObject.FindProperty("prefab"));
                itemObject.ApplyModifiedProperties();
            }
            else
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("newItemName"), new GUIContent("Armor name"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("newItemType"), new GUIContent("Armor type"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("newItemAttachBoneName"), new GUIContent("Attachment bone"));
                serializedObject.ApplyModifiedProperties();
                if (string.IsNullOrWhiteSpace(entry.newItemAttachBoneName))
                    EditorGUILayout.LabelField("Automatic attachment", GuessBoneName(entry) ?? "No character found", EditorStyles.miniLabel);
            }

            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button(entry.appearanceItem ? "Capture fit and update armor choice" : "Capture as new armor choice"))
                {
                    try
                    {
                        var item = Capture(entry);
                        error = null;
                        Debug.Log("Captured " + item.name + " as " + item.slot + " from " + entry.gameObject.scene.path, entry);
                    }
                    catch (Exception exception)
                    {
                        error = exception.Message;
                        Debug.LogException(exception, entry);
                    }
                }
        }

        static AppearanceItemDefinition Capture(ArmorShowcaseEntry entry)
        {
            if (!entry || !entry.gameObject.scene.IsValid() || string.IsNullOrEmpty(entry.gameObject.scene.path))
                throw new InvalidOperationException("Select an armor object in a saved fitting scene.");

            var item = entry.appearanceItem;
            var slot = item ? item.slot : entry.newItemType;
            if (slot == AppearanceSlot.Hair || slot == AppearanceSlot.Beard)
                throw new InvalidOperationException("Choose an armor type, such as Torso, Headwear, Legs, Hands, Feet, or Outer Armor.");
            string boneName = item ? item.attachBoneName : entry.newItemAttachBoneName;
            if (string.IsNullOrWhiteSpace(boneName)) boneName = GuessBoneName(entry);
            if (string.IsNullOrWhiteSpace(boneName))
                throw new InvalidOperationException("Enter an attachment bone name, or $CharacterRoot.");
            var character = entry.GetComponentInParent<CharacterCosmeticsController>();
            if (!character || !character.skeletonRoot)
                throw new InvalidOperationException("The armor must be under a character with CharacterCosmeticsController and a Skeleton Root.");
            var anchor = boneName == "$CharacterRoot" ? character.transform :
                character.skeletonRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == boneName && !t.GetComponentInParent<AppearanceSpawnedItem>());
            if (!anchor) throw new InvalidOperationException("Attachment bone '" + boneName + "' was not found on this character.");

            var adjustment = entry.GetComponent<SkinnedArmorFitAdjustment>();
            if (adjustment)
            {
                if (!item || !item.prefab || !item.skinned)
                    throw new InvalidOperationException("A skinned fitting needs an existing skinned Appearance Item and prefab.");
                CaptureSkinned(entry, item, adjustment);
            }
            else
            {
                if (entry.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                    throw new InvalidOperationException("This armor has a Skinned Mesh Renderer. Use a fitted skinned item with Skinned Armor Fit Adjustment; a rigid capture would lose its bone binding.");
                if (entry.GetComponentsInChildren<Renderer>(true).Length == 0)
                    throw new InvalidOperationException("This armor has no renderer to capture.");
                if (!entry.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("This armor has no visible renderer. Select the visible replacement mesh and add Armor Showcase Entry to it.");
                CaptureRigid(entry, item, slot, boneName, anchor);
            }

            if (!item) item = entry.appearanceItem;
            item.attachBoneName = boneName;
            item.fitNote = "Fit captured from " + entry.gameObject.scene.path + ".";
            EditorUtility.SetDirty(item);
            RegisterInCatalog(item);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(entry.gameObject.scene);
            return item;
        }

        static void CaptureRigid(ArmorShowcaseEntry entry, AppearanceItemDefinition existing,
            AppearanceSlot slot, string boneName, Transform anchor)
        {
            string prefabPath = existing && existing.prefab ? AssetDatabase.GetAssetPath(existing.prefab) : null;
            bool creatingPrefab = string.IsNullOrEmpty(prefabPath);
            bool creatingItem = !existing;
            string assetPath = null;
            if (creatingPrefab)
            {
                string assetName = string.IsNullOrWhiteSpace(entry.newItemName) ?
                    (existing ? existing.name : entry.gameObject.name) : entry.newItemName;
                assetName = SanitizeName(assetName);
                if (string.IsNullOrEmpty(assetName)) throw new InvalidOperationException("Enter a usable armor name.");
                EnsureFolder();
                assetPath = AssetDatabase.GenerateUniqueAssetPath(CapturedFolder + "/" + assetName + ".asset");
                prefabPath = Path.ChangeExtension(assetPath, ".prefab").Replace('\\', '/');
                while (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath))
                {
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(CapturedFolder + "/" + assetName + "_fit.asset");
                    prefabPath = Path.ChangeExtension(assetPath, ".prefab").Replace('\\', '/');
                    assetName += "_fit";
                }
            }
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected Appearance Item must reference an editable .prefab asset.");

            var copy = Instantiate(entry.gameObject);
            try
            {
                copy.name = existing ? existing.prefab.name : Path.GetFileNameWithoutExtension(prefabPath);
                copy.SetActive(true);
                if (PrefabUtility.IsPartOfPrefabInstance(copy))
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var authoring in copy.GetComponentsInChildren<ArmorShowcaseEntry>(true)) DestroyImmediate(authoring);
                foreach (var marker in copy.GetComponentsInChildren<AppearanceSpawnedItem>(true)) DestroyImmediate(marker);
                foreach (var fit in copy.GetComponentsInChildren<SkinnedArmorFitAdjustment>(true)) DestroyImmediate(fit);

                copy.transform.SetParent(null, true);
                copy.transform.localPosition = anchor.InverseTransformPoint(entry.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(anchor.rotation) * entry.transform.rotation;
                var worldScale = entry.transform.lossyScale;
                var anchorScale = anchor.lossyScale;
                if (Mathf.Abs(anchorScale.x) < 0.0001f || Mathf.Abs(anchorScale.y) < 0.0001f || Mathf.Abs(anchorScale.z) < 0.0001f)
                    throw new InvalidOperationException("The attachment bone has zero scale.");
                copy.transform.localScale = new Vector3(worldScale.x / anchorScale.x,
                    worldScale.y / anchorScale.y, worldScale.z / anchorScale.z);

                if (!PrefabUtility.SaveAsPrefabAsset(copy, prefabPath))
                    throw new IOException("Could not save armor prefab " + prefabPath);
            }
            finally { DestroyImmediate(copy); }

            if (creatingPrefab)
            {
                var item = existing ? existing : CreateInstance<AppearanceItemDefinition>();
                if (creatingItem) item.name = Path.GetFileNameWithoutExtension(assetPath);
                item.slot = slot;
                item.attachBoneName = boneName;
                item.skinned = false;
                item.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (creatingItem)
                {
                    AssetDatabase.CreateAsset(item, assetPath);
                    Undo.RecordObject(entry, "Link captured armor item");
                    entry.appearanceItem = item;
                    EditorUtility.SetDirty(entry);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry);
                }
                else EditorUtility.SetDirty(item);
            }
            else
            {
                EditorUtility.SetDirty(existing);
            }
        }

        static void CaptureSkinned(ArmorShowcaseEntry entry, AppearanceItemDefinition item,
            SkinnedArmorFitAdjustment adjustment)
        {
            var renderers = entry.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var sources = adjustment.SourceMeshes;
            if (sources == null || sources.Length != renderers.Length)
                throw new InvalidOperationException("The skinned fitting preview is not initialized.");
            var baked = new HashSet<Mesh>();
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (!source || !baked.Add(source)) continue;
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source)))
                    throw new InvalidOperationException("A skinned source mesh is not saved as an asset.");
                var adjusted = Instantiate(source);
                try
                {
                    SkinnedArmorFitAdjustment.ApplyMatrix(source, adjusted, adjustment.AdjustmentMatrix);
                    EditorUtility.CopySerialized(adjusted, source);
                    EditorUtility.SetDirty(source);
                }
                finally { DestroyImmediate(adjusted); }
            }
            adjustment.ResetTransformToBaseline();
        }

        static void RegisterInCatalog(AppearanceItemDefinition item)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
            if (!catalog) return;
            var current = catalog.wardrobeItems ?? Array.Empty<AppearanceItemDefinition>();
            if (current.Contains(item)) return;
            Undo.RecordObject(catalog, "Register captured armor");
            catalog.wardrobeItems = current.Concat(new[] { item }).ToArray();
            EditorUtility.SetDirty(catalog);
        }

        static string GuessBoneName(ArmorShowcaseEntry entry)
        {
            var character = entry.GetComponentInParent<CharacterCosmeticsController>();
            if (!character) return null;
            var parent = entry.transform.parent;
            if (parent && character.skeletonRoot && parent.IsChildOf(character.skeletonRoot) &&
                !parent.GetComponentInParent<AppearanceSpawnedItem>())
                return parent.name;
            return "$CharacterRoot";
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CharacterCreator/CapturedArmor"))
                AssetDatabase.CreateFolder("Assets/CharacterCreator", "CapturedArmor");
            if (!AssetDatabase.IsValidFolder(CapturedFolder))
                AssetDatabase.CreateFolder("Assets/CharacterCreator/CapturedArmor", "Items");
        }

        static string SanitizeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Trim().Replace(' ', '_');
        }
    }
}
