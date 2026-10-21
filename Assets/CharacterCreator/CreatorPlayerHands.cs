using System;
using JunkerBody;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KenshiVR.CharacterCreator
{
    // Corrects the imported creator wrists and drives its actual finger bones from XR input.
    [DefaultExecutionOrder(10550)]
    public sealed class CreatorPlayerHands : MonoBehaviour
    {
        [Serializable] struct Finger { public Transform bone; public Quaternion rest; public Vector3 axis; public float degrees; public bool index; }
        public VRBodyController body;
        public Vector3 leftWristCorrection = new(0f, 90f, 0f);
        public Vector3 rightWristCorrection = new(0f, -90f, 0f);
        InputAction leftGrip, rightGrip, leftTrigger, rightTrigger;
        Finger[] left = Array.Empty<Finger>(), right = Array.Empty<Finger>();
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
            if (!body || !body.animator || !body.animator.isHuman) return;
            left = Build(true); right = Build(false);
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
            body.leftArm.wristRotationOffset = (Quaternion.Euler(body.leftArm.wristRotationOffset) * Quaternion.Euler(leftWristCorrection)).eulerAngles;
            body.rightArm.wristRotationOffset = (Quaternion.Euler(body.rightArm.wristRotationOffset) * Quaternion.Euler(rightWristCorrection)).eulerAngles;
            corrected = true;
        }
        Finger[] Build(bool isLeft)
        {
            var result = new System.Collections.Generic.List<Finger>();
            var starts = isLeft ? new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal }
                                : new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal };
            foreach (var start in starts)
            {
                var bone = body.animator.GetBoneTransform(start);
                for (int joint = 0; joint < 3 && bone; joint++)
                {
                    bool thumb = start == (isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
                    result.Add(new Finger { bone = bone, rest = bone.localRotation, axis = Vector3.forward * (isLeft ? -1f : 1f), degrees = thumb ? new[] { 20f, 35f, 30f }[joint] : new[] { 68f, 80f, 55f }[joint], index = start == (isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) });
                    bone = bone.childCount > 0 ? bone.GetChild(0) : null;
                }
            }
            return result.ToArray();
        }
        void LateUpdate()
        {
            Pose(left, leftGrip?.ReadValue<float>() ?? 0f, leftTrigger?.ReadValue<float>() ?? 0f);
            Pose(right, rightGrip?.ReadValue<float>() ?? 0f, rightTrigger?.ReadValue<float>() ?? 0f);
        }
        static void Pose(Finger[] fingers, float grip, float trigger)
        {
            foreach (var finger in fingers) if (finger.bone)
                finger.bone.localRotation = finger.rest * Quaternion.AngleAxis(finger.degrees * (finger.index ? Mathf.Max(trigger, grip * .35f) : grip), finger.axis);
        }
    }
}
