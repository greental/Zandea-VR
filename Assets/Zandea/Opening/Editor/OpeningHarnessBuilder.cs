using System;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.Opening.Editor
{
    public static class OpeningHarnessBuilder
    {
        public const string ScenePath = "Assets/Scenes/Zandea/OpeningCaptureTest.unity";
        const string ArenaPath = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatVRArena.unity";
        const string PlayerPath = "Assets/MeleeCombat/VRPlayerMainCombat.prefab";
        const string BanditPath = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";

        [MenuItem("Zandea/Opening/Build test scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPath);
            if (!playerPrefab || !banditPrefab) throw new InvalidOperationException("Opening harness prefabs are missing.");
            var scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            foreach (var old in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.transform.root.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<PhysicalMeleeWeapon>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<BanditDuelMonitor>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            playerObject.name = "Opening VR player";
            playerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            playerObject.SetActive(true);
            var body = playerObject.GetComponentInChildren<VRBodyController>(true);
            var health = playerObject.GetComponentInChildren<ScavengerNPC.ScavengerPlayerHealth>(true);
            if (!body || !body.hmd || !health) throw new InvalidOperationException("Opening player is missing XR body, HMD or health.");
            var members = new StarvingBandit[3];
            for (int i = 0; i < members.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / members.Length;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(banditPrefab, scene);
                instance.name = "Opening staff bandit " + (i + 1);
                instance.transform.SetPositionAndRotation(radial * 3.5f, Quaternion.LookRotation(-radial));
                var bandit = instance.GetComponent<StarvingBandit>();
                if (!bandit) throw new InvalidOperationException("Staff bandit prefab has no StarvingBandit.");
                bandit.target = body.hmd;
                bandit.initialAttackDelay = 2f + i * .4f;
                members[i] = bandit;
            }
            var root = new GameObject("Opening harness");
            SceneManager.MoveGameObjectToScene(root, scene);
            var group = root.AddComponent<BanditGroup>();
            group.player = body.hmd;
            group.banditPrefab = members[0];
            group.authoredMembers = members;
            group.groupSize = members.Length;
            group.buildLocalNavMesh = false;
            group.sightRange = 20f;
            group.fieldOfView = 360f;
            var diagnostics = root.AddComponent<OpeningHarnessDiagnostics>();
            diagnostics.player = body;
            diagnostics.playerHealth = health;
            diagnostics.group = group;
            diagnostics.bandits = members;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Opening harness ready: " + ScenePath);
        }

        [MenuItem("Zandea/Opening/Build and play test scene")]
        public static void BuildAndPlay()
        {
            Build();
            if (SceneManager.GetActiveScene().path == ScenePath) EditorApplication.isPlaying = true;
        }
    }
}
