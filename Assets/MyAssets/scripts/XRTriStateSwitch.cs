using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;

public class XRTriStateSwitch : XRBaseInteractable
{
    public enum RotationAxis { X, Y, Z }
    [System.Serializable] public class IntEvent : UnityEvent<int> { }
    [System.Serializable]
    public class StateSlot
    {
        public bool enabled;
        public float angle;
    }

    [Header("References")]
    public Transform rotationTarget;
    public Transform pivot;
    public Transform grabbedHandVisual;
    public Transform interactorVisualRoot;

    [Header("Motion")]
    public RotationAxis axis = RotationAxis.X;
    [Tooltip("Runtime count of active configured states.")]
    [Range(2, 10)] public int stateCount = 3;
    [Tooltip("Configure up to 10 state angles. Enabled entries are used in array order.")]
    public StateSlot[] states = new StateSlot[10];
    [Tooltip("If enabled, rotation is clamped between first and last configured state angles.")]
    public bool lockAtEdges = false;
    public bool snapOnRelease = true;
    [Range(0f, 90f)] public float snapThresholdDegrees = 6f;
    public bool hideInteractorVisualOnGrab = true;

    [Header("State")]
    [Range(-1, 1)] public int state = 0;
    public int stateIndex = 0;

    [Header("Events")]
    public UnityEvent onLow;
    public UnityEvent onCenter;
    public UnityEvent onHigh;
    public IntEvent onStateChanged;
    public IntEvent onStateIndexChanged;

    IXRSelectInteractor _interactor;
    readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
    float _lastDraggedAngle;
    bool _hasDraggedAngle;
    float _lastRawHandAngle;
    bool _hasRawHandAngle;
    readonly List<float> _configuredAngles = new List<float>(10);

    protected override void OnEnable()
    {
        base.OnEnable();
        selectEntered.AddListener(OnGrab);
        selectExited.AddListener(OnRelease);
    }

    protected override void OnDisable()
    {
        selectEntered.RemoveListener(OnGrab);
        selectExited.RemoveListener(OnRelease);
        SetInteractorVisual(true, _interactor);
        base.OnDisable();
    }

    void Start()
    {
        NormalizeSetup();
        ApplyVisual(AngleForStateIndex(stateIndex));
    }

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);
        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
        if (!isSelected || _interactor == null || pivot == null || rotationTarget == null) return;
        RebuildConfiguredAngles();

        Vector3 dirWorld = _interactor.GetAttachTransform(this).position - pivot.position;
        Vector3 dirLocal = transform.InverseTransformDirection(dirWorld.normalized);
        float rawAngle = ReadAxisAngle(dirLocal);

        if (!_hasRawHandAngle)
        {
            _lastRawHandAngle = rawAngle;
            _hasRawHandAngle = true;
            if (!_hasDraggedAngle)
            {
                _lastDraggedAngle = AngleForStateIndex(stateIndex);
                _hasDraggedAngle = true;
            }
        }

        float delta = Mathf.DeltaAngle(_lastRawHandAngle, rawAngle);
        _lastRawHandAngle = rawAngle;

        float angle = _lastDraggedAngle + delta;
        if (lockAtEdges)
        {
            float minAngle = Mathf.Min(AngleForStateIndex(0), AngleForStateIndex(stateCount - 1));
            float maxAngle = Mathf.Max(AngleForStateIndex(0), AngleForStateIndex(stateCount - 1));
            angle = Mathf.Clamp(angle, minAngle, maxAngle);
        }
        _lastDraggedAngle = angle;
        _hasDraggedAngle = true;

        int nextIndex = ClosestStateIndexForAngle(angle);
        float targetAngle = AngleForStateIndex(nextIndex);
        float d = DistanceToStateAngle(angle, targetAngle);
        float snappedAngle = NearestEquivalentAngle(angle, targetAngle);

        if (snapThresholdDegrees > 0f && d <= snapThresholdDegrees)
        {
            ApplyVisual(snappedAngle);
            SetStateIndex(nextIndex, true);
        }
        else
        {
            ApplyVisual(angle);
        }
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        _interactor = args.interactorObject;
        _lastDraggedAngle = AngleForStateIndex(stateIndex);
        _hasDraggedAngle = true;
        _hasRawHandAngle = false;
        if (grabbedHandVisual != null) grabbedHandVisual.gameObject.SetActive(true);
        if (hideInteractorVisualOnGrab) SetInteractorVisual(false, args.interactorObject);
    }

    void OnRelease(SelectExitEventArgs args)
    {
        _interactor = null;
        if (snapOnRelease)
        {
            int releaseIndex = _hasDraggedAngle ? ClosestStateIndexForAngle(_lastDraggedAngle) : stateIndex;
            SetStateIndex(releaseIndex, true);
            ApplyVisual(AngleForStateIndex(stateIndex));
        }
        _hasDraggedAngle = false;
        _hasRawHandAngle = false;
        if (grabbedHandVisual != null) grabbedHandVisual.gameObject.SetActive(false);
        if (hideInteractorVisualOnGrab) SetInteractorVisual(true, args.interactorObject);
    }

    public void SetState(int nextState, bool fromGrab = false)
    {
        // Backward/legacy compatibility:
        // - signed mode: -1 / 0 / +1
        // - index mode:  0..(stateCount-1)
        if (nextState < -1 || nextState > 1)
        {
            SetStateIndex(nextState, fromGrab);
            return;
        }

        SetStateIndex(IndexFromSignedState(Mathf.Clamp(nextState, -1, 1), stateCount), fromGrab);
    }

    public void SetStateIndex(int nextIndex, bool fromGrab = false)
    {
        RebuildConfiguredAngles();
        nextIndex = Mathf.Clamp(nextIndex, 0, stateCount - 1);

        bool indexChanged = stateIndex != nextIndex;
        stateIndex = nextIndex;
        int signed = SignedStateFromIndex(stateIndex, stateCount);
        bool stateChanged = state != signed;
        state = signed;
        if (!indexChanged && !stateChanged) return;

        if (state < 0) onLow?.Invoke();
        else if (state > 0) onHigh?.Invoke();
        else onCenter?.Invoke();
        onStateChanged?.Invoke(state);
        onStateIndexChanged?.Invoke(stateIndex);
    }

    public void SetStateIndexAndApplyVisual(int nextIndex)
    {
        SetStateIndex(nextIndex);
        ApplyVisual(AngleForStateIndex(stateIndex));
    }

    public void StepState(int step)
    {
        if (step == 0) return;
        SetStateIndex(stateIndex + step);
        ApplyVisual(AngleForStateIndex(stateIndex));
    }

    public void NextState() => StepState(1);
    public void PreviousState() => StepState(-1);

    int ClosestStateIndexForAngle(float angle)
    {
        RebuildConfiguredAngles();
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < stateCount; i++)
        {
            float a = AngleForStateIndex(i);
            float d = DistanceToStateAngle(angle, a);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    static float NearestEquivalentAngle(float referenceAngle, float baseAngle)
    {
        // Return baseAngle shifted by 360k, closest to referenceAngle.
        float turns = Mathf.Round((referenceAngle - baseAngle) / 360f);
        return baseAngle + (turns * 360f);
    }

    static float DistanceToStateAngle(float referenceAngle, float baseAngle)
    {
        return Mathf.Abs(referenceAngle - NearestEquivalentAngle(referenceAngle, baseAngle));
    }

    float AngleForStateIndex(int index)
    {
        RebuildConfiguredAngles();
        index = Mathf.Clamp(index, 0, Mathf.Max(0, stateCount - 1));
        return _configuredAngles[index];
    }

    int SignedStateFromIndex(int index, int count)
    {
        if (count <= 2) return index <= 0 ? -1 : 1;
        int mid = (count - 1) / 2;
        if (index < mid) return -1;
        if (index > mid) return 1;
        return 0;
    }

    int IndexFromSignedState(int signed, int count)
    {
        if (count <= 2) return signed < 0 ? 0 : count - 1;
        if (signed < 0) return 0;
        if (signed > 0) return count - 1;
        return (count - 1) / 2;
    }

    void ApplyVisual(float angle)
    {
        if (rotationTarget == null) return;
        Vector3 e = rotationTarget.localEulerAngles;
        switch (axis)
        {
            case RotationAxis.X: e.x = angle; break;
            case RotationAxis.Y: e.y = angle; break;
            case RotationAxis.Z: e.z = angle; break;
        }
        rotationTarget.localRotation = Quaternion.Euler(e);
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
        NormalizeSetup();
        ApplyVisual(AngleForStateIndex(stateIndex));
    }

    void NormalizeSetup()
    {
        EnsureStateSlots();
        RebuildConfiguredAngles();
        stateIndex = Mathf.Clamp(stateIndex, 0, stateCount - 1);
        if (pivot == null) pivot = transform;
        if (rotationTarget == null) rotationTarget = pivot;
        state = SignedStateFromIndex(stateIndex, stateCount);
    }

    void EnsureStateSlots()
    {
        if (states == null || states.Length != 10)
        {
            var old = states;
            states = new StateSlot[10];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new StateSlot();
                if (old != null && i < old.Length && old[i] != null)
                {
                    states[i].enabled = old[i].enabled;
                    states[i].angle = old[i].angle;
                }
            }
        }
        else
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i] == null) states[i] = new StateSlot();
        }
    }

    void RebuildConfiguredAngles()
    {
        EnsureStateSlots();
        _configuredAngles.Clear();
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == null || !states[i].enabled) continue;
            _configuredAngles.Add(states[i].angle);
        }

        // Safe fallback so existing scenes continue working until slots are configured.
        if (_configuredAngles.Count < 2)
        {
            _configuredAngles.Clear();
            _configuredAngles.Add(-35f);
            _configuredAngles.Add(0f);
            _configuredAngles.Add(35f);
        }

        stateCount = Mathf.Clamp(_configuredAngles.Count, 2, 10);
    }

    void SetInteractorVisual(bool visible, IXRInteractor interactor)
    {
        Transform root = interactorVisualRoot;
        if (root == null && interactor != null) root = interactor.transform;
        if (root == null) return;

        if (!visible)
        {
            _hiddenRenderers.Clear();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled) continue;
                renderers[i].enabled = false;
                _hiddenRenderers.Add(renderers[i]);
            }
        }
        else
        {
            for (int i = 0; i < _hiddenRenderers.Count; i++)
                if (_hiddenRenderers[i] != null) _hiddenRenderers[i].enabled = true;
            _hiddenRenderers.Clear();
        }
    }
}
