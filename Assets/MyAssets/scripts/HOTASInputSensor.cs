using UnityEngine;

public class HOTASInputSensor : MonoBehaviour
{
    [Header("References")]
    public Transform handle;          // Joystick root transform
    public Transform flightFrame;     // Ship's actual flight orientation root
    public Transform grabbedBy;       // The XR Hand transform currently grabbing

    [Header("Settings")]
    public float rollRange = 0.20f;   // Max offset to left/right
    public float pitchRange = 0.20f;  // Max offset forward/back
    public float yawRange = 0.15f;    // Twist range or vertical twist proxy
    public float deadzone = 0.05f;    // Input ignored inside this threshold
    public float curve = 1.6f;        // Exponential response curve

    [Header("Debug")]
    public bool debug = false;

    // Public outputs
    public float Pitch { get; private set; }
    public float Roll  { get; private set; }
    public float Yaw   { get; private set; }

    // Internal
    public bool IsGrabbed { get; private set; } = false;
    public void SetGrabState(bool state)
    {
        IsGrabbed = state;
    }
    private Vector3 neutralWorldPos;

    void Start()
    {
        neutralWorldPos = handle.position;
    }

    public void OnGrab(Transform grabber)
    {
        grabbedBy = grabber;
        IsGrabbed = true;
        neutralWorldPos = handle.position;
    }

    public void OnRelease()
    {
        IsGrabbed = false;
        grabbedBy = null;
        ZeroOutputs();
    }

    void Update()
    {
        if (!IsGrabbed)
        {
            ZeroOutputs();
            DebugIfNeeded("[HOTAS] Not grabbed → zeroing inputs");
            return;
        }

        // 1. Compute world-space offset
        Vector3 worldOffset = handle.position - neutralWorldPos;

        // 2. Convert offset to SHIP'S LOCAL FRAME
        Vector3 localOffset = flightFrame.InverseTransformDirection(worldOffset);

        // 3. Normalize each axis into [-1, 1]
        float rawPitch = Mathf.Clamp(localOffset.z / pitchRange, -1f, 1f); // forward/back
        float rawRoll  = Mathf.Clamp(localOffset.x / rollRange, -1f, 1f);  // left/right
        float rawYaw   = Mathf.Clamp(localOffset.y / yawRange, -1f, 1f);   // twist

        // 4. Deadzone
        rawPitch = ApplyDeadzone(rawPitch);
        rawRoll  = ApplyDeadzone(rawRoll);
        rawYaw   = ApplyDeadzone(rawYaw);

        // 5. Response curve
        Pitch = ApplyCurve(rawPitch);
        Roll  = ApplyCurve(rawRoll);
        Yaw   = ApplyCurve(rawYaw);

        DebugIfNeeded(
            "[HOTAS] Update\n" +
            $"  worldOffset: {worldOffset}\n" +
            $"  localOffset: {localOffset}\n" +
            $"  rawPitch: {rawPitch:F3}  rawRoll: {rawRoll:F3}  rawYaw: {rawYaw:F3}\n" +
            $"  Pitch: {Pitch:F3}  Roll: {Roll:F3}  Yaw: {Yaw:F3}\n" +
            $"--- Ship Frame ---\n" +
            $"  Ship Forward: {flightFrame.forward}\n" +
            $"  Ship Up:      {flightFrame.up}\n" +
            $"  Ship Right:   {flightFrame.right}"
        );
    }

    // --- Utility functions ---------------------------------------------------

    private float ApplyDeadzone(float v)
    {
        if (Mathf.Abs(v) < deadzone) return 0f;
        return Mathf.InverseLerp(deadzone, 1f, Mathf.Abs(v)) * Mathf.Sign(v);
    }

    private float ApplyCurve(float v)
    {
        if (v == 0f) return 0f;
        return Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), curve);
    }

    private void ZeroOutputs()
    {
        Pitch = 0f;
        Roll  = 0f;
        Yaw   = 0f;
    }

    private void DebugIfNeeded(string msg)
    {
        if (debug)
            Debug.Log(msg);
    }
}
