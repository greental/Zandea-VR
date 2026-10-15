using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Two-state cockpit flip switch.
/// Can be used as a trigger-style toggle (select to flip) or grab-drag switch.
/// </summary>
public class XRFlipSwitch : MonoBehaviour
{
    public enum RotationAxis { X, Y, Z }
    public enum InteractionMode { TriggerToggle, GrabDrag }

    [System.Serializable]
    public class BoolEvent : UnityEvent<bool> { }

    [Header("References")]
    [Tooltip("XR interactable source. If null, searched on this object, then parent, then children.")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interactable;
    [Tooltip("Visual handle to rotate. If null, this transform is used.")]
    public Transform handle;
    [Tooltip("Optional pivot/origin for hand direction. If null, this transform is used.")]
    public Transform pivot;

    [Header("Motion")]
    [Tooltip("TriggerToggle: flips state on select press. GrabDrag: drag with hand and release to nearest state.")]
    public InteractionMode interactionMode = InteractionMode.TriggerToggle;
    public RotationAxis axis = RotationAxis.X;
    public float offAngle = -35f;
    public float onAngle = 35f;
    public bool snapOnRelease = true;
    [Range(0f, 1f)]
    [Tooltip("Bias toward current state to avoid accidental flips near center.")]
    public float hysteresis = 0.1f;
    [Tooltip("Minimum seconds between trigger toggles to avoid double-fire (select + activate).")]
    public float triggerDebounceSeconds = 0.12f;

    [Header("Debug")]
    [Tooltip("If true, logs hover/select/activate/state changes to Console.")]
    public bool debugLogs = false;

    [Header("Audio")]
    [Tooltip("Optional AudioSource for switch sounds. If null, one is auto-created.")]
    public AudioSource audioSource;
    [Tooltip("Clip played when switching ON.")]
    public AudioClip switchOnClip;
    [Tooltip("Clip played when switching OFF.")]
    public AudioClip switchOffClip;
    [Range(0f, 1f)]
    public float switchVolume = 0.85f;

    [Header("State")]
    public bool isOn;

    [Header("Events")]
    public UnityEvent onTurnOn;
    public UnityEvent onTurnOff;
    public BoolEvent onValueChanged;

    UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable _interactable;
    UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grab;
    UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor _interactor;
    float _lastToggleTime = -999f;

    void Awake()
    {
        ResolveReferences();
        if (handle == null) handle = transform;
        if (pivot == null) pivot = transform;
        EnsureAudioSource();

        if (_interactable == null)
            Debug.LogWarning($"[XRFlipSwitch:{name}] No XRBaseInteractable found. Add XRSimpleInteractable or XRGrabInteractable.");
    }

    void ResolveReferences()
    {
        if (interactable == null)
            interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        if (interactable == null)
            interactable = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        if (interactable == null)
            interactable = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();

        _interactable = interactable;
        _grab = _interactable as UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable;
    }

    void OnEnable()
    {
        ResolveReferences();
        if (_interactable != null)
        {
            _interactable.selectEntered.AddListener(OnGrab);
            _interactable.selectExited.AddListener(OnRelease);
            _interactable.activated.AddListener(OnActivated);
            _interactable.hoverEntered.AddListener(OnHoverEntered);
            _interactable.hoverExited.AddListener(OnHoverExited);
        }
    }

    void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnGrab);
            _interactable.selectExited.RemoveListener(OnRelease);
            _interactable.activated.RemoveListener(OnActivated);
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
            _interactable.hoverExited.RemoveListener(OnHoverExited);
        }
    }

    void Start()
    {
        ApplyVisual(isOn ? onAngle : offAngle);
    }

    void Update()
    {
        // Keep visual synced if state changed externally (inspector/other script).
        if (interactionMode == InteractionMode.TriggerToggle || _interactor == null)
            ApplyVisual(isOn ? onAngle : offAngle);

        if (interactionMode != InteractionMode.GrabDrag) return;
        if (_grab == null) return;
        if (_interactor == null || pivot == null || handle == null) return;

        Vector3 dirWorld = _interactor.GetAttachTransform(_grab).position - pivot.position;
        Vector3 dirLocal = transform.InverseTransformDirection(dirWorld.normalized);
        float angle = ReadAxisAngle(dirLocal);
        float min = Mathf.Min(offAngle, onAngle);
        float max = Mathf.Max(offAngle, onAngle);
        angle = Mathf.Clamp(angle, min, max);
        ApplyVisual(angle);

        float threshold = Mathf.Lerp(offAngle, onAngle, 0.5f);
        float band = Mathf.Abs(onAngle - offAngle) * hysteresis * 0.5f;
        bool next = isOn;
        if (angle > threshold + band) next = true;
        else if (angle < threshold - band) next = false;
        SetState(next, true);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] selectEntered by {args.interactorObject?.transform?.name ?? "unknown"}");

        if (interactionMode == InteractionMode.TriggerToggle)
        {
            TryToggleFromTrigger();
            return;
        }
        if (_grab == null) return;
        _interactor = args.interactorObject;
    }

    void OnActivated(ActivateEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] activated by {args.interactorObject?.transform?.name ?? "unknown"}");

        if (interactionMode != InteractionMode.TriggerToggle) return;
        TryToggleFromTrigger();
    }

    void OnRelease(SelectExitEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] selectExited by {args.interactorObject?.transform?.name ?? "unknown"}");

        if (interactionMode == InteractionMode.TriggerToggle) return;
        _interactor = null;
        if (snapOnRelease)
            ApplyVisual(isOn ? onAngle : offAngle);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] hoverEntered by {args.interactorObject?.transform?.name ?? "unknown"}");
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] hoverExited by {args.interactorObject?.transform?.name ?? "unknown"}");
    }

    public void Toggle()
    {
        SetState(!isOn);
        ApplyVisual(isOn ? onAngle : offAngle);
    }

    void TryToggleFromTrigger()
    {
        if (Time.unscaledTime - _lastToggleTime < Mathf.Max(0.01f, triggerDebounceSeconds))
            return;
        _lastToggleTime = Time.unscaledTime;
        Toggle();
    }

    public void SetOn() { SetState(true); ApplyVisual(onAngle); }
    public void SetOff() { SetState(false); ApplyVisual(offAngle); }

    public void SetState(bool value, bool fromGrab = false)
    {
        if (isOn == value) return;
        isOn = value;
        if (debugLogs)
            Debug.Log($"[XRFlipSwitch:{name}] state -> {(isOn ? "ON" : "OFF")} ({(fromGrab ? "from interaction" : "external")})");

        PlaySwitchSound(isOn);
        if (isOn) onTurnOn?.Invoke();
        else onTurnOff?.Invoke();
        onValueChanged?.Invoke(isOn);
    }

    void EnsureAudioSource()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 0.5f;
        audioSource.maxDistance = 8f;
    }

    void PlaySwitchSound(bool on)
    {
        if (audioSource == null) return;
        var clip = on ? switchOnClip : switchOffClip;
        if (clip == null) return;
        audioSource.PlayOneShot(clip, switchVolume);
    }

    void ApplyVisual(float angle)
    {
        if (handle == null) return;
        Vector3 e = handle.localEulerAngles;
        switch (axis)
        {
            case RotationAxis.X: e.x = angle; break;
            case RotationAxis.Y: e.y = angle; break;
            case RotationAxis.Z: e.z = angle; break;
        }
        handle.localRotation = Quaternion.Euler(e);
    }

    float ReadAxisAngle(Vector3 localDir)
    {
        switch (axis)
        {
            case RotationAxis.X: return Mathf.Atan2(localDir.z, localDir.y) * Mathf.Rad2Deg;
            case RotationAxis.Y: return Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            default: return Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
        }
    }

    void OnValidate()
    {
        if (handle == null) handle = transform;
        if (pivot == null) pivot = transform;
        EnsureAudioSource();
#if UNITY_EDITOR
        if (switchOnClip == null)
            switchOnClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/switch1.mp3");
        if (switchOffClip == null)
            switchOffClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/switch2.mp3");
#endif
    }
}
