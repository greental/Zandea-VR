using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace JunkerBody
{
    [DefaultExecutionOrder(10000)]
    public sealed class VRBodyController : MonoBehaviour, IHoverBikeHandPose
    {
        [Serializable]
        public sealed class Limb
        {
            public Transform upper, lower, tip, target, hint;
            public Vector3 wristPositionOffset;
            public Vector3 wristRotationOffset;
            public Vector3 hintOffset;
            public bool Valid => upper && lower && tip && target && hint;
        }
        [Header("Existing tracking (read only)")]
        public Transform xrOrigin, hmd, leftController, rightController;
        public Animator animator;
        public XRHandTrackingEvents leftOpticalTracking, rightOpticalTracking;
        public Transform leftOpticalWrist, rightOpticalWrist;
        public JunkerTrackingCalibration trackingCalibration;
        public Renderer[] opticalHandVisuals = Array.Empty<Renderer>();
        bool[] previousOpticalVisibility;
        public Vector3 opticalWristRotationOffset = new Vector3(90f, 0f, 0f);
        [Header("Skeleton and independent targets")]
        public Transform avatarRoot, hips, chest, head, headTarget;
        public Limb leftArm = new Limb(), rightArm = new Limb(), leftLeg = new Limb(), rightLeg = new Limb();
        [NonSerialized] public Transform leftGripTarget, rightGripTarget;
        [NonSerialized] public Transform leftMeleeGripTarget, rightMeleeGripTarget;
        [Min(.01f)] public float meleeHandReleaseSeconds = .18f;
        Vector3 leftReleasedGripPosition, rightReleasedGripPosition;
        Quaternion leftReleasedGripRotation = Quaternion.identity, rightReleasedGripRotation = Quaternion.identity;
        float leftReleaseBlend, rightReleaseBlend;
        public void SetMeleeGrip(bool left, Transform grip)
        {
            if (left) { leftMeleeGripTarget = grip; leftReleaseBlend = 0; }
            else { rightMeleeGripTarget = grip; rightReleaseBlend = 0; }
        }
        public void SetMeleeGrip(Transform interactor, Transform grip)
        {
            if (leftController && (interactor == leftController || interactor.IsChildOf(leftController))) leftMeleeGripTarget = grip;
            if (rightController && (interactor == rightController || interactor.IsChildOf(rightController))) rightMeleeGripTarget = grip;
        }
        public void ClearMeleeGrip(Transform grip)
        {
            if (leftMeleeGripTarget == grip)
            { leftReleasedGripPosition = grip.position; leftReleasedGripRotation = grip.rotation; leftReleaseBlend = 1; leftMeleeGripTarget = null; }
            if (rightMeleeGripTarget == grip)
            { rightReleasedGripPosition = grip.position; rightReleasedGripRotation = grip.rotation; rightReleaseBlend = 1; rightMeleeGripTarget = null; }
        }
        public void SetHoverBikeGrip(Transform interactor, Transform grip)
        {
            if (leftController && (interactor == leftController || interactor.IsChildOf(leftController))) leftGripTarget = grip;
            if (rightController && (interactor == rightController || interactor.IsChildOf(rightController))) rightGripTarget = grip;
        }
        public void ClearHoverBikeGrip(Transform grip)
        {
            if (leftGripTarget == grip) leftGripTarget = null;
            if (rightGripTarget == grip) rightGripTarget = null;
        }
        [Header("Head and torso")]
        public Vector3 headPositionOffset = new Vector3(0f, -0.1f, -0.08f);
        public Vector3 headRotationOffset;
        public Vector3 bodyHorizontalOffset = new Vector3(0f, 0f, -0.04f);
        [Min(0f)] public float bodyRotationSmoothing = 7f;
        [Range(0f, 90f)] public float rotationDeadZone = 35f;
        [Header("Player fit (independent of tracking floor)")]
        [Min(0.1f), Tooltip("Overall model size. Apply with Zandea/JunkerBody/Apply avatar size and eye offset.")]
        public float meshScale = 1.4f;
        [Min(0.1f), Tooltip("Physical height to the crown; informational, distinct from HMD eye height.")]
        public float playerHeight = 1.78f;
        [Range(0.8f, 1.35f), Tooltip("Arm segment length adjustment, without enlarging the hands or moving controllers.")]
        public float armLengthMultiplier = 1.1f;
        [Header("Physical crouch")]
        [Tooltip("Standing HMD eye height above the tracking floor, not total body height. Calibrate while standing.")]
        [Min(0.1f)] public float standingHeight = 1.66f;
        [Min(0f)] public float crouchThreshold = 0.08f;
        [Min(0.1f)] public float crouchRange = 0.75f;
        [Min(0f)] public float crouchSmoothing = 12f;
        [Range(0f, 35f)] public float crouchLean = 18f;
        public float CrouchAmount { get; private set; }
        JunkerSprint locomotion;
        Transform[] poseBones;
        Vector3[] positions;
        Quaternion[] rotations;
        bool poseSaved;
        float yaw;
        Quaternion lastOriginRotation;
        Vector3 restHeadPosition;

        void OnEnable()
        {
            previousOpticalVisibility = new bool[opticalHandVisuals.Length];
            for (int i = 0; i < opticalHandVisuals.Length; i++)
                if (opticalHandVisuals[i]) { previousOpticalVisibility[i] = opticalHandVisuals[i].forceRenderingOff; opticalHandVisuals[i].forceRenderingOff = true; }
        }
        void Awake()
        {
            if (!trackingCalibration) trackingCalibration = GetComponent<JunkerTrackingCalibration>();
            locomotion = GetComponent<JunkerSprint>();
            if (!xrOrigin || !hmd || !leftController || !rightController || !animator || !avatarRoot || !hips || !chest || !head || !headTarget ||
                !leftArm.Valid || !rightArm.Valid || !leftLeg.Valid || !rightLeg.Valid)
            { Debug.LogError("Junker body references are incomplete. Run Zandea/JunkerBody/Configure scene.", this); enabled = false; return; }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            restHeadPosition = avatarRoot.InverseTransformPoint(head.position);
            poseBones = hips.GetComponentsInChildren<Transform>();
            positions = new Vector3[poseBones.Length]; rotations = new Quaternion[poseBones.Length];
            yaw = GetYaw(hmd.forward);
            lastOriginRotation = xrOrigin.rotation;
        }
        // Restore the previous unmodified animation pose before Animator evaluation. This also
        // prevents accumulated offsets if an animation does not key every bone on every frame.
        void Update() { RestorePose(); }
        void RestorePose()
        {
            if (!poseSaved) return;
            for (int i = 0; i < poseBones.Length; i++) { poseBones[i].localPosition = positions[i]; poseBones[i].localRotation = rotations[i]; }
            poseSaved = false;
        }
        void OnDisable()
        {
            RestorePose();
            if (previousOpticalVisibility == null) return;
            for (int i = 0; i < opticalHandVisuals.Length; i++)
                if (opticalHandVisuals[i]) opticalHandVisuals[i].forceRenderingOff = previousOpticalVisibility[i];
        }
        float GetYaw(Vector3 forward)
        {
            forward = xrOrigin.InverseTransformDirection(forward); forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : yaw;
        }
        void LateUpdate()
        {
            for (int i = 0; i < poseBones.Length; i++) { positions[i] = poseBones[i].localPosition; rotations[i] = poseBones[i].localRotation; }
            poseSaved = true;
            float desired = GetYaw(hmd.forward);
            float delta = Mathf.DeltaAngle(yaw, desired);
            // Origin turns already carry the body around; yaw is kept in origin space.
            if (Quaternion.Angle(lastOriginRotation, xrOrigin.rotation) > 60f) yaw = desired;
            else if (Mathf.Abs(delta) > rotationDeadZone)
                yaw = Mathf.LerpAngle(yaw, desired - Mathf.Sign(delta) * rotationDeadZone, 1f - Mathf.Exp(-bodyRotationSmoothing * Time.deltaTime));
            lastOriginRotation = xrOrigin.rotation;
            avatarRoot.rotation = xrOrigin.rotation * Quaternion.Euler(0f, yaw, 0f);
            Vector3 localHead = xrOrigin.InverseTransformPoint(hmd.position);
            // Floor calibration moves the common tracking parent. Compensate the
            // crouch reference by the same amount so a corrected standing pose is
            // not mistaken for a physical crouch.
            float correctedStandingHeight = standingHeight + (trackingCalibration ? trackingCalibration.floorOffset + trackingCalibration.LocomotionCrouchOffset : 0f);
            float crouch = Mathf.Clamp01((correctedStandingHeight - localHead.y - crouchThreshold) / crouchRange);
            if (locomotion) crouch = Mathf.Max(crouch, locomotion.CrouchBlend);
            CrouchAmount = Mathf.Lerp(CrouchAmount, crouch, 1f - Mathf.Exp(-crouchSmoothing * Time.deltaTime));
            Vector3 floor = xrOrigin.TransformPoint(new Vector3(localHead.x, 0f, localHead.z));
            avatarRoot.position = floor + avatarRoot.rotation * bodyHorizontalOffset;
            // Remove the model's rest horizontal head displacement, without touching tracking.
            Vector3 restOffset = avatarRoot.TransformVector(restHeadPosition);
            avatarRoot.position -= Vector3.ProjectOnPlane(restOffset, xrOrigin.up);
            SaveFoot(leftLeg); SaveFoot(rightLeg);
            chest.rotation = Quaternion.AngleAxis(crouchLean * CrouchAmount, avatarRoot.right) * chest.rotation;
            headTarget.SetPositionAndRotation(hmd.TransformPoint(headPositionOffset), hmd.rotation * Quaternion.Euler(headRotationOffset));
            hips.position += headTarget.position - head.position;
            head.rotation = headTarget.rotation;
            SolveLeg(leftLeg); SolveLeg(rightLeg);
            leftReleaseBlend = Mathf.MoveTowards(leftReleaseBlend, 0, Time.deltaTime / meleeHandReleaseSeconds);
            rightReleaseBlend = Mathf.MoveTowards(rightReleaseBlend, 0, Time.deltaTime / meleeHandReleaseSeconds);
            TrackArm(leftArm, leftController, leftOpticalTracking, leftOpticalWrist,
                leftMeleeGripTarget ? leftMeleeGripTarget : leftGripTarget, leftMeleeGripTarget,
                leftReleasedGripPosition, leftReleasedGripRotation, leftReleaseBlend);
            TrackArm(rightArm, rightController, rightOpticalTracking, rightOpticalWrist,
                rightMeleeGripTarget ? rightMeleeGripTarget : rightGripTarget, rightMeleeGripTarget,
                rightReleasedGripPosition, rightReleasedGripRotation, rightReleaseBlend);
        }
        void SaveFoot(Limb leg) { leg.target.SetPositionAndRotation(leg.tip.position, leg.tip.rotation); }
        void SolveLeg(Limb leg)
        {
            leg.hint.position = leg.upper.position + avatarRoot.rotation * leg.hintOffset;
            SolveTwoBone(leg.upper, leg.lower, leg.tip, leg.target.position, leg.hint.position);
            leg.tip.rotation = leg.target.rotation;
        }
        void TrackArm(Limb arm, Transform source, XRHandTrackingEvents optical, Transform wrist,
            Transform gripTarget, bool meleeGrip, Vector3 releasePosition, Quaternion releaseRotation, float releaseBlend)
        {
            // Roll around the controller forward axis before aligning the model wrist.
            Quaternion palmRoll = Quaternion.AngleAxis(arm == rightArm ? -90f : 90f, Vector3.forward);
            // Applied to this frame's unmodified animation pose. RestorePose removes the
            // adjustment before the next Animator evaluation, so lengths never accumulate.
            arm.lower.localPosition *= armLengthMultiplier;
            arm.tip.localPosition *= armLengthMultiplier;
            bool useOptical = optical && optical.isActiveAndEnabled && optical.handIsTracked && wrist;
            if (gripTarget) arm.target.SetPositionAndRotation(gripTarget.position,
                (meleeGrip ? gripTarget.rotation : source.rotation) * palmRoll * Quaternion.Euler(arm.wristRotationOffset));
            else if (useOptical) arm.target.SetPositionAndRotation(wrist.position, wrist.rotation * Quaternion.Euler(opticalWristRotationOffset));
            else arm.target.SetPositionAndRotation(source.TransformPoint(arm.wristPositionOffset), source.rotation * palmRoll * Quaternion.Euler(arm.wristRotationOffset));
            if (!meleeGrip && releaseBlend > 0)
                arm.target.SetPositionAndRotation(Vector3.Lerp(arm.target.position, releasePosition, releaseBlend),
                    Quaternion.Slerp(arm.target.rotation, releaseRotation * palmRoll * Quaternion.Euler(arm.wristRotationOffset), releaseBlend));
            arm.hint.position = chest.position + avatarRoot.rotation * arm.hintOffset;
            SolveTwoBone(arm.upper, arm.lower, arm.tip, arm.target.position, arm.hint.position);
            arm.tip.rotation = arm.target.rotation;
        }
        // Analytic two-bone solve. Reach is clamped to physical limb lengths and the hint
        // determines the bend plane, including near-chest and overhead targets.
        public static void SolveTwoBone(Transform upper, Transform lower, Transform tip, Vector3 target, Vector3 hint)
        {
            Vector3 start = upper.position;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, tip.position);
            if (a < 0.0001f || b < 0.0001f) return;
            Vector3 direction = target - start;
            if (direction.sqrMagnitude < 0.000001f) direction = tip.position - start;
            if (direction.sqrMagnitude < 0.000001f) direction = upper.forward;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);
            direction.Normalize();
            Vector3 bend = Vector3.ProjectOnPlane(hint - start, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(lower.position - start, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right);
            bend.Normalize();
            float along = (a * a + distance * distance - b * b) / (2f * distance);
            Vector3 elbow = start + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - start, elbow - start) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(tip.position - lower.position, start + direction * distance - lower.position) * lower.rotation;
        }
        [ContextMenu("Calibrate standing height (stand upright)")]
        public void CalibrateStandingHeight() { if (hmd && xrOrigin) standingHeight = Mathf.Max(0.1f, xrOrigin.InverseTransformPoint(hmd.position).y); }
    }
}
