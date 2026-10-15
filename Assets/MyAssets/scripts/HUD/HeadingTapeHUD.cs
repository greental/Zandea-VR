using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedural heading tape for canopy HUD.
/// Renders scrolling heading marks with cardinal labels (N/E/S/W) above center reticle.
/// </summary>
[ExecuteAlways]
public class HeadingTapeHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("Parent RectTransform (under reticle canvas). Auto-resolved if null.")]
    public RectTransform mount;
    [Tooltip("Optional optical/device heading source. When assigned, no ship is required.")]
    public Transform headingSource;

    [Header("Placement")]
    [Tooltip("Anchored position of heading tape relative to reticle canvas.")]
    public Vector2 tapePosition = new Vector2(0f, 90f);
    [Tooltip("Local rotation offset (degrees) for heading tape root.")]
    public Vector3 tapeRotation = Vector3.zero;
    [Tooltip("Local scale for heading tape root.")]
    public float tapeScale = 1f;

    [Header("Layout")]
    [Tooltip("Pixel distance per heading degree.")]
    public float pixelsPerDegree = 3.5f;
    [Tooltip("Show marks this many degrees from center heading.")]
    public float visibleDegrees = 70f;
    [Tooltip("Minor tick step (degrees).")]
    public int minorStep = 10;
    [Tooltip("Major tick step (degrees).")]
    public int majorStep = 30;
    [Tooltip("Height of major ticks.")]
    public float majorTickHeight = 10f;
    [Tooltip("Height of minor ticks.")]
    public float minorTickHeight = 5f;
    [Tooltip("Tick thickness.")]
    public float tickThickness = 1.5f;
    [Tooltip("Vertical offset for labels above ticks.")]
    public float labelYOffset = 10f;

    [Header("Style")]
    public Color tickColor = new Color(0.2f, 1f, 0.3f, 0.8f);
    public Color cardinalColor = new Color(0.2f, 1f, 0.3f, 1f);
    public Color degreeColor = new Color(0.2f, 1f, 0.3f, 0.75f);
    public float cardinalFontSize = 12f;
    public float degreeFontSize = 9f;
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;

    [Header("Center marker")]
    [Tooltip("Show a center caret marker above tape center.")]
    public bool showCenterCaret = true;
    public Color centerCaretColor = new Color(0.2f, 1f, 0.3f, 1f);
    public Vector2 centerCaretSize = new Vector2(8f, 6f);
    public float centerCaretYOffset = 20f;

    RectTransform _tapeContainer;
    Marking[] _markings;
    Rigidbody _rb;
    bool _built;
    bool _needsRebuild;

    struct Marking
    {
        public float headingDeg;
        public RectTransform root;
    }

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
            if (_tapeContainer != null)
            {
                SafeDestroy(_tapeContainer.gameObject);
                _tapeContainer = null;
            }
            _markings = null;
            TryBuild();
        }

        if (!_built) { TryBuild(); if (!_built) return; }

        if (_rb == null && hudManager != null)
            _rb = hudManager.GetRigidbody();
        if ((!headingSource && _rb == null) || _tapeContainer == null || _markings == null || _markings.Length == 0)
            return;

        _tapeContainer.anchoredPosition = tapePosition;
        _tapeContainer.localRotation = Quaternion.Euler(tapeRotation);
        _tapeContainer.localScale = Vector3.one * tapeScale;

        Vector3 flatForward = Vector3.ProjectOnPlane(headingSource ? headingSource.forward : _rb.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f)
            return;

        float heading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        if (heading < 0f) heading += 360f;

        for (int i = 0; i < _markings.Length; i++)
        {
            float delta = Mathf.DeltaAngle(heading, _markings[i].headingDeg);
            if (_markings[i].root == null) continue;
            bool visible = Mathf.Abs(delta) <= visibleDegrees;
            if (_markings[i].root.gameObject.activeSelf != visible)
                _markings[i].root.gameObject.SetActive(visible);
            if (visible)
                _markings[i].root.anchoredPosition = new Vector2(delta * pixelsPerDegree, 0f);
        }
    }

    void TryBuild()
    {
        if (headingSource && mount)
        {
            if (!_built) { BuildTape(mount); _built = true; }
            return;
        }
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (hudManager == null) return;

        if (_rb == null)
            _rb = hudManager.GetRigidbody();

        RectTransform parent = ResolveParent();
        if (parent == null) return;

        Transform existing = parent.Find("HeadingTape");
        if (existing != null && _markings != null && _markings.Length > 0)
        {
            _tapeContainer = existing as RectTransform;
            _built = true;
            return;
        }

        if (existing != null)
            SafeDestroy(existing.gameObject);

        BuildTape(parent);
        _built = true;
    }

    void BuildTape(RectTransform parent)
    {
        var tapeGo = new GameObject("HeadingTape");
        tapeGo.transform.SetParent(parent, false);
        _tapeContainer = tapeGo.AddComponent<RectTransform>();
        _tapeContainer.anchorMin = new Vector2(0.5f, 0.5f);
        _tapeContainer.anchorMax = new Vector2(0.5f, 0.5f);
        _tapeContainer.pivot = new Vector2(0.5f, 0.5f);
        _tapeContainer.anchoredPosition = tapePosition;
        _tapeContainer.localRotation = Quaternion.Euler(tapeRotation);
        _tapeContainer.localScale = Vector3.one * tapeScale;
        _tapeContainer.sizeDelta = Vector2.zero;

        if (showCenterCaret)
            CreateCenterCaret(_tapeContainer);

        int steps = Mathf.Max(1, 360 / Mathf.Max(1, minorStep));
        _markings = new Marking[steps];
        int idx = 0;
        for (int heading = 0; heading < 360; heading += Mathf.Max(1, minorStep))
        {
            if (idx >= _markings.Length) break;
            _markings[idx] = CreateMarking(_tapeContainer, heading);
            idx++;
        }
        if (idx < _markings.Length)
            System.Array.Resize(ref _markings, idx);
    }

    Marking CreateMarking(RectTransform parent, int heading)
    {
        var marking = new Marking { headingDeg = heading };

        bool isMajor = heading % Mathf.Max(1, majorStep) == 0;
        float tickHeight = isMajor ? majorTickHeight : minorTickHeight;

        var rootGo = new GameObject($"Hdg_{heading:000}");
        rootGo.transform.SetParent(parent, false);
        var rootRt = rootGo.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = Vector2.zero;
        rootRt.anchoredPosition = Vector2.zero;
        marking.root = rootRt;

        CreateTick(rootRt, tickHeight);

        if (isMajor)
        {
            string label = HeadingLabel(heading);
            bool isCardinal = label.Length == 1;
            CreateLabel(rootRt, label, isCardinal);
        }

        return marking;
    }

    void CreateTick(RectTransform parent, float height)
    {
        var tickGo = new GameObject("Tick");
        tickGo.transform.SetParent(parent, false);
        var img = tickGo.AddComponent<Image>();
        img.sprite = HUDCanvasHelper.GetWhiteSprite();
        img.color = tickColor;
        img.raycastTarget = false;

        var rt = tickGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(tickThickness, height);

        var outline = tickGo.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1f, 1f);
        outline.useGraphicAlpha = true;
    }

    void CreateLabel(RectTransform parent, string text, bool isCardinal)
    {
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(parent, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.fontSize = isCardinal ? cardinalFontSize : degreeFontSize;
        tmp.color = isCardinal ? cardinalColor : degreeColor;
        ApplyTMPOutlineSafe(tmp, outlineWidth, outlineColor);

        var rt = labelGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, labelYOffset);
        rt.sizeDelta = new Vector2(28f, 14f);
    }

    void CreateCenterCaret(RectTransform parent)
    {
        var go = new GameObject("CenterCaret");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = HUDCanvasHelper.GetWhiteSprite();
        img.color = centerCaretColor;
        img.raycastTarget = false;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, centerCaretYOffset);
        rt.sizeDelta = centerCaretSize;
    }

    static string HeadingLabel(int heading)
    {
        int h = ((heading % 360) + 360) % 360;
        if (h == 0) return "N";
        if (h == 90) return "E";
        if (h == 180) return "S";
        if (h == 270) return "W";
        return h.ToString("000");
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
}
