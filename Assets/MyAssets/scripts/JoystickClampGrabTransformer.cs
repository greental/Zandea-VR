using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace MyGame.XR
{
    [AddComponentMenu("XR/Transformers/Joystick Clamp Grab Transformer (Debug)")]
    public class JoystickClampGrabTransformer_Debug : XRBaseGrabTransformer, IXRGrabTransformer
    {
        [Header("Angle limits (degrees)")]
        public float minPitch = -25f;
        public float maxPitch = 25f;

        public float minRoll = -25f;
        public float maxRoll = 25f;

        [Header("Sensitivity")]
        public float pitchSensitivity = 0.5f;
        public float rollSensitivity = 0.5f;

        [Header("Debug")]
        [Tooltip("Enable to log every frame (can spam console)")]
        public bool verboseLog = false;

        private Quaternion _initialRotation;

        protected override RegistrationMode registrationMode => RegistrationMode.Multiple;

        public override void OnGrab(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            _initialRotation = grabInteractable.transform.rotation;
            if (verboseLog)
                Debug.Log($"🟩 JoystickGrabTransformer: OnGrab → initialRotation = {_initialRotation.eulerAngles}");
        }

        public override void Process(
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose,
            ref Vector3 localScale)
        {
            Quaternion desiredRotation = targetPose.rotation;
            Quaternion delta = Quaternion.Inverse(_initialRotation) * desiredRotation;
            Vector3 deltaEuler = delta.eulerAngles;

            deltaEuler.x = NormalizeAngle(deltaEuler.x);
            deltaEuler.y = NormalizeAngle(deltaEuler.y);
            deltaEuler.z = NormalizeAngle(deltaEuler.z);

            float pitch = deltaEuler.x * pitchSensitivity;
            float roll  = deltaEuler.z * rollSensitivity;

            float clampedPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            float clampedRoll  = Mathf.Clamp(roll,  minRoll,  maxRoll);

            Quaternion clampedDelta = Quaternion.Euler(clampedPitch, 0f, clampedRoll);
            targetPose.rotation = _initialRotation * clampedDelta;

            if (verboseLog)
            {
                Debug.Log($"🎮 JoystickClamp: desired={desiredRotation.eulerAngles} delta=({deltaEuler.x:F1},{deltaEuler.z:F1}) clamped=({clampedPitch:F1},{clampedRoll:F1}) final={targetPose.rotation.eulerAngles}");
            }
        }

        private float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }
    }
}
