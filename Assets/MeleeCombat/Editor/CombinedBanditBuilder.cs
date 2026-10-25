using System;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class CombinedBanditBuilder
    {
        const string ScenePath = "Assets/MeleeCombat/MeleePOC.unity";
        const string ModelPath = "Assets/CharacterCreator/Models/male_creator.glb";
        const string PrefabPath = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";

        [MenuItem("Zandea/Melee Combat/Build combined bandit prefab")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject copy = null;
            try
            {
                StarvingBandit original = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out StarvingBandit found)) { original = found; break; }
                if (!original) throw new InvalidOperationException("Authored bandit missing from " + ScenePath);
                var creatorModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (!creatorModel) throw new InvalidOperationException("Creator model missing: " + ModelPath);

                // Clone the entire authored object. This preserves its guard pose, joints,
                // weapon, attack definitions, navigation, health, and internal references.
                copy = UnityEngine.Object.Instantiate(original.gameObject);
                copy.name = "Combined Starving Bandit";
                var motion = copy.GetComponent<BanditRigMotion>();
                if (!motion || !motion.visualRoot) throw new InvalidOperationException("Authored rig motion missing");
                var visual = motion.visualRoot;
                var sourceMeshes = creatorModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var targetMeshes = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var byPath = new Dictionary<string, SkinnedMeshRenderer>();
                foreach (var source in sourceMeshes) byPath.Add(Path(creatorModel.transform, source.transform), source);
                if (targetMeshes.Length != sourceMeshes.Length) throw new InvalidOperationException("Creator and combat mesh counts differ");
                foreach (var target in targetMeshes)
                {
                    if (!byPath.TryGetValue(Path(visual, target.transform), out var source))
                        throw new InvalidOperationException("Creator mesh path missing: " + target.name);
                    if (source.bones.Length != target.bones.Length)
                        throw new InvalidOperationException("Creator bone count differs: " + target.name);
                    for (int i = 0; i < source.bones.Length; i++)
                        if (source.bones[i].name != target.bones[i].name)
                            throw new InvalidOperationException("Creator bone order differs: " + target.name);
                    target.sharedMesh = source.sharedMesh;
                    target.sharedMaterials = source.sharedMaterials;
                }

                if (!copy.GetComponent<ScavengerNPC.ScavengerRagdoll>())
                    MeleePocBuilder.BuildRagdoll(copy, visual, copy.GetComponent<CapsuleCollider>());

                var appearance = copy.AddComponent<CharacterAppearanceController>();
                appearance.visualRoot = visual;
                var cosmetics = copy.AddComponent<CharacterCosmeticsController>();
                cosmetics.appearance = appearance;
                cosmetics.skeletonRoot = visual;
                cosmetics.catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
                var npc = copy.AddComponent<NPCCharacterAppearance>();
                npc.appearance = appearance;
                npc.cosmetics = cosmetics;
                npc.archetype = AssetDatabase.LoadAssetAtPath<AppearanceArchetype>("Assets/CharacterCreator/Archetypes/Starving Bandit.asset");
                var expressions = copy.AddComponent<NPCFacialExpressionController>();
                expressions.visualRoot = visual;

                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Built combined character prefab: " + PrefabPath);
            }
            finally
            {
                if (copy) UnityEngine.Object.DestroyImmediate(copy);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static string Path(Transform root, Transform child)
        {
            var names = new List<string>();
            for (var t = child; t && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
