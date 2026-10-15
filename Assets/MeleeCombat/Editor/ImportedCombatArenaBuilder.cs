using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ImportedCombatArenaBuilder
{
    public const string Output = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatArena.unity";

    [MenuItem("Tools/Melee Combat/Build imported combat arena")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        foreach (var old in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        StarvingBandit Spawn(string variant, Vector3 position, Quaternion rotation, int first, float delay)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_" + variant + ".prefab");
            var fighter = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            fighter.name = variant + " bandit";
            fighter.transform.SetPositionAndRotation(position, rotation);
            var brain = fighter.GetComponent<StarvingBandit>();
            brain.detectRange = 12f;
            brain.cycleAttackDirections = true;
            brain.firstAttackDirection = first;
            brain.initialAttackDelay = delay;
            brain.dodgeIncomingNpcAttacks = true;
            brain.dodgeChance = .38f;
            brain.parryIncomingNpcAttacks = true;
            brain.parryChance = .72f;
            brain.health.maxHealth = 160f;
            brain.staff.damagePerJoule = 0f;
            var score = fighter.GetComponent<BanditDuelStrikeDamage>();
            if (!score) score = fighter.AddComponent<BanditDuelStrikeDamage>();
            score.brain = brain;
            score.damage = 7f;
            score.cleanHitChance = .7f;
            score.maximumRootDistance = 1.9f;
            return brain;
        }
        var axe = Spawn("Axe", new Vector3(0, 0, -2.7f), Quaternion.identity, 0, 0f);
        var hook = Spawn("HookSword", new Vector3(0, 0, 2.7f), Quaternion.Euler(0, 180, 0), 4, 5f);
        axe.target = hook.transform;
        hook.target = axe.transform;
        var monitor = Object.FindFirstObjectByType<BanditDuelMonitor>();
        if (!monitor) monitor = new GameObject("Arena HUD").AddComponent<BanditDuelMonitor>();
        monitor.left = axe.health;
        monitor.right = hook.health;
        monitor.captureSnapshots = false;
        var telemetry = monitor.GetComponent<BanditArenaTelemetry>();
        if (!telemetry) telemetry = monitor.gameObject.AddComponent<BanditArenaTelemetry>();
        telemetry.left = axe;
        telemetry.right = hook;
        var camera = Object.FindFirstObjectByType<Camera>();
        camera.transform.position = new Vector3(4.8f, 3.2f, -7.5f);
        camera.transform.LookAt(new Vector3(0, 1.1f, 0));
        EditorSceneManager.SaveScene(scene, Output);
        AssetDatabase.SaveAssets();
        Debug.Log("IMPORTED_COMBAT_ARENA " + Output);
    }
}
