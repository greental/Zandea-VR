using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class BoStaffRecoveryPlayValidation
    {
        const string Pending = "KenshiVR.BoStaffRecoveryPlayValidation.Pending";
        static float started;
        static bool prepared, placed, sawReservation, sawPath, sawMovement;
        static int placedAfterFrame;
        static PhysicalMeleeWeapon weapon;
        static NpcGeneratedWeapon equipment;
        static Vector3 initialPosition;
        static BoStaffRecoveryPlayValidation() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorSceneManager.OpenScene(BoStaffDisarmArenaBuilder.Output);
            EditorPrefs.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (!Application.isBatchMode || !EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            var telemetry = Object.FindFirstObjectByType<BanditArenaTelemetry>();
            if (!telemetry || !telemetry.left || !telemetry.right) return;
            if (!prepared)
            {
                prepared = true;
                started = Time.realtimeSinceStartup;
                var left = telemetry.left;
                var right = telemetry.right;
                right.enabled = false;
                var rightNav = right.GetComponent<NavMeshAgent>();
                if (rightNav && rightNav.enabled && rightNav.isOnNavMesh) rightNav.isStopped = true;
                equipment = left.GetComponent<NpcGeneratedWeapon>();
                weapon = equipment ? equipment.Instance : null;
                if (!weapon) { Finish(false, "no initial shared staff"); return; }
                initialPosition = left.transform.position;
                equipment.acquireNearbyWeapons = false;
                equipment.Drop();
                placedAfterFrame = Time.frameCount + 5;
            }
            if (!placed)
            {
                if (Time.frameCount < placedAfterFrame) return;
                placed = true;
                started = Time.realtimeSinceStartup;
                weapon.transform.position = initialPosition + telemetry.left.transform.right * 2.8f + Vector3.up * .6f;
                weapon.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                equipment.acquireNearbyWeapons = true;
            }
            sawReservation |= weapon && weapon.Reservation == equipment;
            var nav = telemetry.left.GetComponent<NavMeshAgent>();
            sawPath |= nav && nav.enabled && nav.isOnNavMesh &&
                (nav.pathPending || nav.hasPath);
            sawMovement |= telemetry.left && Vector3.Distance(telemetry.left.transform.position, initialPosition) > .8f;
            if (equipment && equipment.Instance == weapon)
            {
                Finish(sawReservation && sawPath && sawMovement && weapon.Owner == equipment &&
                    weapon.State == PhysicalMeleeWeapon.HoldState.HeldByNpc,
                    $"reserved={sawReservation} path={sawPath} moved={sawMovement} sameInstance={equipment.Instance == weapon} elapsed={Time.realtimeSinceStartup - started:F1}s");
                return;
            }
            if (Time.realtimeSinceStartup - started > 20f)
                Finish(false, $"timeout reserved={sawReservation} path={sawPath} moved={sawMovement} weaponState={weapon?.State} npcPosition={telemetry.left.transform.position} weaponPosition={weapon?.transform.position}");
        }
        static void Finish(bool pass, string details)
        {
            Debug.Log($"RECOVERY_PLAY_{(pass ? "PASS" : "FAIL")} {details}");
            EditorPrefs.DeleteKey(Pending);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
