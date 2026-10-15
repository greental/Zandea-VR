using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace UnityEngine.XR.Content.Interaction
{
    /// <summary>
    /// An interactable lever that snaps into an on or off position by a direct interactor,
    /// with added analog -1..1 thrust value and snap-to-zero support.
    /// </summary>
    public class EngineLever : XRBaseInteractable
    {
        const float k_LeverDeadZone = 0.1f; // Prevents rapid switching between on and off states when right in the middle

        [SerializeField]
        [Tooltip("The object that is visually grabbed and manipulated")]
        Transform m_Handle = null;

        [SerializeField]
        [Tooltip("Binary ON/OFF state of the lever")]
        bool m_Value = false;

        [SerializeField]
        [Tooltip("If enabled, the lever will snap to the ON/OFF position when released")]
        bool m_LockToValue;

        [SerializeField]
        [Tooltip("Angle of the lever in the 'on' position (full forward)")]
        [Range(-90.0f, 90.0f)]
        float m_MaxAngle = 90.0f;

        [SerializeField]
        [Tooltip("Angle of the lever in the 'off' position (full back)")]
        [Range(-90.0f, 90.0f)]
        float m_MinAngle = -90.0f;

        [SerializeField]
        [Tooltip("Events to trigger when the lever activates (switches ON)")]
        UnityEvent m_OnLeverActivate = new UnityEvent();

        [SerializeField]
        [Tooltip("Events to trigger when the lever deactivates (switches OFF)")]
        UnityEvent m_OnLeverDeactivate = new UnityEvent();

        [SerializeField]
        [Tooltip("Analog thrust value (-1 at min angle, 0 at center, +1 at max angle)")]
        [Range(-1f, 1f)]
        float m_NormalizedValue = 0f;

        [SerializeField]
        [Tooltip("If true, logs thrust value when it changes")]
        bool m_DebugThrust = true;

        [SerializeField]
        [Tooltip("When lever angle is within this many degrees of center (0°), it snaps to 0 so the handle stays centered and ship stops completely.")]
        [Range(0f, 20f)]
        float m_SnapToZeroAngleDeg = 8f;

        IXRSelectInteractor m_Interactor;

        /// <summary>
        /// Analog thrust value: -1 at minAngle, 0 at center (0°), +1 at maxAngle.
        /// </summary>
        public float normalizedValue => m_NormalizedValue;

        /// <summary>
        /// Binary ON/OFF state of the lever.
        /// </summary>
        public bool value
        {
            get => m_Value;
            set => SetValue(value, true);
        }

        /// <summary>
        /// If enabled, the lever will snap to ON/OFF when released.
        /// </summary>
        public bool lockToValue { get; set; }

        /// <summary>
        /// Angle of the lever in the 'on' position.
        /// </summary>
        public float maxAngle
        {
            get => m_MaxAngle;
            set => m_MaxAngle = value;
        }

        /// <summary>
        /// Angle of the lever in the 'off' position.
        /// </summary>
        public float minAngle
        {
            get => m_MinAngle;
            set => m_MinAngle = value;
        }

        public UnityEvent onLeverActivate => m_OnLeverActivate;
        public UnityEvent onLeverDeactivate => m_OnLeverDeactivate;

        void Start()
        {
            // Start at center (0°) with thrust 0
            SetHandleAngle(0f);
            UpdateNormalizedValue(0f);
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

        void StartGrab(SelectEnterEventArgs args)
        {
            m_Interactor = args.interactorObject;
        }

        void EndGrab(SelectExitEventArgs args)
        {
            m_Interactor = null;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            // Check for snap-to-zero input every frame
            CheckSnapInput();

            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
            {
                if (isSelected)
                {
                    UpdateValue();
                }
            }
        }

        void CheckSnapInput()
        {
            // Oculus X button on LEFT controller = primaryButton on XRNode.LeftHand
            if (InputDevices.GetDeviceAtXRNode(XRNode.LeftHand)
                .TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed)
                && pressed)
            {
                SnapToZero();
            }
        }

        /// <summary>
        /// Snap the lever back to center (0°) and thrust 0.
        /// </summary>
        public void SnapToZero()
        {
            SetHandleAngle(0f);
            UpdateNormalizedValue(0f);

            // Optional: treat snap-to-center as OFF in binary sense
            m_Value = false;
        }

        Vector3 GetLookDirection()
        {
            Vector3 direction = m_Interactor.GetAttachTransform(this).position - m_Handle.position;
            direction = transform.InverseTransformDirection(direction);
            direction.x = 0;

            return direction.normalized;
        }

        void UpdateValue()
        {
            var lookDirection = GetLookDirection();
            var lookAngle = Mathf.Atan2(lookDirection.z, lookDirection.y) * Mathf.Rad2Deg;

            if (m_MinAngle < m_MaxAngle)
                lookAngle = Mathf.Clamp(lookAngle, m_MinAngle, m_MaxAngle);
            else
                lookAngle = Mathf.Clamp(lookAngle, m_MaxAngle, m_MinAngle);

            // Snap to center when within threshold so handle and thrust go to 0
            if (m_SnapToZeroAngleDeg > 0f && Mathf.Abs(lookAngle) <= m_SnapToZeroAngleDeg)
            {
                lookAngle = 0f;
                UpdateNormalizedValue(0f);
                SetHandleAngle(0f);
                m_Value = false;
                return;
            }

            UpdateNormalizedValue(lookAngle);

            var maxAngleDistance = Mathf.Abs(m_MaxAngle - lookAngle);
            var minAngleDistance = Mathf.Abs(m_MinAngle - lookAngle);

            if (m_Value)
                maxAngleDistance *= (1.0f - k_LeverDeadZone);
            else
                minAngleDistance *= (1.0f - k_LeverDeadZone);

            var newValue = (maxAngleDistance < minAngleDistance);

            SetHandleAngle(lookAngle);
            SetValue(newValue);
        }

        void UpdateNormalizedValue(float angle)
        {
            // Map angle from [minAngle..maxAngle] → [0..1]
            float t01 = Mathf.InverseLerp(m_MinAngle, m_MaxAngle, angle);

            // Then map [0..1] → [-1..1] so center becomes 0
            float newValue = Mathf.Lerp(-1f, 1f, t01);

            if (m_DebugThrust && !Mathf.Approximately(newValue, m_NormalizedValue))
            {
                Debug.Log($"[EngineLever] Thrust: {newValue:F2} (angle {angle:F1}°)");
            }

            m_NormalizedValue = newValue;
        }

        void SetValue(bool isOn, bool forceRotation = false)
        {
            if (m_Value == isOn)
            {
                if (forceRotation)
                    SetHandleAngle(m_Value ? m_MaxAngle : m_MinAngle);

                return;
            }

            m_Value = isOn;

            if (m_Value)
                m_OnLeverActivate.Invoke();
            else
                m_OnLeverDeactivate.Invoke();

            if (!isSelected && (m_LockToValue || forceRotation))
                SetHandleAngle(m_Value ? m_MaxAngle : m_MinAngle);
        }

        void SetHandleAngle(float angle)
        {
            if (m_Handle != null)
                m_Handle.localRotation = Quaternion.Euler(angle, 0.0f, 0.0f);
        }

        void OnDrawGizmosSelected()
        {
            var angleStartPoint = m_Handle != null ? m_Handle.position : transform.position;

            const float k_AngleLength = 0.25f;

            var angleMaxPoint = angleStartPoint + transform.TransformDirection(Quaternion.Euler(m_MaxAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;
            var angleMinPoint = angleStartPoint + transform.TransformDirection(Quaternion.Euler(m_MinAngle, 0.0f, 0.0f) * Vector3.up) * k_AngleLength;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(angleStartPoint, angleMaxPoint);

            Gizmos.color = Color.red;
            Gizmos.DrawLine(angleStartPoint, angleMinPoint);
        }

        void OnValidate()
        {
            // In editor, keep lever visually centered by default
            SetHandleAngle(0f);
            UpdateNormalizedValue(0f);
        }
    }
}
