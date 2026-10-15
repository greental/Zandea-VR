using System.Collections.Generic;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace KenshiVR.MeleeCombat
{
    // Procedural motion for the CC_Base male skeleton. The existing Mixamo clips
    // belong to a different rig; this keeps the physical staff as the hand target.
    [DefaultExecutionOrder(150)]
    public sealed class BanditRigMotion : MonoBehaviour
    {
        public Transform visualRoot;
        public PhysicalMeleeWeapon staff;
        public StarvingBandit brain;
        public NavMeshAgent agent;
        ScavengerRagdoll ragdoll;
        [Header("Locomotion")]
        [Min(0)] public float runCadence = 2.5f, chasseCadence = 1.4f;
        [Min(0)] public float runStepLength = .4f, chasseStepLength = .25f;
        [Min(0)] public float stepHeight = .12f;

        Transform hips, spineLow, spineHigh, head, leftClavicle, rightClavicle;
        Transform leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand;
        Transform leftThigh, leftCalf, leftFoot, rightThigh, rightCalf, rightFoot;
        Vector3 restHipPosition, leftFootLocal, rightFootLocal;
        Quaternion restHip, restSpineLow, restSpineHigh, restHead, restLeftClavicle, restRightClavicle;
        Quaternion restLeftUpper, restLeftLower, restLeftHand, restRightUpper, restRightLower, restRightHand;
        Quaternion restLeftThigh, restLeftCalf, restLeftFoot, restRightThigh, restRightCalf, restRightFoot;
        readonly List<Finger> fingers = new();
        struct Finger { public Transform bone; public Quaternion rest; }
        float runPhase, chassePhase, runWeight, chasseWeight;
        float bodyYaw, bodyPitch, bodyCrouch, bodyLunge, bodyShift;
        Vector3 leftGripLocal, rightGripLocal, leftPoleLocal, rightPoleLocal;
        Quaternion leftHandOnStaff, rightHandOnStaff;
        float leftReach, rightReach;
        float leftGuardReach, rightGuardReach;
        public float LeftGripError { get; private set; }
        public float RightGripError { get; private set; }

        void Awake()
        {
            if (!brain) brain = GetComponent<StarvingBandit>();
            ragdoll = GetComponent<ScavengerRagdoll>();
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!visualRoot) return;
            var animator = visualRoot.GetComponentInChildren<Animator>();
            if (animator) animator.enabled = false;
            hips = Bone("CC_Base_Hip_02"); spineLow = Bone("CC_Base_Spine01_034");
            spineHigh = Bone("CC_Base_Spine02_035"); head = Bone("CC_Base_Head_038");
            leftUpper = Bone("CC_Base_L_Upperarm_050"); leftLower = Bone("CC_Base_L_Forearm_051"); leftHand = Bone("CC_Base_L_Hand_055");
            rightUpper = Bone("CC_Base_R_Upperarm_074"); rightLower = Bone("CC_Base_R_Forearm_077"); rightHand = Bone("CC_Base_R_Hand_081");
            foreach (var bone in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.Contains("L_Clavicle")) leftClavicle = bone;
                if (bone.name.Contains("R_Clavicle")) rightClavicle = bone;
            }
            leftThigh = Bone("CC_Base_L_Thigh_04"); leftCalf = Bone("CC_Base_L_Calf_05"); leftFoot = Bone("CC_Base_L_Foot_06");
            rightThigh = Bone("CC_Base_R_Thigh_00"); rightCalf = Bone("CC_Base_R_Calf_021"); rightFoot = Bone("CC_Base_R_Foot_022");
            if (!hips || !spineLow || !spineHigh || !head || !leftUpper || !leftLower || !leftHand || !rightUpper || !rightLower || !rightHand ||
                !leftThigh || !leftCalf || !leftFoot || !rightThigh || !rightCalf || !rightFoot)
            { enabled = false; Debug.LogError("Male rig bones missing for bandit motion", this); return; }
            restHipPosition = hips.localPosition; restHip = hips.localRotation;
            restSpineLow = spineLow.localRotation; restSpineHigh = spineHigh.localRotation; restHead = head.localRotation;
            if (leftClavicle) restLeftClavicle = leftClavicle.localRotation;
            if (rightClavicle) restRightClavicle = rightClavicle.localRotation;
            restLeftUpper = leftUpper.localRotation; restLeftLower = leftLower.localRotation; restLeftHand = leftHand.localRotation;
            restRightUpper = rightUpper.localRotation; restRightLower = rightLower.localRotation; restRightHand = rightHand.localRotation;
            restLeftThigh = leftThigh.localRotation; restLeftCalf = leftCalf.localRotation; restLeftFoot = leftFoot.localRotation;
            restRightThigh = rightThigh.localRotation; restRightCalf = rightCalf.localRotation; restRightFoot = rightFoot.localRotation;
            leftFootLocal = transform.InverseTransformPoint(leftFoot.position);
            rightFootLocal = transform.InverseTransformPoint(rightFoot.position);
            CacheFingers("CC_Base_L_"); CacheFingers("CC_Base_R_");
            if (staff)
            {
                var shaft = staff.transform;
                leftGripLocal = shaft.InverseTransformPoint(leftHand.position);
                rightGripLocal = shaft.InverseTransformPoint(rightHand.position);
                leftHandOnStaff = Quaternion.Inverse(shaft.rotation) * leftHand.rotation;
                rightHandOnStaff = Quaternion.Inverse(shaft.rotation) * rightHand.rotation;
                leftPoleLocal = transform.InverseTransformDirection(leftLower.position - leftUpper.position).normalized;
                rightPoleLocal = transform.InverseTransformDirection(rightLower.position - rightUpper.position).normalized;
                leftReach = Vector3.Distance(leftUpper.position, leftLower.position) + Vector3.Distance(leftLower.position, leftHand.position);
                rightReach = Vector3.Distance(rightUpper.position, rightLower.position) + Vector3.Distance(rightLower.position, rightHand.position);
                leftGuardReach = Vector3.Distance(leftUpper.position, leftHand.position);
                rightGuardReach = Vector3.Distance(rightUpper.position, rightHand.position);
            }
        }
        Transform Bone(string name)
        {
            foreach (var t in visualRoot.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        void CacheFingers(string prefix)
        {
            string[] digits = { "Index", "Mid", "Ring", "Pinky", "Thumb" };
            foreach (var digit in digits)
                for (int segment = 1; segment <= 3; segment++)
                    foreach (var t in visualRoot.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith(prefix + digit + segment + "_") && !t.name.EndsWith("scaleCompensation"))
                        {
                            fingers.Add(new Finger { bone = t, rest = t.localRotation });
                            break;
                        }
        }
        void LateUpdate()
        {
            if (!hips || ragdoll && ragdoll.Active || (brain && brain.state == StarvingBandit.State.Dead)) return;
            if (brain && brain.guardAuthoringMode && !brain.previewEnabled) return;
            float speed = agent && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
            runWeight = Mathf.MoveTowards(runWeight, brain && brain.IsRunning ? Mathf.Clamp01(speed / 2f) : 0f, Time.deltaTime * 5f);
            chasseWeight = Mathf.MoveTowards(chasseWeight, brain && brain.IsChasse ? Mathf.Clamp01(speed / .6f) : 0f, Time.deltaTime * 5f);
            runPhase += Time.deltaTime * runCadence * Mathf.Max(.6f, speed / 2f) * Mathf.PI * 2f;
            chassePhase = Mathf.Repeat(chassePhase + Time.deltaTime * chasseCadence * Mathf.Max(.6f, speed), 1f);
            RestorePose();
            BodyPose();
            Feet();
            if (brain) brain.ResolveDesiredStaffPoseAfterBody();
            HoldStaff();
            // Finger rotations are the user's authored grip; do not overwrite them.
        }
        void RestorePose()
        {
            hips.localPosition = restHipPosition; hips.localRotation = restHip;
            spineLow.localRotation = restSpineLow; spineHigh.localRotation = restSpineHigh; head.localRotation = restHead;
            if (leftClavicle) leftClavicle.localRotation = restLeftClavicle;
            if (rightClavicle) rightClavicle.localRotation = restRightClavicle;
            leftUpper.localRotation = restLeftUpper; leftLower.localRotation = restLeftLower; leftHand.localRotation = restLeftHand;
            rightUpper.localRotation = restRightUpper; rightLower.localRotation = restRightLower; rightHand.localRotation = restRightHand;
            leftThigh.localRotation = restLeftThigh; leftCalf.localRotation = restLeftCalf; leftFoot.localRotation = restLeftFoot;
            rightThigh.localRotation = restRightThigh; rightCalf.localRotation = restRightCalf; rightFoot.localRotation = restRightFoot;
        }
        public void RestoreAuthoredGuard()
        {
            if (!hips) return;
            RestorePose();
            foreach (var finger in fingers) if (finger.bone) finger.bone.localRotation = finger.rest;
            LeftGripError = RightGripError = 0;
        }
        void BodyPose()
        {
            float yaw = 0, pitch = 0, crouch = .035f * chasseWeight, lunge = 0, shift = 0;
            var attackPose = brain ? brain.CurrentCombatPose : null;
            if (attackPose != null) { yaw = attackPose.torsoYaw; pitch = attackPose.torsoPitch; shift = attackPose.weightShift; lunge = attackPose.forwardLunge; }
            float smoothing = brain && brain.previewEnabled ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 12f);
            bodyYaw = Mathf.Lerp(bodyYaw, yaw, smoothing);
            bodyPitch = Mathf.Lerp(bodyPitch, pitch, smoothing);
            bodyCrouch = Mathf.Lerp(bodyCrouch, crouch, smoothing);
            bodyLunge = Mathf.Lerp(bodyLunge, lunge, smoothing);
            bodyShift = Mathf.Lerp(bodyShift, shift, smoothing);
            hips.position += transform.forward * bodyLunge + transform.right * bodyShift - transform.up * bodyCrouch;
            hips.position += transform.up * (Mathf.Abs(Mathf.Sin(runPhase)) * .035f * runWeight);
            hips.localRotation = restHip * Quaternion.Euler(bodyPitch * .35f, attackPose != null ? attackPose.hipYaw : bodyYaw * .5f, 0);
            spineLow.localRotation = restSpineLow * Quaternion.Euler(bodyPitch * .45f + runWeight * 7f, bodyYaw * .4f, 0);
            spineHigh.localRotation = restSpineHigh * Quaternion.Euler(bodyPitch * .4f, bodyYaw * .35f, Mathf.Sin(runPhase) * 3f * runWeight);
            head.localRotation = restHead * Quaternion.Euler(-bodyPitch * .3f, -bodyYaw * .65f, 0);
            if (attackPose != null)
            {
                float shoulderYaw = Mathf.Clamp(attackPose.shoulderYaw, -22f, 22f);
                float lift = Mathf.Clamp(attackPose.shoulderLift, -15f, 18f);
                if (leftClavicle) leftClavicle.localRotation = restLeftClavicle * Quaternion.Euler(0, shoulderYaw * .5f, -lift);
                if (rightClavicle) rightClavicle.localRotation = restRightClavicle * Quaternion.Euler(0, shoulderYaw * .5f, lift);
            }
        }
        void Feet()
        {
            float lead = Mathf.SmoothStep(0, 1, Mathf.Clamp01(chassePhase * 2f));
            float trail = Mathf.SmoothStep(0, 1, Mathf.Clamp01((chassePhase - .5f) * 2f));
            float rightSlide = (lead - chassePhase) * chasseStepLength * chasseWeight;
            float leftSlide = (trail - chassePhase) * chasseStepLength * chasseWeight;
            float rightLift = Mathf.Sin(Mathf.Clamp01(chassePhase * 2f) * Mathf.PI) * stepHeight * .55f * chasseWeight;
            float leftLift = Mathf.Sin(Mathf.Clamp01((chassePhase - .5f) * 2f) * Mathf.PI) * stepHeight * .55f * chasseWeight;
            Vector3 right = rightFootLocal + new Vector3(0,
                Mathf.Max(0, Mathf.Cos(runPhase)) * stepHeight * runWeight + rightLift,
                Mathf.Sin(runPhase) * runStepLength * runWeight + rightSlide + .04f * chasseWeight + bodyLunge * .4f);
            Vector3 left = leftFootLocal + new Vector3(0,
                Mathf.Max(0, -Mathf.Cos(runPhase)) * stepHeight * runWeight + leftLift,
                -Mathf.Sin(runPhase) * runStepLength * runWeight + leftSlide - .04f * chasseWeight - bodyLunge * .15f);
            ScavengerPose.Solve(rightThigh, rightCalf, rightFoot, transform.TransformPoint(right), transform.forward);
            ScavengerPose.Solve(leftThigh, leftCalf, leftFoot, transform.TransformPoint(left), transform.forward);
        }
        void HoldStaff()
        {
            if (!staff) return;
            var shaft = staff.transform;
            Vector3 leftWrist = shaft.TransformPoint(leftGripLocal);
            Vector3 rightWrist = shaft.TransformPoint(rightGripLocal);
            var pose = brain ? brain.CurrentCombatPose : null;
            Vector3 leftPole = pose != null && pose.leftElbowHint.sqrMagnitude > .01f ?
                Vector3.Slerp(leftPoleLocal, pose.leftElbowHint.normalized, .35f).normalized : leftPoleLocal;
            Vector3 rightPole = pose != null && pose.rightElbowHint.sqrMagnitude > .01f ?
                Vector3.Slerp(rightPoleLocal, pose.rightElbowHint.normalized, .35f).normalized : rightPoleLocal;
            ScavengerPose.Solve(leftUpper, leftLower, leftHand, leftWrist, transform.TransformDirection(leftPole));
            ScavengerPose.Solve(rightUpper, rightLower, rightHand, rightWrist, transform.TransformDirection(rightPole));
            leftHand.rotation = shaft.rotation * leftHandOnStaff;
            rightHand.rotation = shaft.rotation * rightHandOnStaff;
            LeftGripError = Vector3.Distance(leftHand.position, leftWrist);
            RightGripError = Vector3.Distance(rightHand.position, rightWrist);
        }
        // Move the staff as a rigid object so both authored grip offsets remain fixed.
        public void ConstrainStaffPose(ref Vector3 position, ref Quaternion rotation,
            Vector3 guardPosition, Quaternion guardRotation, float margin, out float error, out string reason)
        {
            error = 0;
            reason = "None";
            if (!leftUpper || !rightUpper) return;
            float leftLimit = Mathf.Max(leftReach - margin, leftGuardReach + .005f);
            float rightLimit = Mathf.Max(rightReach - margin, rightGuardReach + .005f);
            Vector3 originalPosition = position;
            Quaternion originalRotation = rotation;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int iteration = 0; iteration < 7; iteration++)
                {
                    Vector3 correction = Vector3.zero;
                    int count = 0;
                    CorrectArm(leftUpper.position, position + rotation * leftGripLocal,
                        leftLimit, ref correction, ref count);
                    CorrectArm(rightUpper.position, position + rotation * rightGripLocal,
                        rightLimit, ref correction, ref count);
                    if (count == 0) break;
                    position += correction / count;
                }
                error = Mathf.Max(
                    Vector3.Distance(leftUpper.position, position + rotation * leftGripLocal) - leftLimit,
                    Vector3.Distance(rightUpper.position, position + rotation * rightGripLocal) - rightLimit);
                if (error <= .005f) break;
                rotation = Quaternion.Slerp(rotation, guardRotation, .15f);
                position = Vector3.Lerp(position, guardPosition, .08f);
            }
            error = Mathf.Max(0, error);
            if (Vector3.Distance(originalPosition, position) > .001f || Quaternion.Angle(originalRotation, rotation) > .1f)
                reason = "Arm reach";
            // Keep the shaft outside the chest and head during close windups.
            if (spineHigh && staff)
            {
                Vector3 positive, negative;
                if (brain) brain.GetTipLocals(out positive, out negative);
                else { positive = Vector3.forward * .95f; negative = -positive; }
                Vector3 a = position + rotation * negative;
                Vector3 b = position + rotation * positive;
                float chestDistance = Vector3.Distance(spineHigh.position, Closest(a, b, spineHigh.position));
                float headDistance = Vector3.Distance(head.position, Closest(a, b, head.position));
                float push = Mathf.Max(.16f - chestDistance, .18f - headDistance);
                if (push > 0)
                {
                    Vector3 candidate = position + transform.forward * Mathf.Min(push, .12f);
                    float candidateError = Mathf.Max(
                        Vector3.Distance(leftUpper.position, candidate + rotation * leftGripLocal) - leftLimit,
                        Vector3.Distance(rightUpper.position, candidate + rotation * rightGripLocal) - rightLimit);
                    if (candidateError <= .005f) { position = candidate; reason = "Chest/head clearance"; }
                    else reason = "Chest/head clearance limited by arm reach";
                }
            }
            error = Mathf.Max(0,
                Vector3.Distance(leftUpper.position, position + rotation * leftGripLocal) - leftLimit,
                Vector3.Distance(rightUpper.position, position + rotation * rightGripLocal) - rightLimit);
        }
        static void CorrectArm(Vector3 shoulder, Vector3 wrist, float reach, ref Vector3 correction, ref int count)
        {
            Vector3 delta = wrist - shoulder;
            if (delta.magnitude <= reach || delta.sqrMagnitude < .000001f) return;
            correction += shoulder + delta.normalized * reach - wrist;
            count++;
        }
        static Vector3 Closest(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 segment = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
            return a + segment * t;
        }
        public void DrawGripGizmos()
        {
            if (!staff || !leftHand || !rightHand) return;
            Vector3 leftTarget = staff.transform.TransformPoint(leftGripLocal);
            Vector3 rightTarget = staff.transform.TransformPoint(rightGripLocal);
            Gizmos.color = LeftGripError > .025f ? Color.red : Color.green;
            Gizmos.DrawSphere(leftTarget, .025f);
            Gizmos.DrawLine(leftTarget, leftHand.position);
            Gizmos.color = RightGripError > .025f ? Color.red : Color.green;
            Gizmos.DrawSphere(rightTarget, .025f);
            Gizmos.DrawLine(rightTarget, rightHand.position);
        }
    }
}
