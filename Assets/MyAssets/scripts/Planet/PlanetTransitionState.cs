using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Central altitude-driven transition signal for the current single planet.
    /// 0 = surface atmosphere, 1 = deep space.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Transition State")]
    public sealed class PlanetTransitionState : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Transform target;
        [SerializeField] private ActivePlanetSelector activePlanetSelector;
        [SerializeField] private PlanetSystemRig owningRig;
        [SerializeField, Min(0f)] private float lowerAtmosphereAltitudeMeters = 250f;
        [SerializeField, Min(0f)] private float upperAtmosphereAltitudeMeters = 2500f;
        [SerializeField] private bool autoFindTargetRigidbody = true;

        [Header("Live values")]
        [SerializeField] private PlanetTravelMode travelMode = PlanetTravelMode.Space;
        [SerializeField] private float altitudeMeters;
        [SerializeField, Range(0f, 1f)] private float atmosphereFactor;
        [SerializeField] private bool shouldPrepareTerrain;
        [SerializeField] private bool surfaceTerrainActive;

        public PlanetDefinition Planet => planet;
        public Transform Target => target;
        public PlanetTravelMode TravelMode => travelMode;
        public float AltitudeMeters => altitudeMeters;
        public float AtmosphereFactor => atmosphereFactor;
        public float SurfaceFactor => 1f - atmosphereFactor;
        public bool ShouldPrepareTerrain => shouldPrepareTerrain;
        public bool SurfaceTerrainActive => surfaceTerrainActive;
        private PlanetTerrainTransitionController terrainTransition;

        public float SurfaceOriginY => ResolveTerrainTransition() != null ? terrainTransition.SurfaceOriginY : 0f;

        private PlanetTerrainTransitionController ResolveTerrainTransition()
        {
            if (terrainTransition == null)
                terrainTransition = owningRig != null ? owningRig.TerrainTransitionController : GetComponent<PlanetTerrainTransitionController>();
            return terrainTransition;
        }

        public void SetTravelMode(PlanetTravelMode mode)
        {
            travelMode = mode;
        }

        public void Configure(PlanetDefinition planetDefinition, Transform targetTransform)
        {
            Configure(planetDefinition, targetTransform, activePlanetSelector, owningRig);
        }

        public void Configure(PlanetDefinition planetDefinition, Transform targetTransform, ActivePlanetSelector selector, PlanetSystemRig rig)
        {
            planet = planetDefinition;
            target = targetTransform;
            activePlanetSelector = selector;
            owningRig = rig;
            autoFindTargetRigidbody = target == null;
        }

        public void SetMovingTarget(Transform movingTarget)
        {
            target = movingTarget;
            autoFindTargetRigidbody = false;
        }

        public void ConfigureAltitudeBands(float lowerAltitudeMeters, float upperAltitudeMeters)
        {
            lowerAtmosphereAltitudeMeters = Mathf.Max(0f, lowerAltitudeMeters);
            upperAtmosphereAltitudeMeters = Mathf.Max(lowerAtmosphereAltitudeMeters + 1f, upperAltitudeMeters);
        }

        private void Reset()
        {
            planet = GetComponent<PlanetDefinition>();
            ResolveTarget();
        }

        private void Awake()
        {
            ResolveTarget();
        }

        private void Update()
        {
            if (target == null)
                ResolveTarget();
            if (planet == null || target == null)
                return;

            bool isActivePlanet = IsActivePlanet();
            altitudeMeters = isActivePlanet
                ? travelMode == PlanetTravelMode.Surface
                    ? (ResolveTerrainTransition() != null ? terrainTransition.GetSurfaceAltitude(target.position) : target.position.y)
                    : planet.GetAltitude(target.position)
                : 0f;

            if (!isActivePlanet || !planet.HasAtmosphere)
            {
                atmosphereFactor = 1f;
                shouldPrepareTerrain = false;
                surfaceTerrainActive = false;
                return;
            }

            float upper = Mathf.Max(lowerAtmosphereAltitudeMeters + 1f, Mathf.Max(upperAtmosphereAltitudeMeters, planet.AtmosphereHeightMeters));
            atmosphereFactor = Smooth01(Mathf.InverseLerp(lowerAtmosphereAltitudeMeters, upper, altitudeMeters));
            shouldPrepareTerrain = altitudeMeters <= planet.TerrainPrepareAltitudeMeters;
            surfaceTerrainActive = altitudeMeters <= planet.SurfaceModeAltitudeMeters;
        }

        bool IsActivePlanet()
        {
            if (activePlanetSelector == null)
                return true;

            return owningRig != null
                ? activePlanetSelector.IsActiveRig(owningRig)
                : activePlanetSelector.ActivePlanet == planet;
        }

        void ResolveTarget()
        {
            if (!autoFindTargetRigidbody || target != null)
                return;

            Rigidbody rb = FindFirstObjectByType<Rigidbody>();
            if (rb != null)
                target = rb.transform;
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
