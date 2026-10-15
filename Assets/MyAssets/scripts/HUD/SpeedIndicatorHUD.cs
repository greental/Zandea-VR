using UnityEngine;
using TMPro;

/// <summary>
/// Drives an existing TextMeshPro speed indicator in front of the left screen.
/// Binds to ShipHUDManager and updates the text with the ship's speed.
/// </summary>
public class SpeedIndicatorHUD : MonoBehaviour
{
    [Header("References")]
    public ShipHUDManager hudManager;
    [Tooltip("Existing TextMeshPro (TMP_Text) used for the speed readout.")]
    public TMP_Text speedText;

    [Header("Display")]
    [Tooltip("Format string for speed magnitude, e.g. \"0\" or \"0.0\". \" m/s\" is appended automatically.")]
    public string format = "0";
    [Tooltip("Color when moving forward.")]
    public Color forwardColor = new Color(0.05f, 0.65f, 0.05f, 1f);
    [Tooltip("Color when moving in reverse.")]
    public Color reverseColor = new Color(0.9f, 0.15f, 0.1f, 1f);

    void Start()
    {
        if (hudManager == null)
            hudManager = GetComponentInParent<ShipHUDManager>();
        if (hudManager == null)
            hudManager = FindFirstObjectByType<ShipHUDManager>();
        if (speedText == null)
            speedText = GetComponent<TMP_Text>();
        if (speedText == null)
            speedText = GetComponentInChildren<TMP_Text>(true);
        if (speedText == null && hudManager != null && hudManager.leftScreen != null)
            speedText = hudManager.leftScreen.GetComponentInChildren<TMP_Text>(true);
    }

    void LateUpdate()
    {
        if (hudManager == null || speedText == null)
            return;

        float signedSpeed = hudManager.GetSignedSpeed();
        float magnitude = Mathf.Abs(signedSpeed);
        bool reverse = signedSpeed < -0.01f;

        speedText.text = (reverse ? "-" : "") + magnitude.ToString(format) + " m/s";
        speedText.color = reverse ? reverseColor : forwardColor;
    }
}

