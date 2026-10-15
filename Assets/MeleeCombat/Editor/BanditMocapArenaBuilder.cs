using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BanditMocapArenaBuilder
{
    public const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants/BoStaffFullCombatArena.unity";
    [MenuItem("Tools/Melee Combat/Build Bo Staff full combat arena")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        foreach (var old in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff.prefab");
        if (!prefab) throw new System.InvalidOperationException("Bo Staff prefab missing");
        StarvingBandit Spawn(string name, Vector3 position, Quaternion rotation, int firstDirection, float delay)
        {
            var fighter = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            fighter.name = name;
            fighter.transform.SetPositionAndRotation(position, rotation);
            var brain = fighter.GetComponent<StarvingBandit>();
            brain.detectRange = 12f;
            brain.dodgeIncomingNpcAttacks = true;
            brain.dodgeChance = .38f;
            brain.dodgeCooldown = 1.7f;
            brain.parryIncomingNpcAttacks = true;
            brain.parryChance = .72f;
            brain.cycleAttackDirections = true;
            brain.firstAttackDirection = firstDirection;
            brain.initialAttackDelay = delay;
            // The joint and collider still produce weapon resistance and parry contacts;
            // the duel scorer awards one predictable body hit per clean attack.
            brain.staff.damagePerJoule = 0f;
            var score = fighter.GetComponent<BanditDuelStrikeDamage>();
            if (!score) score = fighter.AddComponent<BanditDuelStrikeDamage>();
            score.brain = brain;
            score.damage = 8f;
            score.cleanHitChance = .76f;
            score.maximumRootDistance = 1.9f;
            return brain;
        }
        var ash = Spawn("Ash - Bo Staff", new Vector3(0, 0, -2.7f), Quaternion.identity, 0, 0f);
        var rust = Spawn("Rust - Bo Staff", new Vector3(0, 0, 2.7f), Quaternion.Euler(0, 180, 0), 4, 4f);
        ash.target = rust.transform;
        rust.target = ash.transform;
        var monitor = Object.FindFirstObjectByType<BanditDuelMonitor>();
        if (!monitor) monitor = new GameObject("Arena HUD").AddComponent<BanditDuelMonitor>();
        monitor.left = ash.health;
        monitor.right = rust.health;
        monitor.captureSnapshots = false;
        var telemetry = monitor.GetComponent<BanditArenaTelemetry>();
        if (!telemetry) telemetry = monitor.gameObject.AddComponent<BanditArenaTelemetry>();
        telemetry.left = ash;
        telemetry.right = rust;
        var camera = Object.FindFirstObjectByType<Camera>();
        camera.transform.position = new Vector3(4.8f, 3.2f, -7.5f);
        camera.transform.LookAt(new Vector3(0, 1.1f, 0));
        EditorSceneManager.SaveScene(scene, Output);
        AssetDatabase.SaveAssets();
        Debug.Log("MOCAP_FULL_ARENA " + Output);
    }
}
