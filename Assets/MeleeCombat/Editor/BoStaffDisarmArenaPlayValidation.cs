using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    public static class BoStaffDisarmArenaPlayValidation
    {
        const string Pending = "KenshiVR.BoStaffDisarmArenaPlayValidation.Pending";
        static float started;
        static bool sawStrike, sawDisarm, sawFallingWeapon;
        static float closestDistance = float.PositiveInfinity;
        static float closestWeaponDistance = float.PositiveInfinity;
        static PhysicalMeleeWeapon dropped;
        static float dropHeight, fallDistance;
        static bool sawUnarmed;
        static BoStaffDisarmArenaPlayValidation() => EditorApplication.update += Tick;

        public static void Run()
        {
            EditorSceneManager.OpenScene(BoStaffDisarmArenaBuilder.Output);
            EditorPrefs.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!Application.isBatchMode || !EditorPrefs.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            var resolver = WeaponDisarmResolver.Active;
            if (!resolver) return;
            if (started == 0)
            {
                started = Time.realtimeSinceStartup;
                resolver.mode = WeaponDisarmResolver.ProbabilityMode.ForcedTest;
                resolver.logRejected = resolver.logAccepted = resolver.logRolls = resolver.logTransitions = true;
            }
            var telemetry = Object.FindFirstObjectByType<BanditArenaTelemetry>();
            if (telemetry && telemetry.left && telemetry.right)
            {
                sawStrike |= telemetry.left.state == StarvingBandit.State.Strike ||
                    telemetry.right.state == StarvingBandit.State.Strike;
                closestDistance = Mathf.Min(closestDistance, Vector3.Distance(
                    telemetry.left.transform.position, telemetry.right.transform.position));
                var lw = telemetry.left.GetComponent<NpcGeneratedWeapon>()?.Instance;
                var rw = telemetry.right.GetComponent<NpcGeneratedWeapon>()?.Instance;
                if (lw && rw)
                {
                    var lc = lw.GetComponent<Collider>();
                    var rc = rw.GetComponent<Collider>();
                    if (lc && rc)
                        closestWeaponDistance = Mathf.Min(closestWeaponDistance, Vector3.Distance(
                            lc.ClosestPoint(rc.bounds.center), rc.ClosestPoint(lc.bounds.center)));
                }
            }
            sawDisarm |= resolver.SuccessfulDisarms > 0;
            foreach (var weapon in PhysicalMeleeWeapon.Active)
                if (weapon && weapon.State == PhysicalMeleeWeapon.HoldState.World &&
                    weapon.GetComponent<Rigidbody>().useGravity)
                {
                    if (!dropped) { dropped = weapon; dropHeight = weapon.transform.position.y; }
                    if (weapon == dropped)
                    {
                        fallDistance = Mathf.Max(fallDistance, dropHeight - weapon.transform.position.y);
                        sawFallingWeapon |= fallDistance > .1f;
                    }
                }
            if (telemetry && telemetry.left && telemetry.right && sawDisarm)
            {
                var leftEquipment = telemetry.left.GetComponent<NpcGeneratedWeapon>();
                var rightEquipment = telemetry.right.GetComponent<NpcGeneratedWeapon>();
                sawUnarmed |= leftEquipment && !leftEquipment.Instance && !telemetry.left.staff ||
                    rightEquipment && !rightEquipment.Instance && !telemetry.right.staff;
            }
            if (Time.realtimeSinceStartup - started < 26f) return;
            bool pass = sawStrike && sawDisarm && sawFallingWeapon && sawUnarmed;
            var leftWeapon = telemetry && telemetry.left ? telemetry.left.GetComponent<NpcGeneratedWeapon>()?.Instance : null;
            var rightWeapon = telemetry && telemetry.right ? telemetry.right.GetComponent<NpcGeneratedWeapon>()?.Instance : null;
            var leftCollider = leftWeapon ? leftWeapon.GetComponent<Collider>() : null;
            var rightCollider = rightWeapon ? rightWeapon.GetComponent<Collider>() : null;
            Debug.Log($"DISARM_ARENA_{(pass ? "PASS" : "FAIL")} strike={sawStrike} closest={closestDistance:F2} staffClosest={closestWeaponDistance:F2} qualifying={resolver.QualifyingClashes} rolls={resolver.ProbabilityRolls} disarms={resolver.SuccessfulDisarms} unarmed={sawUnarmed} falling={sawFallingWeapon} fallDistance={fallDistance:F2} contacts={leftWeapon?.WeaponContactEntries}/{rightWeapon?.WeaponContactEntries} maxSpeed={leftWeapon?.MaximumWeaponContactSpeed:F2}/{rightWeapon?.MaximumWeaponContactSpeed:F2} maxImpulse={leftWeapon?.MaximumWeaponContactImpulse:F2}/{rightWeapon?.MaximumWeaponContactImpulse:F2} ignored={(leftCollider && rightCollider ? Physics.GetIgnoreCollision(leftCollider, rightCollider) : false)} layers={leftWeapon?.gameObject.layer}/{rightWeapon?.gameObject.layer} colliderAxes={leftWeapon?.GetComponent<CapsuleCollider>()?.direction}/{rightWeapon?.GetComponent<CapsuleCollider>()?.direction} staffPositions={leftWeapon?.transform.position}/{rightWeapon?.transform.position}");
            EditorPrefs.DeleteKey(Pending);
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
