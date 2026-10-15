using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedural pitch ladder on the canopy HUD. Horizontal lines at regular degree intervals
/// scroll with ship pitch and rotate with ship roll so the horizon stays level.
/// Positive pitch lines have upward tick marks; negative lines are dashed with downward ticks.
/// </summary>
[ExecuteAlways]
public class PitchLadderHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("Parent RectTransform (under reticle canvas). Auto-resolved if null.")]
    public RectTransform mount;

    [Header("Placement (tweak in Inspector)")]
    [Tooltip("Anchored position of the ladder container relative to its parent canvas.")]
    public Vector2 ladderPosition = Vector2.zero;
    [Tooltip("Extra local rotation applied to the ladder container (before roll compensation).")]
    public Vector3 ladderRotationOffset = Vector3.zero;
    [Tooltip("Flip pitch ladder roll direction if your HUD mount orientation is mirrored.")]
    public bool invertRollDirection = false;
    [Tooltip("Multiplier for roll response (1 = normal, 0 = static, 2 = exaggerated).")]
    public float rollResponseScale = 1f;
    [Tooltip("Scale of the ladder container.")]
    public float ladderScale = 1f;

    [Header("Layout")]
    [Tooltip("Degrees between each pitch marking.")]
    public float degreeSpacing = 5f;
    [Tooltip("Pixels per degree of pitch (controls scroll speed / density).")]
    public float pixelsPerDegree = 8f;
    [Tooltip("Max pitch angle to generate markings for (e.g. 90 = -90 to +90).")]
    public float maxPitchRange = 90f;
    [Tooltip("Lines further than this (pixels) from center are hidden.")]
    public float visibleRange = 120f;
    [Tooltip("Half-gap width at center so the reticle cross stays visible.")]
    public float centerGap = 20f;
    [Tooltip("Length of each line segment (pixels).")]
    public float lineLength = 40f;

    [Header("Horizon line (0 deg)")]
    public float horizonLineLength = 80f;
    public float horizonThickness = 2.5f;
    public Color horizonColor = new Color(0.2f, 1f, 0.3f, 0.9f);

    [Header("Positive pitch (above horizon)")]
    public float positiveThickness = 1.5f;
    public Color positiveColor = new Color(0.2f, 1f, 0.3f, 0.7f);

    [Header("Negative pitch (below horizon)")]
    public float negativeThickness = 1.5f;
    public Color negativeColor = new Color(0.2f, 1f, 0.3f, 0.5f);
    [Tooltip("Use dashed lines for negative pitch markings.")]
    public bool dashedNegative = true;
    [Tooltip("Number of dash segments per line side (when dashed).")]
    [Range(2, 6)]
    public int dashSegments = 3;

    [Header("Tick marks")]
    [Tooltip("Height of the tick at line ends (points up for positive, down for negative).")]
    public float tickHeight = 6f;

    [Header("Labels")]
    public float labelFontSize = 10f;
    public Color labelColor = new Color(0.2f, 1f, 0.3f, 0.8f);
    [Tooltip("Distance from line end to label center.")]
    public float labelOffset = 18f;

    [Header("Outline")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    public Vector2 outlineDistance = new Vector2(1f, 1f);

    RectTransform _ladderContainer;
    PitchMarking[] _markings;
    bool _built;
    bool _needsRebuild;
    Rigidbody _rb;

    struct PitchMarking
    {
        public float degree;
        public RectTransform root;
    }

    void OnEnable() { TryBuild(); }
    void Start() { TryBuild(); }

    void OnValidate()
    {
        // Can't create GameObjects/components in OnValidate (SendMessage restriction).
        // Defer the rebuild to next LateUpdate.
        _needsRebuild = true;
    }

    void LateUpdate()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            _built = false;
            if (_ladderContainer != null)
            {
                SafeDestroy(_ladderContainer.gameObject);
                _ladderContainer = null;
            }
            _markings = null;
            TryBuild();
        }

        if (!_built) { TryBuild(); if (!_built) return; }

        // Re-resolve rigidbody if it wasn't available during build (edit mode).
        if (_rb == null && hudManager != null)
            _rb = hudManager.GetRigidbody();
        if (_rb == null || hudManager == null || _ladderContainer == null || _markings == null || _markings.Length == 0) return;

        Transform ship = _rb.transform;
        Vector3 fwd = ship.forward;
        float pitchDeg = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;

        // Roll: signed angle between ship.up and world up, measured around ship.forward.
        float rollDeg = Mathf.Atan2(
            Vector3.Dot(ship.right, Vector3.up),
            Vector3.Dot(ship.up, Vector3.up)
        ) * Mathf.Rad2Deg;

        // Apply placement: position, scale, and rotation (offset + roll compensation).
        float rollDirection = invertRollDirection ? -1f : 1f;
        float compensatedRollDeg = rollDeg * rollDirection * rollResponseScale;
        _ladderContainer.anchoredPosition = ladderPosition;
        _ladderContainer.localScale = Vector3.one * ladderScale;
        _ladderContainer.localRotation =
            Quaternion.Euler(ladderRotationOffset.x, ladderRotationOffset.y, ladderRotationOffset.z + compensatedRollDeg);

        for (int i = 0; i < _markings.Length; i++)
        {
            float yOffset = (_markings[i].degree - pitchDeg) * pixelsPerDegree;
            if (_markings[i].root == null) continue;
            bool visible = Mathf.Abs(yOffset) <= visibleRange;
            if (_markings[i].root.gameObject.activeSelf != visible)
                _markings[i].root.gameObject.SetActive(visible);
            if (visible)
                _markings[i].root.anchoredPosition = new Vector2(0f, yOffset);
        }
    }

    void TryBuild()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (hudManager == null) return;

        if (_rb == null)
            _rb = hudManager.GetRigidbody();

        RectTransform parent = ResolveParent();
        if (parent == null) return;

        Transform existing = parent.Find("PitchLadder");
        if (existing != null && _markings != null && _markings.Length > 0)
        {
            _ladderContainer = existing as RectTransform;
            _built = true;
            return;
        }

        if (existing != null)
            SafeDestroy(existing.gameObject);

        BuildLadder(parent);
        _built = true;
    }

    void BuildLadder(RectTransform parent)
    {
        var containerGo = new GameObject("PitchLadder");
        containerGo.transform.SetParent(parent, false);
        _ladderContainer = containerGo.AddComponent<RectTransform>();
        _ladderContainer.anchorMin = new Vector2(0.5f, 0.5f);
        _ladderContainer.anchorMax = new Vector2(0.5f, 0.5f);
        _ladderContainer.pivot = new Vector2(0.5f, 0.5f);
        _ladderContainer.anchoredPosition = ladderPosition;
        _ladderContainer.localScale = Vector3.one * ladderScale;
        _ladderContainer.sizeDelta = Vector2.zero;

        int count = Mathf.RoundToInt(maxPitchRange * 2f / degreeSpacing) + 1;
        _markings = new PitchMarking[count];
        int idx = 0;
        for (float deg = -maxPitchRange; deg <= maxPitchRange + 0.01f; deg += degreeSpacing)
        {
            if (idx >= count) break;
            _markings[idx] = CreateMarking(deg);
            idx++;
        }
        if (idx < count)
            System.Array.Resize(ref _markings, idx);
    }

    PitchMarking CreateMarking(float degree)
    {
        var marking = new PitchMarking { degree = degree };

        bool isHorizon = Mathf.Abs(degree) < 0.01f;
        bool isNegative = degree < -0.01f;

        float thickness = isHorizon ? horizonThickness : (isNegative ? negativeThickness : positiveThickness);
        Color color = isHorizon ? horizonColor : (isNegative ? negativeColor : positiveColor);
        float len = isHorizon ? horizonLineLength : lineLength;
        float gap = centerGap;

        string markName = isHorizon ? "H" : degree.ToString("+0;-0");
        var rootGo = new GameObject($"Pitch_{markName}");
        rootGo.transform.SetParent(_ladderContainer, false);
        var rootRt = rootGo.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;
        rootRt.sizeDelta = Vector2.zero;
        marking.root = rootRt;

        if (isNegative && dashedNegative && dashSegments >= 2)
        {
            float segLen = len / (2f * dashSegments - 1f);
            for (int s = 0; s < dashSegments; s++)
            {
                float fromGap = (2f * s + 0.5f) * segLen;
                CreateLine(rootRt, $"LD{s}", color, thickness,
                    new Vector2(-(gap + fromGap), 0f),
                    new Vector2(segLen, thickness));
                CreateLine(rootRt, $"RD{s}", color, thickness,
                    new Vector2(gap + fromGap, 0f),
                    new Vector2(segLen, thickness));
            }
        }
        else
        {
            CreateLine(rootRt, "L", color, thickness,
                new Vector2(-(gap + len * 0.5f), 0f),
                new Vector2(len, thickness));
            CreateLine(rootRt, "R", color, thickness,
                new Vector2(gap + len * 0.5f, 0f),
                new Vector2(len, thickness));
        }

        if (!isHorizon)
        {
            float tickDir = isNegative ? -1f : 1f;
            CreateLine(rootRt, "LT", color, thickness,
                new Vector2(-(gap + len), tickDir * tickHeight * 0.5f),
                new Vector2(thickness, tickHeight));
            CreateLine(rootRt, "RT", color, thickness,
                new Vector2(gap + len, tickDir * tickHeight * 0.5f),
                new Vector2(thickness, tickHeight));
        }

        if (!isHorizon)
        {
            string text = degree.ToString("0;-0");
            CreateLabel(rootRt, "LL", text,
                new Vector2(-(gap + len + labelOffset), 0f));
            CreateLabel(rootRt, "RL", text,
                new Vector2(gap + len + labelOffset, 0f));
        }

        return marking;
    }

    void CreateLine(RectTransform parent, string name, Color color, float thickness,
        Vector2 center, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = HUDCanvasHelper.GetWhiteSprite();
        img.color = color;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.useGraphicAlpha = true;
    }

    void CreateLabel(RectTransform parent, string name, string text, Vector2 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = labelFontSize;
        tmp.color = labelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        ApplyTMPOutlineSafe(tmp, outlineDistance.x * 0.15f, outlineColor);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(30f, 14f);
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
            // Ignore outline setup when TMP material pipeline is not ready.
        }
    }
}
