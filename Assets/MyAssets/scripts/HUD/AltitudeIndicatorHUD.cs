using UnityEngine;
using TMPro;
using MyAssets.Planet;

/// <summary>
/// Procedural HUD readout for altitude above sea level.
/// Prefers ActivePlanetSelector for multi-body spherical altitude: the HUD shows 0 until the
/// ship enters a planet/moon activation shell, then displays altitude above that body's surface.
/// If no selector is present, a directly assigned PlanetDefinition uses spherical altitude;
/// otherwise it preserves the legacy world Y minus sea-level behavior.
/// Prefers a Canvas under <see cref="ShipHUDManager.leftScreen"/>; falls back to canopy ReticleCanvas.
/// Lives on the <b>same GameObject</b> as <see cref="ShipHUDManager"/> (required) so Inspector fields are always visible in Edit mode.
/// </summary>
[ExecuteAlways]
public class AltitudeIndicatorHUD : MonoBehaviour, MyAssets.Planet.IFloatingOriginListener
{
    public void OnOriginShift(Vector3 shift)
    {
        if (seaLevelReference == null) seaLevelWorldY -= shift.y;
        effectiveSeaLevelWorldY -= shift.y;
    }
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("World position for altitude (defaults to ship rigidbody). If null, resolved from ShipHUDManager.")]
    public Transform positionSource;
    [Tooltip("Optional: use this transform's world Y as sea level each frame (e.g. water surface). If null, seaLevelWorldY is used.")]
    public Transform seaLevelReference;
    [Tooltip("Parent RectTransform (HUD canvas). Auto-resolved if null.")]
    public RectTransform mount;

    [Header("Planet altitude")]
    [Tooltip("Optional spherical planet source. If assigned, altitude is distance from planet radius instead of world Y.")]
    public PlanetDefinition planet;
    [Tooltip("Preferred multi-planet altitude source. When assigned, altitude is 0 until the ship enters an active planet/moon activation shell.")]
    public ActivePlanetSelector activePlanetSelector;
    [Tooltip("When using Active Planet Selector, display 0 instead of fallback when no planet/moon is active.")]
    public bool zeroAltitudeWhenNoActivePlanet = true;

    [Header("Sea level")]
    [Tooltip("World Y coordinate of sea level when seaLevelReference is not set.")]
    public float seaLevelWorldY = 0f;

    [Header("Inspector — live values")]
    [Tooltip("Meters above sea level (same as HUD). Updates every frame in Play Mode.")]
    [SerializeField] float currentAltitudeMeters;
    [Tooltip("Sea level world Y used for the last frame in legacy flat mode. In planet mode this is 0.")]
    [SerializeField] float effectiveSeaLevelWorldY;

    [Header("Inspector — test override")]
    [Tooltip("When enabled, HUD and Current Altitude use Altitude Override (m) instead of the ship position.")]
    public bool useAltitudeOverride;
    [Tooltip("Manual altitude in meters to display when override is on.")]
    public float altitudeOverrideMeters;

    [Header("Placement")]
    public Vector2 labelPosition = new Vector2(-120f, -130f);
    public float labelScale = 1f;
    public float fontSize = 10f;

    [Header("Display")]
    [Tooltip("Digits after decimal point.")]
    [Range(0, 6)]
    public int decimalPlaces = 0;
    public string prefix = "ALT";
    public string unitSuffix = " m";
    public string fallbackLabel = "ALT --";

    [Header("Colors")]
    public Color normalColor = new Color(0.35f, 0.85f, 1f, 0.95f);
    public Color negativeColor = new Color(1f, 0.5f, 0.35f, 0.95f);
    public Color fallbackColor = new Color(1f, 0.45f, 0.2f, 0.75f);

    [Header("Outline")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;

    RectTransform _container;
    TextMeshProUGUI _altText;
    bool _built;
    bool _needsRebuild;

    void OnEnable() { TryBuild(); }
    void Start() { TryBuild(); }

    void OnValidate()
    {
        _needsRebuild = true;
        if (!Application.isPlaying)
            RefreshAltitudeLabelPreview();
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
            _altText = null;
            TryBuild();
        }

        if (!_built) { TryBuild(); if (!_built) return; }
        if (_container == null || _altText == null) return;

        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;

        RefreshAltitudeLabel();
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

        Transform existing = parent.Find("AltitudeLabel");
        if (existing != null && _altText != null)
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
        var go = new GameObject("AltitudeLabel");
        go.transform.SetParent(parent, false);
        _container = go.AddComponent<RectTransform>();
        _container.anchorMin = new Vector2(0.5f, 0.5f);
        _container.anchorMax = new Vector2(0.5f, 0.5f);
        _container.pivot = new Vector2(0.5f, 0.5f);
        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;
        _container.sizeDelta = new Vector2(140f, 18f);

        _altText = go.AddComponent<TextMeshProUGUI>();
        _altText.text = fallbackLabel;
        _altText.fontSize = fontSize;
        _altText.color = fallbackColor;
        _altText.alignment = TextAlignmentOptions.Center;
        _altText.textWrappingMode = TextWrappingModes.NoWrap;
        _altText.overflowMode = TextOverflowModes.Overflow;
        _altText.raycastTarget = false;
        ApplyTMPOutlineSafe(_altText, outlineWidth, outlineColor);
    }

    void RefreshAltitudeLabel()
    {
        if (_altText == null) return;

        if (!TryComputeAltitude(out float altitude, out float seaY))
        {
            effectiveSeaLevelWorldY = planet != null ? 0f : (seaLevelReference != null ? seaLevelReference.position.y : seaLevelWorldY);
            currentAltitudeMeters = 0f;
            _altText.text = fallbackLabel;
            _altText.color = fallbackColor;
            return;
        }

        effectiveSeaLevelWorldY = seaY;
        currentAltitudeMeters = altitude;

        string fmt = decimalPlaces <= 0 ? "F0" : ("F" + decimalPlaces);
        _altText.text = $"{prefix} {altitude.ToString(fmt)}{unitSuffix}";
        _altText.color = altitude < 0f ? negativeColor : normalColor;
    }

    /// <summary>Updates inspector fields when not playing (e.g. after editing sea level or override).</summary>
    void RefreshAltitudeLabelPreview()
    {
        if (TryComputeAltitude(out float altitude, out float seaY))
        {
            effectiveSeaLevelWorldY = seaY;
            currentAltitudeMeters = altitude;
        }
        else
        {
            effectiveSeaLevelWorldY = planet != null ? 0f : (seaLevelReference != null ? seaLevelReference.position.y : seaLevelWorldY);
            currentAltitudeMeters = 0f;
        }
    }

    bool TryComputeAltitude(out float altitude, out float seaY)
    {
        altitude = 0f;
        seaY = seaLevelReference != null ? seaLevelReference.position.y : seaLevelWorldY;

        if (useAltitudeOverride)
        {
            altitude = altitudeOverrideMeters;
            return true;
        }

        if (hudManager == null)
            return false;

        Rigidbody rb = hudManager.GetRigidbody();
        if (positionSource == null && rb != null)
            positionSource = rb.transform;

        if (positionSource == null)
            return false;

        if (activePlanetSelector == null)
            activePlanetSelector = FindFirstObjectByType<ActivePlanetSelector>();

        if (activePlanetSelector != null)
        {
            if (activePlanetSelector.Target == null)
                activePlanetSelector.ConfigureTarget(positionSource, rb);

            seaY = 0f;
            PlanetDefinition activePlanet = activePlanetSelector.ActivePlanet;
            if (activePlanet != null)
            {
                altitude = activePlanetSelector.ActiveAltitudeMeters;
                return true;
            }

            altitude = 0f;
            return zeroAltitudeWhenNoActivePlanet;
        }

        if (planet != null)
        {
            seaY = 0f;
            altitude = planet.GetAltitude(positionSource.position);
            return true;
        }

        seaY = seaLevelReference != null ? seaLevelReference.position.y : seaLevelWorldY;
        altitude = positionSource.position.y - seaY;
        return true;
    }

    RectTransform ResolveParent()
    {
        if (mount != null) return mount;
        if (hudManager == null) return null;

        if (hudManager.leftScreen != null)
        {
            var canvases = hudManager.leftScreen.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].name == "LeftScreenCanvas")
                {
                    mount = canvases[i].GetComponent<RectTransform>();
                    return mount;
                }
            }
            if (canvases.Length > 0)
            {
                mount = canvases[0].GetComponent<RectTransform>();
                return mount;
            }
        }

        Transform canopy = hudManager.canopyHUD;
        if (canopy == null) return null;

        Canvas reticleCanvas = null;
        var canopyCanvases = canopy.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canopyCanvases.Length; i++)
        {
            if (canopyCanvases[i] != null && canopyCanvases[i].name == "ReticleCanvas")
            {
                reticleCanvas = canopyCanvases[i];
                break;
            }
        }
        if (reticleCanvas == null && canopyCanvases.Length > 0)
            reticleCanvas = canopyCanvases[0];
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
            // Ignore outline setup when TMP material pipeline is not ready.
        }
    }
}
