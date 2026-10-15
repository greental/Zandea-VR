using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ScavengerNPC;
using VRKoombar.WeaponsStaging.Contracts;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class BoStaffDisarmValidation
    {
        const string Pending = "KenshiVR.BoStaffDisarmValidation.Pending";
        static bool ran;
        static BoStaffDisarmValidation() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorPrefs.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!Application.isBatchMode || !EditorPrefs.GetBool(Pending, false) ||
                !EditorApplication.isPlaying || ran) return;
            ran = true;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab");
                Check(prefab, "unified NPC prefab");
                var leftObject = UnityEngine.Object.Instantiate(prefab, new Vector3(-3, 0, 0), Quaternion.identity);
                var rightObject = UnityEngine.Object.Instantiate(prefab, new Vector3(3, 0, 0), Quaternion.identity);
                var left = leftObject.GetComponent<NpcGeneratedWeapon>();
                var right = rightObject.GetComponent<NpcGeneratedWeapon>();
                var attacker = leftObject.GetComponent<StarvingBandit>();
                var defender = rightObject.GetComponent<StarvingBandit>();
                attacker.target = defender.transform;
                defender.target = attacker.transform;
                attacker.state = StarvingBandit.State.Strike;
                defender.state = StarvingBandit.State.Block;
                var a = left.Instance;
                var b = right.Instance;
                Check(a && b && a != b, "distinct shared prefab instances equipped");
                var resolver = new GameObject("Disarm validation resolver").AddComponent<WeaponDisarmResolver>();
                resolver.useSeededRandom = true;
                resolver.randomSeed = 12;
                resolver.logRejected = resolver.logAccepted = resolver.logRolls = true;
                resolver.mode = WeaponDisarmResolver.ProbabilityMode.ForcedTest;
                resolver.pairCooldown = 0;
                Check(Mathf.Approximately(resolver.baselineChance, .05f), "baseline chance is five percent");
                Check(!resolver.Evaluate(a, b, .2f, .05f, Vector3.right), "minor contact rejected");
                Check(resolver.ProbabilityRolls == 0 && right.Instance == b, "minor contact never rolled");
                Check(!resolver.Evaluate(a, b, 5f, 4f, Vector3.right), "sustained contact rejected");
                Check(resolver.ProbabilityRolls == 0, "sustained contact never rolled");
                resolver.NotifySeparation(a, b);
                Check(resolver.Evaluate(a, b, 5f, 4f, Vector3.right), "forced qualifying disarm");
                Check(resolver.SuccessfulDisarms == 1 && resolver.ProbabilityRolls == 1, "single disarm roll");
                Check(right.Instance == null && b.State == PhysicalMeleeWeapon.HoldState.World &&
                    b.Owner == null && b.GetComponent<Rigidbody>().useGravity &&
                    b.GetComponent<XRGrabInteractable>().enabled && PhysicalMeleeWeapon.Active.Contains(b),
                    "real weapon physically released and grabbable");
                right.Drop();
                Check(resolver.SuccessfulDisarms == 1 && b.State == PhysicalMeleeWeapon.HoldState.World,
                    "repeated drop idempotent");
                Check(b.TryReserve(right), "world reservation");
                Check(right.TryEquipWorldWeapon(b), "dropped instance re-equipped");
                Check(right.Instance == b && b.Owner == right && b.Reservation == null, "ownership and reservation cleared");
                resolver.NotifySeparation(a, b);
                resolver.pairCooldown = 2f;
                Check(!resolver.Evaluate(a, b, 5f, 4f, Vector3.right) && resolver.ProbabilityRolls == 1,
                    "pair cooldown prevents repeat roll");
                resolver.NotifySeparation(a, b);
                resolver.pairCooldown = 0;
                resolver.mode = WeaponDisarmResolver.ProbabilityMode.Disabled;
                attacker.state = StarvingBandit.State.Strike;
                defender.state = StarvingBandit.State.Block;
                Check(!resolver.Evaluate(a, b, 5f, 4f, Vector3.right), "disabled probability rejects disarm");
                Debug.Log($"Disabled check: instance={right.Instance == b}, rolls={resolver.ProbabilityRolls}, aState={a.State}, bState={b.State}, attacker={attacker.state}, defender={defender.state}");
                Check(right.Instance == b && resolver.ProbabilityRolls == 2, "zero chance keeps weapon");
                var health = rightObject.GetComponent<ScavengerHealth>();
                health.ApplyDamage(new DamagePacket { BaseDamage = health.maxHealth + 1,
                    DamageType = DamageType.Kinetic, Source = leftObject, Timestamp = Time.time });
                right.Drop();
                Check(health.IsDead && right.Instance == null && b.State == PhysicalMeleeWeapon.HoldState.World,
                    "death and repeated drop release exactly once");
                Check(a.State == PhysicalMeleeWeapon.HoldState.HeldByNpc,
                    "other wielder and parry weapon remain intact");
                Debug.Log("DISARM_VALIDATION_PASS minor=0 rolls, sustained=0 rolls, forced=1 disarm, disabled=0 disarms, same-instance re-equip, death idempotent");
                Finish(0);
            }
            catch (Exception error)
            {
                Debug.LogError("DISARM_VALIDATION_FAIL " + error);
                Finish(1);
            }
        }
        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
        }
        static void Finish(int exit)
        {
            EditorPrefs.DeleteKey(Pending);
            EditorApplication.Exit(exit);
        }
    }
}
