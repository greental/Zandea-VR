using UnityEngine;

/// <summary>
/// Trigger volume that sets FlightController.inAtmosphere on ships that enter/exit.
/// Add to a GameObject with a Collider (isTrigger = true). When the ship (with FlightController on it or on a parent) enters, it gets wind drift when stationary.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AtmosphereVolume : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        TrySetAtmosphere(other, true);
    }

    void OnTriggerExit(Collider other)
    {
        TrySetAtmosphere(other, false);
    }

    static void TrySetAtmosphere(Collider other, bool inAtmosphere)
    {
        var rb = other.attachedRigidbody;
        var root = rb != null ? rb.transform : other.transform;
        var flight = root.GetComponentInParent<FlightController>();
        if (flight != null)
            flight.inAtmosphere = inAtmosphere;
    }
}
