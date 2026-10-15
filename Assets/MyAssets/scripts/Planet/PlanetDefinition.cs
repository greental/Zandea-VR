using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Minimal single-planet gameplay definition for the planetary terrain/treadmill architecture.
    /// Keeps planet-space values centralized without introducing a solar-system framework.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Definition")]
    public sealed class PlanetDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string displayName;

        [Header("Planet space")]
        [Tooltip("Optional transform used as the planet center. If null, this component's transform is used.")]
        [SerializeField] private Transform centerOverride;
        [Tooltip("Gameplay radius from planet center to sea-level/surface zero altitude, in meters.")]
        [SerializeField, Min(1f)] private float radiusMeters = 50000f;

        [Header("Atmosphere / transition")]
        [Tooltip("Altitude above surface where atmosphere visuals should be considered fully exited.")]
        [SerializeField, Min(0f)] private float atmosphereHeightMeters = 15000f;
        [Tooltip("Altitude where local terrain should begin preparing before it is fully visible.")]
        [SerializeField, Min(0f)] private float terrainPrepareAltitudeMeters = 5000f;
        [Tooltip("Altitude where surface/local-frame terrain should be active.")]
        [SerializeField, Min(0f)] private float surfaceModeAltitudeMeters = 2500f;
        [Tooltip("Ship must be this many meters or less above this body's surface before this body becomes altitude/planet-mode relevant.")]
        [SerializeField, Min(0f)] private float activationAltitudeMeters = 15000f;
        [Tooltip("Active body remains selected until the ship exceeds this altitude above the body's surface. Keep above Activation Altitude to avoid selection flicker.")]
        [SerializeField, Min(0f)] private float deactivationAltitudeMeters = 18000f;
        [Tooltip("If disabled, this body can still enter terrain/surface mode but does not drive atmosphere fog or FlightController.inAtmosphere.")]
        [SerializeField] private bool hasAtmosphere = true;

        [Header("Gravity")]
        [Tooltip("Gameplay gravity acceleration magnitude in meters per second squared.")]
        [SerializeField, Min(0f)] private float gravityAcceleration = 9.81f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color radiusColor = new Color(0.1f, 0.7f, 1f, 0.35f);
        [SerializeField] private Color atmosphereColor = new Color(0.4f, 0.8f, 1f, 0.18f);

        public Vector3 Center => centerOverride != null ? centerOverride.position : transform.position;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
        public float RadiusMeters => radiusMeters;
        public float AtmosphereHeightMeters => atmosphereHeightMeters;
        public float AtmosphereRadiusMeters => radiusMeters + atmosphereHeightMeters;
        public float TerrainPrepareAltitudeMeters => terrainPrepareAltitudeMeters;
        public float SurfaceModeAltitudeMeters => surfaceModeAltitudeMeters;
        public float ActivationAltitudeMeters => activationAltitudeMeters;
        public float DeactivationAltitudeMeters => Mathf.Max(activationAltitudeMeters, deactivationAltitudeMeters);
        public bool HasAtmosphere => hasAtmosphere && atmosphereHeightMeters > 0f;
        public float GravityAcceleration => gravityAcceleration;

        public void ConfigurePrototypeScale(float radius, float atmosphereHeight, float terrainPrepareAltitude, float surfaceModeAltitude)
        {
            radiusMeters = Mathf.Max(1f, radius);
            atmosphereHeightMeters = Mathf.Max(0f, atmosphereHeight);
            terrainPrepareAltitudeMeters = Mathf.Max(0f, terrainPrepareAltitude);
            surfaceModeAltitudeMeters = Mathf.Max(0f, surfaceModeAltitude);
        }

        public Vector3 GetSurfaceUp(Vector3 planetSpacePosition)
        {
            return PlanetFrameMath.GetSurfaceUp(Center, planetSpacePosition);
        }

        public float GetAltitude(Vector3 planetSpacePosition)
        {
            return PlanetFrameMath.GetAltitude(Center, radiusMeters, planetSpacePosition);
        }

        public Vector3 GetSurfacePoint(Vector3 planetSpacePosition)
        {
            return PlanetFrameMath.GetSurfacePoint(Center, radiusMeters, planetSpacePosition);
        }

        public PlanetLocalFrame BuildLocalFrame(Vector3 anchorPlanetSpacePosition)
        {
            return PlanetLocalFrame.FromPlanetPosition(Center, radiusMeters, anchorPlanetSpacePosition);
        }

        private void OnValidate()
        {
            radiusMeters = Mathf.Max(1f, radiusMeters);
            atmosphereHeightMeters = Mathf.Max(0f, atmosphereHeightMeters);
            terrainPrepareAltitudeMeters = Mathf.Max(0f, terrainPrepareAltitudeMeters);
            surfaceModeAltitudeMeters = Mathf.Max(0f, surfaceModeAltitudeMeters);
            activationAltitudeMeters = Mathf.Max(0f, activationAltitudeMeters);
            deactivationAltitudeMeters = Mathf.Max(activationAltitudeMeters, deactivationAltitudeMeters);
            gravityAcceleration = Mathf.Max(0f, gravityAcceleration);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Vector3 c = Center;
            Gizmos.color = radiusColor;
            Gizmos.DrawWireSphere(c, radiusMeters);
            Gizmos.color = atmosphereColor;
            Gizmos.DrawWireSphere(c, AtmosphereRadiusMeters);
        }
    }
}
