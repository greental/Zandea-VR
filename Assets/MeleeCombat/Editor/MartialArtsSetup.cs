#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class MartialArtsSetup
    {
        const string Root = "Assets/MeleeCombat/MartialArts";
        const string Clips = "Assets/EEJANAI_Team/FreeFighterAnimations/Animations/";
        const string SourcePrefab = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";
        const string SourceScene = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDisarmArena.unity";

        [MenuItem("Kenshi VR/Build Martial Arts Combat")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/MartialArts.controller"))
            {
                Debug.Log("Martial arts assets already exist; edit the profiles and controller directly.");
                return;
            }
            var sourceController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "/MartialArts.controller");
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            var machine = controller.layers[0].stateMachine;
            var locomotion = sourceController ? FindMotion(sourceController, "Locomotion") : null;
            var idle = machine.AddState("FightIdle");
            idle.motion = LoadClip("5 inch punch");
            idle.speed = 0f;
            machine.defaultState = idle;
            var move = machine.AddState("FightMove");
            move.motion = locomotion;
            var guard = machine.AddState("FightGuard");
            guard.motion = LoadClip("5 inch punch");
            guard.speed = 0f;
            string[] names = { "5 inch punch", "back fist", "combo", "low kick", "axe kick", "knee strike" };
            foreach (string name in names)
            {
                var state = machine.AddState(name);
                state.motion = LoadClip(name);
            }
            var starving = MakeProfile("StarvingBandit", controller, .95f, 1.7f, .08f, .05f, 1.5f,
                new[] { 5f, 4f, 0f, 2f, 0f, 0f });
            var regular = MakeProfile("RegularBandit", controller, 1.1f, 2.1f, .28f, .2f, 1.1f,
                new[] { 7f, 7f, 5f, 4f, 1f, 2f });
            var artist = MakeProfile("MartialArtist", controller, 1.25f, 2.5f, .55f, .45f, .85f,
                new[] { 8f, 8f, 10f, 6f, 5f, 4f });
            AttachProfile(SourcePrefab, starving);
            CreateVariant("RegularBandit_Unarmed", regular);
            CreateVariant("MartialArtist_Unarmed", artist);
            CreateVariant("StarvingBandit_Unarmed", starving);
            AssetDatabase.SaveAssets();
            BuildScene();
            AssetDatabase.Refresh();
            Debug.Log("Martial arts profiles, controller, prefabs, and arena built.");
        }


        static Motion FindMotion(AnimatorController controller, string name)
        {
            foreach (var layer in controller.layers)
                foreach (var state in layer.stateMachine.states)
                    if (state.state.name == name) return state.state.motion;
            return null;
        }

        static AnimationClip LoadClip(string name) =>
            AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + name + ".anim");

        static MartialArtsProfile MakeProfile(string name, RuntimeAnimatorController controller,
            float range, float speed, float block, float counter, float recovery, float[] weights)
        {
            var profile = ScriptableObject.CreateInstance<MartialArtsProfile>();
            profile.controller = controller;
            profile.preferredRange = range;
            profile.approachSpeed = speed;
            profile.blockChance = block;
            profile.counterChance = counter;
            profile.recoveryMultiplier = recovery;
            string[] names = { "5 inch punch", "back fist", "combo", "low kick", "axe kick", "knee strike" };
            HumanBodyBones[] limbs = { HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.RightFoot, HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg };
            profile.strikes = new MartialStrike[names.Length];
            for (int i = 0; i < names.Length; i++)
                profile.strikes[i] = new MartialStrike {
                    stateName = names[i], limb = limbs[i], damage = i >= 3 ? 8f : 6f,
                    range = i >= 3 ? 1.5f : 1.25f, activeFrom = i == 2 ? .25f : .3f,
                    activeUntil = i == 2 ? .78f : .68f, cooldown = i == 2 ? .65f : .4f,
                    staggerStrength = i >= 3 ? 1.4f : 1f, weight = weights[i]
                };
            AssetDatabase.CreateAsset(profile, Root + "/" + name + ".asset");
            return profile;
        }

        static void AttachProfile(string path, MartialArtsProfile profile)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            var martial = root.GetComponent<NpcMartialArts>();
            if (!martial) martial = root.AddComponent<NpcMartialArts>();
            martial.profile = profile;
            martial.brain = root.GetComponent<StarvingBandit>();
            martial.animator = root.GetComponentInChildren<Animator>(true);
            martial.brain.martialArts = martial;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void CreateVariant(string name, MartialArtsProfile profile)
        {
            var root = PrefabUtility.LoadPrefabContents(SourcePrefab);
            var equipment = root.GetComponent<NpcGeneratedWeapon>();
            if (equipment) { equipment.startEquipped = false; equipment.acquireNearbyWeapons = false; }
            var martial = root.GetComponent<NpcMartialArts>();
            martial.profile = profile;
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + name + ".prefab");
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MyAssets/prefabs/Player/VR player - JunkerWeapon.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = Vector3.zero;
            var body = player.GetComponentInChildren<JunkerBody.VRBodyController>(true);
            foreach (var camera in scene.GetRootGameObjects())
                if (camera.name == "Duel camera") camera.SetActive(false);
            var navData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(
                "Assets/MeleeCombat/BanditDuelNavMesh.asset");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!behaviour || behaviour.GetType().FullName != "Unity.AI.Navigation.NavMeshSurface") continue;
                    var serialized = new SerializedObject(behaviour);
                    serialized.FindProperty("m_NavMeshData").objectReferenceValue = navData;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            for (int i = 0; i < 3; i++)
            {
                string name = i == 0 ? "StarvingBandit_Unarmed" : i == 1 ? "RegularBandit_Unarmed" : "MartialArtist_Unarmed";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + name + ".prefab");
                var npc = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                npc.name = name;
                npc.transform.position = new Vector3(-3f + i * 3f, 0f, 4f);
                var brain = npc.GetComponent<StarvingBandit>();
                if (brain && body) brain.target = body.hmd;
                npc.SetActive(i == 0); // Enable additional profiles in Inspector for group testing.
            }
            EditorSceneManager.SaveScene(scene, Root + "/MartialArtsVRArena.unity");
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
