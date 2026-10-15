using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Procedural canopy HUD label showing landing gear state (GEAR DN / GEAR UP / transitioning).
/// Auto-creates its own TMP element under ReticleCanvas.
/// </summary>
[ExecuteAlways]
public class LandingGearStatusHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    public LandingGearController gearController;
    [Tooltip("Parent RectTransform (under reticle canvas). Auto-resolved if null.")]
    public RectTransform mount;

    [Header("Placement")]
    public Vector2 labelPosition = new Vector2(-120f, -85f);
    public float labelScale = 1f;
    public float fontSize = 10f;

    [Header("Labels")]
    public string deployedLabel = "GEAR DN";
    public string foldedLabel = "GEAR UP";
    public string transitionLabel = "GEAR...";
    public string fallbackLabel = "GEAR --";

    [Header("Colors")]
    public Color deployedColor = new Color(0.2f, 1f, 0.3f, 0.9f);
    public Color foldedColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
    public Color transitionColor = new Color(1f, 0.85f, 0.35f, 0.95f);
    public Color fallbackColor = new Color(1f, 0.45f, 0.2f, 0.7f);

    [Header("Transition blink")]
    [Tooltip("Blink the label while gear is moving.")]
    public bool blinkDuringTransition = true;
    [Tooltip("Blinks per second during transition.")]
    public float blinkRate = 3f;

    [Header("Outline")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Range(0f, 1f)]
    public float outlineWidth = 0.2f;

    RectTransform _container;
    TextMeshProUGUI _gearText;
    bool _built;
    bool _needsRebuild;

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
            _gearText = null;
            TryBuild();
        }

        if (!_built) { TryBuild(); if (!_built) return; }
        if (_container == null || _gearText == null) return;

        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;

        RefreshGearLabel();
    }

    void TryBuild()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();

        if (gearController == null)
            gearController = GetComponentInParent<LandingGearController>();
        if (gearController == null)
            gearController = FindFirstObjectByType<LandingGearController>();

        if (hudManager == null) return;

        RectTransform parent = ResolveParent();
        if (parent == null) return;

        Transform existing = parent.Find("GearStatusLabel");
        if (existing != null && _gearText != null)
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
        var go = new GameObject("GearStatusLabel");
        go.transform.SetParent(parent, false);
        _container = go.AddComponent<RectTransform>();
        _container.anchorMin = new Vector2(0.5f, 0.5f);
        _container.anchorMax = new Vector2(0.5f, 0.5f);
        _container.pivot = new Vector2(0.5f, 0.5f);
        _container.anchoredPosition = labelPosition;
        _container.localScale = Vector3.one * labelScale;
        _container.sizeDelta = new Vector2(60f, 16f);

        _gearText = go.AddComponent<TextMeshProUGUI>();
        _gearText.text = fallbackLabel;
        _gearText.fontSize = fontSize;
        _gearText.color = fallbackColor;
        _gearText.alignment = TextAlignmentOptions.Center;
        _gearText.textWrappingMode = TextWrappingModes.NoWrap;
        _gearText.overflowMode = TextOverflowModes.Overflow;
        _gearText.raycastTarget = false;
        ApplyTMPOutlineSafe(_gearText, outlineWidth, outlineColor);
    }

    void RefreshGearLabel()
    {
        if (_gearText == null) return;

        if (gearController == null)
        {
            _gearText.text = fallbackLabel;
            _gearText.color = fallbackColor;
            return;
        }

        if (gearController.IsMoving)
        {
            _gearText.text = transitionLabel;
            _gearText.color = transitionColor;

            if (blinkDuringTransition)
            {
                float alpha = (Mathf.Sin(Time.time * blinkRate * Mathf.PI * 2f) + 1f) * 0.5f;
                var c = transitionColor;
                c.a *= Mathf.Lerp(0.3f, 1f, alpha);
                _gearText.color = c;
            }
        }
        else if (gearController.gearDeployed)
        {
            _gearText.text = deployedLabel;
            _gearText.color = deployedColor;
        }
        else
        {
            _gearText.text = foldedLabel;
            _gearText.color = foldedColor;
        }
    }

    // --- Button-callable helpers ---
    public void ToggleGear()
    {
        if (gearController != null) gearController.ToggleGear();
    }

    public void DeployGear()
    {
        if (gearController != null) gearController.SetGearDeployed(true);
    }

    public void FoldGear()
    {
        if (gearController != null) gearController.SetGearDeployed(false);
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
