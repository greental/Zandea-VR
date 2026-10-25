using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.MeleeCombat.Editor
{
    public static class StaffAttackPathValidator
    {
        const string ScenePath = "Assets/MeleeCombat/MeleePOC.unity";
        const string StampPath = "Library/MeleeAttackPathsValidatedV16.done";

        [InitializeOnLoadMethod]
        static void QueueValidation() => EditorApplication.update += ValidateOnReload;
        static void ValidateOnReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= ValidateOnReload;
            if (File.Exists(StampPath) || !File.Exists(ScenePath)) return;
            if (Validate()) File.WriteAllText(StampPath, "Read-only attack path screening completed.\n");
        }
        [MenuItem("Zandea/Melee Combat/Validate eight staff paths")]
        public static void ValidateFromMenu() => Validate();

        static bool Validate()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                StarvingBandit bandit = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out bandit)) break;
                if (!bandit || !bandit.staff || bandit.attacks == null || bandit.attacks.Length != 8) return false;
                var motion = bandit.GetComponent<BanditRigMotion>();
                if (!motion || !motion.visualRoot) return false;
                Transform Bone(string name)
                {
                    foreach (var t in motion.visualRoot.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
                    return null;
                }
                var lu = Bone("CC_Base_L_Upperarm_050"); var ll = Bone("CC_Base_L_Forearm_051"); var lh = Bone("CC_Base_L_Hand_055");
                var ru = Bone("CC_Base_R_Upperarm_074"); var rl = Bone("CC_Base_R_Forearm_077"); var rh = Bone("CC_Base_R_Hand_081");
                var chest = Bone("CC_Base_Spine02_035"); var head = Bone("CC_Base_Head_038");
                if (!lu || !ll || !lh || !ru || !rl || !rh || !chest || !head) return false;
                var shaft = bandit.staff.transform;
                Vector3 guardLocal = bandit.transform.InverseTransformPoint(shaft.position);
                Quaternion guardRotation = Quaternion.Inverse(bandit.transform.rotation) * shaft.rotation;
                Vector3 leftLocal = shaft.InverseTransformPoint(lh.position), rightLocal = shaft.InverseTransformPoint(rh.position);
                Quaternion leftRotation = Quaternion.Inverse(shaft.rotation) * lh.rotation;
                Quaternion rightRotation = Quaternion.Inverse(shaft.rotation) * rh.rotation;
                Debug.Log($"Authored staff guard: local position {guardLocal}, local rotation {guardRotation.eulerAngles}; left hand on staff {leftLocal} / {leftRotation.eulerAngles}, right hand on staff {rightLocal} / {rightRotation.eulerAngles}.", bandit);
                float leftReach = Vector3.Distance(lu.position, ll.position) + Vector3.Distance(ll.position, lh.position) - bandit.reachMargin;
                float rightReach = Vector3.Distance(ru.position, rl.position) + Vector3.Distance(rl.position, rh.position) - bandit.reachMargin;
                float guardExcess = Mathf.Max(Vector3.Distance(lu.position, lh.position) - leftReach,
                    Vector3.Distance(ru.position, rh.position) - rightReach);
                Debug.Log($"Authored guard raw reach excess {Mathf.Max(0, guardExcess) * 100f:F1} cm (includes reach margin).", bandit);
                for (int direction = 0; direction < 8; direction++)
                {
                    float worstReach = 0, nearestChest = float.MaxValue, nearestHead = float.MaxValue, tipTravel = 0, worstTipError = 0;
                    float maximumJump = 0, impactMiss = 0;
                    bandit.GetTipLocals(out var positiveTip, out var negativeTip);
                    Vector3 tipLocal = bandit.attacks[direction].useNegativeTip ? negativeTip : positiveTip;
                    Vector3 previousTip = shaft.TransformPoint(tipLocal);
                    Quaternion windupRotation = Quaternion.identity;
                    Vector3 windupTip = previousTip;
                    float maxArc = 0;
                    foreach (StarvingBandit.AttackPhase phase in System.Enum.GetValues(typeof(StarvingBandit.AttackPhase)))
                    for (int sample = 0; sample <= 20; sample++)
                    {
                        StarvingBandit.EvaluatePose(bandit.attacks[direction], phase, sample / 20f,
                            guardLocal, guardRotation, positiveTip, negativeTip, out var offset, out var rotation, out var requestedTip);
                        Vector3 pos = bandit.transform.TransformPoint(guardLocal + offset);
                        Quaternion rot = bandit.transform.rotation * guardRotation * rotation;
                        worstReach = Mathf.Max(worstReach, Vector3.Distance(lu.position, pos + rot * leftLocal) - leftReach,
                            Vector3.Distance(ru.position, pos + rot * rightLocal) - rightReach);
                        Vector3 a = pos + rot * negativeTip, b = pos + rot * positiveTip;
                        Vector3 strikingTip = pos + rot * tipLocal;
                        worstTipError = Mathf.Max(worstTipError,
                            Vector3.Distance(bandit.transform.TransformPoint(requestedTip), strikingTip));
                        tipTravel += Vector3.Distance(previousTip, strikingTip);
                        maximumJump = Mathf.Max(maximumJump, Vector3.Distance(previousTip, strikingTip));
                        previousTip = strikingTip;
                        if (phase == StarvingBandit.AttackPhase.Strike && sample == 20)
                        {
                            float step = Mathf.Clamp(bandit.previewTargetLocal.z - bandit.attacks[direction].comfortableRange,
                                0, bandit.attacks[direction].maximumApproachStep);
                            impactMiss = Vector3.Distance(bandit.transform.InverseTransformPoint(strikingTip) + Vector3.forward * step,
                                bandit.previewTargetLocal + new Vector3(0, 0, -.18f));
                        }
                        if (phase == StarvingBandit.AttackPhase.Windup && sample == 20)
                        { windupRotation = rot; windupTip = strikingTip; }
                        if (phase == StarvingBandit.AttackPhase.Strike)
                        {
                            maxArc = Mathf.Max(maxArc, Quaternion.Angle(windupRotation, rot));
                            if (sample % 5 == 0)
                                Debug.Log($"Staff {StarvingBandit.ClockDirections[direction]} strike {sample * 5}%: desired tip {requestedTip}, rigid tip {bandit.transform.InverseTransformPoint(strikingTip)}, arc {Quaternion.Angle(windupRotation, rot):F1}°, tip displacement {Vector3.Distance(windupTip, strikingTip) * 100f:F1} cm.", bandit);
                        }
                        nearestChest = Mathf.Min(nearestChest, DistanceToSegment(a, b, chest.position));
                        nearestHead = Mathf.Min(nearestHead, DistanceToSegment(a, b, head.position));
                    }
                    Debug.Log($"Staff {StarvingBandit.ClockDirections[direction]} rigid endpoint error: {worstTipError * 1000f:F2} mm (all sampled phases).", bandit);
                    Debug.Log($"Staff {StarvingBandit.ClockDirections[direction]} full-body path: largest 5% sample step {maximumJump * 100f:F1} cm; preview impact miss {impactMiss * 100f:F1} cm; range {bandit.attacks[direction].comfortableRange:F2} to {bandit.attacks[direction].comfortableRange + bandit.attacks[direction].maximumApproachStep:F2} m.", bandit);
                    Debug.Log($"Staff path {StarvingBandit.ClockDirections[direction]}: raw reach excess {Mathf.Max(0, worstReach) * 100f:F1} cm, closest chest {nearestChest * 100f:F1} cm, head {nearestHead * 100f:F1} cm, cumulative tip travel {tipTravel * 100f:F1} cm, maximum strike arc {maxArc:F1}°. Runtime whole-staff reach correction applies.", bandit);
                }
                return true;
            }
            finally { if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true); }
        }
        static float DistanceToSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 segment = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
            return Vector3.Distance(point, a + segment * t);
        }
    }
}
