using System;
using JunkerBody;
using UnityEngine;
namespace JunkerEquipment
{
    [DefaultExecutionOrder(10600)]
    public sealed class JunkerGloveHand : MonoBehaviour, IGrabPoseOverride, IPlayerHandVisual
    {
        [Serializable] public struct Joint
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 localAxis;
            public float curlDegrees;
            public bool index;
        }
        public VRBodyController body;
        public bool left;
        public Transform HandController => body ? (left ? body.leftController : body.rightController) : null;
        public GameObject HandVisual => gameObject;
        public bool IsLeftHand => left;
        public AnimateControllerHand input;
        public Joint[] joints;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        [Range(0,1)] public float grip, trigger;
        [Tooltip("Use only for inspecting/testing poses without controller input.")]
        public bool previewPose;
        [NonSerialized] bool followBodyArmTarget = true;
        public void SetGrabPoseOverride(bool grabbed) { followBodyArmTarget = !grabbed; }
        void LateUpdate()
        {
            if(body && followBodyArmTarget)
            {
                var arm=left?body.leftArm:body.rightArm;
                var rotation=arm.target.rotation*Quaternion.Inverse(Quaternion.Euler(arm.wristRotationOffset));
                transform.SetPositionAndRotation(arm.target.position+rotation*positionOffset,rotation*Quaternion.Euler(rotationOffset));
            }
            if(!previewPose && input)
            {
                grip=input.gripValue.action?.ReadValue<float>()??0;
                trigger=input.triggerValue.action?.ReadValue<float>()??0;
            }
            SetCurl(grip,trigger);
        }
        public void SetCurl(float gripValue,float triggerValue)
        {
            foreach(var joint in joints)
                if(joint.bone) joint.bone.localRotation=joint.rest*Quaternion.AngleAxis(joint.curlDegrees*(joint.index?Mathf.Max(triggerValue,gripValue*.35f):gripValue),joint.localAxis);
        }
    }
}
