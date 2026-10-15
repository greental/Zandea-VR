using UnityEngine;
using TMPro;

/// <summary>
/// Drives the warning banner in front of the pilot. Finds or creates a TMP_Text for the message.
/// </summary>
public class WarningBannerDisplay : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    public WarningMessageSystem warningSystem;
    [Tooltip("Existing TextMeshPro (TMP_Text) for the warning banner. Left null to auto-find or create.")]
    public TMP_Text bannerText;

    [Header("Appearance (used when creating banner at runtime)")]
    public Color bannerColor = new Color(1f, 0.35f, 0.35f, 1f);
    public float fontSize = 0.5f;

    bool _createdBanner;

    void Start()
    {
        ResolveReferences();
    }

    void LateUpdate()
    {
        if (bannerText == null)
            ResolveReferences();

        if (bannerText == null) return;

        string msg = warningSystem != null ? warningSystem.CurrentMessage : null;
        bannerText.text = msg ?? string.Empty;
    }

    void ResolveReferences()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (warningSystem == null)
            warningSystem = FindFirstObjectByType<WarningMessageSystem>();

        if (bannerText != null) return;

        bannerText = GetComponent<TMP_Text>();
        if (bannerText != null) return;
        bannerText = GetComponentInChildren<TMP_Text>(true);
        if (bannerText != null) return;

        Transform wb = hudManager != null ? hudManager.warningBanner : null;
        if (wb == null && hudManager != null && hudManager.hudRoot != null)
        {
            wb = FindInDescendants(hudManager.hudRoot, "WarningBanner");
            if (wb == null) wb = FindInDescendants(hudManager.hudRoot, "warningBanner");
        }
        if (wb != null)
        {
            bannerText = wb.GetComponentInChildren<TMP_Text>(true);
            if (bannerText != null) return;
        }

        // Prefer any TMP under hudRoot that is the warning banner (by name), not the speed readout
        if (hudManager != null && hudManager.hudRoot != null)
        {
            var all = hudManager.hudRoot.GetComponentsInChildren<TMP_Text>(true);
            foreach (var tmp in all)
            {
                if (tmp.name.Contains("peed") || tmp.name.Contains("Speed") || tmp.name.Contains("speedIndicator")) continue;
                if (tmp.name.Contains("arning") || tmp.name.Contains("warning") || tmp.name.Contains("Warning"))
                {
                    bannerText = tmp;
                    return;
                }
            }
            foreach (var tmp in all)
            {
                if (tmp.name.Contains("peed") || tmp.name.Contains("Speed")) continue;
                bannerText = tmp;
                return;
            }
        }

        // No TMP found: create on the reticle canvas so it's visible
        if (bannerText == null && !_createdBanner && hudManager != null && hudManager.canopyHUD != null)
        {
            var canvas = hudManager.canopyHUD.GetComponentInChildren<Canvas>();
            if (canvas != null)
            {
                CreateBannerOnCanvas(canvas.GetComponent<RectTransform>());
                return;
            }
        }
        if (bannerText == null && !_createdBanner && wb != null)
            CreateBannerTMP(wb);
    }

    static Transform FindInDescendants(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindInDescendants(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    void CreateBannerTMP(Transform parent)
    {
        _createdBanner = true;
        var go = new GameObject("WarningBannerText");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * fontSize;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "";
        tmp.color = bannerColor;
        tmp.fontSize = 36;
        tmp.alignment = TextAlignmentOptions.Center;

        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            tmp.font = font;

        bannerText = tmp;
    }

    void CreateBannerOnCanvas(RectTransform canvasRect)
    {
        if (canvasRect == null) return;
        _createdBanner = true;

        var go = new GameObject("WarningBannerText");
        go.transform.SetParent(canvasRect, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.color = bannerColor;
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.Top;
        tmp.enableAutoSizing = false;

        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            tmp.font = font;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -20f);
        rt.sizeDelta = new Vector2(320f, 40f);

        bannerText = tmp;
    }
}
