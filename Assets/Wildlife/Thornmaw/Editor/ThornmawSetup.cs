using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Zandea.Wildlife.Editor
{
    public sealed class ThornmawModelImport : AssetPostprocessor
    {
        const string ModelPath = "Assets/Wildlife/Thornmaw/Art/Thornmaw_Rigged.fbx";

        void OnPreprocessModel()
        {
            if (assetPath != ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Contains(ModelPath) || imported.Contains("Assets/Wildlife/Thornmaw/Editor/ThornmawSetup.cs"))
                EditorApplication.delayCall += ThornmawSetup.BuildIfMissing;
        }
    }

    public static class ThornmawSetup
    {
        const string Folder = "Assets/Wildlife/Thornmaw";
        const string ModelPath = Folder + "/Art/Thornmaw_Rigged.fbx";

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.delayCall += BuildIfMissing;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += BuildIfMissing;
            };
        }

        internal static void BuildIfMissing()
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Thornmaw.prefab")) Build();
            else EnsureBehaviorAndSpawn();
        }

        static void ConfigureHunter(GameObject root)
        {
            var animator = root.GetComponent<Animator>();
            if (animator && !animator.avatar)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>()
                    .FirstOrDefault(a => a && a.isValid);
                if (avatar) animator.avatar = avatar;
            }
            var collider = root.GetComponent<CapsuleCollider>();
            if (!collider) collider = root.AddComponent<CapsuleCollider>();
            collider.center = Vector3.zero;
            collider.height = 1.1f;
            collider.radius = .42f;
            var body = root.GetComponent<Rigidbody>();
            if (!body) body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            if (!root.GetComponent<ThornmawHunter>()) root.AddComponent<ThornmawHunter>();
        }

        [MenuItem("Zandea/Thornmaw/Update Hunter Behavior")]
        public static void EnsureBehaviorAndSpawn()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prefabPath = Folder + "/Thornmaw.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) return;
            var prefabAnimator = prefab.GetComponent<Animator>();
            var generatedAvatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>()
                .FirstOrDefault(a => a && a.isValid);
            if (!prefab.GetComponent<ThornmawHunter>() || !prefab.GetComponent<CapsuleCollider>() ||
                !prefab.GetComponent<Rigidbody>() || (prefabAnimator && !prefabAnimator.avatar && generatedAvatar))
            {
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    ConfigureHunter(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            var settingsPath = "Assets/Resources/Wildlife/ThornmawSpawnSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<ThornmawSpawnSettings>(settingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<ThornmawSpawnSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            var hunter = prefab.GetComponent<ThornmawHunter>();
            if (settings.prefab != hunter)
            {
                settings.prefab = hunter;
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Zandea/Thornmaw/Build Rigged Prefab")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) return;
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer != null)
            {
                var clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                if (clips != null && clips.Length > 0 && clips.Any(c => !c.loopTime || !c.loopPose))
                {
                    foreach (var clip in clips) { clip.loopTime = true; clip.loopPose = true; }
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                    return;
                }
            }

            var animations = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)).ToArray();
            var walk = animations.FirstOrDefault(c => c.name.Contains("Thornmaw_Walk"));
            var run = animations.FirstOrDefault(c => c.name.Contains("Thornmaw_Run"));
            if (!walk || !run)
            {
                Debug.LogError("Thornmaw: walk and run clips missing from FBX. Found: " + string.Join(", ", animations.Select(c => c.name)));
                return;
            }

            var materialPath = Folder + "/Thornmaw.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = "Thornmaw" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Art/Thornmaw_Albedo.png");
            if (texture)
            {
                material.mainTexture = texture;
                EditorUtility.SetDirty(material);
            }

            var controllerPath = Folder + "/Thornmaw.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (!controller.parameters.Any(p => p.name == "IsRunning"))
                controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var walkState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Walk") ?? machine.AddState("Walk");
            var runState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Run") ?? machine.AddState("Run");
            walkState.motion = walk;
            runState.motion = run;
            machine.defaultState = walkState;
            if (!walkState.transitions.Any(t => t.destinationState == runState))
            {
                var transition = walkState.AddTransition(runState);
                transition.hasExitTime = false;
                transition.duration = 0.15f;
                transition.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
            }
            if (!runState.transitions.Any(t => t.destinationState == walkState))
            {
                var transition = runState.AddTransition(walkState);
                transition.hasExitTime = false;
                transition.duration = 0.15f;
                transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");
            }

            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Thornmaw";
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
                // Unity components can be "fake null" after native destruction; ?? only checks the managed reference.
                var animator = instance.GetComponent<Animator>();
                if (!animator) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                ConfigureHunter(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, Folder + "/Thornmaw.prefab");
            }
            finally
            {
                if (instance) UnityEngine.Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
            EnsureBehaviorAndSpawn();
            Debug.Log("Thornmaw prefab ready: " + Folder + "/Thornmaw.prefab");
        }
    }
}
