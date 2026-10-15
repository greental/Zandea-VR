using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedural canopy HUD label showing current flight mode (DIRECT / TARGET / DRIFT).
/// Auto-creates its own TMP element under ReticleCanvas. Also exposes button-callable mode switching.
/// </summary>
[ExecuteAlways]
public class FlightModeIndicatorHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("Parent RectTransform (under reticle canvas). Auto-resolved if null.")]
    public RectTransform mount;

    [Header("Placement")]
    public Vector2 labelPosition = new Vector2(-120f, -70f);
    public float labelScale = 1f;
    public float fontSize = 11f;

    [Header("Labels")]
    public string directLabel = "DIRECT";
    public string targetLabel = "TARGET";
    public string driftLabel = "DRIFT";
    public string fallbackLabel = "MODE --";
    [Tooltip("Label to show when systems are offline (e.g. engines off).")]
    public string offlineLabel = "MODE OFF";

    [Header("Colors")]
    public Color directColor = new Color(0.2f, 1f, 0.3f, 0.9f);
    public Color targetColor = new Color(0.35f, 0.8f, 1f, 0.95f);
    public Color driftColor = new Color(1f, 0.85f, 0.35f, 0.95f);
    public Color fallbackColor = new Color(1f, 0.45f, 0.2f, 0.95f);

    [Header("Outline")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;

    RectTransform _container;
    TextMeshProUGUI _modeText;
    bool _built;
    bool _needsRebuild;
    bool _systemOnline = true;

    void OnEnable() { TryBuild(); }
    void Start() { TryBuild(); }

    void OnValidate()
    {
        _needsRebuild = true;
    }

    void LateUpdate()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            _built = false;
            if (_container != null)
            {
                SafeDestroy(_container.gameObject);
                _container = null;
            }
            _modeText = null;
            TryBuild();
        }

        if (!_built) { TryBuild(); if (!_built) return; }
        if (_container == null || _modeText == null) return;

        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;

        RefreshModeLabel();
    }

    void TryBuild()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (hudManager == null) return;

        RectTransform parent = ResolveParent();
        if (parent == null) return;

        Transform existing = parent.Find("FlightModeLabel");
        if (existing != null && _modeText != null)
        {
            _container = existing as RectTransform;
            _built = true;
            return;
        }

        if (existing != null)
            SafeDestroy(existing.gameObject);

        BuildLabel(parent);
        _built = true;
    }

    void BuildLabel(RectTransform parent)
    {
        var go = new GameObject("FlightModeLabel");
        go.transform.SetParent(parent, false);
        _container = go.AddComponent<RectTransform>();
        _container.anchorMin = new Vector2(0.5f, 0.5f);
        _container.anchorMax = new Vector2(0.5f, 0.5f);
        _container.pivot = new Vector2(0.5f, 0.5f);
        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;
        _container.sizeDelta = new Vector2(60f, 18f);

        _modeText = go.AddComponent<TextMeshProUGUI>();
        _modeText.text = fallbackLabel;
        _modeText.fontSize = fontSize;
        _modeText.color = fallbackColor;
        _modeText.alignment = TextAlignmentOptions.Center;
        _modeText.textWrappingMode = TextWrappingModes.NoWrap;
        _modeText.overflowMode = TextOverflowModes.Overflow;
        _modeText.raycastTarget = false;
        ApplyTMPOutlineSafe(_modeText, outlineWidth, outlineColor);
    }

    void RefreshModeLabel()
    {
        if (_modeText == null) return;

        if (!_systemOnline)
        {
            _modeText.text = offlineLabel;
            _modeText.color = fallbackColor;
            return;
        }

        var flight = hudManager != null ? hudManager.flight : null;
        if (flight == null)
        {
            _modeText.text = fallbackLabel;
            _modeText.color = fallbackColor;
            return;
        }

        switch (flight.flightModelMode)
        {
            case FlightModelMode.DirectTorque:
                _modeText.text = directLabel;
                _modeText.color = directColor;
                break;
            case FlightModelMode.TargetOrientation:
                _modeText.text = targetLabel;
                _modeText.color = targetColor;
                break;
            case FlightModelMode.InertialDrift:
                _modeText.text = driftLabel;
                _modeText.color = driftColor;
                break;
            default:
                _modeText.text = fallbackLabel;
                _modeText.color = fallbackColor;
                break;
        }
    }

    // --- Button-callable helpers ---
    public void SetDirectMode() { SetMode(FlightModelMode.DirectTorque); }
    public void SetTargetMode() { SetMode(FlightModelMode.TargetOrientation); }
    public void SetDriftMode() { SetMode(FlightModelMode.InertialDrift); }

    public void CycleMode()
    {
        var flight = hudManager != null ? hudManager.flight : null;
        if (flight == null) return;

        switch (flight.flightModelMode)
        {
            case FlightModelMode.DirectTorque:
                flight.flightModelMode = FlightModelMode.TargetOrientation;
                break;
            case FlightModelMode.TargetOrientation:
                flight.flightModelMode = FlightModelMode.InertialDrift;
                break;
            default:
                flight.flightModelMode = FlightModelMode.DirectTorque;
                break;
        }
        RefreshModeLabel();
    }

    void SetMode(FlightModelMode mode)
    {
        var flight = hudManager != null ? hudManager.flight : null;
        if (flight == null) return;
        flight.flightModelMode = mode;
        RefreshModeLabel();
    }

    public void SetSystemOnline(bool online)
    {
        _systemOnline = online;
        RefreshModeLabel();
    }

    RectTransform ResolveParent()
    {
        if (mount != null) return mount;

        Transform canopy = hudManager != null ? hudManager.canopyHUD : null;
        if (canopy == null) return null;

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
        if (reticleCanvas == null) return null;

        mount = reticleCanvas.GetComponent<RectTransform>();
        return mount;
    }

    static void SafeDestroy(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    static void ApplyTMPOutlineSafe(TextMeshProUGUI tmp, float width, Color color)
    {
        if (tmp == null) return;
        try
        {
            if (tmp.font == null && TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            if (tmp.font == null || tmp.fontSharedMaterial == null)
                return;

            var mat = tmp.fontMaterial;
            if (mat == null) return;
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, color);
        }
        catch
        {
            // Some TMP setups can have null source materials at runtime; skip outline safely.
        }
    }
}
