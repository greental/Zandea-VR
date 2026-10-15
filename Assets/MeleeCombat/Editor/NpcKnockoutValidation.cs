using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;

[InitializeOnLoad]
public static class NpcKnockoutValidation
{
    const string Key = "KenshiVR.NpcKnockoutValidation";
    const string Arena = "Assets/MeleeCombat/MartialArts/MartialArtsVRArena.unity";
    const string ArmedPrefab = "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab";
    static ScavengerHealth unarmed, armed;
    static NpcGeneratedWeapon equipment;
    static int phase;
    static float next;
    static bool pass;

    static NpcKnockoutValidation() { EditorApplication.update += Tick; }

    [MenuItem("Kenshi VR/Validate NPC Knockout Slice")]
    public static void Run()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene(Arena);
        SessionState.SetBool(Key, true);
        phase = 0;
        pass = true;
        if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
    }

    static void Check(bool condition, string name)
    {
        pass &= condition;
        Debug.Log($"KO_CHECK {(condition ? "PASS" : "FAIL")} {name}");
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.time < next) return;
        if (phase == 0)
        {
            foreach (var bandit in Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
            {
                if (!unarmed && bandit.martialArts && !bandit.staff) unarmed = bandit.health;
                bandit.enabled = false;
            }
            if (!unarmed) { Finish(false, "unarmed bandit missing"); return; }
            var attacker = new GameObject("Knockout chance probe");
            var attackerStats = attacker.AddComponent<CharacterAttributes>();
            var defenderStats = unarmed.GetComponent<CharacterAttributes>();
            var head = unarmed.GetComponent<ScavengerRagdoll>().animator.GetBoneTransform(HumanBodyBones.Head);
            var probe = new DamagePacket { DamageType = DamageType.Kinetic,
                PhysicalImpact = PhysicalImpactKind.Unarmed, Source = attacker, HitPointWorld = head.position };
            int toughness = defenderStats.Get(CharacterAttribute.Toughness);
            defenderStats.SetRuntime(CharacterAttribute.Toughness, 50);
            attackerStats.SetRuntime(CharacterAttribute.Strength, 50);
            attackerStats.SetRuntime(CharacterAttribute.MartialArts, 10);
            float novice = unarmed.HeadKnockoutChance(probe, 12f);
            attackerStats.SetRuntime(CharacterAttribute.MartialArts, 90);
            float skilled = unarmed.HeadKnockoutChance(probe, 12f);
            defenderStats.SetRuntime(CharacterAttribute.Toughness, 90);
            float tough = unarmed.HeadKnockoutChance(probe, 12f);
            Check(novice < skilled && tough < skilled && skilled < 1f &&
                unarmed.HeadKnockoutChance(probe, 1f) == 0f,
                "head chance uses skill, toughness, and impact threshold");
            defenderStats.SetRuntime(CharacterAttribute.Toughness, toughness);
            Object.Destroy(attacker);
            unarmed.knockoutDuration = 1f;
            unarmed.knockoutTrauma = 15f;
            unarmed.recoveryHealthFraction = .05f;
            Hit(unarmed, 8f, PhysicalImpactKind.Unarmed, unarmed.gameObject);
            Check(unarmed.IsUnconscious && !unarmed.IsDead && unarmed.GetComponent<ScavengerRagdoll>().Active,
                "fist knockout produces a living ragdoll");
            next = Time.time + 2.5f;
            phase = 1;
        }
        else if (phase == 1)
        {
            Check(unarmed.State == NpcConsciousness.Conscious && !unarmed.GetComponent<ScavengerRagdoll>().Active,
                "bandit wakes from fist knockout");
            Check(unarmed.GetComponent<NavMeshAgent>().isOnNavMesh, "navigation restored");
            Hit(unarmed, 8f, PhysicalImpactKind.Unarmed, unarmed.gameObject);
            Check(unarmed.IsUnconscious, "repeat knockout works");
            unarmed.ForceDeath();
            next = Time.time + 2f;
            phase = 2;
        }
        else if (phase == 2)
        {
            Check(unarmed.IsDead && unarmed.GetComponent<ScavengerRagdoll>().Active, "dead bandit never wakes");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArmedPrefab);
            if (!prefab) { Finish(false, "armed prefab missing"); return; }
            Vector3 position = unarmed.transform.position + Vector3.right * 2f;
            if (NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas)) position = hit.position;
            var instance = Object.Instantiate(prefab, position, Quaternion.identity);
            armed = instance.GetComponent<ScavengerHealth>();
            equipment = instance.GetComponent<NpcGeneratedWeapon>();
            instance.GetComponent<StarvingBandit>().enabled = false;
            if (!armed || !equipment) { Finish(false, "armed components missing"); return; }
            armed.knockoutDuration = 1f;
            armed.knockoutTrauma = 15f;
            armed.recoveryHealthFraction = .05f;
            next = Time.time + .3f;
            phase = 3;
        }
        else if (phase == 3)
        {
            var staff = equipment.Instance;
            Check(staff && staff.State == PhysicalMeleeWeapon.HoldState.HeldByNpc, "staff starts equipped");
            Hit(armed, 20f, PhysicalImpactKind.Blunt, unarmed.gameObject);
            Check(armed.IsUnconscious && equipment.Instance == null &&
                staff && staff.State == PhysicalMeleeWeapon.HoldState.World, "staff drops on knockout");
            next = Time.time + 2.5f;
            phase = 4;
        }
        else if (phase == 4)
        {
            Check(armed.State == NpcConsciousness.Conscious && equipment.Instance == null &&
                !equipment.acquireNearbyWeapons, "bandit wakes without reclaiming the staff");
            Finish(pass, "complete");
        }
    }

    static void Hit(ScavengerHealth target, float damage, PhysicalImpactKind kind, GameObject source) =>
        target.ApplyDamage(new DamagePacket { BaseDamage = damage, DamageType = DamageType.Kinetic,
            PhysicalImpact = kind, Source = source,
            HitPointWorld = target.transform.position + Vector3.up * 1.3f });

    static void Finish(bool success, string reason)
    {
        SessionState.SetBool(Key, false);
        Debug.Log($"KO_VALIDATION_{(success ? "PASS" : "FAIL")} {reason}");
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode)
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}
