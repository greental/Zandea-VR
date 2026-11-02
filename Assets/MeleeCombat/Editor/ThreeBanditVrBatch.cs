using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class ThreeBanditVrBatch
    {
        const string Pending = "KenshiVR.ThreeBanditVrBatch.Pending";
        const string Started = "KenshiVR.ThreeBanditVrBatch.Started";
        const string Mixed = "KenshiVR.ThreeBanditVrBatch.Mixed";
        static double nextSwing;
        static double nextDiagnostic;
        static bool initialized;
        static bool swingPhase;

        static ThreeBanditVrBatch() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ThreeBanditVrParrySceneBuilder.ScenePath);
            EditorPrefs.SetBool(Pending, true);
            EditorPrefs.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            EditorApplication.isPlaying = true;
        }

        public static void RunMixedRange()
        {
            EditorPrefs.SetBool(Mixed, true);
            Run();
        }

        static void Tick()
        {
            if (!EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            var proof = Object.FindFirstObjectByType<ThreeBanditVrCombatProof>();
            if (proof && proof.setupValid && !initialized)
            {
                initialized = true;
                if (EditorPrefs.GetBool(Mixed, false))
                {
                    var profile = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_LongBlade.prefab");
                    var generated = profile ? profile.GetComponent<NpcGeneratedWeapon>() : null;
                    var group = proof.bandits[1].combatGroup;
                    bool equipped = generated && group && group.EquipAuthoredMember(1,
                        generated.weaponPrefab, profile.GetComponent<StarvingBandit>());
                    Debug.Log("THREE_BANDIT_MIXED_EQUIP_" + (equipped ? "PASS" : "FAIL"));
                }
                nextSwing = EditorApplication.timeSinceStartup + 18;
                var playerHealth = proof.player.GetComponentInParent<ScavengerNPC.ScavengerPlayerHealth>();
                if (playerHealth) { playerHealth.maxHealth = 10000f; playerHealth.Restore(); }
                var sword = proof.playerSword;
                var field = typeof(PhysicalMeleeWeapon).GetField("currentState", BindingFlags.Instance | BindingFlags.NonPublic);
                field?.SetValue(sword, PhysicalMeleeWeapon.HoldState.HeldByPlayer);
                sword.ownerRoot = proof.player.transform.root;
                sword.damagePerJoule = 0f;
                sword.GetComponent<Rigidbody>().useGravity = false;
            }
            if (initialized && proof && EditorApplication.timeSinceStartup >= nextSwing)
            {
                if (!swingPhase) { swingPhase = true; proof.bandits[0].defensiveOnly = true; }
                nextSwing = EditorApplication.timeSinceStartup + 1.8;
                var defender = proof.bandits[0];
                var swordBody = proof.playerSword.GetComponent<Rigidbody>();
                swordBody.position = defender.transform.position + defender.transform.forward * 1.25f + Vector3.up * 1.2f;
                swordBody.rotation = Quaternion.identity;
                swordBody.linearVelocity = -defender.transform.forward * 6f;
                swordBody.angularVelocity = Vector3.zero;
            }
            if (proof && EditorApplication.timeSinceStartup >= nextDiagnostic)
            {
                nextDiagnostic = EditorApplication.timeSinceStartup + 5;
                string states = "";
                foreach (var bandit in proof.bandits)
                    if (bandit) states += $" {bandit.name}:{bandit.state}/nav={bandit.agent && bandit.agent.isOnNavMesh}/engaged={bandit.groupEngaged}/dist={Vector3.Distance(bandit.transform.position, proof.player.hmd.position):F1}/reach={(bandit.staff ? bandit.staff.CombatReach : 0f):F2}/ring={bandit.PreferredGroupCombatDistance(1.72f):F2}";
                Debug.Log($"THREE_BANDIT_DIAG hmd={proof.player.hmd.position} {states}");
            }
            if (Time.time < 32f) return;
            bool pass = proof && proof.setupValid && proof.attackStarts > 0 &&
                proof.attacksByBandit[0] > 0 && proof.attacksByBandit[1] > 0 &&
                proof.attacksByBandit[2] > 0 && proof.maxConcurrentAttackers <= 1 &&
                proof.parryAttempts > 0 && proof.playerWeaponContacts > 0 &&
                proof.widestSurroundAngle > 90f;
            float ringDifference = proof ? Mathf.Abs(proof.bandits[0].PreferredGroupCombatDistance(1.72f) -
                proof.bandits[1].PreferredGroupCombatDistance(1.72f)) : 0f;
            if (EditorPrefs.GetBool(Mixed, false)) pass &= ringDifference > .1f;
            Debug.Log($"THREE_BANDIT_VR_BATCH_{(pass ? "PASS" : "FAIL")} " +
                $"setup={proof?.setupValid} attacks={proof?.attackStarts ?? 0} " +
                $"byBandit={proof?.attacksByBandit[0] ?? 0}/{proof?.attacksByBandit[1] ?? 0}/{proof?.attacksByBandit[2] ?? 0} " +
                $"maxConcurrent={proof?.maxConcurrentAttackers ?? 0} " +
                $"parries={proof?.parryAttempts ?? 0} contacts={proof?.playerWeaponContacts ?? 0} " +
                $"spread={proof?.widestSurroundAngle ?? 0f:F1} ringDifference={ringDifference:F2}");
            EditorPrefs.DeleteKey(Pending);
            EditorPrefs.DeleteKey(Started);
            EditorPrefs.DeleteKey(Mixed);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
