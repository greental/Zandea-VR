using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using ScavengerNPC;

namespace KenshiVR.MeleeCombat
{
    [ExecuteAlways, DefaultExecutionOrder(250), RequireComponent(typeof(Animator))]
    public sealed class MocapStaffPreview : MonoBehaviour
    {
        public enum Attachment { RawAnimation, PrimaryHand, BothHands }
        public AnimationClip attack;
        public AnimationClip[] animations = Array.Empty<AnimationClip>();
        [Tooltip("Use mirrored Animator states while scrubbing left-hand attack clips.")]
        public bool mirrorDirectionalPreview;
        public AnimationClip[] directionalPreviewClips = Array.Empty<AnimationClip>();
        [Min(0)] public int animationIndex;
        public PhysicalMeleeWeapon staff;
        [Tooltip("Optional second visual weapon, for the shield-and-sword preview.")]
        public Transform secondaryVisualWeapon;
        public Quaternion secondaryWeaponFromRightHand = Quaternion.identity;
        public Transform desiredStaff, positiveTip, negativeTip, target;
        public Attachment attachment = Attachment.BothHands;
        public bool primaryHandIsLeft;
        public bool secondaryHandGrips = true;
        [Range(0, 1)] public float normalizedTime;
        public bool playing = true;
        [Range(.1f, 2)] public float playbackSpeed = 1;
        [Tooltip("Disable only when an external system drives the actual staff. Hands still follow its actual pose.")]
        public bool driveVisualStaff = true;
        [NonSerialized] public Vector3 runtimeWeaponOffsetWorld;
        [NonSerialized] public float runtimeWeaponOffsetWeight;
        [NonSerialized] public Vector3 runtimeWeaponFaceLocal;
        [NonSerialized] public Vector3 runtimeWeaponFacingWorld;
        [NonSerialized] public float runtimeWeaponFaceWeight;
        [NonSerialized] public Vector3 runtimeWeaponAxisLocal;
        [NonSerialized] public Vector3 runtimeWeaponAxisWorld;
        [NonSerialized] public float runtimeWeaponAxisWeight;
        [Header("Wrist targets copied in staff-local space from CombinedStarvingBandit")]
        public Vector3 rightGripLocal, leftGripLocal;
        public Quaternion rightHandFromStaff = Quaternion.identity, leftHandFromStaff = Quaternion.identity;
        [Serializable] public struct FingerPose { public Transform bone; public Quaternion rotation; }
        public FingerPose[] fingers = Array.Empty<FingerPose>();
        public bool applySavedFingerPose = true;
        public bool showTrajectory = true, tracePositiveTip = true;
        public float LeftGripError { get; private set; }
        public float RightGripError { get; private set; }
        public float LeftCorrection { get; private set; }
        public float RightCorrection { get; private set; }
        public float ReachCorrection { get; private set; }
        Animator animator;
        Transform rightHand, leftHand, rightUpper, rightLower, leftUpper, leftLower;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        AnimatorControllerPlayable controllerPlayable;
        bool graphUsesController;
        int controllerDirection;
        AnimationClip graphClip;
        RuntimeAnimatorController previousController;
        readonly Vector3[] trail = new Vector3[256];
        int trailCount;
        float previousTime = -1;
        public AnimationClip CurrentClip => animations != null && animations.Length > 0
            ? animations[Mathf.Clamp(animationIndex, 0, animations.Length - 1)] : attack;
        void OnDisable() => ReleaseGraph();
        void OnDestroy() => ReleaseGraph();
        void ReleaseGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            if (animator && previousController) animator.runtimeAnimatorController = previousController;
            graphClip = null;
            graphUsesController = false;
        }
        bool Prepare()
        {
            var clip = CurrentClip;
            if (!clip || !staff) return false;
            int mirroredDirection = mirrorDirectionalPreview ? Array.IndexOf(directionalPreviewClips, clip) : -1;
            if (!animator) animator = GetComponent<Animator>();
            // BanditRigMotion.Awake disables the Animator even when that legacy motion
            // component is disabled on the experimental prefab. This preview owns it.
            if (Application.isPlaying && animator && !animator.enabled) animator.enabled = true;
            if (!animator.avatar || !animator.avatar.isHuman || !animator.enabled) return false;
            if (graph.IsValid() && graphClip == clip) return true;
            ReleaseGraph();
            previousController = animator.runtimeAnimatorController;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            graph = PlayableGraph.Create("Isolated staff animation preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            graphUsesController = mirroredDirection >= 0 && previousController;
            controllerDirection = mirroredDirection;
            if (graphUsesController)
            {
                controllerPlayable = AnimatorControllerPlayable.Create(graph, previousController);
                AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(controllerPlayable);
            }
            else
            {
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(playable);
            }
            graphClip = clip;
            graph.Play();
            return true;
        }
        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            var clip = CurrentClip;
            if (playing && clip && clip.length > 0) normalizedTime = Mathf.Repeat(normalizedTime + Time.deltaTime * playbackSpeed / clip.length, 1f);
            Sample();
        }
        public void Sample()
        {
            if (!Prepare()) return;
            // Manual graph is the only animation clock. IK and attachment execute once after it.
            if (graphUsesController)
                controllerPlayable.Play("AttackDirection" + controllerDirection, 0, Mathf.Clamp01(normalizedTime));
            else playable.SetTime(Mathf.Clamp01(normalizedTime) * CurrentClip.length);
            graph.Evaluate(0);
            ApplyGripFromCurrentPose();
        }
        // The gameplay Animator owns the body pose. Reuse the same weapon/hand fit
        // after it has evaluated, without starting the preview PlayableGraph.
        public void ApplyGripFromCurrentPose()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman || !staff) return;
            if (!rightHand)
            {
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            }
            if (!rightHand || !leftHand || !rightUpper || !leftUpper) return;
            foreach (var renderer in staff.GetComponentsInChildren<Renderer>(true)) renderer.enabled = attachment != Attachment.RawAnimation;
            LeftGripError = RightGripError = LeftCorrection = RightCorrection = ReachCorrection = 0;
            if (attachment == Attachment.RawAnimation) return;
            var rightAnimated = rightHand.position;
            var leftAnimated = leftHand.position;
            var rightBend = rightLower.position - rightUpper.position;
            var leftBend = leftLower.position - leftUpper.position;
            Quaternion rotation;
            Vector3 position;
            if (attachment == Attachment.PrimaryHand)
            {
                var primary = primaryHandIsLeft ? leftHand : rightHand;
                rotation = primary.rotation * Quaternion.Inverse(primaryHandIsLeft ? leftHandFromStaff : rightHandFromStaff);
                position = primary.position - rotation * (primaryHandIsLeft ? leftGripLocal : rightGripLocal);
            }
            else
            {
                // Fit one rigid weapon to the two animated wrists while preserving palm geometry.
                var baseRotation = Quaternion.Slerp(rightHand.rotation * Quaternion.Inverse(rightHandFromStaff),
                    leftHand.rotation * Quaternion.Inverse(leftHandFromStaff), .5f);
                rotation = Quaternion.FromToRotation(baseRotation * (leftGripLocal - rightGripLocal), leftAnimated - rightAnimated) * baseRotation;
                position = (rightAnimated + leftAnimated) * .5f - rotation * ((rightGripLocal + leftGripLocal) * .5f);
            }
            // A defensive guard can shift the rigid weapon target. The same IK
            // solve then brings the hand to its actual grip on that target.
            position += runtimeWeaponOffsetWorld * Mathf.Clamp01(runtimeWeaponOffsetWeight);
            if (runtimeWeaponFaceWeight > 0 && runtimeWeaponFaceLocal.sqrMagnitude > .5f &&
                runtimeWeaponFacingWorld.sqrMagnitude > .5f)
            {
                var normal = rotation * runtimeWeaponFaceLocal.normalized;
                var facing = runtimeWeaponFacingWorld.normalized;
                if (Vector3.Dot(normal, facing) < 0) facing = -facing;
                var faceRotation = Quaternion.FromToRotation(normal, facing) * rotation;
                rotation = Quaternion.Slerp(rotation, faceRotation, Mathf.Clamp01(runtimeWeaponFaceWeight));
            }
            if (runtimeWeaponAxisWeight > 0 && runtimeWeaponAxisLocal.sqrMagnitude > .0001f &&
                runtimeWeaponAxisWorld.sqrMagnitude > .0001f)
            {
                var shaft = rotation * runtimeWeaponAxisLocal.normalized;
                var axis = runtimeWeaponAxisWorld.normalized;
                if (Vector3.Dot(shaft, axis) < 0) axis = -axis;
                var interceptRotation = Quaternion.FromToRotation(shaft, axis) * rotation;
                rotation = Quaternion.Slerp(rotation, interceptRotation, Mathf.Clamp01(runtimeWeaponAxisWeight));
            }
            var original = position;
            // Same reach projection used by BanditRigMotion. Never stretch arm bones.
            for (int i = 0; i < 12; i++)
            {
                if (secondaryHandGrips || primaryHandIsLeft)
                    Constrain(ref position, rotation * leftGripLocal, leftUpper, leftLower, leftHand);
                if (secondaryHandGrips || !primaryHandIsLeft)
                    Constrain(ref position, rotation * rightGripLocal, rightUpper, rightLower, rightHand);
            }
            ReachCorrection = Vector3.Distance(original, position);
            if (desiredStaff) desiredStaff.SetPositionAndRotation(position, rotation);
            if (driveVisualStaff) staff.transform.SetPositionAndRotation(position, rotation);
            var actual = staff.transform;
            var leftTarget = actual.TransformPoint(leftGripLocal);
            var rightTarget = actual.TransformPoint(rightGripLocal);
            LeftCorrection = secondaryHandGrips || primaryHandIsLeft ? Vector3.Distance(leftAnimated, leftTarget) : 0;
            RightCorrection = secondaryHandGrips || !primaryHandIsLeft ? Vector3.Distance(rightAnimated, rightTarget) : 0;
            // Reuse the existing solver with the animated elbow plane, preserving torso/shoulders.
            if (secondaryHandGrips || primaryHandIsLeft)
            {
                ScavengerPose.Solve(leftUpper, leftLower, leftHand, leftTarget, leftBend);
                leftHand.rotation = actual.rotation * leftHandFromStaff;
                LeftGripError = Vector3.Distance(leftHand.position, leftTarget);
            }
            if (secondaryHandGrips || !primaryHandIsLeft)
            {
                ScavengerPose.Solve(rightUpper, rightLower, rightHand, rightTarget, rightBend);
                rightHand.rotation = actual.rotation * rightHandFromStaff;
                RightGripError = Vector3.Distance(rightHand.position, rightTarget);
            }
            if (applySavedFingerPose)
                foreach (var finger in fingers)
                    if (finger.bone && (secondaryHandGrips || finger.bone.name.Contains(primaryHandIsLeft ? "_L_" : "_R_")))
                        finger.bone.localRotation = finger.rotation;
            if (secondaryVisualWeapon && rightHand)
                secondaryVisualWeapon.SetPositionAndRotation(rightHand.position, rightHand.rotation * secondaryWeaponFromRightHand);
            if (normalizedTime < previousTime) trailCount = 0;
            if (Mathf.Abs(normalizedTime - previousTime) > .0001f)
            {
                var tip = tracePositiveTip ? positiveTip : negativeTip;
                if (tip && trailCount < trail.Length) trail[trailCount++] = tip.position;
            }
            previousTime = normalizedTime;
        }
        static void Constrain(ref Vector3 position, Vector3 offset, Transform upper, Transform lower, Transform hand)
        {
            var reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) - .003f;
            var delta = position + offset - upper.position;
            if (delta.magnitude > reach) position += delta.normalized * (reach - delta.magnitude);
        }
        void OnDrawGizmos()
        {
            if (!showTrajectory) return;
            Gizmos.color = Color.yellow;
            for (int i = 1; i < trailCount; i++) Gizmos.DrawLine(trail[i - 1], trail[i]);
            if (!staff || attachment == Attachment.RawAnimation) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(staff.transform.TransformPoint(leftGripLocal), .025f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(staff.transform.TransformPoint(rightGripLocal), .025f);
        }
    }
}
