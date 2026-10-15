using TMPro;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;

// Uses the visible speed text surface instead of positioning extra labels behind the cockpit mesh.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(SpeedIndicatorHUD))]
public sealed class PlayerVitalsScreenV3 : MonoBehaviour
{
    [SerializeField] SpeedIndicatorHUD speedIndicator;
    [SerializeField] CombatDamageable hull;
    [SerializeField] ShieldSystem shields;
    [SerializeField, Min(1f)] float speedFontSize = 17f;
    [SerializeField, Min(1f)] float vitalsFontSize = 13f;
    [SerializeField] Color shieldColor = new Color(.2f, .9f, 1f);
    [SerializeField] Color hullColor = new Color(1f, .65f, .3f);

    TMP_Text readout;

    void Start()
    {
        if (speedIndicator == null) speedIndicator = GetComponent<SpeedIndicatorHUD>();
        if (hull == null) hull = GetComponentInParent<CombatDamageable>();
        if (shields == null) shields = GetComponentInParent<ShieldSystem>();
        readout = speedIndicator.speedText != null ? speedIndicator.speedText : GetComponentInChildren<TMP_Text>(true);
        if (readout == null) { Debug.LogWarning("Player vitals could not find Razara's speed text.", this); enabled = false; return; }
        speedIndicator.speedText = readout;
        readout.fontSize = speedFontSize;
        readout.richText = true;
        readout.overflowMode = TextOverflowModes.Overflow;
        RectTransform rect = readout.rectTransform;
        if (rect != null) rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(rect.sizeDelta.y, 18f));
    }

    void LateUpdate()
    {
        if (readout == null) return;
        float shieldFraction = shields != null ?
            (shields.GetArcNormalized(ShieldArc.Front) + shields.GetArcNormalized(ShieldArc.Rear) +
             shields.GetArcNormalized(ShieldArc.Left) + shields.GetArcNormalized(ShieldArc.Right)) * .25f : 0f;
        float hullFraction = hull != null ? hull.HullFraction : 0f;
        readout.text += "\n<size=" + vitalsFontSize.ToString("0") + "><color=#" + ColorUtility.ToHtmlStringRGB(shieldColor) +
            ">SHIELD " + Mathf.RoundToInt(shieldFraction * 100f) + "%</color>\n<color=#" +
            ColorUtility.ToHtmlStringRGB(hullColor) + ">HULL " + Mathf.RoundToInt(hullFraction * 100f) + "%</color></size>";
    }
}
