using UnityEngine;

/// <summary>
/// Assign a <see cref="ShipFlightProfile"/> asset on the ship root (same hierarchy as
/// <see cref="Rigidbody"/>). Applies tuning in <see cref="Awake"/> so all scenes and prefab
/// instances share one set of numbers. Terrain vs other scenes: no difference as long as the
/// same profile + binder are used.
/// </summary>
[DefaultExecutionOrder(-400)]
public class ShipFlightProfileBinder : MonoBehaviour
{
    public ShipFlightProfile profile;

    [Tooltip("If null, uses GetComponentInChildren<Rigidbody>() on this object.")]
    public Rigidbody shipRigidbody;

    [Tooltip("If off, only validates profile is assigned — use for temporary debugging.")]
    public bool applyOnAwake = true;

    void Awake()
    {
        if (applyOnAwake)
            ApplyNow();
    }

    [ContextMenu("Apply Profile Now")]
    public void ApplyNow()
    {
        if (profile == null)
        {
            Debug.LogWarning("[ShipFlightProfileBinder] No ShipFlightProfile assigned on '" + name + "'.", this);
            return;
        }

        var rb = shipRigidbody != null ? shipRigidbody : GetComponentInChildren<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning("[ShipFlightProfileBinder] No Rigidbody under '" + name + "'.", this);
            return;
        }

        ApplyToHierarchy(rb.gameObject, profile);
    }

    public static void ApplyToHierarchy(GameObject shipRoot, ShipFlightProfile p)
    {
        if (shipRoot == null || p == null) return;

        foreach (var j in shipRoot.GetComponentsInChildren<HOTASJoystick>(true))
            ApplyJoystick(j, p);

        foreach (var h in shipRoot.GetComponentsInChildren<HOTASController>(true))
            ApplyHotas(h, p);

        foreach (var f in shipRoot.GetComponentsInChildren<FlightController>(true))
            ApplyFlight(f, p);
    }

    static void ApplyJoystick(HOTASJoystick j, ShipFlightProfile p)
    {
        if (j == null) return;
        j.reachPitchMeters = p.reachPitchMeters;
        j.reachRollMeters = p.reachRollMeters;
        j.deadzone = p.deadzone;
        j.responseCurve = p.responseCurve;
        j.maxPitchAngle = p.maxPitchAngle;
        j.maxRollAngle = p.maxRollAngle;
        j.recenterSpeed = p.recenterSpeed;
        j.maxYawAngle = p.maxYawAngle;
        j.gripTwistAxis = p.gripTwistAxis;
        j.invertPitch = p.invertPitch;
        j.invertRoll = p.invertRoll;
        j.invertYaw = p.invertYaw;
    }

    static void ApplyHotas(HOTASController h, ShipFlightProfile p)
    {
        if (h == null) return;
        h.pitchStrength = p.pitchStrength;
        h.rollStrength = p.rollStrength;
        h.yawStrength = p.yawStrength;
        h.speedStrength = p.speedStrength;
        h.rightStickDeadzone = p.rightStickDeadzone;
        h.throttleSnapDeadzone = p.throttleSnapDeadzone;
        h.dominantAxisThreshold = p.dominantAxisThreshold;
        h.crossAxisThreshold = p.crossAxisThreshold;
        h.showInputMetrics = p.showInputMetrics;
    }

    static void ApplyFlight(FlightController f, ShipFlightProfile p)
    {
        if (f == null) return;
        if (p.maxSpeedOverride > 0.01f)
            f.maxSpeed = p.maxSpeedOverride;
        if (p.maxForwardThrustOverride > 0.01f)
            f.maxForwardThrust = p.maxForwardThrustOverride;
    }
}
