using System;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class ThreeBanditVrParrySceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MeleeCombat/ThreeBanditVrParry.unity";
        const string SourceScene = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatVRArena.unity";
        const string MainScene = "Assets/Main.unity";
        const string MainPlayerPrefab = "Assets/CharacterCreator/Player/VR player - Man Creator.prefab";
        const string CombatPlayerPrefab = "Assets/MeleeCombat/VRPlayerMainCombat.prefab";
        const string BanditPrefab = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_ShortSaber.prefab";

        [MenuItem("Zandea/Melee Combat/Build three bandit VR parry scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPrefab);
            if (!prefab) throw new InvalidOperationException("Missing sword bandit prefab: " + BanditPrefab);
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            foreach (var old in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<BanditDuelMonitor>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);

            var oldBody = UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
            if (oldBody) UnityEngine.Object.DestroyImmediate(oldBody.transform.root.gameObject);
            var main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Additive);
            GameObject mainPlayer = null;
            foreach (var root in main.GetRootGameObjects())
                if (root.name == "VR player - Man Creator") { mainPlayer = root; break; }
            if (!mainPlayer || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mainPlayer) != MainPlayerPrefab)
                throw new InvalidOperationException("Main scene's current VR player instance was not found");
            var combatPlayer = PrefabUtility.SaveAsPrefabAsset(mainPlayer, CombatPlayerPrefab);
            if (!combatPlayer) throw new InvalidOperationException("Could not capture Main's VR player overrides");
            var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(combatPlayer, scene);
            playerObject.name = "VR player - Man Creator";
            playerObject.SetActive(true);
            EditorSceneManager.CloseScene(main, true);
            var body = playerObject.GetComponentInChildren<VRBodyController>(true);
            var player = playerObject.transform;
            if (!body || !body.hmd) throw new InvalidOperationException("Main VR player/HMD missing after copy");
            player.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);

            var members = new StarvingBandit[3];
            for (int i = 0; i < members.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / members.Length;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "Sword bandit " + (i + 1);
                instance.transform.SetPositionAndRotation(radial * 3.4f, Quaternion.LookRotation(-radial));
                var bandit = instance.GetComponent<StarvingBandit>();
                bandit.target = body.hmd;
                bandit.parryIncomingNpcAttacks = true;
                bandit.parryChance = .8f;
                bandit.dodgeIncomingNpcAttacks = false;
                bandit.initialAttackDelay = 1.5f + i * .45f;
                members[i] = bandit;
            }
            var group = new GameObject("Three sword bandits - surround group").AddComponent<BanditGroup>();
            SceneManager.MoveGameObjectToScene(group.gameObject, scene);
            group.transform.position = Vector3.zero;
            group.player = body.hmd;
            group.banditPrefab = members[0];
            group.authoredMembers = members;
            group.groupSize = 3;
            group.buildLocalNavMesh = false;
            group.surroundRadius = 1.72f;
            group.sightRange = 20f;
            group.fieldOfView = 360f;

            var proof = new GameObject("Three bandit VR combat results").AddComponent<ThreeBanditVrCombatProof>();
            SceneManager.MoveGameObjectToScene(proof.gameObject, scene);
            proof.player = body;
            proof.bandits = members;
            var playerSword = GameObject.Find("Player practice sword - generated long blade");
            if (playerSword) proof.playerSword = playerSword.GetComponent<PhysicalMeleeWeapon>();
            if (!proof.playerSword) throw new InvalidOperationException("Player practice sword missing from source scene");
            var table = GameObject.Find("Practice sword table");
            if (table) table.transform.position = new Vector3(-.8f, .5f, .6f);
            playerSword.transform.position = new Vector3(-.8f, 1.12f, .6f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(playerSword.transform);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("THREE_BANDIT_VR_SCENE " + ScenePath);
        }
    }
}
