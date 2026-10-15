using UnityEngine;

/// <summary>
/// Controls a canopy hinge with optional inside/outside cockpit flip switches.
/// Closed = local X angle at <see cref="closedAngleX"/>. Open = local X angle at <see cref="openAngleX"/>.
/// </summary>
public class CanopyController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Pivot/hinge transform to rotate. If null, this transform is used.")]
    public Transform canopyHinge;
    [Tooltip("Optional cockpit switch. OFF = closed, ON = open.")]
    public XRFlipSwitch insideSwitch;
    [Tooltip("Optional exterior switch. OFF = closed, ON = open.")]
    public XRFlipSwitch outsideSwitch;

    [Header("Canopy Angles")]
    [Tooltip("Local X rotation when canopy is fully closed.")]
    public float closedAngleX = 0f;
    [Tooltip("Local X rotation when canopy is fully open. Typical values: -90 to -120.")]
    [Range(-180f, 0f)]
    public float openAngleX = -90f;

    [Header("Motion")]
    [Tooltip("Maximum canopy rotation speed in degrees per second.")]
    [Range(5f, 180f)]
    public float openCloseSpeed = 45f;
    [Tooltip("Fraction of max speed used at the start of movement for a hydraulic feel.")]
    [Range(0.01f, 1f)]
    public float startSpeedFraction = 0.2f;
    [Tooltip("Ease-in profile. Starts slow and speeds up as the canopy moves.")]
    public AnimationCurve hydraulicEase = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 1.2f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("State")]
    [Tooltip("Current desired canopy state.")]
    public bool canopyOpen;

    float _targetAngleX;
    float _moveStartAngleX;
    float _moveDistance;
    bool _listeningToSwitches;

    void Awake()
    {
        ResolveRefs();
    }

    void OnEnable()
    {
        ResolveRefs();
        SubscribeSwitches();
    }

    void OnDisable()
    {
        UnsubscribeSwitches();
    }

    void Start()
    {
        ResolveRefs();

        bool initialState = canopyOpen;
        if (insideSwitch != null)
            initialState = insideSwitch.isOn;
        else if (outsideSwitch != null)
            initialState = outsideSwitch.isOn;

        SetCanopyOpenImmediate(initialState, syncSwitches: true);
    }

    void Update()
    {
        AnimateCanopy(Time.deltaTime);
    }

    void ResolveRefs()
    {
        if (canopyHinge == null)
            canopyHinge = transform;
    }

    void SubscribeSwitches()
    {
        if (_listeningToSwitches)
            return;

        if (insideSwitch != null)
            insideSwitch.onValueChanged.AddListener(OnInsideSwitchChanged);
        if (outsideSwitch != null)
            outsideSwitch.onValueChanged.AddListener(OnOutsideSwitchChanged);

        _listeningToSwitches = true;
    }

    void UnsubscribeSwitches()
    {
        if (!_listeningToSwitches)
            return;

        if (insideSwitch != null)
            insideSwitch.onValueChanged.RemoveListener(OnInsideSwitchChanged);
        if (outsideSwitch != null)
            outsideSwitch.onValueChanged.RemoveListener(OnOutsideSwitchChanged);

        _listeningToSwitches = false;
    }

    void OnInsideSwitchChanged(bool isOn)
    {
        SetCanopyOpen(isOn, switchSource: insideSwitch);
    }

    void OnOutsideSwitchChanged(bool isOn)
    {
        SetCanopyOpen(isOn, switchSource: outsideSwitch);
    }

    public void ToggleCanopy()
    {
        SetCanopyOpen(!canopyOpen);
    }

    public void OpenCanopy()
    {
        SetCanopyOpen(true);
    }

    public void CloseCanopy()
    {
        SetCanopyOpen(false);
    }

    public void SetCanopyOpen(bool open)
    {
        SetCanopyOpen(open, null);
    }

    void SetCanopyOpen(bool open, XRFlipSwitch switchSource)
    {
        ResolveRefs();
        canopyOpen = open;

        float currentAngle = GetCurrentLocalAngleX();
        float nextTarget = canopyOpen ? openAngleX : closedAngleX;
        bool targetChanged = !Mathf.Approximately(_targetAngleX, nextTarget);

        if (targetChanged)
        {
            _moveStartAngleX = currentAngle;
            _targetAngleX = nextTarget;
            _moveDistance = Mathf.Abs(_targetAngleX - _moveStartAngleX);
        }

        SyncSwitches(switchSource);
    }

    public void SetCanopyOpenImmediate(bool open, bool syncSwitches = true)
    {
        ResolveRefs();
        canopyOpen = open;
        _targetAngleX = canopyOpen ? openAngleX : closedAngleX;
        _moveStartAngleX = _targetAngleX;
        _moveDistance = 0f;
        ApplyAngleImmediate(_targetAngleX);

        if (syncSwitches)
            SyncSwitches(null);
    }

    void SyncSwitches(XRFlipSwitch switchSource)
    {
        if (insideSwitch != null && insideSwitch != switchSource)
            insideSwitch.SetState(canopyOpen);

        if (outsideSwitch != null && outsideSwitch != switchSource)
            outsideSwitch.SetState(canopyOpen);
    }

    void AnimateCanopy(float dt)
    {
        if (canopyHinge == null)
            return;

        float currentAngle = GetCurrentLocalAngleX();
        float remaining = Mathf.Abs(Mathf.DeltaAngle(currentAngle, _targetAngleX));
        if (remaining <= 0.01f)
        {
            ApplyAngleImmediate(_targetAngleX);
            return;
        }

        float travel = Mathf.Max(0.0001f, _moveDistance);
        float progress = 1f - Mathf.Clamp01(remaining / travel);
        float eased = hydraulicEase != null ? hydraulicEase.Evaluate(progress) : progress;
        float speedMultiplier = Mathf.Lerp(startSpeedFraction, 1f, Mathf.Clamp01(eased));
        float step = Mathf.Max(0.01f, openCloseSpeed * Mathf.Max(0.01f, speedMultiplier) * dt);
        float nextAngle = Mathf.MoveTowardsAngle(currentAngle, _targetAngleX, step);
        ApplyAngleImmediate(nextAngle);
    }

    float GetCurrentLocalAngleX()
    {
        if (canopyHinge == null)
            return 0f;

        return NormalizeSignedAngle(canopyHinge.localEulerAngles.x);
    }

    void ApplyAngleImmediate(float localAngleX)
    {
        if (canopyHinge == null)
            return;

        Vector3 euler = canopyHinge.localEulerAngles;
        euler.x = localAngleX;
        canopyHinge.localRotation = Quaternion.Euler(euler);
    }

    static float NormalizeSignedAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }

    void OnValidate()
    {
        if (canopyHinge == null)
            canopyHinge = transform;

        if (hydraulicEase == null || hydraulicEase.length == 0)
        {
            hydraulicEase = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 1.2f),
                new Keyframe(1f, 1f, 0f, 0f));
        }

        if (!Application.isPlaying)
        {
            _targetAngleX = canopyOpen ? openAngleX : closedAngleX;
            ApplyAngleImmediate(_targetAngleX);
        }
    }
}