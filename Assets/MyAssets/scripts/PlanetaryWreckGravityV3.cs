using MyAssets.Planet;
using UnityEngine;

// Applies planet-directed gravity only while wreckage is within an atmosphere.
[RequireComponent(typeof(Rigidbody))]
public sealed class PlanetaryWreckGravityV3 : MonoBehaviour
{
    Rigidbody body;
    PlanetDefinition[] planets;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        planets = FindObjectsByType<PlanetDefinition>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    void FixedUpdate()
    {
        if (body == null || body.isKinematic) return;
        PlanetDefinition closest = null;
        float altitude = float.PositiveInfinity;
        foreach (PlanetDefinition planet in planets)
        {
            if (planet == null || !planet.HasAtmosphere) continue;
            float candidate = planet.GetAltitude(body.position);
            if (candidate > planet.AtmosphereHeightMeters || candidate < -planet.RadiusMeters * .5f) continue;
            if (candidate >= altitude) continue;
            altitude = candidate;
            closest = planet;
        }
        if (closest != null)
            body.AddForce(-closest.GetSurfaceUp(body.position) * closest.GravityAcceleration, ForceMode.Acceleration);
    }
}
