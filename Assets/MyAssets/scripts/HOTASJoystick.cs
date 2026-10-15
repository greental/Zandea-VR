using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// HOTAS pitch/roll from hand POSITION in cockpit (ship) local space. Single frame, no orientation math,
/// no singularities or direction flips when upside down. Deflection = hand position (cockpit local) minus
/// neutral (hand at grab); pitch = back/forward axis, roll = left/right axis.
/// </summary>
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class HOTASJoystick : MonoBehaviour
{
    const string k_ProxyName = "HOTASInputProxy";

    [Header("Reference frame (all math in this frame)")]
    [Tooltip("Stick base / pivot. Must be set. Defines stick origin and parent for visual proxy.")]
    public Transform pivot;

    [Tooltip("Cockpit/ship frame for input. Hand position is transformed into this frame. If null, uses Pivot. Use the ship body (e.g. Rigidbody transform) so axes stay consistent when ship rotates.")]
    public Transform shipReference;

    [Tooltip("Optional: child transform used as input proxy visual. If null, one is created under Pivot.")]
    public Transform inputProxy;

    [Header("Position-based deflection (meters in cockpit space)")]
    [Tooltip("Distance (meters) from neutral that counts as full pitch deflection (±1).")]
    public float reachPitchMeters = 0.08f;
    [Tooltip("Distance (meters) from neutral that counts as full roll deflection (±1).")]
    public float reachRollMeters = 0.08f;

    [Header("Axes (cockpit local: Unity right = +X, forward = +Z)")]
    [Tooltip("Cockpit-local axis for pitch: pull back = positive pitch (nose up). Default +Z = forward; pull back = -Z so we use -Z.")]
    public Vector3 pitchAxis = new Vector3(0f, 0f, -1f);
    [Tooltip("Cockpit-local axis for roll: right = positive roll. Default +X = right.")]
    public Vector3 rollAxis = new Vector3(1f, 0f, 0f);

    [Header("Invert (if stick sense is opposite)")]
    public bool invertPitch;
    public bool invertRoll;
    public bool invertYaw;

    [Header("Visual (proxy tilt)")]
    [Range(10f, 90f)]
    public float maxPitchAngle = 45f;
    [Range(10f, 90f)]
    public float maxRollAngle = 45f;
    public float recenterSpeed = 12f;

    [Header("Input shaping")]
    [Range(0f, 0.5f)]
    public float deadzone = 0.03f;
    [Tooltip("1 = linear; higher sharpens toward extremes.")]
    public float responseCurve = 1f;

    [Header("Debug log")]
    public bool logToFile = false;
    public int logMaxLines = 400;
    static string LogPath => Path.Combine(Application.dataPath, "MyAssets", "scripts", "hotas_joystick_debug.log");
    static List<string> s_logLines = new List<string>();
    static float s_nextLogTime;

    [Header("Yaw (wrist twist while grabbing)")]
    [Tooltip("Twist wrist left/right around stick axis for yaw. Max twist angle (degrees) for full deflection.")]
    [Range(15f, 90f)]
    public float maxYawAngle = 45f;
    [Tooltip("Controller axis to measure twist (default Right = thumb side).")]
    public Vector3 gripTwistAxis = Vector3.right;

    // Outputs: [-1, 1]. +Pitch = pull back (nose up), +Roll = right, +Yaw = twist right (nose right)
    public float Pitch { get; private set; }
    public float Roll  { get; private set; }
    public float Yaw   { get; private set; }
    public bool IsGrabbed { get; private set; }

    private Transform handTransform;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;

    // Neutral = hand position at grab, in cockpit local. Single frame, no rotation stored.
    private Vector3 _neutralCockpitLocal;
    private bool _neutralCaptured;
    // Yaw: wrist twist in cockpit XZ plane (angle of gripTwistAxis), neutral at grab.
    private float _twistNeutralAngleRad;
    private bool _twistNeutralCaptured;

    Transform CockpitFrame => shipReference != null ? shipReference : pivot;

    public void SetGrabState(bool state, Transform handOrAttachTransform = null)
    {
        if (state)
        {
            handTransform = handOrAttachTransform;
            IsGrabbed = true;
        }
        else
        {
            handTransform = null;
            _neutralCaptured = false;
            _twistNeutralCaptured = false;
            Pitch = 0f;
            Roll  = 0f;
            Yaw   = 0f;
            IsGrabbed = false;
        }
    }

    /// <summary>Set current hand position and twist as neutral (re-center without releasing).</summary>
    public void SetNeutralNow()
    {
        if (CockpitFrame == null || handTransform == null) return;
        _neutralCockpitLocal = CockpitFrame.InverseTransformPoint(handTransform.position);
        _neutralCaptured = true;
        _twistNeutralCaptured = false; // re-capture twist neutral next frame
    }

    void Awake()
    {
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (pivot == null) pivot = transform;
        if (shipReference == null)
        {
            var rb = GetComponentInParent<Rigidbody>();
            if (rb != null)
                shipReference = rb.transform;
        }
        EnsureProxy();
    }

    void OnEnable()
    {
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }
    }

    void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
        }
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        handTransform = args.interactorObject.GetAttachTransform(args.interactableObject);
        IsGrabbed = true;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        IsGrabbed = false;
        handTransform = null;
        _neutralCaptured = false;
        _twistNeutralCaptured = false;
        Pitch = 0f;
        Roll  = 0f;
        Yaw   = 0f;
    }

    void EnsureProxy()
    {
        if (inputProxy != null || pivot == null) return;
        Transform child = pivot.Find(k_ProxyName);
        if (child != null)
        {
            inputProxy = child;
            return;
        }
        GameObject go = new GameObject(k_ProxyName);
        go.transform.SetParent(pivot, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        inputProxy = go.transform;
    }

    void Update()
    {
        if (pivot != null)
            EnsureProxy();

        if (IsGrabbed && handTransform != null && CockpitFrame != null)
        {
            UpdatePositionBasedInput();
        }
        else
        {
            if (inputProxy != null)
                RecenterProxy();
            Pitch = 0f;
            Roll  = 0f;
            Yaw   = 0f;
        }
    }

    /// <summary>
    /// All math in cockpit frame. Hand position (world) -> cockpit local; deflection = hand - neutral;
    /// pitch/roll from deflection along cockpit axes. No angles, no singularities.
    /// </summary>
    void UpdatePositionBasedInput()
    {
        Transform frame = CockpitFrame;
        Vector3 handCockpitLocal = frame.InverseTransformPoint(handTransform.position);

        if (!_neutralCaptured)
        {
            _neutralCockpitLocal = handCockpitLocal;
            _neutralCaptured = true;
            if (logToFile) LogHotasLine($"{Time.time:F2} | CAPTURE neutral (hand in cockpit local)");
        }

        // Deflection vector in cockpit local (meters)
        Vector3 deflection = handCockpitLocal - _neutralCockpitLocal;

        // Project onto pitch/roll axes (normalized) and scale by reach -> raw [-1,1]
        Vector3 pitchAxisN = pitchAxis.normalized;
        Vector3 rollAxisN = rollAxis.normalized;
        float pitchComponent = Vector3.Dot(deflection, pitchAxisN);
        float rollComponent = Vector3.Dot(deflection, rollAxisN);

        float rawPitch = Mathf.Clamp(pitchComponent / Mathf.Max(0.001f, reachPitchMeters), -1f, 1f);
        float rawRoll = Mathf.Clamp(rollComponent / Mathf.Max(0.001f, reachRollMeters), -1f, 1f);

        // Visual: proxy tilt from same deflection
        float pitchDegForVisual = rawPitch * maxPitchAngle;
        float rollDegForVisual = rawRoll * maxRollAngle;
        if (inputProxy != null && pivot != null)
            inputProxy.localRotation = Quaternion.Euler(pitchDegForVisual, 0f, -rollDegForVisual);

        rawPitch = ApplyDeadzone(rawPitch);
        rawRoll = ApplyDeadzone(rawRoll);
        rawPitch = invertPitch ? -rawPitch : rawPitch;
        rawRoll = invertRoll ? -rawRoll : rawRoll;
        Pitch = ApplyCurve(rawPitch);
        Roll  = ApplyCurve(rawRoll);

        // Yaw from wrist twist: angle of gripTwistAxis in cockpit XZ plane (ship up = Y), relative to grab.
        Vector3 twistDirWorld = handTransform.TransformDirection(gripTwistAxis.normalized);
        Vector3 twistCockpitLocal = frame.InverseTransformDirection(twistDirWorld);
        float twistX = twistCockpitLocal.x;
        float twistZ = twistCockpitLocal.z;
        float twistLenXZ = Mathf.Sqrt(twistX * twistX + twistZ * twistZ);
        if (twistLenXZ > 0.01f)
        {
            float twistAngleRad = Mathf.Atan2(twistX, twistZ);
            if (!_twistNeutralCaptured)
            {
                _twistNeutralAngleRad = twistAngleRad;
                _twistNeutralCaptured = true;
            }
            float deltaRad = Mathf.DeltaAngle(_twistNeutralAngleRad * Mathf.Rad2Deg, twistAngleRad * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float maxYawRad = maxYawAngle * Mathf.Deg2Rad;
            float rawYaw = Mathf.Clamp(deltaRad / Mathf.Max(0.001f, maxYawRad), -1f, 1f);
            rawYaw = ApplyDeadzone(rawYaw);
            rawYaw = invertYaw ? -rawYaw : rawYaw;
            Yaw = ApplyCurve(rawYaw);
        }
        else
        {
            Yaw = 0f;
        }

        if (logToFile && Time.time >= s_nextLogTime)
        {
            s_nextLogTime = Time.time + 0.1f;
            s_logLines.Add($"{Time.time:F2} | defl=({deflection.x:F3},{deflection.y:F3},{deflection.z:F3}) Pitch={Pitch:F3} Roll={Roll:F3} Yaw={Yaw:F3}");
            if (s_logLines.Count > logMaxLines) s_logLines.RemoveAt(0);
            try { File.WriteAllText(LogPath, string.Join("\n", s_logLines)); }
            catch (System.Exception ex) { Debug.LogWarning("HOTASJoystick log: " + ex.Message); }
        }
    }

    static void LogHotasLine(string line)
    {
        s_logLines.Add(line);
        if (s_logLines.Count > 400) s_logLines.RemoveAt(0);
        try { File.WriteAllText(LogPath, string.Join("\n", s_logLines)); }
        catch (System.Exception ex) { Debug.LogWarning("HOTASJoystick log: " + ex.Message); }
    }

    void RecenterProxy()
    {
        if (inputProxy == null) return;
        inputProxy.localRotation = Quaternion.Slerp(
            inputProxy.localRotation,
            Quaternion.identity,
            recenterSpeed * Time.deltaTime
        );
    }

    float ApplyDeadzone(float v)
    {
        float abs = Mathf.Abs(v);
        if (abs < deadzone) return 0f;
        float t = Mathf.InverseLerp(deadzone, 1f, abs);
        return t * Mathf.Sign(v);
    }

    float ApplyCurve(float v)
    {
        if (Mathf.Approximately(v, 0f)) return 0f;
        float abs = Mathf.Abs(v);
        float c = Mathf.Pow(abs, responseCurve);
        return Mathf.Sign(v) * c;
    }
}
