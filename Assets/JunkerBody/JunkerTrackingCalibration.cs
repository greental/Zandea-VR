using UnityEngine;
using Unity.XR.CoreUtils;

namespace JunkerBody
{
    // Optional correction for a measured headset-floor calibration error. It moves the
    // existing common tracking parent, keeping HMD, controllers and optical wrists together.
    // Zero means use the XR runtime's floor as reported; never guess a correction from stature.
    [DefaultExecutionOrder(-200)]
    public sealed class JunkerTrackingCalibration : MonoBehaviour
    {
        public XROrigin xrOrigin;
        public Transform trackingOffset;
        public Transform leftController;
        public Transform rightController;
        [Range(-0.5f, 0.5f), Tooltip("Metres added to all tracked poses. Negative lowers the tracked player. Leave at zero unless a residual floor error is measured.")]
        public float floorOffset;
        public float LocomotionCrouchOffset { get; private set; }
        [Min(0f), Tooltip("Height of the controller's tracking origin above the floor when physically resting on it. Used only by the explicit calibration command.")]
        public float controllerRestHeight = 0.05f;
        Vector3 initialOffset;
        bool initialized;
        void OnEnable()
        {
            ResolveTrackingOffset();
            if (!xrOrigin || !trackingOffset || !leftController) enabled = false;
        }
        void Start()
        {
            if (!isActiveAndEnabled) return;
            // XROrigin applies its Floor/Device offset during initialization. Capture
            // the baseline afterwards so the prefab's edit-time Y offset is not used.
            ResolveTrackingOffset();
            if (!xrOrigin || !trackingOffset || !leftController) { enabled = false; return; }
            initialOffset = trackingOffset.localPosition; initialized = true; Apply();
        }
        void Update() { Apply(); }
        void Apply()
        {
            if (initialized) trackingOffset.localPosition = initialOffset + Vector3.up * (floorOffset + LocomotionCrouchOffset);
        }
        public void SetLocomotionCrouchOffset(float offset)
        {
            LocomotionCrouchOffset = offset;
            Apply();
        }
        void ResolveTrackingOffset()
        {
            var body = GetComponent<VRBodyController>();
            if (body)
            {
                if (!xrOrigin) xrOrigin = body.GetComponentInChildren<XROrigin>();
                if (!leftController) leftController = body.leftController;
                if (!rightController) rightController = body.rightController;
            }
            if (!xrOrigin || !xrOrigin.Camera || !leftController) return;

            // The camera's direct parent can be a camera-only ViewOffset. Calibration
            // must move the lowest parent shared by the HMD and both controllers so
            // physical hand-to-floor reach changes together with eye height.
            Transform candidate = xrOrigin.Camera.transform;
            while (candidate &&
                   (!leftController.IsChildOf(candidate) ||
                    (rightController && !rightController.IsChildOf(candidate))))
                candidate = candidate.parent;

            if (candidate && candidate != xrOrigin.transform && candidate.IsChildOf(xrOrigin.transform))
                trackingOffset = candidate;
        }
        void OnDisable()
        {
            if (initialized && trackingOffset) trackingOffset.localPosition = initialOffset;
            LocomotionCrouchOffset = 0f;
            initialized = false;
        }
        [ContextMenu("Calibrate floor (left controller resting on physical floor)")]
        public void CalibrateFloor()
        {
            if (!Application.isPlaying || !initialized) return;
            float measured = xrOrigin.Origin.transform.InverseTransformPoint(leftController.position).y;
            floorOffset = Mathf.Clamp(floorOffset + controllerRestHeight - measured, -0.5f, 0.5f);
            Apply();
        }
        [ContextMenu("Set measured 30 cm floor correction")]
        public void SetThirtyCentimeterCorrection()
        {
            floorOffset = -0.30f;
            Apply();
        }
    }
}
