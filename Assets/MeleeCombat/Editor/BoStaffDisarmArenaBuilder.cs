using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class BoStaffDisarmArenaBuilder
    {
        public const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffDisarmArena.unity";
        const string Npc = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";

        [MenuItem("Tools/Melee Combat/Build Bo Staff disarm arena")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
            foreach (var old in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Npc);
            if (!prefab) throw new InvalidOperationException("Unified Bo Staff NPC prefab missing");
            StarvingBandit Spawn(string name, Vector3 position, Quaternion rotation, int direction, float delay)
            {
                var fighter = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                fighter.name = name;
                fighter.transform.SetPositionAndRotation(position, rotation);
                var brain = fighter.GetComponent<StarvingBandit>();
                brain.detectRange = 12f;
                brain.minimumFightDistance = .45f;
                brain.preferredFightDistance = .75f;
                brain.postAttackRetreatSeconds = .25f;
                var nav = fighter.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nav) nav.stoppingDistance = .35f;
                brain.dodgeChance = 0f;
                brain.parryChance = 1f;
                foreach (var attack in brain.attacks)
                    if (attack != null) attack.comfortableRange = .7f;
                brain.cycleAttackDirections = true;
                brain.firstAttackDirection = direction;
                brain.initialAttackDelay = delay;
                brain.dodgeIncomingNpcAttacks = true;
                brain.parryIncomingNpcAttacks = true;
                brain.health.maxHealth = 160f;
                var scorer = fighter.GetComponent<BanditDuelStrikeDamage>();
                if (!scorer) scorer = fighter.AddComponent<BanditDuelStrikeDamage>();
                scorer.brain = brain;
                scorer.damage = 7f;
                scorer.cleanHitChance = .7f;
                scorer.maximumRootDistance = 1.9f;
                return brain;
            }
            var ash = Spawn("Ash - unified Bo Staff", new Vector3(0, 0, -2.7f), Quaternion.identity, 0, 0f);
            var rust = Spawn("Rust - unified Bo Staff", new Vector3(0, 0, 2.7f), Quaternion.Euler(0, 180, 0), 4, 4f);
            ash.target = rust.transform;
            rust.target = ash.transform;
            var monitor = UnityEngine.Object.FindFirstObjectByType<BanditDuelMonitor>();
            if (!monitor) monitor = new GameObject("Arena HUD").AddComponent<BanditDuelMonitor>();
            monitor.left = ash.health;
            monitor.right = rust.health;
            monitor.captureSnapshots = false;
            var telemetry = monitor.GetComponent<BanditArenaTelemetry>();
            if (!telemetry) telemetry = monitor.gameObject.AddComponent<BanditArenaTelemetry>();
            telemetry.left = ash;
            telemetry.right = rust;
            var resolver = new GameObject("Weapon disarm resolver").AddComponent<WeaponDisarmResolver>();
            resolver.logAccepted = resolver.logRolls = resolver.logTransitions = true;
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera)
            {
                camera.transform.position = new Vector3(4.8f, 3.2f, -7.5f);
                camera.transform.LookAt(new Vector3(0, 1.1f, 0));
            }
            EditorSceneManager.SaveScene(scene, Output);
            AssetDatabase.SaveAssets();
            Debug.Log("Bo Staff disarm arena built: " + Output);
        }
    }
}
