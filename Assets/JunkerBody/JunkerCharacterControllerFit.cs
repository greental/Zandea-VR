using UnityEngine;

namespace JunkerBody
{
    [DefaultExecutionOrder(12000)]
    public sealed class JunkerCharacterControllerFit : MonoBehaviour
    {
        public VRBodyController body;
        public CharacterController characterController;
        [Tooltip("Keep the capsule at the configured player height. Enable only if physical crouching should shrink it.")]
        public bool followPhysicalCrouch;
        public float crouchHeight = .55f;
        public float minimumHeight = 1.0f;
        [Min(0f), Tooltip("Raises the capsule relative to the XR Origin. Gravity then settles the XR Origin downward, correcting a player who floats above the physical floor.")]
        public float groundingCenterOffset = .30f;
        bool originCorrected;

        void Awake() { FindReferences(); }
        void OnEnable() { FindReferences(); }
        void Start()
        {
            FindReferences();
            if (body && body.xrOrigin && !originCorrected && groundingCenterOffset > 0f)
            {
                // Lower the complete playspace while raising the capsule by the same
                // amount, keeping its world-space floor contact unchanged.
                body.xrOrigin.position -= body.xrOrigin.up * groundingCenterOffset;
                originCorrected = true;
            }
        }
        void FindReferences()
        {
            if (!body) body = GetComponent<VRBodyController>();
            if (!characterController && body && body.xrOrigin)
                characterController = body.xrOrigin.GetComponent<CharacterController>();
            if (!characterController) characterController = GetComponentInChildren<CharacterController>();
        }
        void LateUpdate()
        {
            if (!body || !characterController) { FindReferences(); if (!body || !characterController) return; }
            float height = body.playerHeight;
            if (followPhysicalCrouch) height -= body.CrouchAmount * crouchHeight;
            height = Mathf.Max(minimumHeight, height);
            characterController.height = height;
            characterController.center = new Vector3(characterController.center.x,
                height * .5f + characterController.skinWidth + groundingCenterOffset,
                characterController.center.z);
        }
    }
}
