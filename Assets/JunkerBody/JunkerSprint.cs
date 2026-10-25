using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

namespace JunkerBody
{
    // Set movement, crouch and jump input before the existing XRI providers process it.
    [DefaultExecutionOrder(-100)]
    public sealed class JunkerSprint : MonoBehaviour
    {
        // Held tools reserve only the click, leaving stick axes and movement available.
        static readonly System.Collections.Generic.HashSet<Object> leftClickOwners = new(), rightClickOwners = new();
        public static void ReserveStickClick(Object owner, bool left, bool reserve)
        {
            var owners = left ? leftClickOwners : rightClickOwners;
            if (reserve) owners.Add(owner); else owners.Remove(owner);
        }
        public static bool IsStickClickReserved(bool left)
        {
            var owners = left ? leftClickOwners : rightClickOwners;
            owners.RemoveWhere(owner => !owner);
            return owners.Count > 0;
        }
        public ContinuousMoveProvider moveProvider;
        public Transform xrOrigin;
        public Animator animator;
        [Min(0.01f)] public float walkSpeed = 2f;
        [Min(0.01f)] public float sprintSpeed = 4f;
        [Min(0.01f)] public float crouchSpeed = 1.5f;
        [Min(0f)] public float crouchDepth = .55f;
        [Min(.01f)] public float crouchTransitionSpeed = 5f;
        [Range(.5f, .95f)] public float stickDirectionThreshold = .8f;
        [Range(0f, 1f)] public float inputThreshold = 0.15f;
        // OpenXR exposes the left stick click as the Primary2DAxisClick usage;
        // the underlying control is named thumbstickClicked on Oculus Touch.
        public InputActionProperty sprintAction = new InputActionProperty(new InputAction("Junker Sprint", InputActionType.Button, "<XRController>{LeftHand}/{Primary2DAxisClick}"));
        public InputActionProperty crouchAction = new InputActionProperty(new InputAction("Junker Crouch", InputActionType.Button, "<XRController>{RightHand}/{Primary2DAxisClick}"));
        public InputActionProperty rightStickAction = new InputActionProperty(new InputAction("Junker Right Stick", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis"));
        public bool IsSprinting { get; private set; }
        public bool IsCrouched { get; private set; }
        public float CrouchBlend { get; private set; }
        public float MeasuredSpeed { get; private set; }
        float originalSpeed;
        Vector3 previousPosition;
        Quaternion previousRotation;
        bool ownsAction;
        bool ownsCrouchAction;
        bool ownsRightStickAction;
        bool sprintLatched;
        bool wasMoving;
        bool rightUpHeld, jumpHeld;
        JumpProvider jumpProvider;
        GravityProvider jumpGravity;
        bool registeredJump;
        XRInputButtonReader.InputSourceMode previousJumpInputMode;
        JunkerTrackingCalibration trackingCalibration;
        VRBodyController body;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Jumping = Animator.StringToHash("Jumping");
        bool hasJumpingParameter;
        float jumpAnimationUntil;
        void OnEnable()
        {
            if (!moveProvider || !xrOrigin || !animator) { Debug.LogError("Junker sprint references are incomplete.", this); enabled = false; return; }
            hasJumpingParameter = false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == Jumping && parameter.type == AnimatorControllerParameterType.Bool)
                { hasJumpingParameter = true; break; }
            originalSpeed = moveProvider.moveSpeed;
            previousPosition = xrOrigin.position;
            previousRotation = xrOrigin.rotation;
            var action = sprintAction.action;
            ownsAction = action != null && !action.enabled;
            if (ownsAction) action.Enable();
            var crouch = crouchAction.action;
            ownsCrouchAction = crouch != null && !crouch.enabled;
            if (ownsCrouchAction) crouch.Enable();
            var rightAction = rightStickAction.action;
            ownsRightStickAction = rightAction != null && !rightAction.enabled;
            if (ownsRightStickAction) rightAction.Enable();
            body = GetComponent<VRBodyController>();
            trackingCalibration = GetComponent<JunkerTrackingCalibration>();
            jumpProvider = xrOrigin.GetComponentInChildren<JumpProvider>(true);
            if (jumpProvider)
            {
                jumpGravity = xrOrigin.GetComponentInChildren<GravityProvider>(true);
                if (jumpGravity && !jumpGravity.gravityControllers.Contains(jumpProvider))
                {
                    jumpGravity.gravityControllers.Add(jumpProvider);
                    registeredJump = true;
                }
                previousJumpInputMode = jumpProvider.jumpInput.inputSourceMode;
                jumpProvider.jumpInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
                SetManualJump(false);
            }
        }
        void Update()
        {
            var left = moveProvider.leftHandMoveInput.ReadValue();
            if (!IsStickClickReserved(false) && (crouchAction.action?.WasPressedThisFrame() ?? false)) IsCrouched = !IsCrouched;
            Vector2 right = rightStickAction.action?.ReadValue<Vector2>() ?? Vector2.zero;
            bool rightUp = right.y > stickDirectionThreshold && Mathf.Abs(right.x) < .45f;
            if (rightUp && !rightUpHeld)
            {
                // Only the explicit locomotion crouch consumes an up-flick. A
                // headset height mismatch must not permanently block jumping.
                bool rising = IsCrouched || CrouchBlend > .1f;
                if (IsCrouched) IsCrouched = false;
                jumpHeld = !rising;
                // Each deliberate up-flick starts a new permitted jump. This does
                // not depend on the provider observing a one-frame release edge.
                if (jumpHeld && jumpProvider && jumpProvider.isActiveAndEnabled && jumpProvider.CanJump())
                { jumpProvider.Jump(); jumpAnimationUntil=Time.time+.72f; }
            }
            else if (!rightUp) jumpHeld = false;
            rightUpHeld = rightUp;
            SetManualJump(jumpHeld && !IsCrouched);
            CrouchBlend = Mathf.MoveTowards(CrouchBlend, IsCrouched ? 1f : 0f, crouchTransitionSpeed * Time.deltaTime);
            if (trackingCalibration) trackingCalibration.SetLocomotionCrouchOffset(-crouchDepth * CrouchBlend);
            bool moving = moveProvider.isActiveAndEnabled && left.magnitude > inputThreshold;
            if ((!moving && wasMoving) || IsCrouched) sprintLatched = false;
            if (moving && !IsCrouched && !IsStickClickReserved(true) && (sprintAction.action?.WasPressedThisFrame() ?? false)) sprintLatched = !sprintLatched;
            IsSprinting = moving && sprintLatched && !IsCrouched;
            moveProvider.moveSpeed = IsCrouched ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            // Measure only rig locomotion, never camera displacement from room-scale tracking.
            var displacement = Vector3.ProjectOnPlane(xrOrigin.position - previousPosition, xrOrigin.up);
            float speed = displacement.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            var input = left + moveProvider.rightHandMoveInput.ReadValue();
            bool discontinuity = Quaternion.Angle(previousRotation, xrOrigin.rotation) > 10f || speed > sprintSpeed * 2f;
            MeasuredSpeed = moveProvider.isActiveAndEnabled && input.magnitude > inputThreshold && !discontinuity ? speed : 0f;
            float blend = MeasuredSpeed < 0.03f ? 0f : IsSprinting ? Mathf.Lerp(1f, 2f, Mathf.InverseLerp(walkSpeed, sprintSpeed, MeasuredSpeed)) : Mathf.Clamp01(MeasuredSpeed / (IsCrouched ? crouchSpeed : walkSpeed));
            animator.SetFloat(Speed, blend, 0.12f, Time.deltaTime);
            if (hasJumpingParameter) animator.SetBool(Jumping, Time.time < jumpAnimationUntil);
            // Keep the first frame of the idle pose instead of playing its breathing/sway loop.
            if (blend < .01f)
            {
                animator.Play("Locomotion", 0, 0f);
                animator.speed = 0f;
            }
            else animator.speed = 1f;
            previousPosition = xrOrigin.position;
            previousRotation = xrOrigin.rotation;
            wasMoving = moving;
        }
        void SetManualJump(bool pressed)
        {
            if (!jumpProvider) return;
            var input = jumpProvider.jumpInput;
            if (input.manualPerformed != pressed)
            {
                if (pressed) input.manualFramePerformed = Time.frameCount;
                else input.manualFrameCompleted = Time.frameCount;
            }
            input.manualPerformed = pressed;
            input.manualValue = pressed ? 1f : 0f;
        }
        void OnDisable()
        {
            if (registeredJump && jumpGravity) jumpGravity.gravityControllers.Remove(jumpProvider);
            registeredJump = false;
            if (moveProvider) moveProvider.moveSpeed = originalSpeed;
            if (ownsAction) sprintAction.action?.Disable();
            if (ownsCrouchAction) crouchAction.action?.Disable();
            if (ownsRightStickAction) rightStickAction.action?.Disable();
            if (jumpProvider)
            {
                SetManualJump(false);
                jumpProvider.jumpInput.inputSourceMode = previousJumpInputMode;
            }
            if (trackingCalibration) trackingCalibration.SetLocomotionCrouchOffset(0f);
            IsSprinting = false;
            IsCrouched = false;
            CrouchBlend = 0f;
            sprintLatched = false;
            rightUpHeld = jumpHeld = false;
            if (animator) animator.speed = 1f;
            if (animator && hasJumpingParameter) animator.SetBool(Jumping, false);
        }
    }
}
