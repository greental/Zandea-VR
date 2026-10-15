using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a simple center reticle (cross) under the canopy HUD anchor. Ship-space, not head-locked.
/// Add to the same GameObject as ShipHUDManager or to CanopyHUD; ensures a Canvas with cross exists.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ShipHUDManager))]
public class CanopyReticle : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;

    [Header("Appearance")]
    public Color color = new Color(0.2f, 1f, 0.3f, 0.9f);
    [Tooltip("Outline color for reticle lines.")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Tooltip("Outline thickness in canvas pixels.")]
    public Vector2 outlineDistance = new Vector2(1.5f, 1.5f);
    [Tooltip("Width of each line as fraction of screen-like size.")]
    [Range(0.005f, 0.05f)]
    public float lineThickness = 0.015f;
    [Tooltip("Length of each line (half-length from center).")]
    [Range(0.02f, 0.15f)]
    public float lineHalfLength = 0.04f;

    Canvas _canvas;
    RectTransform _root;

    void OnEnable()
    {
        EnsureBuilt();
    }

    void Start()
    {
        EnsureBuilt();
    }

    void OnValidate()
    {
        // Allow building in edit mode so HUD elements can be positioned before Play.
        EnsureBuilt();
    }

    void EnsureBuilt()
    {
        if (hudManager == null)
            hudManager = GetComponent<ShipHUDManager>();
        if (hudManager == null || hudManager.canopyHUD == null) return;

        BuildReticle();
    }

    void BuildReticle()
    {
        Transform anchor = hudManager.canopyHUD;
        // If a reticle canvas already exists, hook references and stop.
        var existing = anchor.Find("ReticleCanvas");
        if (existing != null)
        {
            _canvas = existing.GetComponent<Canvas>();
            _root = existing.GetComponent<RectTransform>();
            return;
        }
        if (anchor.GetComponentInChildren<Canvas>() != null) return; // already built (legacy)

        var go = new GameObject("ReticleCanvas");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = Camera.main;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;
        scaler.referencePixelsPerUnit = 100;
        go.AddComponent<GraphicRaycaster>();

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400, 400);
        rt.localScale = Vector3.one * 0.0001f; // 0.1 of original size for a much smaller cross

        _root = rt;

        // Horizontal line
        CreateLine("H", new Vector2(lineHalfLength * 2f, lineThickness));
        // Vertical line
        CreateLine("V", new Vector2(lineThickness, lineHalfLength * 2f));
    }

    void LateUpdate()
    {
        if (_canvas != null && Camera.main != null)
            _canvas.worldCamera = Camera.main;
    }

    void CreateLine(string name, Vector2 size)
    {
        var go = new GameObject("Line_" + name);
        go.transform.SetParent(_root, false);
        var image = go.AddComponent<Image>();
        image.sprite = HUDCanvasHelper.GetWhiteSprite();
        image.color = color;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.useGraphicAlpha = true;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size * 400f; // scale with our 400 ref
    }
}
