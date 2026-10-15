using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VRKoombar.WeaponsStaging.Core;

/// <summary>
/// Central power logic for cockpit panel switches.
/// - 24V master switch powers HUD on/off.
/// - Dual engine arm switches must BOTH be on for propulsion controls.
/// </summary>
public class ShipPowerController : MonoBehaviour
{
    [Header("Panel Inputs")]
    public XRFlipSwitch master24VSwitch;
    public XRFlipSwitch engineSwitchA;
    public XRFlipSwitch engineSwitchB;
    [Tooltip("Optional two-state switch for weapon group selection. Off = Primary, On = Secondary.")]
    public XRFlipSwitch weaponSelectionSwitch;
    [Tooltip("Tri-state target selector. Low = previous target, high = next target; center is neutral.")]
    public XRTriStateSwitch targetToggleSwitch;

    [Header("Systems")]
    public ShipHUDManager hudManager;
    public FlightController flightController;
    public HOTASController hotasController;
    public FlightModeIndicatorHUD flightModeIndicatorHUD;
    public ShipWeaponsOrchestrator shipWeaponsOrchestrator;

    [Header("Behavior")]
    [Tooltip("When engines go offline, zero velocity/angVel so ship fully stops.")]
    public bool hardStopWhenEnginesOff = true;
    [Tooltip("Disable these canopy anchors when 24V is off.")]
    public bool powerHudByAnchorGameObject = true;
    [Tooltip("Enable gravity on the ship rigidbody when engines are offline.")]
    public bool enableGravityWhenEnginesOff = true;
    [Tooltip("Disable atmosphere drift/buffeting while engines are offline.")]
    public bool disableAtmosphereWhenEnginesOff = true;

    [Header("Startup")]
    [Tooltip("When enabled, startup turns on 24V and both engines, folds the landing gear, selects Drift mode, and seats the player at the configured pilot seat.")]
    [SerializeField] private bool startFlightReadyAndSeated;
    [Tooltip("Optional preferred pilot seat. If empty, the first active SitInCockpit under this ship is used, preferring one whose name includes 'pilot'.")]
    [SerializeField] private SitInCockpit startupPilotSeat;

    [Header("HUD Power-On Boot Sequence")]
    [Tooltip("Delay in seconds before HUD starts booting after 24V goes ON.")]
    public float hudBootDelay = 0.35f;
    [Tooltip("Number of on/off blinks before fade-in.")]
    [Range(0, 8)]
    public int hudBootBlinkCount = 2;
    [Tooltip("Seconds for each blink half-cycle.")]
    public float hudBootBlinkStep = 0.08f;
    [Tooltip("Seconds to ramp HUD alpha from 0 to 1 after blinks.")]
    public float hudBootFadeTime = 0.45f;
    [Tooltip("Debounce for 24V master switch state (prevents rapid boot loop flicker).")]
    public float hudPowerDebounceSeconds = 0.12f;

    [Header("Runtime (read-only)")]
    public bool hudPowered = true;
    public bool enginesOnline = true;

    bool _lastHudPowered;
    bool _lastEnginesOnline;
    bool _hudBooting;
    float _hudBootStartTime;
    bool _stableHudSwitchState = true;
    bool _lastRawHudSwitchState = true;
    float _hudSwitchLastChangeTime;
    bool _hudSwitchInitialized;
    bool _lastWeaponSelectionSecondary;
    bool _weaponSelectionInitialized;
    XRTriStateSwitch _boundTargetToggle;

    void OnEnable() => BindTargetToggle();

    void OnDisable()
    {
        if (_boundTargetToggle != null)
            _boundTargetToggle.onStateChanged?.RemoveListener(OnTargetToggleStateChanged);
        _boundTargetToggle = null;
    }

    void Awake()
    {
        ResolveRefs();
    }

    void Start()
    {
        ResolveRefs();
        if (startFlightReadyAndSeated)
            StartCoroutine(ApplyFlightReadyStartup());
        else
            ApplyPowerState(force: true);
    }

    IEnumerator ApplyFlightReadyStartup()
    {
        // Let XR Origin, switch bindings, and gear finish their own Start initialization first.
        yield return null;

        ResolveRefs();
        master24VSwitch?.SetOn();
        engineSwitchA?.SetOn();
        if (engineSwitchB != engineSwitchA)
            engineSwitchB?.SetOn();

        LandingGearController gear = flightController != null && flightController.landingGear != null
            ? flightController.landingGear
            : GetShipRoot().GetComponentInChildren<LandingGearController>(true);
        if (gear != null)
            gear.ApplyStateImmediate(false);

        // Apply power before selecting mode so the flight-mode switch binding is live.
        ApplyPowerState(force: true);
        bool modeSet = false;
        FlightModeSwitchBinding[] modeBindings = GetShipRoot().GetComponentsInChildren<FlightModeSwitchBinding>(true);
        foreach (FlightModeSwitchBinding binding in modeBindings)
        {
            if (binding == null)
                continue;
            binding.flightController ??= flightController;
            binding.powerController ??= this;
            modeSet |= binding.SetModeAndSwitch(FlightModelMode.InertialDrift);
        }
        if (!modeSet && flightController != null)
            flightController.flightModelMode = FlightModelMode.InertialDrift;

        SitInCockpit seat = ResolveStartupPilotSeat();
        bool seated = seat != null && seat.StartSeated();
        if (seat == null || !seated)
            Debug.LogWarning("[ShipPowerController] Flight-ready startup completed, but no player was seated. Assign a valid startup pilot seat and check its XR Origin / camera references.", this);
        else
            Debug.Log("[ShipPowerController] Started flight-ready and seated: 24V on, engines on, gear folded, Drift mode.", this);
    }

    SitInCockpit ResolveStartupPilotSeat()
    {
        if (startupPilotSeat != null)
            return startupPilotSeat;

        SitInCockpit[] seats = GetShipRoot().GetComponentsInChildren<SitInCockpit>(true);
        foreach (SitInCockpit seat in seats)
        {
            if (seat != null && seat.isActiveAndEnabled && seat.name.IndexOf("pilot", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return seat;
        }
        foreach (SitInCockpit seat in seats)
        {
            if (seat != null && seat.isActiveAndEnabled)
                return seat;
        }
        return null;
    }

    Transform GetShipRoot()
    {
        return flightController != null && flightController.rb != null
            ? flightController.rb.transform
            : transform.root;
    }

    void Update()
    {
        ResolveRefs();
        BindTargetToggle();
        ApplyPowerState(force: false);
        UpdateHudBootSequence();
    }

    void BindTargetToggle()
    {
        if (_boundTargetToggle == targetToggleSwitch) return;
        if (_boundTargetToggle != null)
            _boundTargetToggle.onStateChanged?.RemoveListener(OnTargetToggleStateChanged);
        _boundTargetToggle = targetToggleSwitch;
        if (_boundTargetToggle == null) return;
        if (_boundTargetToggle.onStateChanged == null)
            _boundTargetToggle.onStateChanged = new XRTriStateSwitch.IntEvent();
        _boundTargetToggle.onStateChanged.AddListener(OnTargetToggleStateChanged);
    }

    void OnTargetToggleStateChanged(int state)
    {
        if (!hudPowered || state == 0) return;
        EnemyTargetHudV2 targetHud = FindFirstObjectByType<EnemyTargetHudV2>();
        if (targetHud == null) return;
        if (state > 0) targetHud.NextTarget();
        else targetHud.PreviousTarget();
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

        if (hotasController == null && hudManager != null)
            hotasController = hudManager.hotas;
        if (hotasController == null)
            hotasController = FindFirstObjectByType<HOTASController>();
        if (hotasController != null && hotasController.flight == null)
            hotasController.flight = flightController;

        if (flightModeIndicatorHUD == null)
            flightModeIndicatorHUD = FindFirstObjectByType<FlightModeIndicatorHUD>();

        if (shipWeaponsOrchestrator == null)
            shipWeaponsOrchestrator = GetComponentInParent<ShipWeaponsOrchestrator>();
        if (shipWeaponsOrchestrator == null)
            shipWeaponsOrchestrator = FindFirstObjectByType<ShipWeaponsOrchestrator>();
    }

    void ApplyPowerState(bool force)
    {
        bool rawHudSwitch = master24VSwitch == null || master24VSwitch.isOn;
        if (force || !_hudSwitchInitialized)
        {
            _stableHudSwitchState = rawHudSwitch;
            _lastRawHudSwitchState = rawHudSwitch;
            _hudSwitchLastChangeTime = Time.unscaledTime;
            _hudSwitchInitialized = true;
        }
        else
        {
            if (rawHudSwitch != _lastRawHudSwitchState)
            {
                _lastRawHudSwitchState = rawHudSwitch;
                _hudSwitchLastChangeTime = Time.unscaledTime;
            }

            if (Time.unscaledTime - _hudSwitchLastChangeTime >= Mathf.Max(0f, hudPowerDebounceSeconds))
                _stableHudSwitchState = _lastRawHudSwitchState;
        }

        bool nextHudPowered = _stableHudSwitchState;
        bool bothConfigured = engineSwitchA != null && engineSwitchB != null;
        bool bothOn = bothConfigured && engineSwitchA.isOn && engineSwitchB.isOn;
        bool bothOff = bothConfigured && !engineSwitchA.isOn && !engineSwitchB.isOn;
        bool nextEnginesOnline;
        if (!bothConfigured)
        {
            nextEnginesOnline = true;
        }
        else if (bothOn)
        {
            nextEnginesOnline = true;
        }
        else if (bothOff)
        {
            nextEnginesOnline = false;
        }
        else
        {
            // Safety interlock: mixed position does not change current engine state.
            nextEnginesOnline = _lastEnginesOnline;
        }

        // Master 24V off forces engines offline regardless of engine arm switch positions.
        hudPowered = nextHudPowered;
        enginesOnline = nextHudPowered && nextEnginesOnline;

        if (force || hudPowered != _lastHudPowered)
        {
            SetHudPowered(hudPowered);
            _lastHudPowered = hudPowered;
        }

        if (force || enginesOnline != _lastEnginesOnline)
        {
            SetEnginesOnline(enginesOnline);
            _lastEnginesOnline = enginesOnline;
        }

        ApplyWeaponSelection(force);
    }

    void ApplyWeaponSelection(bool force)
    {
        if (shipWeaponsOrchestrator == null)
            return;

        bool selectSecondary = weaponSelectionSwitch != null && weaponSelectionSwitch.isOn;
        if (!force && _weaponSelectionInitialized && selectSecondary == _lastWeaponSelectionSecondary)
            return;

        _weaponSelectionInitialized = true;
        _lastWeaponSelectionSecondary = selectSecondary;

        if (selectSecondary)
            shipWeaponsOrchestrator.SelectSecondary();
        else
            shipWeaponsOrchestrator.SelectPrimary();
    }

    void SetHudPowered(bool powered)
    {
        if (hudManager == null) return;

        if (!powered)
        {
            _hudBooting = false;
            SetHudAlpha(0f);
            if (powerHudByAnchorGameObject)
            {
                SetActiveSafe(hudManager.canopyHUD, false);
                SetActiveSafe(hudManager.leftScreen, false);
                SetActiveSafe(hudManager.rightScreen, false);
                SetActiveSafe(hudManager.warningBanner, false);
            }
            return;
        }

        // Power on: ensure anchors are active, then run delayed blink+fade boot sequence.
        SetActiveSafe(hudManager.canopyHUD, true);
        SetActiveSafe(hudManager.leftScreen, true);
        SetActiveSafe(hudManager.rightScreen, true);
        SetActiveSafe(hudManager.warningBanner, true);
        SetHudAlpha(0f);
        _hudBooting = true;
        _hudBootStartTime = Time.unscaledTime;
    }

    void SetEnginesOnline(bool online)
    {
        if (hotasController != null)
            hotasController.enabled = online;

        if (flightController != null)
            flightController.enabled = online;

        if (flightController != null && flightController.rb != null)
        {
            if (enableGravityWhenEnginesOff)
                flightController.rb.useGravity = !online;
        }

        if (!online && hardStopWhenEnginesOff && flightController != null && flightController.rb != null)
        {
            flightController.rb.linearVelocity = Vector3.zero;
            flightController.rb.angularVelocity = Vector3.zero;
        }

        if (flightController != null && disableAtmosphereWhenEnginesOff)
        {
            if (!online)
                flightController.inAtmosphere = false;
        }

        if (flightModeIndicatorHUD != null)
            flightModeIndicatorHUD.SetSystemOnline(online);
    }

    void UpdateHudBootSequence()
    {
        if (!_hudBooting) return;
        if (!hudPowered || hudManager == null)
        {
            _hudBooting = false;
            return;
        }

        float t = Mathf.Max(0f, Time.unscaledTime - _hudBootStartTime);

        // Stage 1: initial delay
        if (t < hudBootDelay)
        {
            SetHudAlpha(0f);
            return;
        }
        t -= hudBootDelay;

        // Stage 2: blink cycles
        float blinkDuration = Mathf.Max(0f, hudBootBlinkCount) * Mathf.Max(0.001f, hudBootBlinkStep) * 2f;
        if (t < blinkDuration && hudBootBlinkCount > 0)
        {
            float phase = Mathf.Floor(t / Mathf.Max(0.001f, hudBootBlinkStep));
            bool on = ((int)phase % 2) == 0;
            SetHudAlpha(on ? 1f : 0f);
            return;
        }
        t -= blinkDuration;

        // Stage 3: fade to full
        float fade = Mathf.Max(0.001f, hudBootFadeTime);
        float a = Mathf.Clamp01(t / fade);
        SetHudAlpha(a);
        if (a >= 1f)
            _hudBooting = false;
    }

    void SetHudAlpha(float alpha)
    {
        SetAlphaSafe(hudManager != null ? hudManager.canopyHUD : null, alpha);
        SetAlphaSafe(hudManager != null ? hudManager.leftScreen : null, alpha);
        SetAlphaSafe(hudManager != null ? hudManager.rightScreen : null, alpha);
        SetAlphaSafe(hudManager != null ? hudManager.warningBanner : null, alpha);
    }

    static void SetActiveSafe(Transform t, bool active)
    {
        if (t == null || t.gameObject == null) return;
        if (t.gameObject.activeSelf != active)
            t.gameObject.SetActive(active);
    }

    static void SetAlphaSafe(Transform t, float alpha)
    {
        if (t == null || t.gameObject == null) return;
        var cg = t.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = t.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = Mathf.Clamp01(alpha);
        cg.interactable = alpha > 0.99f;
        cg.blocksRaycasts = alpha > 0.99f;
    }
}
