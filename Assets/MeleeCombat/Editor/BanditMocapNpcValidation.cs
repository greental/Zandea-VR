using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;

[InitializeOnLoad]
public static class BanditMocapNpcValidation
{
    const string Key = "BanditMocapNpcValidation.Running";
    static GameObject bandit;
    static StarvingBandit brain;
    static BanditMocapNpcDriver driver;
    static ScavengerHealth health;
    static ScavengerRagdoll ragdoll;
    static Animator animator;
    static Transform target;
    static StarvingBandit threat;
    static float start;
    static int phase;

    static BanditMocapNpcValidation() { EditorApplication.update += Tick; }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/MeleeCombat/BanditDuel.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void Setup()
    {
        if (!NavMesh.SamplePosition(Vector3.zero, out var hit, 20, NavMesh.AllAreas))
            throw new System.Exception("No baked NavMesh near duel arena");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_SpikedMace.prefab");
        bandit = Object.Instantiate(prefab, hit.position, Quaternion.identity);
        brain = bandit.GetComponent<StarvingBandit>();
        health = bandit.GetComponent<ScavengerHealth>();
        ragdoll = bandit.GetComponent<ScavengerRagdoll>();
        driver = bandit.GetComponentInChildren<BanditMocapNpcDriver>();
        animator = driver.GetComponent<Animator>();
        target = new GameObject("Validation target").transform;
        target.position = hit.position + Vector3.forward * 5;
        brain.target = target;
        brain.detectRange = 20;
        start = Time.realtimeSinceStartup;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (!bandit) Setup();
        float elapsed = Time.realtimeSinceStartup - start;
        if (phase == 0 && elapsed > .8f && brain.agent.velocity.magnitude > .05f)
        {
            Debug.Log($"NPC_CHECK approach={brain.state} nav={brain.agent.isOnNavMesh} speed={brain.agent.velocity.magnitude:F2} controller={animator.runtimeAnimatorController != null} driver={driver.enabled}");
            if (!brain.agent.isOnNavMesh || !driver.enabled || !animator.runtimeAnimatorController ||
                brain.state != StarvingBandit.State.Approach)
                throw new System.Exception("NPC animation wiring failed");
            phase = 1;
            var threatObject = new GameObject("Validation incoming attacker");
            threat = threatObject.AddComponent<StarvingBandit>();
            threat.enabled = false;
            threat.state = StarvingBandit.State.Strike;
            threat.transform.position = bandit.transform.position + bandit.transform.forward * 1.8f;
            brain.target = threat.transform;
            brain.parryChance = 1;
            start = Time.realtimeSinceStartup;
        }
        else if (phase == 0 && elapsed > 3f)
            throw new System.Exception("NPC never began navigating");
        else if (phase == 1 && brain.state == StarvingBandit.State.Parry)
        {
            Debug.Log($"NPC_CHECK parry={brain.state}");
            brain.target = target;
            health.onDamaged.Invoke();
            phase = 2;
            start = Time.realtimeSinceStartup;
        }
        else if (phase == 1 && elapsed > 2f)
            throw new System.Exception("NPC never parried incoming attack");
        else if (phase == 2 && elapsed > .15f)
        {
            Debug.Log($"NPC_CHECK hit={animator.GetCurrentAnimatorStateInfo(0).IsName("Hit") || animator.IsInTransition(0)}");
            phase = 3;
            target.position = bandit.transform.position + bandit.transform.forward * 1.25f;
            start = Time.realtimeSinceStartup;
        }
        else if (phase == 3 && (brain.state == StarvingBandit.State.Anticipate ||
            brain.state == StarvingBandit.State.Strike))
        {
            Debug.Log($"NPC_CHECK attack={brain.state} animator={animator.GetCurrentAnimatorStateInfo(0).shortNameHash}");
            phase = 4;
            health.ApplyDamage(new DamagePacket { BaseDamage = health.maxHealth + 1,
                HitPointWorld = bandit.transform.position + Vector3.up,
                HitNormalWorld = Vector3.back });
            start = Time.realtimeSinceStartup;
        }
        else if (phase == 3 && elapsed > 4f)
            throw new System.Exception("NPC never started an attack");
        else if (phase == 4 && elapsed > .15f)
        {
            Debug.Log($"NPC_CHECK dead={health.IsDead} ragdoll={ragdoll.Active} animator={animator.enabled}");
            if (!health.IsDead || !ragdoll.Active || animator.enabled)
                throw new System.Exception("Ragdoll death failed");
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
