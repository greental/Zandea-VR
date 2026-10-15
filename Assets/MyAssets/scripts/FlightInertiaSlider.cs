using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hook a UI Slider to FlightController.stickNeutralAngularDriftScale so you can tweak
/// roll/pitch inertia (drift when stick is released) in-game.
/// Add a Canvas with a Slider, assign this component, link Flight Controller and Slider.
/// Slider range is 0.2 (more coast) to 1 (stop quickly).
/// </summary>
[RequireComponent(typeof(Slider))]
public class FlightInertiaSlider : MonoBehaviour
{
    public FlightController flight;
    Slider _slider;

    void Awake()
    {
        _slider = GetComponent<Slider>();
    }

    void Start()
    {
        if (flight == null || _slider == null) return;
        _slider.minValue = 0.2f;
        _slider.maxValue = 1f;
        _slider.value = flight.stickNeutralAngularDriftScale;
        _slider.onValueChanged.AddListener(OnSliderChanged);
    }

    void OnSliderChanged(float value)
    {
        if (flight != null)
            flight.stickNeutralAngularDriftScale = value;
    }
}
