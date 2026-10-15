using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vertical throttle bar on the left flight display. Zero is 1/3 from the bottom;
/// forward throttle fills upward, reverse fills downward. Builds once, then only updates size/position.
/// </summary>
[ExecuteAlways]
public class ThrottleBarHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;

    [Tooltip("Optional: explicit mount for the bar (under a Canvas). If null, a mount will be created under the reticle canvas.")]
    public RectTransform mount;

    [Tooltip("The bar will attach to this transform (read-only at runtime).")]
    public RectTransform attachedTo;

    [Header("Layout")]
    [Tooltip("Zero line position: 0.33 = one third from bottom.")]
    [Range(0.1f, 0.5f)]
    public float zeroFraction = 0.33f;
    [Tooltip("Bar width in pixels (canvas space).")]
    public float barWidth = 12f;
    [Tooltip("Total bar area height in pixels (forward + reverse range).")]
    public float barHeight = 80f;
    [Tooltip("Extra bar container rotation (local euler). Keep (0,0,0) unless you need to correct a facing issue.")]
    public Vector3 barRotation = Vector3.zero;
    [Tooltip("Bar container scale. Use 1 to match sibling (speed text); smaller = tinier bar.")]
    public float barScale = 1f;

    [Header("Placement (tweak in Inspector)")]
    [Tooltip("Bar anchored position in the reticle canvas (pixels).")]
    public Vector2 barAnchoredPosition = new Vector2(-80f, 0f);
    [Tooltip("Bar local Z offset relative to its mount (meters). Use a small negative value to pull it toward the pilot if the screen mesh occludes it.")]
    public float barLocalZ = -0.01f;

    [Header("Display")]
    [Tooltip("If true, bar shows current speed as fraction of ship max speed (fills as you accelerate). If false, bar shows throttle input.")]
    public bool showSpeedNotThrottle = true;

    [Header("Appearance")]
    public Color forwardColor = new Color(0.2f, 0.7f, 0.3f, 0.9f);
    public Color reverseColor = new Color(0.85f, 0.2f, 0.15f, 0.9f);
    [Tooltip("Optional track background.")]
    public Color trackColor = new Color(0.15f, 0.15f, 0.2f, 0.6f);

    RectTransform _containerRect;
    RectTransform _fillRect;
    Image _fillImage;
    bool _built;

    void OnEnable()
    {
        TryBuild();
    }

    void OnValidate()
    {
        // Best-effort: build/refresh in edit mode so you can tweak placement without Play.
        // Guard against missing references while editing prefabs or unloaded scenes.
        if (!isActiveAndEnabled) return;
        _built = false;
        TryBuild();
    }

    void Start()
    {
        TryBuild();
    }

    void LateUpdate()
    {
        if (!_built)
        {
            TryBuild();
            if (!_built) return;
        }

        if (hudManager == null || _fillRect == null || _fillImage == null || _containerRect == null)
            return;

        // Keep layout synced even if the object already existed in-scene.
        _containerRect.sizeDelta = new Vector2(barWidth + 4f, barHeight + 8f);
        _containerRect.anchoredPosition = barAnchoredPosition;
        _containerRect.localRotation = Quaternion.Euler(barRotation.x, barRotation.y, barRotation.z);
        _containerRect.localScale = Vector3.one * barScale;
        var lp = _containerRect.localPosition;
        _containerRect.localPosition = new Vector3(lp.x, lp.y, barLocalZ);

        float value;
        if (showSpeedNotThrottle)
        {
            float signedSpeed = hudManager.GetSignedSpeed();
            float maxSpeed = hudManager.GetMaxSpeed();
            if (maxSpeed < 0.001f) maxSpeed = 1f;
            value = Mathf.Clamp(signedSpeed / maxSpeed, -1f, 1f);
        }
        else
        {
            value = Mathf.Clamp(hudManager.GetThrottle(), -1f, 1f);
            // With gear down, forward authority is reduced: full lever only fills to landingGearThrustScale of the forward segment.
            if (value > 0f && hudManager.flight != null)
                value *= hudManager.flight.GetForwardThrottleAuthority();
        }

        float reverseRange = barHeight * zeroFraction;
        float forwardRange = barHeight * (1f - zeroFraction);

        float height;
        float posY;
        const float minFillHeight = 2f;
        if (value >= 0f)
        {
            height = Mathf.Max(value * forwardRange, value > 0f ? minFillHeight : 0f);
            posY = height * 0.5f;
            _fillImage.color = forwardColor;
        }
        else
        {
            height = Mathf.Max(Mathf.Abs(value) * reverseRange, minFillHeight);
            posY = -height * 0.5f;
            _fillImage.color = reverseColor;
        }

        _fillRect.sizeDelta = new Vector2(barWidth, height);
        _fillRect.anchoredPosition = new Vector2(0f, posY);
    }

    void TryBuild()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (hudManager == null)
            return;

        RectTransform parent = ResolveParentUnderReticleCanvas();
        if (parent == null)
            return;

        Transform existing = parent.Find("ThrottleBar");
        if (existing != null)
        {
            _containerRect = existing as RectTransform;
            _fillRect = existing.Find("Fill") as RectTransform;
            _fillImage = _fillRect != null ? _fillRect.GetComponent<Image>() : null;
            _built = _containerRect != null && _fillRect != null && _fillImage != null;
            return;
        }

        var containerGo = new GameObject("ThrottleBar");
        containerGo.transform.SetParent(parent, false);
        var containerRt = containerGo.AddComponent<RectTransform>();
        containerRt.anchorMin = new Vector2(0.5f, 0.5f);
        containerRt.anchorMax = new Vector2(0.5f, 0.5f);
        containerRt.pivot = new Vector2(0.5f, 0.5f);
        containerRt.anchoredPosition = barAnchoredPosition;
        containerRt.sizeDelta = new Vector2(barWidth + 4f, barHeight + 8f);
        containerRt.localRotation = Quaternion.Euler(barRotation.x, barRotation.y, barRotation.z);
        containerRt.localScale = Vector3.one * barScale;
        {
            var lp = containerRt.localPosition;
            containerRt.localPosition = new Vector3(lp.x, lp.y, barLocalZ);
        }
        _containerRect = containerRt;

        var trackGo = new GameObject("Track");
        trackGo.transform.SetParent(containerRt, false);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.sprite = HUDCanvasHelper.GetImageSprite();
        if (trackImg.sprite == null) trackImg.sprite = HUDCanvasHelper.GetWhiteSprite();
        trackImg.color = trackColor;
        trackImg.raycastTarget = false;
        var trackRt = trackGo.GetComponent<RectTransform>();
        trackRt.anchorMin = Vector2.zero;
        trackRt.anchorMax = Vector2.one;
        trackRt.offsetMin = Vector2.zero;
        trackRt.offsetMax = Vector2.zero;

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(containerRt, false);
        _fillImage = fillGo.AddComponent<Image>();
        _fillImage.sprite = HUDCanvasHelper.GetImageSprite();
        if (_fillImage.sprite == null) _fillImage.sprite = HUDCanvasHelper.GetWhiteSprite();
        _fillImage.color = forwardColor;
        _fillImage.raycastTarget = false;
        _fillRect = fillGo.GetComponent<RectTransform>();
        _fillRect.anchorMin = new Vector2(0.5f, zeroFraction);
        _fillRect.anchorMax = new Vector2(0.5f, zeroFraction);
        _fillRect.pivot = new Vector2(0.5f, 0.5f);
        _fillRect.anchoredPosition = Vector2.zero;
        _fillRect.sizeDelta = new Vector2(barWidth, 2f);

        _built = _containerRect != null && _fillRect != null && _fillImage != null;
    }

    RectTransform ResolveParentUnderReticleCanvas()
    {
        // 1) User-provided mount wins (must be under a Canvas to render).
        if (mount != null)
        {
            attachedTo = mount;
            return mount;
        }

        // 2) Otherwise, create/find a mount under the reticle Canvas (ReticleCanvas created by CanopyReticle).
        Transform canopy = hudManager != null ? hudManager.canopyHUD : null;
        if (canopy == null)
        {
            attachedTo = null;
            return null;
        }

        Canvas reticleCanvas = null;
        var canvases = canopy.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].name == "ReticleCanvas")
            {
                reticleCanvas = canvases[i];
                break;
            }
        }
        if (reticleCanvas == null && canvases.Length > 0)
            reticleCanvas = canvases[0];
        if (reticleCanvas == null)
        {
            attachedTo = null;
            return null;
        }

        var canvasRt = reticleCanvas.GetComponent<RectTransform>();
        if (canvasRt == null)
        {
            attachedTo = null;
            return null;
        }

        var existingMount = canvasRt.Find("ThrottleBarMount") as RectTransform;
        if (existingMount != null)
        {
            mount = existingMount;
            attachedTo = existingMount;
            return existingMount;
        }

        var go = new GameObject("ThrottleBarMount");
        go.transform.SetParent(canvasRt, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        mount = rt;
        attachedTo = rt;
        return rt;
    }

    static Transform FindInDescendants(Transform root, string name)
    {
        if (root == null) return null;
        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindInDescendants(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
