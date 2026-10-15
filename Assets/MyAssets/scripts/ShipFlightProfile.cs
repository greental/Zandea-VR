using UnityEngine;

/// <summary>
/// Single source of truth for Razara-class ship flight feel. Assign one asset to
/// <see cref="ShipFlightProfileBinder"/> on each ship prefab root so every scene matches
/// without per-scene number tweaks.
/// </summary>
[CreateAssetMenu(fileName = "ShipFlightProfile", menuName = "MyAssets/Ship Flight Profile", order = 0)]
public class ShipFlightProfile : ScriptableObject
{
    [Header("HOTAS joystick (position + twist)")]
    public float reachPitchMeters = 0.08f;
    public float reachRollMeters = 0.08f;
    [Range(0f, 0.5f)] public float deadzone = 0.07f;
    public float responseCurve = 1f;
    [Range(10f, 90f)] public float maxPitchAngle = 20f;
    [Range(10f, 90f)] public float maxRollAngle = 20f;
    public float recenterSpeed = 8f;
    [Range(15f, 90f)] public float maxYawAngle = 45f;
    public Vector3 gripTwistAxis = new Vector3(1f, 0f, 0f);
    public bool invertPitch = true;
    public bool invertRoll = false;
    public bool invertYaw = false;

    [Header("HOTAS controller (gains)")]
    [Range(0f, 4f)] public float pitchStrength = 2f;
    [Range(0f, 4f)] public float rollStrength = 1f;
    [Range(0.5f, 4f)] public float yawStrength = 3f;
    [Range(0.25f, 4f)] public float speedStrength = 2f;
    [Range(0f, 0.4f)] public float rightStickDeadzone = 0.15f;
    [Range(0.02f, 0.25f)] public float throttleSnapDeadzone = 0.12f;
    [Range(0.5f, 0.95f)] public float dominantAxisThreshold = 0.7f;
    [Range(0.1f, 0.5f)] public float crossAxisThreshold = 0.25f;
    public bool showInputMetrics = false;

    [Header("Flight controller (optional — 0 = leave component / prefab value)")]
    [Tooltip("If > 0, sets FlightController.maxSpeed.")]
    public float maxSpeedOverride = 0f;
    [Tooltip("If > 0, sets FlightController.maxForwardThrust.")]
    public float maxForwardThrustOverride = 0f;
}
