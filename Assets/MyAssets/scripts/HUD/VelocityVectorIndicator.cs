using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows ship velocity direction on the canopy HUD (where the ship is going vs where it's pointing).
/// Uses an existing UI Image (yellow dot) inside your canopy Canvas instead of creating canvases at runtime.
/// </summary>
public class VelocityVectorIndicator : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("RectTransform for the ship reticle (your 'o' TextMeshPro) used as the center.")]
    public RectTransform reticleRoot;
    [Tooltip("RectTransform for the yellow velocity dot Image to move around the reticle.")]
    public RectTransform indicatorRect;

    [Header("Layout")]
    [Tooltip("Max offset from the reticle (in RectTransform units) when velocity is full forward/sideways.")]
    [Range(10f, 500f)]
    public float maxOffset = 220f;
    [Tooltip("Scale velocity so indicator moves more with speed. Higher = more movement.")]
    [Range(0.2f, 5f)]
    public float sensitivity = 1.5f;
    [Tooltip("Optional override sensitivity set by another script at runtime. If >= 0, this value is used instead of Sensitivity.")]
    public float externalSensitivityOverride = -1f;
    [Tooltip("Speed (m/s) at which the indicator reaches full deflection (before sensitivity).")]
    public float speedForFullDeflection = 40f;
    [Tooltip("Treat very small direction offsets as centered (prevents lingering off-center).")]
    [Range(0.001f, 0.2f)]
    public float directionDeadzone = 0.04f;

    [Header("Lateral (strafe)")]
    [Tooltip("Blends how much commanded strafe is removed from the forward velocity term (higher = cancel more strafe from path vector). Independent of HUD strafe wiggle.")]
    [Range(0f, 1.5f)]
    public float lateralSensitivity = 0.6f;
    [Tooltip("Extra dampening for strafe-only HUD deflection only. Forward path uses dirForward; strafe is added separately. Lower = less reticle travel when strafing (try 0.15–0.35).")]
    [Range(0f, 1f)]
    public float strafeReticleScale = 0.22f;
    [Tooltip("Optional max length of the strafe HUD vector (before maxOffset). 0 = no cap. When enabled, can make the dot feel like it ‘fights’ or snaps near full stick — use 0 unless you need a cap.")]
    [Range(0f, 2f)]
    public float maxStrafeReticleMagnitude = 0f;
    [Tooltip("Invert horizontal strafe direction on the reticle.")]
    public bool invertStrafeX = false;
    [Tooltip("Invert vertical strafe direction on the reticle.")]
    public bool invertStrafeY = false;

    [Header("Smoothing")]
    [Tooltip("Seconds to smooth when following velocity changes (smaller = snappier).")]
    [Range(0.01f, 1f)]
    public float followSmoothTime = 0.12f;
    [Tooltip("Seconds to smooth when returning to center (bigger = slower return).")]
    [Range(0.02f, 2f)]
    public float returnSmoothTime = 0.25f;
    [Tooltip("Below this speed, the target offset becomes zero (but still smoothed).")]
    public float centerSpeedThreshold = 0.8f;

    [Header("Appearance")]
    [Tooltip("Sprite for the velocity dot (e.g. Circle_60x60 Outline 4 from XR samples). Leave empty to use a simple white circle.")]
    public Sprite velocityDotSprite;
    [Tooltip("Color of the velocity indicator dot.")]
    public Color indicatorColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Outline color for the velocity dot.")]
    public Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
    [Tooltip("Outline thickness for the velocity dot.")]
    public Vector2 outlineDistance = new Vector2(1.5f, 1.5f);

    Rigidbody _rb;
    Vector2 _smoothVel;

    public float EffectiveSensitivity => externalSensitivityOverride >= 0f ? externalSensitivityOverride : sensitivity;

    /// <summary>Allow other systems (settings/UI) to tune sensitivity at runtime.</summary>
    public void SetSensitivity(float newSensitivity)
    {
        externalSensitivityOverride = Mathf.Max(0f, newSensitivity);
    }

    void Start()
    {
        TryInit();
    }

    void LateUpdate()
    {
        // In case CanopyReticle built its Canvas after our Start, keep trying to wire up.
        if (indicatorRect == null || _rb == null || hudManager == null || hudManager.canopyHUD == null)
        {
            TryInit();
            if (indicatorRect == null || _rb == null || hudManager == null || hudManager.canopyHUD == null)
                return;
        }

        Transform anchor = hudManager.canopyHUD;
        Vector3 vel = _rb.linearVelocity;
        float speed = vel.magnitude;

        Vector2 targetOffset = Vector2.zero;

        if (speed >= centerSpeedThreshold)
        {
            Vector3 velLocal = anchor.InverseTransformDirection(vel);
            // Use ratios against forward component so "mostly forward" => near center.
            // This avoids the high-speed behavior where normalizing makes tiny vertical drift pin the dot to the edge.
            float z = Mathf.Abs(velLocal.z);
            {
                Vector2 dirForward = Vector2.zero;
                if (z >= 0.1f)
            {
                // Forward flight/path vector (pitch/yaw behavior) from velocity direction.
                float compX = velLocal.x;
                float compY = velLocal.y;
                if (hudManager != null && hudManager.hotas != null)
                {
                    // Cancel commanded strafe from forward term. When lateralSensitivity=0, cancel fully.
                    float cancelAmount = 1f - Mathf.Clamp01(lateralSensitivity);
                    float compScale = speedForFullDeflection * cancelAmount;
                    compX -= hudManager.hotas.Strafe * compScale;
                    compY -= hudManager.hotas.Vertical * compScale;
                }
                float fwdRatioX = Mathf.Clamp(compX / z, -1.5f, 1.5f);
                float fwdRatioY = Mathf.Clamp(compY / z, -1.5f, 1.5f);
                // Signed slip ratios in canopy space (flip both signs in code if your canopy is mirrored).
                dirForward = new Vector2(fwdRatioX, fwdRatioY);
                }

                // Strafe vector comes from explicit HOTAS strafe/vertical input so it's independent
                // from forward-speed-based velocity shaping.
                Vector2 dirStrafe = Vector2.zero;
                if (hudManager != null && hudManager.hotas != null)
                {
                    float sx = hudManager.hotas.Strafe;
                    float sy = hudManager.hotas.Vertical;
                    if (invertStrafeX) sx = -sx;
                    if (invertStrafeY) sy = -sy;
                    dirStrafe = new Vector2(sx, sy);
                }

                // Strafe HUD term uses strafeReticleScale so it can be tuned independently of lateralSensitivity (used for velocity cancellation above).
                Vector2 strafeHud = dirStrafe * (lateralSensitivity * strafeReticleScale);
                if (maxStrafeReticleMagnitude > 0.001f && strafeHud.sqrMagnitude > maxStrafeReticleMagnitude * maxStrafeReticleMagnitude)
                    strafeHud = strafeHud.normalized * maxStrafeReticleMagnitude;

                // Combine: forward path indicator + separately-scaled strafe indicator.
                Vector2 dir = dirForward + strafeHud;

                if (dir.sqrMagnitude < directionDeadzone * directionDeadzone)
                    dir = Vector2.zero;

                float denom = Mathf.Max(0.001f, speedForFullDeflection);
                float speedT = Mathf.Clamp01(speed / denom);
                float scale = speedT * EffectiveSensitivity;
                targetOffset = dir * (maxOffset * scale);
            }
        }

        float smoothTime = targetOffset.sqrMagnitude < 0.01f ? returnSmoothTime : followSmoothTime;
        indicatorRect.anchoredPosition = Vector2.SmoothDamp(indicatorRect.anchoredPosition, targetOffset, ref _smoothVel, smoothTime);
    }

    void TryInit()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null || hudManager.canopyHUD == null)
            return;

        if (_rb == null)
            _rb = hudManager.GetRigidbody();

        // Try to auto-wire the reticle and indicator so the user doesn't have to.
        if (reticleRoot == null && hudManager.canopyHUD != null)
        {
            var canvas = hudManager.canopyHUD.GetComponentInChildren<Canvas>();
            if (canvas != null)
                reticleRoot = canvas.GetComponent<RectTransform>();
        }

        if (indicatorRect == null && reticleRoot != null)
        {
            // Dot must be a child of the Canvas (reticleRoot) so it renders with the reticle
            var candidate = reticleRoot.Find("VelocityVector");
            if (candidate != null)
            {
                indicatorRect = candidate as RectTransform;
                var img = indicatorRect != null ? indicatorRect.GetComponent<Image>() : null;
                if (img != null)
                {
                    if (img.sprite == null) img.sprite = velocityDotSprite != null ? velocityDotSprite : HUDCanvasHelper.GetImageSprite();
                    img.color = indicatorColor;
                    img.raycastTarget = false;
                    EnsureOutline(img);
                    indicatorRect.sizeDelta = new Vector2(24, 24);
                }
            }
            else
            {
                var go = new GameObject("VelocityVector");
                go.transform.SetParent(reticleRoot, false);
                var image = go.AddComponent<Image>();
                image.sprite = velocityDotSprite != null ? velocityDotSprite : HUDCanvasHelper.GetImageSprite();
                if (image.sprite == null)
                    image.sprite = HUDCanvasHelper.GetWhiteSprite();
                image.color = indicatorColor;
                image.raycastTarget = false;
                EnsureOutline(image);
                indicatorRect = go.GetComponent<RectTransform>();
                indicatorRect.anchorMin = new Vector2(0.5f, 0.5f);
                indicatorRect.anchorMax = new Vector2(0.5f, 0.5f);
                indicatorRect.pivot = new Vector2(0.5f, 0.5f);
                indicatorRect.anchoredPosition = Vector2.zero;
                indicatorRect.sizeDelta = new Vector2(24, 24);
            }
        }
    }

    void EnsureOutline(Image image)
    {
        if (image == null) return;
        var outline = image.GetComponent<Outline>();
        if (outline == null)
            outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = outlineDistance;
        outline.useGraphicAlpha = true;
    }
}
