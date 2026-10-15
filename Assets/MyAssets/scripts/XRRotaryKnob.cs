using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Rotary knob (e.g. volume) driven by XR selection.
/// Outputs normalized [0..1] and optional stepped index.
/// Works with XRGrabInteractable or XRSimpleInteractable.
/// </summary>
public class XRRotaryKnob : MonoBehaviour
{
    public enum RotationAxis { X, Y, Z }

    [System.Serializable]
    public class FloatEvent : UnityEvent<float> { }

    [System.Serializable]
    public class IntEvent : UnityEvent<int> { }

    [Header("References")]
    [Tooltip("Interactable source for selection/drag input.")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interactable;
    public Transform handle;
    public Transform pivot;
    [Tooltip("Optional visual hand anchor/proxy to show while grabbed.")]
    public Transform grabbedHandVisual;

    [Header("Motion")]
    public RotationAxis axis = RotationAxis.Y;
    public float minAngle = -120f;
    public float maxAngle = 120f;
    [Tooltip("If > 1, output also snaps to this many discrete steps.")]
    [Range(0, 16)]
    public int stepCount = 0;
    [Tooltip("When using XRGrabInteractable, keep knob anchored (don't follow interactor transform).")]
    public bool anchorWhileGrabbed = true;
    [Tooltip("When true, grabbedHandVisual follows interactor attach transform while selected.")]
    public bool followHandVisual = true;

    [Header("Output")]
    [Range(0f, 1f)]
    public float normalizedValue = 0.5f;
    public int currentStep = 0;

    [Header("Events")]
    public FloatEvent onValueChanged;
    public IntEvent onStepChanged;

    UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable _interactable;
    UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grab;
    UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor _interactor;
    Transform _anchorParent;
    Vector3 _anchorLocalPosition;
    Quaternion _anchorLocalRotation;
    Vector3 _anchorLocalScale;
    float _grabStartHandAngle;
    float _grabStartKnobAngle;
    bool _grabCaptured;

    void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        if (interactable == null)
            interactable = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        if (interactable == null)
            interactable = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        _interactable = interactable;
        _grab = _interactable as UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable;
        if (handle == null) handle = transform;
        if (pivot == null) pivot = transform;
        if (grabbedHandVisual != null) grabbedHandVisual.gameObject.SetActive(false);
        CaptureAnchorPose();
    }

    void OnEnable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.AddListener(OnGrab);
            _interactable.selectExited.AddListener(OnRelease);
        }
    }

    void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnGrab);
            _interactable.selectExited.RemoveListener(OnRelease);
        }
    }

    void Start()
    {
        SetNormalized(normalizedValue);
    }

    void Update()
    {
        if (anchorWhileGrabbed && _interactor != null)
            RestoreAnchorPose();

        if (_interactor == null || _interactable == null || handle == null || pivot == null) return;

        float handAngle = ReadHandAngle();
        if (!_grabCaptured)
        {
            _grabStartHandAngle = handAngle;
            _grabStartKnobAngle = Mathf.Lerp(minAngle, maxAngle, Mathf.Clamp01(normalizedValue));
            _grabCaptured = true;
        }

        float delta = Mathf.DeltaAngle(_grabStartHandAngle, handAngle);
        float target = Mathf.Clamp(_grabStartKnobAngle + delta, Mathf.Min(minAngle, maxAngle), Mathf.Max(minAngle, maxAngle));

        if (stepCount > 1)
        {
            float stepSize = Mathf.Abs(maxAngle - minAngle) / (stepCount - 1);
            float idxF = Mathf.Round((target - minAngle) / stepSize);
            int idx = Mathf.Clamp(Mathf.RoundToInt(idxF), 0, stepCount - 1);
            target = minAngle + idx * stepSize;
            if (idx != currentStep)
            {
                currentStep = idx;
                onStepChanged?.Invoke(currentStep);
            }
        }

        ApplyAngle(target);
        float nextNorm = Mathf.InverseLerp(minAngle, maxAngle, target);
        if (!Mathf.Approximately(nextNorm, normalizedValue))
        {
            normalizedValue = nextNorm;
            onValueChanged?.Invoke(normalizedValue);
        }

        if (followHandVisual && grabbedHandVisual != null)
        {
            var at = _interactor.GetAttachTransform(_interactable);
            if (at != null)
            {
                grabbedHandVisual.position = at.position;
                grabbedHandVisual.rotation = at.rotation;
            }
        }
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        CaptureAnchorPose();
        _interactor = args.interactorObject;
        _grabCaptured = false;
        if (anchorWhileGrabbed && _grab != null)
        {
            _grab.trackPosition = false;
            _grab.trackRotation = false;
            _grab.throwOnDetach = false;
            RestoreAnchorPose();
        }
        if (grabbedHandVisual != null)
            grabbedHandVisual.gameObject.SetActive(true);
    }

    void OnRelease(SelectExitEventArgs args)
    {
        _interactor = null;
        _grabCaptured = false;
        if (anchorWhileGrabbed)
            RestoreAnchorPose();
        if (grabbedHandVisual != null)
            grabbedHandVisual.gameObject.SetActive(false);
    }

    public void SetNormalized(float value01)
    {
        normalizedValue = Mathf.Clamp01(value01);
        float angle = Mathf.Lerp(minAngle, maxAngle, normalizedValue);
        if (stepCount > 1)
        {
            float stepSize = Mathf.Abs(maxAngle - minAngle) / (stepCount - 1);
            int idx = Mathf.Clamp(Mathf.RoundToInt((angle - minAngle) / stepSize), 0, stepCount - 1);
            currentStep = idx;
            angle = minAngle + idx * stepSize;
        }
        ApplyAngle(angle);
    }

    float ReadHandAngle()
    {
        Vector3 dirWorld = _interactor.GetAttachTransform(_interactable).position - pivot.position;
        Vector3 dirLocal = transform.InverseTransformDirection(dirWorld.normalized);
        switch (axis)
        {
            case RotationAxis.X: return Mathf.Atan2(dirLocal.z, dirLocal.y) * Mathf.Rad2Deg;
            case RotationAxis.Y: return Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg;
            default: return Mathf.Atan2(dirLocal.y, dirLocal.x) * Mathf.Rad2Deg;
        }
    }

    void ApplyAngle(float angle)
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

    void OnValidate()
    {
        if (handle == null) handle = transform;
        if (pivot == null) pivot = transform;
        if (interactable == null)
            interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        CaptureAnchorPose();
    }

    void CaptureAnchorPose()
    {
        _anchorParent = transform.parent;
        _anchorLocalPosition = transform.localPosition;
        _anchorLocalRotation = transform.localRotation;
        _anchorLocalScale = transform.localScale;
    }

    void RestoreAnchorPose()
    {
        if (_anchorParent != null && transform.parent != _anchorParent)
            transform.SetParent(_anchorParent, true);
        transform.localPosition = _anchorLocalPosition;
        transform.localRotation = _anchorLocalRotation;
        transform.localScale = _anchorLocalScale;
    }
}
