using UnityEngine;
using System.IO;

public enum FlightModelMode
{
    DirectTorque,
    InertialDrift,
    TargetOrientation
}

public class FlightController : MonoBehaviour
{
    [Header("References")]
    public Rigidbody rb;
    public HOTASController hotas;
    public LandingGearController landingGear;

    [Header("Flight model mode (switch to test)")]
    [Tooltip("All modes use body-relative axes: pitch = ship right, roll = ship forward. DirectTorque = raw torque with standard damping. InertialDrift = direct torque with very low damping (coasts/drifts). TargetOrientation = desired = current + body pitch/roll delta, then PD toward it.")]
    public FlightModelMode flightModelMode = FlightModelMode.DirectTorque;
    [Tooltip("TargetOrientation only: optional. If null, a child 'FlightTarget' is created.")]
    public Transform flightTarget;
    [Tooltip("TargetOrientation only: torque gain toward target rotation (P term).")]
    public float targetTorqueGain = 200f;
    [Tooltip("TargetOrientation only: damping along error axis for smoother follow (D term).")]
    public float targetAngularDamping = 18f;
    [Tooltip("TargetOrientation only: pitch/roll rate applied to target (deg/s per unit input).")]
    public float targetPitchRollRate = 120f;
    [Tooltip("TargetOrientation only: cap torque magnitude to avoid spikes.")]
    public float targetTorqueMax = 150f;

    [Header("Debug log (file in project for inspection)")]
    [Tooltip("Log flight state to a file when ship pitch is near 90° or stick has input.")]
    public bool logToFile = true;
    [Tooltip("Max lines to keep in the log file (old lines dropped).")]
    public int logMaxLines = 600;

    static string LogPath => Path.Combine(Application.dataPath, "MyAssets", "scripts", "flight_debug.log");
    static System.Collections.Generic.List<string> s_logLines = new System.Collections.Generic.List<string>();

    [Header("Thrust Settings")]
    public float maxForwardThrust = 40f;
    public float maxStrafeThrust = 25f;
    public float maxVerticalThrust = 25f;

    [Header("Reverse Thrust Scaling")]
    [Range(0f, 1f)]
    public float reverseThrustFactor = 0.35f; 

    [Header("Rotation Settings")]
    public float pitchTorque = 25f;
    public float rollTorque = 38f;
    public float yawTorque = 20f;

    [Header("Damping")]
    public float linearDamp = 2.5f;
    [Tooltip("When throttle/lever is at zero, use this stronger damping so residual forward/horizontal drift stops completely.")]
    public float linearDampWhenThrustZero = 10f;
    [Tooltip("Base angular drag. When stick is neutral, effective damping is stronger (counter-thrusters).")]
    public float angularDamp = 2.5f;
    [Tooltip("Extra angular damping when stick is released so rotation stops quickly. (STOP WHEN NOT GRABBED: was 12, now 18)")]
    public float angularDampStickNeutral = 18f;

    [Header("Inertial Drift Mode (low damping profile)")]
    [Tooltip("Scales linear damping while in InertialDrift mode. 0 = no linear damping, 1 = same as normal.")]
    [Range(0f, 1f)]
    public float inertialDriftLinearDampScale = 0.12f;
    [Tooltip("Scales angular damping while stick is active in InertialDrift mode. 0 = no angular damping, 1 = same as normal.")]
    [Range(0f, 1f)]
    public float inertialDriftAngularDampScale = 0.08f;
    [Tooltip("Scales angular damping while stick is neutral in InertialDrift mode.")]
    [Range(0f, 1f)]
    public float inertialDriftAngularNeutralScale = 0.12f;

    [Header("Ship Inertia")]
    [Tooltip("Time in seconds for forward/back thrust to ramp from 0 to full. Movement ramps up instead of snapping when you move the lever.")]
    [Range(0.1f, 1.5f)]
    public float thrustRampTime = 0.4f;
    [Tooltip("Time in seconds for strafe/vertical (horizontal) thrust to ramp. Same feel as forward thrust.")]
    [Range(0.1f, 1.5f)]
    public float horizontalThrustRampTime = 0.4f;
    [Tooltip("When stick is neutral, angular damping is scaled by this so roll/pitch coast a bit before stopping. Lower = more drift. Adjustable in-game via FlightInertiaSlider.")]
    [Range(0.2f, 1f)]
    public float stickNeutralAngularDriftScale = 0.45f;

    [Header("Flight Limits")]
    public float maxSpeed = 120f;
    [Tooltip("If true, deployed or moving landing gear enforces a lower max speed.")]
    public bool limitSpeedWhenGearDeployed = true;
    [Tooltip("Max ship speed (m/s) while landing gear is deployed or in transition.")]
    public float maxSpeedWithLandingGear = 10f;
    [Tooltip("Forward thrust scale (0-1) when landing gear is deployed. Also drives throttle bar fill and engine audio for forward thrust.")]
    [Range(0.05f, 1f)]
    public float landingGearThrustScale = 0.2f;

    [Header("Atmosphere / Wind drift")]
    [Tooltip("Set true when inside an AtmosphereVolume (or for testing). When true and speed is below threshold, a small drift force is applied like wind.")]
    public bool inAtmosphere;
    [Tooltip("Wind drift only applies when linear speed is below this (m/s).")]
    public float windDriftSpeedThreshold = 2.5f;
    [Tooltip("Max magnitude of wind drift force (acceleration).")]
    public float windDriftForceMax = 0.4f;
    [Tooltip("Scale for Perlin noise time so wind direction/strength varies slowly.")]
    public float windDriftNoiseScale = 0.3f;
    [Tooltip("Max roll torque from wind when stationary in atmosphere (gentle buffeting).")]
    public float windDriftRollTorqueMax = 0.2f;

    [Header("Debugging")]
    public bool verboseDebug = false;

    Quaternion targetOrientation;  // TargetOrientation mode: integrated desired orientation (body-relative)
    bool targetInitialized;
    float smoothedThrust;  // ramps toward target throttle for forward/back
    float smoothedStrafe, smoothedVertical;  // ramp for horizontal (strafe + vertical) thrust

    /// <summary>Landing gear down or moving: forward thrust and speed cap apply.</summary>
    public bool IsLandingGearLimitingForwardThrustAndSpeed()
    {
        if (!limitSpeedWhenGearDeployed) return false;
        if (landingGear == null)
            landingGear = GetComponentInChildren<LandingGearController>();
        return landingGear != null && (landingGear.gearDeployed || landingGear.IsMoving);
    }

    /// <summary>Scales forward throttle for HUD bar and engine audio (1 = full bar at max lever; e.g. 0.2 with gear down).</summary>
    public float GetForwardThrottleAuthority()
    {
        return IsLandingGearLimitingForwardThrustAndSpeed() ? Mathf.Clamp(landingGearThrustScale, 0.01f, 1f) : 1f;
    }

    /// <summary>Effective speed cap (m/s) including HOTAS speed strength and landing-gear limit.</summary>
    public float GetEffectiveMaxSpeed()
    {
        float strength = hotas != null ? Mathf.Max(0.01f, hotas.speedStrength) : 1f;
        float cap = Mathf.Max(0.01f, maxSpeed * strength);
        if (IsLandingGearLimitingForwardThrustAndSpeed())
            cap = Mathf.Min(cap, Mathf.Max(0.1f, maxSpeedWithLandingGear));
        return cap;
    }

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

    void FixedUpdate()
    {
        if (rb == null || hotas == null) return;

        bool gearSpeedLimited = IsLandingGearLimitingForwardThrustAndSpeed();
        float thrustScaleGear = gearSpeedLimited ? Mathf.Clamp(landingGearThrustScale, 0.01f, 1f) : 1f;

        // --- INPUTS ---
        float throttleInput = hotas.Throttle;      
        float thrust = throttleInput;     // -1..1 → 0..1
        float pitch = hotas.pitch;
        float roll  = hotas.roll;
        float yaw   = hotas.yaw;

        // --- THRUST (ramp so movement builds up instead of snapping when lever moves) ---
        float targetThrust = thrust >= 0f
            ? thrust                                   // normal full forward
            : thrust * reverseThrustFactor;             // weaker reverse
        float rampRate = thrustRampTime > 0.001f ? (1f / thrustRampTime) : 1000f;
        smoothedThrust = Mathf.MoveTowards(smoothedThrust, targetThrust, rampRate * Time.fixedDeltaTime);
        if (Mathf.Abs(targetThrust) < 0.01f && Mathf.Abs(smoothedThrust) < 0.02f)
            smoothedThrust = 0f;  // snap to zero so ship stops completely when near center

        float thrustScale = hotas.speedStrength * thrustScaleGear;
        rb.AddForce(rb.transform.forward * smoothedThrust * maxForwardThrust * thrustScale, ForceMode.Acceleration);

        // --- STRAFE / VERTICAL (right controller stick, ship-relative; ramp for inertia) ---
        float strafe = hotas.Strafe;
        float vertical = hotas.Vertical;
        float rampH = horizontalThrustRampTime > 0.001f ? (1f / horizontalThrustRampTime) : 1000f;
        smoothedStrafe = Mathf.MoveTowards(smoothedStrafe, strafe, rampH * Time.fixedDeltaTime);
        smoothedVertical = Mathf.MoveTowards(smoothedVertical, vertical, rampH * Time.fixedDeltaTime);
        if (Mathf.Abs(strafe) < 0.02f && Mathf.Abs(smoothedStrafe) < 0.02f) smoothedStrafe = 0f;
        if (Mathf.Abs(vertical) < 0.02f && Mathf.Abs(smoothedVertical) < 0.02f) smoothedVertical = 0f;

        // Strafe / vertical: not scaled by landing gear (gear only limits main forward thrust and top speed).
        rb.AddForce(rb.transform.right * (smoothedStrafe * maxStrafeThrust), ForceMode.Acceleration);
        rb.AddForce(rb.transform.up * (smoothedVertical * maxVerticalThrust), ForceMode.Acceleration);

        // --- ROTATION: body-relative (VTOL/aircraft style) ---
        // Pitch = nose up/down in cockpit → torque around ship RIGHT. Roll = bank → torque around ship FORWARD. Yaw = nose left/right → torque around ship UP.
        Vector3 shipRight = rb.transform.right;
        Vector3 shipFwd = rb.transform.forward;
        Vector3 shipUp = rb.transform.up;
        Vector3 torqueWorld = Vector3.zero;

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
            bool stickNeutralForTarget = Mathf.Abs(pitch) < 0.01f && Mathf.Abs(roll) < 0.01f && Mathf.Abs(yaw) < 0.01f;
            if (stickNeutralForTarget)
            {
                // Like throttle at zero = no thrust: stick neutral = no rotation demand. Snap target to current so we stop applying correction torque and rotation can stop.
                targetOrientation = rb.rotation;
            }
            else
            {
                // Integrate target in body frame: pitch (right), roll (forward), yaw (up). Yaw rate scaled by hotas.yawTorqueScale.
                float yawRate = targetPitchRollRate * hotas.yawStrength;
                Quaternion delta = Quaternion.AngleAxis(pitch * targetPitchRollRate * dt, shipRight)
                    * Quaternion.AngleAxis(-roll * targetPitchRollRate * dt, shipFwd)
                    * Quaternion.AngleAxis(yaw * yawRate * dt, shipUp);
                targetOrientation = targetOrientation * delta;
            }
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
        }
        else
        {
            // DirectTorque / InertialDrift: same body-relative control axes.
            float yawTorqueScaled = yawTorque * hotas.yawStrength;
            torqueWorld = pitch * pitchTorque * shipRight;
            if (Mathf.Abs(yaw * yawTorqueScaled) > 0.01f)
                torqueWorld += yaw * yawTorqueScaled * shipUp;
            torqueWorld += (-roll) * rollTorque * shipFwd;
        }

        rb.AddTorque(torqueWorld, ForceMode.Acceleration);

        // --- DEBUG LOG (near 90° pitch or when stick used) ---
        if (logToFile && rb != null)
        {
            Vector3 fwd = rb.transform.forward;
            float noseUpDeg = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 euler = rb.transform.eulerAngles;
            bool logNearVertical = noseUpDeg >= 60f || noseUpDeg <= -60f;
            bool stickActive = Mathf.Abs(pitch) > 0.01f || Mathf.Abs(roll) > 0.01f;
            if (logNearVertical || stickActive)
            {
                string line = $"{Time.time:F2} | noseUp={noseUpDeg:F1}° euler=({euler.x:F1},{euler.y:F1},{euler.z:F1}) pitchIn={pitch:F2} rollIn={roll:F2} torqueW=({torqueWorld.x:F1},{torqueWorld.y:F1},{torqueWorld.z:F1}) fwd=({fwd.x:F3},{fwd.y:F3},{fwd.z:F3})";
                s_logLines.Add(line);
                if (s_logLines.Count > logMaxLines)
                    s_logLines.RemoveAt(0);
                try
                {
                    File.WriteAllText(LogPath, string.Join("\n", s_logLines));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("FlightController log write failed: " + ex.Message);
                }
            }
        }

        // --- LINEAR DAMPING (stronger when lever at zero to kill residual forward/horizontal drift) ---
        bool isInertialDrift = flightModelMode == FlightModelMode.InertialDrift;
        bool thrustZero = Mathf.Abs(smoothedThrust) < 0.02f;
        float effectiveLinearDamp = thrustZero ? linearDampWhenThrustZero : linearDamp;
        if (isInertialDrift)
            effectiveLinearDamp *= inertialDriftLinearDampScale;
        if (rb.linearVelocity.magnitude > 0.1f)
            rb.AddForce(-rb.linearVelocity * effectiveLinearDamp, ForceMode.Acceleration);
        if (!isInertialDrift && thrustZero && rb.linearVelocity.magnitude < 0.2f)
            rb.linearVelocity = Vector3.zero;  // snap to full stop when nearly stopped and lever at zero

        // --- ANGULAR DAMPING: when stick neutral use scaled damping so rotation coasts a bit (inertia) ---
        bool stickNeutral = Mathf.Abs(pitch) < 0.01f && Mathf.Abs(roll) < 0.01f && Mathf.Abs(yaw) < 0.01f;
        float damp = stickNeutral
            ? angularDampStickNeutral * stickNeutralAngularDriftScale
            : (flightModelMode == FlightModelMode.TargetOrientation ? angularDamp * 0.5f : angularDamp);
        if (isInertialDrift)
        {
            damp *= stickNeutral ? inertialDriftAngularNeutralScale : inertialDriftAngularDampScale;
        }
        // Use a very low threshold so we keep damping until rotation is negligible (was 0.01f = ~0.1 rad/s, so we stopped damping too early and ship kept bleeding)
        float angVelSq = rb.angularVelocity.sqrMagnitude;
        if (angVelSq > 1e-6f)
        {
            if (logToFile)
            {
                float angVelMag = rb.angularVelocity.magnitude;
                string line = $"{Time.time:F2} | angular damp applied damp={damp:F1} angVelMag={angVelMag:F3} stickNeutral={stickNeutral}";
                s_logLines.Add(line);
                if (s_logLines.Count > logMaxLines)
                    s_logLines.RemoveAt(0);
                try
                {
                    File.WriteAllText(LogPath, string.Join("\n", s_logLines));
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("FlightController log write failed: " + ex.Message);
                }
            }
            rb.AddTorque(-rb.angularVelocity * damp, ForceMode.Acceleration);
        }

        // --- SPEED LIMIT ---
        float speedLimit = maxSpeed * hotas.speedStrength;
        if (gearSpeedLimited)
            speedLimit = Mathf.Min(speedLimit, Mathf.Max(0.1f, maxSpeedWithLandingGear));
        if (rb.linearVelocity.magnitude > speedLimit)
            rb.linearVelocity = rb.linearVelocity.normalized * speedLimit;

        // --- WIND DRIFT (when stationary in atmosphere: linear drift + slight roll + self-leveling) ---
        if (inAtmosphere && rb.linearVelocity.sqrMagnitude < windDriftSpeedThreshold * windDriftSpeedThreshold)
        {
            float t = Time.time * windDriftNoiseScale;
            if (windDriftForceMax > 0f)
            {
                float nx = Mathf.PerlinNoise(t, 0f) - 0.5f;
                float nz = Mathf.PerlinNoise(0f, t + 100f) - 0.5f;
                float ny = (Mathf.PerlinNoise(t + 50f, t) - 0.5f) * 0.5f;
                Vector3 wind = new Vector3(nx, ny, nz).normalized * windDriftForceMax;
                rb.AddForce(wind, ForceMode.Acceleration);
            }
            if (windDriftRollTorqueMax > 0f)
            {
                float rollNoise = (Mathf.PerlinNoise(t + 200f, t + 77f) - 0.5f) * 2f;
                rb.AddTorque(rb.transform.forward * (rollNoise * windDriftRollTorqueMax), ForceMode.Acceleration);
            }
        }
    }
}
