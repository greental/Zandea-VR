using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Minimal flight model for testing: keyboard-only, no HOTAS/VR.
/// Body-relative pitch/roll (pitch = ship right, roll = ship forward) same as FlightController.
/// Use in an empty scene with a cube + Rigidbody + this script; camera can be static or follow.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MinimalFlight : MonoBehaviour
{
    [Header("Flight model mode (switch to test)")]
    [Tooltip("DirectTorque / InertialDrift / TargetOrientation — same as FlightController.")]
    public FlightModelMode flightModelMode = FlightModelMode.DirectTorque;
    public Transform flightTarget;
    public float targetTorqueGain = 200f;
    public float targetAngularDamping = 18f;
    public float targetPitchRollRate = 120f;
    public float targetTorqueMax = 150f;

    [Header("Rotation (same math as FlightController)")]
    public float pitchTorque = 25f;
    public float rollTorque = 38f;
    [Header("Damping")]
    public float angularDamp = 2.5f;
    public float angularDampNeutral = 12f;
    [Range(0f, 1f)] public float inertialDriftLinearDampScale = 0.12f;
    [Range(0f, 1f)] public float inertialDriftAngularDampScale = 0.08f;
    [Range(0f, 1f)] public float inertialDriftAngularNeutralScale = 0.12f;
    [Header("Thrust (optional)")]
    public float thrust = 20f;
    public float linearDamp = 2.5f;
    [Header("Diagnostics (validate pitch behaviour)")]
    [Tooltip("Log pitch state to file when W/S held; inspect when pitch seems to stop.")]
    public bool logPitchToFile = true;
    [Tooltip("Show live pitch axis, torque, angular vel on screen.")]
    public bool showOnScreenDebug = true;

    static string LogPath => Path.Combine(Application.dataPath, "MyAssets", "scripts", "minimal_flight_pitch_log.txt");
    static List<string> s_logLines = new List<string>();
    const int k_MaxLogLines = 400;

    Rigidbody rb;
    float pitchInput;
    float rollInput;
    float throttleInput;
    // Last computed values for on-screen display
    Vector3 lastPitchAxis;
    float lastPitchTorqueMag;
    bool lastNearVertical;
    float lastNoseUpDeg;
    Quaternion targetOrientation;
    bool targetInitialized;

    void EnsureFlightTarget()
    {
        if (flightTarget != null) return;
        var go = new GameObject("FlightTarget");
        go.transform.SetParent(rb.transform.parent);
        go.transform.position = rb.position;
        go.transform.rotation = rb.rotation;
        flightTarget = go.transform;
        targetInitialized = false;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.constraints = RigidbodyConstraints.None;
        }
    }

    void Update()
    {
        // Keyboard: pitch = W/S (pull back = nose up in cockpit), roll = A/D
        pitchInput = 0f;
        if (Input.GetKey(KeyCode.W)) pitchInput += 1f;
        if (Input.GetKey(KeyCode.S)) pitchInput -= 1f;
        rollInput = 0f;
        if (Input.GetKey(KeyCode.D)) rollInput += 1f;
        if (Input.GetKey(KeyCode.A)) rollInput -= 1f;
        throttleInput = 0f;
        if (Input.GetKey(KeyCode.E)) throttleInput += 1f;
        if (Input.GetKey(KeyCode.Q)) throttleInput -= 1f;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        float pitch = Mathf.Clamp(pitchInput, -1f, 1f);
        float roll = Mathf.Clamp(rollInput, -1f, 1f);

        // --- Thrust along ship forward ---
        if (Mathf.Abs(throttleInput) > 0.01f)
            rb.AddForce(transform.forward * throttleInput * thrust, ForceMode.Acceleration);
        bool isInertialDrift = flightModelMode == FlightModelMode.InertialDrift;
        float linearDampEffective = isInertialDrift ? linearDamp * inertialDriftLinearDampScale : linearDamp;
        if (rb.linearVelocity.magnitude > 0.1f)
            rb.AddForce(-rb.linearVelocity * linearDampEffective, ForceMode.Acceleration);

        Vector3 shipRight = rb.transform.right;
        Vector3 shipFwd = rb.transform.forward;
        Vector3 torqueWorld;

        if (flightModelMode == FlightModelMode.TargetOrientation)
        {
            EnsureFlightTarget();
            flightTarget.position = rb.position;
            float dt = Time.fixedDeltaTime;
            if (!targetInitialized)
            {
                targetOrientation = rb.rotation;
                targetInitialized = true;
            }
            Quaternion delta = Quaternion.AngleAxis(pitch * targetPitchRollRate * dt, shipRight)
                * Quaternion.AngleAxis(-roll * targetPitchRollRate * dt, shipFwd);
            targetOrientation = targetOrientation * delta;
            flightTarget.rotation = targetOrientation;
            Quaternion desired = targetOrientation;
            Quaternion current = rb.rotation;
            Quaternion err = desired * Quaternion.Inverse(current);
            err.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            if (axis.sqrMagnitude > 0.0001f)
            {
                axis.Normalize();
                float angleRad = angleDeg * Mathf.Deg2Rad;
                float angVelAlongAxis = Vector3.Dot(rb.angularVelocity, axis);
                float torqueMagnitude = angleRad * targetTorqueGain - angVelAlongAxis * targetAngularDamping;
                torqueMagnitude = Mathf.Clamp(torqueMagnitude, -targetTorqueMax, targetTorqueMax);
                torqueWorld = axis * torqueMagnitude;
            }
            else
                torqueWorld = Vector3.zero;
            lastPitchAxis = shipRight;
        }
        else
        {
            torqueWorld = pitch * pitchTorque * shipRight;
            torqueWorld += (-roll) * rollTorque * shipFwd;
            lastPitchAxis = shipRight;
        }

        bool nearVertical = Mathf.Abs(shipFwd.y) > 0.99f;
        lastPitchTorqueMag = torqueWorld.magnitude;

        rb.AddTorque(torqueWorld, ForceMode.Acceleration);

        // --- Diagnostics ---
        lastNearVertical = nearVertical;
        lastNoseUpDeg = Mathf.Asin(Mathf.Clamp(shipFwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        if (logPitchToFile && Mathf.Abs(pitch) > 0.01f)
        {
            float pitchAngVel = Vector3.Dot(rb.angularVelocity, lastPitchAxis);
            string line = $"{Time.time:F2} | noseUp={lastNoseUpDeg:F1}° fwd.y={shipFwd.y:F3} nearVert={nearVertical} pitchAxis=({lastPitchAxis.x:F2},{lastPitchAxis.y:F2},{lastPitchAxis.z:F2}) pitchTorqueMag={lastPitchTorqueMag:F1} angVelDotPitch={pitchAngVel:F3} angVelMag={rb.angularVelocity.magnitude:F3} mode={flightModelMode}";
            s_logLines.Add(line);
            if (s_logLines.Count > k_MaxLogLines) s_logLines.RemoveAt(0);
            try { File.WriteAllText(LogPath, string.Join("\n", s_logLines)); }
            catch (System.Exception ex) { Debug.LogWarning("MinimalFlight log: " + ex.Message); }
        }

        // --- Angular damping (lighter in TargetOrientation; PD handles smoothness there) ---
        bool neutral = Mathf.Abs(pitch) < 0.01f && Mathf.Abs(roll) < 0.01f;
        float damp = flightModelMode == FlightModelMode.TargetOrientation ? angularDamp * 0.5f : (neutral ? angularDampNeutral : angularDamp);
        if (isInertialDrift)
            damp *= neutral ? inertialDriftAngularNeutralScale : inertialDriftAngularDampScale;
        if (rb.angularVelocity.sqrMagnitude > 0.01f)
            rb.AddTorque(-rb.angularVelocity * damp, ForceMode.Acceleration);
    }

    void OnGUI()
    {
        if (!showOnScreenDebug || rb == null) return;
        float pitch = Mathf.Clamp(pitchInput, -1f, 1f);
        string nearVertStr = lastNearVertical ? " [NEAR VERTICAL]" : "";
        float angVelDotPitch = Vector3.Dot(rb.angularVelocity, lastPitchAxis);
        GUILayout.BeginArea(new Rect(10, 10, 420, 220));
        GUILayout.Label($"Nose up: {lastNoseUpDeg:F1}° (fwd.y={rb.transform.forward.y:F3}){nearVertStr}");
        GUILayout.Label($"Pitch axis: ({lastPitchAxis.x:F2}, {lastPitchAxis.y:F2}, {lastPitchAxis.z:F2})");
        GUILayout.Label($"Pitch torque mag: {lastPitchTorqueMag:F1}  (input pitch={pitch:F2})");
        GUILayout.Label($"Angular vel mag: {rb.angularVelocity.magnitude:F3}  (along pitch axis: {angVelDotPitch:F3})");
        GUILayout.Label($"Mode: {flightModelMode}  Log: {(logPitchToFile ? LogPath : "off")}");
        GUILayout.EndArea();
    }

    /// <summary>Call from UI or inspector for debugging.</summary>
    public void SetPitchRoll(float pitch, float roll)
    {
        pitchInput = pitch;
        rollInput = roll;
    }
}
