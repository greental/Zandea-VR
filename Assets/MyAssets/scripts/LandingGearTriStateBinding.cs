using UnityEngine;

/// <summary>
/// Maps tri-state switch (-1/0/+1) to 2-state landing gear commands.
/// Useful when panel has a tri-state part but gear is only up/down.
/// </summary>
public class LandingGearTriStateBinding : MonoBehaviour
{
    public enum StateCommand
    {
        NoChange,
        Fold,
        Deploy,
        Toggle
    }

    [Header("References")]
    public XRTriStateSwitch triStateSwitch;
    public LandingGearController landingGear;
    public ShipPowerController powerController;

    [Header("Mapping")]
    public StateCommand lowStateCommand = StateCommand.Fold;
    public StateCommand centerStateCommand = StateCommand.NoChange;
    public StateCommand highStateCommand = StateCommand.Deploy;

    [Header("Runtime")]
    [SerializeField] int _lastState = 999;
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
        _lastPowerAllowed = PowerAllowsGear();
        ApplyCurrentState(force: true);
    }

    void Update()
    {
        bool powerAllowed = PowerAllowsGear();
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

        if (landingGear == null)
            landingGear = GetComponentInParent<LandingGearController>();
        if (landingGear == null)
            landingGear = FindFirstObjectByType<LandingGearController>();

        if (powerController == null)
            powerController = GetComponentInParent<ShipPowerController>();
        if (powerController == null)
            powerController = FindFirstObjectByType<ShipPowerController>();
    }

    void ApplyCurrentState(bool force)
    {
        if (triStateSwitch == null || landingGear == null) return;
        if (!PowerAllowsGear()) return;

        int state = triStateSwitch.state;
        if (!force && state == _lastState) return;
        _lastState = state;

        StateCommand command = centerStateCommand;
        if (state < 0) command = lowStateCommand;
        else if (state > 0) command = highStateCommand;

        switch (command)
        {
            case StateCommand.Fold:
                landingGear.SetGearDeployed(false);
                break;
            case StateCommand.Deploy:
                landingGear.SetGearDeployed(true);
                break;
            case StateCommand.Toggle:
                landingGear.ToggleGear();
                break;
            default:
                break;
        }
    }

    void OnTriStateChanged(int state)
    {
        if (!PowerAllowsGear()) return;
        ApplyCurrentState(force: true);
    }

    void OnTriStateIndexChanged(int stateIndex)
    {
        if (triStateSwitch == null || landingGear == null) return;
        if (!PowerAllowsGear()) return;

        int count = Mathf.Max(2, triStateSwitch.stateCount);
        if (count <= 2)
        {
            // Dual-state: low/high only.
            if (stateIndex <= 0) ExecuteCommand(lowStateCommand);
            else ExecuteCommand(highStateCommand);
            return;
        }

        int center = (count - 1) / 2;
        if (stateIndex < center) ExecuteCommand(lowStateCommand);
        else if (stateIndex > center) ExecuteCommand(highStateCommand);
        else ExecuteCommand(centerStateCommand);
    }

    void ExecuteCommand(StateCommand command)
    {
        if (landingGear == null) return;
        if (!PowerAllowsGear()) return;
        switch (command)
        {
            case StateCommand.Fold:
                landingGear.SetGearDeployed(false);
                break;
            case StateCommand.Deploy:
                landingGear.SetGearDeployed(true);
                break;
            case StateCommand.Toggle:
                landingGear.ToggleGear();
                break;
            default:
                break;
        }
    }

    bool PowerAllowsGear()
    {
        if (powerController == null) return true;
        return powerController.hudPowered;
    }
}
