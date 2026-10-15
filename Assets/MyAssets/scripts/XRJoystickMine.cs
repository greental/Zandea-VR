using System;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace UnityEngine.XR.Content.Interaction
{
    /// <summary>
    /// An interactable joystick that can move side to side, and forward and back by a direct interactor
    /// </summary>
    public class XRJoystickMine : XRBaseInteractable
    {
        const float k_MaxDeadZonePercent = 0.9f;

        public enum JoystickType
        {
            BothCircle,
            BothSquare,
            FrontBack,
            LeftRight,
        }

        [Serializable]
        public class ValueChangeEvent : UnityEvent<float> { }

        [Tooltip("Controls how the joystick moves")]
        [SerializeField]
        JoystickType m_JoystickMotion = JoystickType.BothCircle;

        [SerializeField]
        [Tooltip("Optional reference frame used to interpret joystick movement (e.g. ship cockpit root).")]
        Transform referenceFrame = null;   // INSERTED FIELD (minimally replacing shipRoot)

        [SerializeField]
        [Tooltip("The object that is visually grabbed and manipulated")]
        Transform m_Handle = null;

        [SerializeField]
        [Tooltip("The value of the joystick")]
        Vector2 m_Value = Vector2.zero;

        [SerializeField]
        [Tooltip("If true, the joystick will return to center on release")]
        bool m_RecenterOnRelease = true;

        [SerializeField]
        [Tooltip("Maximum angle the joystick can move (pitch/roll tilt)")]
        [Range(1.0f, 90.0f)]
        float m_MaxAngle = 60.0f;

        [SerializeField]
        [Tooltip("Maximum yaw twist angle (degrees) for full deflection when driven from HOTASJoystick")]
        [Range(10.0f, 90.0f)]
        float m_MaxYawAngle = 45.0f;

        [SerializeField]
        [Tooltip("Minimum amount the joystick must move off the center to register changes")]
        [Range(1.0f, 90.0f)]
        float m_DeadZoneAngle = 10.0f;

        [SerializeField]
        [Tooltip("Events to trigger when the joystick's x value changes")]
        ValueChangeEvent m_OnValueChangeX = new ValueChangeEvent();

        [SerializeField]
        [Tooltip("Events to trigger when the joystick's y value changes")]
        ValueChangeEvent m_OnValueChangeY = new ValueChangeEvent();

        IXRSelectInteractor m_Interactor;

        [SerializeField]
        [Tooltip("Enable verbose joystick debug logging")]
#pragma warning disable CS0414
        bool m_DebugJoystick = false;

        [SerializeField]
        [Tooltip("Min time (seconds) between debug logs")]
        float m_DebugPrintInterval = 0.1f;

        public HOTASJoystick hotasJoystick;

        float m_LastDebugTime = 0f;
#pragma warning restore CS0414

        /// <summary>
        /// Helper accessor. If a custom reference frame is assigned, the joystick movement
        /// will be interpreted relative to that frame. Otherwise this joystick's own transform is used.
        /// </summary>
        Transform Frame => referenceFrame != null ? referenceFrame : transform;

        /// <summary>Runtime auto-setup: assigns ship body as reference frame only when none is set in the inspector.</summary>
        public void EnsureReferenceFrame(Transform shipBody)
        {
            if (referenceFrame == null && shipBody != null)
                referenceFrame = shipBody;
        }

        /// <summary>
        /// Controls how the joystick moves
        /// </summary>
        public JoystickType joystickMotion
        {
            get => m_JoystickMotion;
            set => m_JoystickMotion = value;
        }

        /// <summary>
        /// The object that is visually grabbed and manipulated
        /// </summary>
        public Transform handle
        {
            get => m_Handle;
            set => m_Handle = value;
        }

        /// <summary>
        /// The value of the joystick
        /// </summary>
        public Vector2 value
        {
            get => m_Value;
            set
            {
                if (!m_RecenterOnRelease)
                {
                    SetValue(value);
                    SetHandleAngle(value * m_MaxAngle);
                }
            }
        }

        /// <summary>
        /// If true, the joystick will return to center on release
        /// </summary>
        public bool recenterOnRelease
        {
            get => m_RecenterOnRelease;
            set => m_RecenterOnRelease = value;
        }

        /// <summary>
        /// Maximum angle the joystick can move
        /// </summary>
        public float maxAngle
        {
            get => m_MaxAngle;
            set => m_MaxAngle = value;
        }

        /// <summary>
        /// Minimum amount the joystick must move off the center to register changes
        /// </summary>
        public float deadZoneAngle
        {
            get => m_DeadZoneAngle;
            set => m_DeadZoneAngle = value;
        }

        /// <summary>
        /// Events to trigger when the joystick's x value changes
        /// </summary>
        public ValueChangeEvent onValueChangeX => m_OnValueChangeX;

        /// <summary>
        /// Events to trigger when the joystick's y value changes
        /// </summary>
        public ValueChangeEvent onValueChangeY => m_OnValueChangeY;

        void Start()
        {
            if (m_RecenterOnRelease)
                SetHandleAngle(Vector2.zero);

            // Align joystick's logical space with the reference frame if supplied
            if (referenceFrame != null)
                transform.rotation = referenceFrame.rotation;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            selectEntered.AddListener(StartGrab);
            selectExited.AddListener(EndGrab);
        }

        protected override void OnDisable()
        {
            selectEntered.RemoveListener(StartGrab);
            selectExited.RemoveListener(EndGrab);
            base.OnDisable();
        }

        private void StartGrab(SelectEnterEventArgs args)
        {
            m_Interactor = args.interactorObject;
            Transform attachTransform = m_Interactor.GetAttachTransform(this);
            hotasJoystick.SetGrabState(true, attachTransform);
        }

        private void EndGrab(SelectExitEventArgs arts)
        {
            UpdateValue();

            if (m_RecenterOnRelease)
            {
                SetHandleAngleWithYaw(0f, 0f, 0f);
                SetValue(Vector2.zero);
            }

            m_Interactor = null;
            if (hotasJoystick != null)
                hotasJoystick.SetGrabState(false, null);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            {
                if (isSelected)
                {
                    // Drive visual from HOTASJoystick so it matches pitch/roll/yaw sent to the ship (including wrist twist).
                    if (hotasJoystick != null && m_Handle != null)
                    {
                        float pitchDeg = hotasJoystick.Pitch * m_MaxAngle;
                        float rollDeg = hotasJoystick.Roll * m_MaxAngle;
                        float yawDeg = hotasJoystick.Yaw * m_MaxYawAngle;
                        SetHandleAngleWithYaw(pitchDeg, rollDeg, yawDeg);
                        m_Value = new Vector2(hotasJoystick.Roll, hotasJoystick.Pitch);
                        m_OnValueChangeX.Invoke(m_Value.x);
                        m_OnValueChangeY.Invoke(m_Value.y);
                    }
                    else
                    {
                        UpdateValue();
                    }
                }
            }
        }

        Vector3 GetLookDirection()
        {
            Vector3 direction = m_Interactor.GetAttachTransform(this).position - m_Handle.position;

            // Use the chosen frame (referenceFrame or local)
            direction = Frame.InverseTransformDirection(direction);

            switch (m_JoystickMotion)
            {
                case JoystickType.FrontBack:
                    direction.x = 0;
                    break;
                case JoystickType.LeftRight:
                    direction.z = 0;
                    break;
            }

            direction.y = Mathf.Clamp(direction.y, 0.01f, 1.0f);
            return direction.normalized;
        }

        void UpdateValue()
        {
            Transform inter = m_Interactor.GetAttachTransform(this);

            // Controller vector in reference frame
            Vector3 localDir = Frame.InverseTransformDirection(inter.position - m_Handle.position);

            float pitch = Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;   // forward/back
            float roll  = Mathf.Atan2(localDir.x, localDir.y) * Mathf.Rad2Deg;   // left/right

            pitch = Mathf.Clamp(pitch, -m_MaxAngle, m_MaxAngle);
            roll  = Mathf.Clamp(roll, -m_MaxAngle, m_MaxAngle);

            SetHandleAngle(new Vector2(roll, pitch));

            m_Value = new Vector2(
                roll  / m_MaxAngle,
                pitch / m_MaxAngle
            );

            m_OnValueChangeX.Invoke(m_Value.x);
            m_OnValueChangeY.Invoke(m_Value.y);
        }


        void SetValue(Vector2 value)
        {
            m_Value = value;
            m_OnValueChangeX.Invoke(m_Value.x);
            m_OnValueChangeY.Invoke(m_Value.y);
        }

        void SetHandleAngle(Vector2 angles)
        {
            SetHandleAngleWithYaw(angles.y, angles.x, 0f);
        }

        /// <summary>
        /// Set handle rotation from pitch/roll/yaw in degrees. Yaw = twist around frame up (matches HOTASJoystick wrist twist).
        /// </summary>
        void SetHandleAngleWithYaw(float pitchDeg, float rollDeg, float yawDeg)
        {
            if (m_Handle == null)
                return;

            // Order: yaw (twist around up), then pitch (tilt back/forward), then roll (tilt left/right). Matches HOTAS pitch/roll/yaw.
            Quaternion rot =
                Quaternion.AngleAxis(yawDeg, Frame.up) *
                Quaternion.AngleAxis(pitchDeg, Frame.right) *
                Quaternion.AngleAxis(-rollDeg, Frame.forward);

            m_Handle.rotation = rot * Frame.rotation;
        }





        void OnDrawGizmosSelected()
        {
            var angleStartPoint = transform.position;

            if (m_Handle != null)
                angleStartPoint = m_Handle.position;

            const float k_AngleLength = 0.25f;

            if (m_JoystickMotion != JoystickType.LeftRight)
            {
                Gizmos.color = Color.green;
                var axisPoint1 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(m_MaxAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;
                var axisPoint2 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(-m_MaxAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;
                Gizmos.DrawLine(angleStartPoint, axisPoint1);
                Gizmos.DrawLine(angleStartPoint, axisPoint2);

                if (m_DeadZoneAngle > 0.0f)
                {
                    Gizmos.color = Color.red;
                    axisPoint1 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(m_DeadZoneAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;
                    axisPoint2 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(-m_DeadZoneAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;
                    Gizmos.DrawLine(angleStartPoint, axisPoint1);
                    Gizmos.DrawLine(angleStartPoint, axisPoint2);
                }
            }

            if (m_JoystickMotion != JoystickType.FrontBack)
            {
                Gizmos.color = Color.green;
                var axisPoint1 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(0.0f, 0.0f, m_MaxAngle) * Vector3.up) * k_AngleLength;
                var axisPoint2 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(0.0f, 0.0f, -m_MaxAngle) * Vector3.up) * k_AngleLength;
                Gizmos.DrawLine(angleStartPoint, axisPoint1);
                Gizmos.DrawLine(angleStartPoint, axisPoint2);

                if (m_DeadZoneAngle > 0.0f)
                {
                    Gizmos.color = Color.red;
                    axisPoint1 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(0.0f, 0.0f, m_DeadZoneAngle) * Vector3.up) * k_AngleLength;
                    axisPoint2 = angleStartPoint + transform.TransformDirection(Quaternion.Euler(0.0f, 0.0f, -m_DeadZoneAngle) * Vector3.up) * k_AngleLength;
                    Gizmos.DrawLine(angleStartPoint, axisPoint1);
                    Gizmos.DrawLine(angleStartPoint, axisPoint2);
                }
            }
        }

        void OnValidate()
        {
            m_DeadZoneAngle = Mathf.Min(m_DeadZoneAngle, m_MaxAngle * k_MaxDeadZonePercent);
        }

        Quaternion GetStableFrameRotation()
        {
            if (Frame == null)
                return transform.rotation;

            Quaternion shipRot = Frame.rotation;

            // Remove roll by projecting forward onto horizontal plane
            Vector3 fwd = Vector3.ProjectOnPlane(shipRot * Vector3.forward, Vector3.up);

            if (fwd.sqrMagnitude < 0.0001f)
                fwd = shipRot * Vector3.forward;

            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }
    }
}
