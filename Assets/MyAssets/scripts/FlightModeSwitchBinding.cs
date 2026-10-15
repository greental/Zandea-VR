using UnityEngine;

/// <summary>
/// Maps tri-state switch output (-1/0/+1) to FlightController flight modes.
/// </summary>
public class FlightModeSwitchBinding : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    public FlightController flightController;
    public XRTriStateSwitch triStateSwitch;
    public ShipPowerController powerController;

    [Header("Mapping")]
    [Tooltip("Switch state -1 maps to this mode.")]
    public FlightModelMode lowStateMode = FlightModelMode.DirectTorque;
    [Tooltip("Switch state 0 maps to this mode.")]
    public FlightModelMode centerStateMode = FlightModelMode.DirectTorque;
    [Tooltip("Switch state +1 maps to this mode.")]
    public FlightModelMode highStateMode = FlightModelMode.InertialDrift;
    int _lastState = 999;
    bool _lastPowerAllowed = true;

    void Awake()
    {
        ResolveRefs();
    }

    void OnEnable()
    {
        ResolveRefs();
        if (triStateSwitch != null)
        {
            triStateSwitch.onStateChanged.AddListener(OnTriStateChanged);
            triStateSwitch.onStateIndexChanged.AddListener(OnTriStateIndexChanged);
        }
    }

    void OnDisable()
    {
        if (triStateSwitch != null)
        {
            triStateSwitch.onStateChanged.RemoveListener(OnTriStateChanged);
            triStateSwitch.onStateIndexChanged.RemoveListener(OnTriStateIndexChanged);
        }
    }

    void Start()
    {
        ResolveRefs();
        _lastPowerAllowed = PowerAllowsModeSwitch();
        ApplyCurrentState(force: true);
    }

    void Update()
    {
        bool powerAllowed = PowerAllowsModeSwitch();
        if (powerAllowed && !_lastPowerAllowed)
        {
            // 24V restored: re-apply current button position immediately.
            ApplyCurrentState(force: true);
        }
        _lastPowerAllowed = powerAllowed;

        ApplyCurrentState(force: false);
    }

    void ResolveRefs()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();

        if (flightController == null && hudManager != null)
            flightController = hudManager.flight;
        if (flightController == null)
            flightController = FindFirstObjectByType<FlightController>();

        if (triStateSwitch == null)
            triStateSwitch = GetComponent<XRTriStateSwitch>();
        if (triStateSwitch == null)
        {
            var all = FindObjectsByType<XRTriStateSwitch>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float best = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                float d = (all[i].transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; triStateSwitch = all[i]; }
            }
        }

        if (powerController == null)
            powerController = GetComponentInParent<ShipPowerController>();
        if (powerController == null)
            powerController = FindFirstObjectByType<ShipPowerController>();
    }

    void ApplyCurrentState(bool force)
    {
        if (triStateSwitch == null) return;
        if (!PowerAllowsModeSwitch()) return;
        if (!force && triStateSwitch.state == _lastState) return;
        _lastState = triStateSwitch.state;
        ApplyTriState(_lastState);
    }

    public void ApplyTriState(int state)
    {
        ResolveRefs();
        if (flightController == null) return;
        if (!PowerAllowsModeSwitch()) return;

        if (state < 0) flightController.flightModelMode = lowStateMode;
        else if (state > 0) flightController.flightModelMode = highStateMode;
        else flightController.flightModelMode = centerStateMode;
    }

    public bool SetModeAndSwitch(FlightModelMode mode)
    {
        ResolveRefs();
        if (flightController == null)
            return false;

        if (triStateSwitch == null)
        {
            flightController.flightModelMode = mode;
            return true;
        }

        int count = Mathf.Max(2, triStateSwitch.stateCount);
        int center = (count - 1) / 2;
        int matchingIndex = -1;
        for (int i = 0; i < count; i++)
        {
            FlightModelMode mapped = count <= 2
                ? (i == 0 ? lowStateMode : highStateMode)
                : i < center ? lowStateMode : i > center ? highStateMode : centerStateMode;
            if (mapped == mode)
            {
                matchingIndex = i;
                if (i == count - 1)
                    break;
            }
        }

        // Make the high detent represent the requested startup mode if this ship's
        // existing mapping has no detent for it, so the next switch update won't undo it.
        if (matchingIndex < 0)
        {
            highStateMode = mode;
            matchingIndex = count - 1;
        }

        triStateSwitch.SetStateIndexAndApplyVisual(matchingIndex);
        _lastState = triStateSwitch.state;
        flightController.flightModelMode = mode;
        return true;
    }

    void OnTriStateChanged(int state)
    {
        if (!PowerAllowsModeSwitch()) return;
        ApplyTriState(state);
    }

    void OnTriStateIndexChanged(int index)
    {
        ResolveRefs();
        if (flightController == null || triStateSwitch == null) return;
        if (!PowerAllowsModeSwitch()) return;

        int count = Mathf.Max(2, triStateSwitch.stateCount);
        if (count <= 2)
        {
            // Dual-state: low/high only, center mapping is ignored.
            flightController.flightModelMode = index <= 0 ? lowStateMode : highStateMode;
            return;
        }

        int center = (count - 1) / 2;
        if (index < center) flightController.flightModelMode = lowStateMode;
        else if (index > center) flightController.flightModelMode = highStateMode;
        else flightController.flightModelMode = centerStateMode;
    }

    bool PowerAllowsModeSwitch()
    {
        if (powerController == null) return true;
        return powerController.hudPowered;
    }
}
