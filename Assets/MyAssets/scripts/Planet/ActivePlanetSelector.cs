using System;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace MyAssets.Planet
{
    /// <summary>
    /// Scene-level authority for deciding which planet/moon is currently relevant to the ship.
    /// Altitude and surface mode are only meaningful while a body is inside its activation shell.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Koombar/Planet/Active Planet Selector")]
    public sealed class ActivePlanetSelector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform target;
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private PlanetSystemRig[] planetRigs;

        [Header("Selection")]
        [SerializeField] private bool autoFindPlanetRigs = true;
        [SerializeField] private bool autoFindTargetRigidbody = true;
        [SerializeField] private bool allowSwitchWhileSurfaceMode = false;
        [SerializeField] private bool logActivePlanetChanges = true;
        [SerializeField] private bool followPlayerAndOccupiedVehicle = true;

        [Header("Live values")]
        [SerializeField] private PlanetSystemRig activeRig;
        [SerializeField] private PlanetDefinition activePlanet;
        [SerializeField] private float activeAltitudeMeters;
        [SerializeField] private float activeDistanceToCenterMeters;
        [SerializeField] private PlanetTravelMode activeTravelMode = PlanetTravelMode.Space;
        XROrigin playerOrigin;
        SitInCockpit[] seats = Array.Empty<SitInCockpit>();
        float nextSeatRefresh;

        public PlanetSystemRig ActiveRig => activeRig;
        public PlanetDefinition ActivePlanet => activePlanet;
        public Transform Target => target;
        public Rigidbody TargetRigidbody => targetRigidbody;
        public PlanetSystemRig[] PlanetRigs => planetRigs;
        public bool HasActivePlanet => activePlanet != null;
        public float ActiveAltitudeMeters => activeAltitudeMeters;
        public float ActiveDistanceToCenterMeters => activeDistanceToCenterMeters;
        public PlanetTravelMode ActiveTravelMode => activeTravelMode;

        public event Action<PlanetSystemRig, PlanetSystemRig> ActivePlanetChanged;

        public void ConfigureTarget(Transform targetTransform, Rigidbody rigidbody = null)
        {
            target = targetTransform;
            targetRigidbody = rigidbody != null ? rigidbody : targetTransform != null ? targetTransform.GetComponentInParent<Rigidbody>() : null;
        }

        public void RefreshPlanetRigs()
        {
            planetRigs = FindObjectsByType<PlanetSystemRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        public void ConfigurePlanetRigs(PlanetSystemRig[] rigs)
        {
            planetRigs = rigs;
        }

        public bool IsActiveRig(PlanetSystemRig rig)
        {
            return rig != null && rig == activeRig;
        }

        public bool TryGetActiveAltitude(out float altitudeMeters)
        {
            altitudeMeters = activeAltitudeMeters;
            return activePlanet != null;
        }

        public float GetSurfaceAltitude(PlanetSystemRig rig)
        {
            if (rig == null || rig.Planet == null || target == null)
                return 0f;

            PlanetTerrainTransitionController transitionController = rig.TerrainTransitionController;
            if (rig == activeRig && transitionController != null && transitionController.TravelMode == PlanetTravelMode.Surface)
                return transitionController.CurrentAltitudeMeters;

            return rig.Planet.GetAltitude(target.position);
        }

        private void Reset()
        {
            ResolveTarget();
            RefreshPlanetRigs();
        }

        private void Awake()
        {
            ResolveTarget();
            if (autoFindPlanetRigs)
                RefreshPlanetRigs();
        }

        private void Update()
        {
            ResolvePlayerVehicleTarget();
            ResolveTarget();
            if (autoFindPlanetRigs && (planetRigs == null || planetRigs.Length == 0))
                RefreshPlanetRigs();

            if (followPlayerAndOccupiedVehicle && target != null && planetRigs != null)
                foreach (var rig in planetRigs)
                    if (rig != null && rig.Treadmill != null && rig.Treadmill.Target != target)
                        rig.FollowMovingTarget(target, targetRigidbody);

            UpdateActivePlanet();
        }

        void ResolvePlayerVehicleTarget()
        {
            if (!followPlayerAndOccupiedVehicle) return;
            if (!playerOrigin) playerOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Exclude);
            if (!playerOrigin) return;
            if (Time.unscaledTime >= nextSeatRefresh)
            {
                seats = FindObjectsByType<SitInCockpit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                nextSeatRefresh = Time.unscaledTime + .15f;
            }

            Transform desired = playerOrigin.transform;
            Rigidbody body = desired.GetComponentInParent<Rigidbody>();
            foreach (var seat in seats)
            {
                if (!seat) continue;
                if (!seat.IsSeated || seat.xrOrigin != desired) continue;
                var bike = seat.GetComponentInParent<HoverBikeController>();
                var ship = bike ? null : seat.transform.root.GetComponentInChildren<FlightController>(true);
                body = bike ? (bike.body ? bike.body : bike.GetComponent<Rigidbody>()) :
                    ship ? (ship.rb ? ship.rb : ship.GetComponent<Rigidbody>()) : null;
                if (!body) body = seat.GetComponentInParent<Rigidbody>();
                if (!body && seat.seatAnchor) body = seat.seatAnchor.GetComponentInParent<Rigidbody>();
                desired = body ? body.transform : seat.transform.root;
                break;
            }
            if (target == desired && targetRigidbody == body) return;
            ConfigureTarget(desired, body);
            if (planetRigs != null)
                foreach (var rig in planetRigs)
                    if (rig) rig.FollowMovingTarget(desired, body);
            if (Application.isPlaying) FloatingOriginManager.EnsureFor(desired);
            if (logActivePlanetChanges)
                Debug.Log($"[ActivePlanetSelector] Terrain follows {desired.name} ({(body ? body.name : "player transform")}).", this);
        }

        void ResolveTarget()
        {
            if (!autoFindTargetRigidbody && target != null)
                return;

            if (target == null && targetRigidbody != null)
                target = targetRigidbody.transform;

            if (targetRigidbody == null && target == null && autoFindTargetRigidbody)
                targetRigidbody = FindFirstObjectByType<Rigidbody>();

            if (target == null && targetRigidbody != null)
                target = targetRigidbody.transform;
        }

        void UpdateActivePlanet()
        {
            if (target == null)
            {
                SetActiveRig(null, 0f, 0f);
                return;
            }

            // Select by distance to the surface across every rig. Terrain reveal remains
            // controlled by each planet's approach altitude, so selection is safe in space.
            PlanetSystemRig current = activeRig;
            if (current != null && current.Planet != null && !CanSwitchFromCurrent())
            {
                SetActiveRig(current, GetSurfaceAltitude(current), Vector3.Distance(target.position, current.Planet.Center));
                return;
            }

            PlanetSystemRig best = FindClosestPlanet();
            if (best == null)
            {
                SetActiveRig(null, 0f, 0f);
                return;
            }

            float distance = Vector3.Distance(target.position, best.Planet.Center);
            SetActiveRig(best, GetSurfaceAltitude(best), distance);
        }

        bool CanSwitchFromCurrent()
        {
            if (activeRig == null || activeRig.TerrainTransitionController == null)
                return true;
            return allowSwitchWhileSurfaceMode || activeRig.TerrainTransitionController.TravelMode != PlanetTravelMode.Surface;
        }

        PlanetSystemRig FindClosestPlanet()
        {
            if (planetRigs == null || target == null)
                return null;

            PlanetSystemRig best = null;
            float bestAltitude = float.PositiveInfinity;

            for (int i = 0; i < planetRigs.Length; i++)
            {
                PlanetSystemRig rig = planetRigs[i];
                PlanetDefinition planet = rig != null ? rig.Planet : null;
                if (rig == null || !rig.gameObject.activeInHierarchy || planet == null)
                    continue;

                float altitude = GetSurfaceAltitude(rig);
                if (altitude < bestAltitude)
                {
                    bestAltitude = altitude;
                    best = rig;
                }
            }

            return best;
        }

        void SetActiveRig(PlanetSystemRig nextRig, float altitudeMeters, float distanceToCenterMeters)
        {
            PlanetSystemRig previous = activeRig;
            activeRig = nextRig;
            activePlanet = nextRig != null ? nextRig.Planet : null;
            activeAltitudeMeters = activePlanet != null ? altitudeMeters : 0f;
            activeDistanceToCenterMeters = activePlanet != null ? distanceToCenterMeters : 0f;
            activeTravelMode = nextRig != null && nextRig.TerrainTransitionController != null ? nextRig.TerrainTransitionController.TravelMode : PlanetTravelMode.Space;

            if (previous == nextRig)
                return;

            if (logActivePlanetChanges)
            {
                string previousName = previous != null && previous.Planet != null ? previous.Planet.DisplayName : "none";
                string nextName = activePlanet != null ? activePlanet.DisplayName : "none";
                Debug.Log($"[ActivePlanetSelector] Active planet changed {previousName} -> {nextName}", this);
            }

            ActivePlanetChanged?.Invoke(previous, nextRig);
        }
    }
}
