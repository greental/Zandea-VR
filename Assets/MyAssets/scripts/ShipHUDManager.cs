using UnityEngine;

/// <summary>
/// Central HUD controller: owns anchor points and ship system references.
/// All HUD elements are world-space and ship-locked. See docs/HUD_ARCHITECTURE.md.
/// <see cref="AltitudeIndicatorHUD"/> is on the <b>same GameObject</b> so you can edit it in the Inspector (not only at runtime).
/// </summary>
[RequireComponent(typeof(AltitudeIndicatorHUD))]
public class ShipHUDManager : MonoBehaviour
{
    [Header("Ship systems (bind in inspector)")]
    public FlightController flight;
    public HOTASController hotas;

    [Header("HUD anchor points (optional: leave null to auto-create under HUD root)")]
    public Transform canopyHUD;
    public Transform leftScreen;
    public Transform rightScreen;
    public Transform warningBanner;

    [Header("Optional: parent for all HUD anchors (default: this transform)")]
    public Transform hudRoot;

    [Header("Debug")]
    [Tooltip("Show a test warning on the banner so you can verify it works. Requires WarningBannerDisplay on the ship.")]
    public bool testWarning;

    Transform _createdRoot;

    void Start()
    {
        if (flight == null)
            flight = GetComponentInChildren<FlightController>();
        if (hotas == null && flight != null)
            hotas = flight.hotas;

        EnsureAnchors();
        EnsureWarningMessageSystem();
    }

    void EnsureWarningMessageSystem()
    {
        var wms = GetComponent<WarningMessageSystem>();
        if (wms == null)
            wms = FindFirstObjectByType<WarningMessageSystem>();
        if (wms == null)
            wms = gameObject.AddComponent<WarningMessageSystem>();
        wms.testWarning = testWarning;
    }

    void EnsureAnchors()
    {
        Transform root = hudRoot != null ? hudRoot : transform;
        if (canopyHUD == null) canopyHUD = FindOrCreateChild(root, "CanopyHUD");
        if (leftScreen == null) leftScreen = FindOrCreateChild(root, "LeftScreen");
        if (rightScreen == null) rightScreen = FindOrCreateChild(root, "RightScreen");
        if (warningBanner == null)
        {
            warningBanner = root.Find("WarningBanner");
            if (warningBanner == null) warningBanner = root.Find("warningBanner");
            if (warningBanner == null) warningBanner = FindInDescendants(root, "warningBanner");
            if (warningBanner == null) warningBanner = FindInDescendants(root, "WarningBanner");
            if (warningBanner == null) warningBanner = FindOrCreateChild(root, "WarningBanner");
        }
    }

    static Transform FindInDescendants(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindInDescendants(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    void LateUpdate()
    {
        // Don't overwrite WarningMessageSystem.testWarning every frame – only sync in Start.
        // That way the debug checkbox stays on if you enable it at runtime.
        RefreshHUDData();
    }

    void RefreshHUDData()
    {
        // Stub: child components (e.g. FlightDisplayBinder) will read from flight/hotas
        // and update their own UI. Manager can expose cached values here if needed.
    }

    // --- Convenience for other HUD components ---

    public float GetSpeed() => flight != null && flight.rb != null ? flight.rb.linearVelocity.magnitude : 0f;
    /// <summary>Current effective speed cap used by FlightController (includes HOTAS speed strength).</summary>
    public float GetMaxSpeed()
    {
        if (flight == null)
            return 1f;
        return flight.GetEffectiveMaxSpeed();
    }
    /// <summary>Signed speed based on throttle direction (positive forward, negative reverse).</summary>
    public float GetSignedSpeed()
    {
        if (flight == null || flight.rb == null)
            return 0f;

        float magnitude = flight.rb.linearVelocity.magnitude;
        float sign = (hotas != null && hotas.throttle < -0.01f) ? -1f : 1f;
        return magnitude * sign;
    }
    public float GetThrottle() => hotas != null ? hotas.throttle : 0f;
    public Rigidbody GetRigidbody() => flight != null ? flight.rb : null;
}
