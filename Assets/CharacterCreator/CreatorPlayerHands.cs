using System;
using JunkerBody;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Linq;

namespace KenshiVR.CharacterCreator
{
    // Corrects the imported creator wrists and drives its actual finger bones from XR input.
    [DefaultExecutionOrder(10550)]
    public sealed class CreatorPlayerHands : MonoBehaviour
    {
        [Serializable] struct Finger { public Transform bone; public Quaternion rest; public Vector3 axis; public float degrees; }
        public VRBodyController body;
        [Tooltip("Known-good controller-to-wrist bases copied from the Junker player rig.")]
        public Vector3 leftWristOffset = new(14.827744f, 90f, 90.000046f);
        public Vector3 rightWristOffset = new(16.187147f, 269.99997f, 269.9999f);
        InputAction leftGrip, rightGrip, leftTrigger, rightTrigger;
        Finger[] left = Array.Empty<Finger>(), right = Array.Empty<Finger>();
        public int LeftJointCount => left.Length;
        public int RightJointCount => right.Length;
        XRDirectInteractor leftInteractor, rightInteractor;
        [Range(0f, 1f), Tooltip("Curl while holding an object; leaves room around its handle.")]
        public float heldCurl = .72f;
        [NonSerialized] public bool previewFist;
        [NonSerialized] public bool previewHeldGrip;
        bool corrected;

        public static void Ensure(CharacterAppearanceController appearance)
        {
            if (!Application.isPlaying || !appearance || !appearance.vrBody || !appearance.visualRoot ||
                !appearance.visualRoot.name.Contains("Man customizable")) return;
            var hands = appearance.GetComponent<CreatorPlayerHands>();
            if (!hands) hands = appearance.gameObject.AddComponent<CreatorPlayerHands>();
            hands.body = appearance.vrBody;
        }

        void Awake()
        {
            if (!body) body = GetComponent<VRBodyController>();
            CorrectWrists();
            if (!body || !body.animator) return;
            left = Build(true); right = Build(false);
            if (body.leftController) leftInteractor = body.leftController.GetComponentInChildren<XRDirectInteractor>(true);
            if (body.rightController) rightInteractor = body.rightController.GetComponentInChildren<XRDirectInteractor>(true);
            leftGrip = Action("Creator left grip", "<XRController>{LeftHand}/grip");
            rightGrip = Action("Creator right grip", "<XRController>{RightHand}/grip");
            leftTrigger = Action("Creator left trigger", "<XRController>{LeftHand}/trigger");
            rightTrigger = Action("Creator right trigger", "<XRController>{RightHand}/trigger");
        }
        static InputAction Action(string name, string binding) { var action = new InputAction(name, InputActionType.Value, binding); action.Enable(); return action; }
        void OnDestroy() { leftGrip?.Dispose(); rightGrip?.Dispose(); leftTrigger?.Dispose(); rightTrigger?.Dispose(); }
        void CorrectWrists()
        {
            if (corrected || !body) return;
            body.leftArm.wristRotationOffset = leftWristOffset;
            body.rightArm.wristRotationOffset = rightWristOffset;
            corrected = true;
        }
        Finger[] Build(bool isLeft)
        {
            var result = new System.Collections.Generic.List<Finger>();
            var names = new[] { "Thumb", "Index", "Mid", "Ring", "Pinky" };
            var chains = isLeft ? new[] {
                new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal },
                new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
                new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
                new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
                new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal }
            } : new[] {
                new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
                new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
                new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
                new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
                new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal }
            };
            var hand = FindRigBone(isLeft, "Hand", 0,
                isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var indexBase = FindRigBone(isLeft, "Index", 1, chains[1][0]);
            var middleBase = FindRigBone(isLeft, "Mid", 1, chains[2][0]);
            var littleBase = FindRigBone(isLeft, "Pinky", 1, chains[4][0]);
            if (!hand || !indexBase || !middleBase || !littleBase) return result.ToArray();
            var palmNormal = Vector3.Cross(indexBase.position - littleBase.position,
                middleBase.position - hand.position).normalized * (isLeft ? -1f : 1f);
            for (var digit = 0; digit < chains.Length; digit++)
            {
                var chain = chains[digit];
                for (int joint = 0; joint < chain.Length; joint++)
                {
                    var bone = FindRigBone(isLeft, names[digit], joint + 1, chain[joint]);
                    var next = joint < 2 ? FindRigBone(isLeft, names[digit], joint + 2, chain[joint + 1]) : null;
                    var previous = joint > 0 ? FindRigBone(isLeft, names[digit], joint, chain[joint - 1]) : null;
                    if (!bone || !hand) continue;
                    var direction = next ? next.position - bone.position :
                        previous ? bone.position - previous.position : bone.TransformDirection(Vector3.forward);
                    // The four fingers fold away from their nails toward the palm.
                    // The thumb instead crosses the palm toward the index knuckle.
                    var thumbTarget = middleBase.position - palmNormal * .075f;
                    var axis = digit == 0
                        ? Vector3.Cross(direction, thumbTarget - bone.position).normalized
                        : -Vector3.Cross(direction, palmNormal).normalized;
                    if (axis.sqrMagnitude < .001f) axis = bone.right;
                    result.Add(new Finger { bone = bone, rest = bone.localRotation,
                        axis = bone.InverseTransformDirection(axis),
                        degrees = digit == 0 ? new[] { 5f, 60f, 10f }[joint] : new[] { 78f, 105f, 70f }[joint] });
                }
            }
            return result.ToArray();
        }
        public Transform FindRigBone(bool isLeft, string finger, int joint, HumanBodyBones humanoidBone)
        {
            var prefix = "CC_Base_" + (isLeft ? "L_" : "R_") + finger +
                (joint > 0 ? joint.ToString() : "") + "_";
            var named = body.animator.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.StartsWith(prefix, StringComparison.Ordinal) &&
                    !t.name.EndsWith("scaleCompensation", StringComparison.Ordinal));
            return named ? named : body.animator.isHuman ? body.animator.GetBoneTransform(humanoidBone) : null;
        }
        void LateUpdate()
        {
            bool preview = previewFist || previewHeldGrip;
            Pose(left, preview ? 1f : leftGrip?.ReadValue<float>() ?? 0f,
                preview ? 1f : leftTrigger?.ReadValue<float>() ?? 0f,
                previewHeldGrip || !previewFist && leftInteractor && leftInteractor.hasSelection, heldCurl);
            Pose(right, preview ? 1f : rightGrip?.ReadValue<float>() ?? 0f,
                preview ? 1f : rightTrigger?.ReadValue<float>() ?? 0f,
                previewHeldGrip || !previewFist && rightInteractor && rightInteractor.hasSelection, heldCurl);
        }
        static void Pose(Finger[] fingers, float grip, float trigger, bool holding, float heldCurl)
        {
            float curl = holding ? heldCurl : Mathf.Max(grip, trigger);
            foreach (var finger in fingers) if (finger.bone)
                finger.bone.localRotation = finger.rest * Quaternion.AngleAxis(finger.degrees * curl, finger.axis);
        }
    }
}
