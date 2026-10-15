using System;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class ClothingBakerImport
    {
        static readonly string[] Required = { "WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular" };

        [MenuItem("Assets/Character Creator/Create Clothing Item From Model", true)]
        static bool CanCreate() => Selection.activeObject is GameObject &&
            AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

        [MenuItem("Assets/Character Creator/Create Clothing Item From Model")]
        static void Create()
        {
            var model = (GameObject)Selection.activeObject;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) { Debug.LogError("Model contains no skinned mesh."); return; }
            foreach (var renderer in renderers)
            {
                if (!renderer.sharedMesh) { Debug.LogError("Clothing renderer has no mesh."); return; }
                foreach (var morph in Required)
                    if (renderer.sharedMesh.GetBlendShapeIndex(morph) < 0)
                    { Debug.LogError(model.name + " is missing required morph " + morph); return; }
                if (renderer.bones.Length == 0) { Debug.LogError("Clothing has no skeleton bones."); return; }
            }
            string modelPath = AssetDatabase.GetAssetPath(model);
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                System.IO.Path.ChangeExtension(modelPath, ".asset"));
            var item = ScriptableObject.CreateInstance<AppearanceItemDefinition>();
            item.name = model.name;
            item.prefab = model;
            item.skinned = true;
            item.requiredMorphs = (string[])Required.Clone();
            item.slot = AppearanceSlot.Torso;
            AssetDatabase.CreateAsset(item, destination);
            AssetDatabase.SaveAssets();
            Selection.activeObject = item;
            Debug.Log("Created " + destination + ". Set the slot and assign it to CharacterCosmeticsController.");
        }
    }
}
