using UnityEngine;

[DisallowMultipleComponent]
public class OctaTruckDriver : VehicleRouteDriver
{
    [Header("Octa Auto Setup")]
    [Tooltip("Try to auto-fill wheel visuals from children using keyword matching.")]
    public bool autoAssignWheelsFromChildren = true;
    public string[] wheelNameKeywords = { "wheel", "tyre", "tire" };
    [Min(0.01f)] public float defaultWheelRadius = 0.52f;
    [Tooltip("If true, front 4 auto-assigned wheels get steering visual yaw.")]
    public bool steerFrontFourWheels = true;

    protected override void Reset()
    {
        base.Reset();
        ApplyOctaDefaults();
        TryAutoAssignWheels();
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        if (autoAssignWheelsFromChildren && (wheelVisuals == null || wheelVisuals.Count == 0))
            TryAutoAssignWheels();
    }

    void ApplyOctaDefaults()
    {
        cruiseSpeed = 10f;
        maxSpeed = 18f;
        acceleration = 5f;
        deceleration = 8f;
        turnRateDegrees = 85f;
        waypointReachDistance = 3.5f;

        terrainProbeHeight = 4.5f;
        terrainProbeDistance = 14f;
        groundOffset = 0.12f;
        maxTraversableSlope = 40f;

        uphillSlowdownStrength = 0.65f;
        steepClimbExtraSlowdown = 0.3f;

        minEnginePitch = 0.8f;
        maxEnginePitch = 1.35f;
        minEngineVolume = 0.2f;
        maxEngineVolume = 0.9f;
    }

    [ContextMenu("Auto Assign Octa Wheels")]
    public void TryAutoAssignWheels()
    {
        wheelVisuals.Clear();
        Transform[] children = GetComponentsInChildren<Transform>(true);
        int wheelCount = 0;

        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t == transform)
                continue;

            string n = t.name.ToLowerInvariant();
            if (!IsWheelName(n))
                continue;

            WheelVisual wheel = new WheelVisual();
            wheel.id = t.name;
            wheel.wheel = t;
            wheel.radius = defaultWheelRadius;
            wheel.axleLocalAxis = Vector3.right;
            wheel.applySteerYaw = steerFrontFourWheels && wheelCount < 4;
            wheelVisuals.Add(wheel);
            wheelCount++;
        }
    }

    bool IsWheelName(string lowerName)
    {
        if (wheelNameKeywords == null || wheelNameKeywords.Length == 0)
            return lowerName.Contains("wheel");

        for (int i = 0; i < wheelNameKeywords.Length; i++)
        {
            string keyword = wheelNameKeywords[i];
            if (string.IsNullOrWhiteSpace(keyword))
                continue;
            if (lowerName.Contains(keyword.ToLowerInvariant()))
                return true;
        }
        return false;
    }
}
