using MyAssets.PlanetTerrain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MyAssets.Planet
{
    /// <summary>
    /// Top-level scene rig for the single-planet terrain/transition stack.
    /// Assign the ship Rigidbody/target here, then run Auto Wire/Build Children to cascade references
    /// into transition, terrain, atmosphere, proxy, flight bridge and debug components.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Koombar/Planet/Planet System Rig")]
    public sealed class PlanetSystemRig : MonoBehaviour
    {
        [Header("Primary input")]
        [SerializeField] private Rigidbody shipRigidbody;
        [SerializeField] private Transform shipTarget;
        [SerializeField] private FlightController flightController;

        [Header("Core planet stack")]
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private PlanetTerrainTreadmill treadmill;
        [SerializeField] private ActivePlanetSelector activePlanetSelector;

        [Header("Celestial system frame")]
        [Tooltip("Root containing this planet and its moons. Defaults to the nearest ancestor named 'Planets', or the planet hierarchy root.")]
        [SerializeField] private Transform celestialSystemRoot;

        [Header("Generated child objects")]
        [SerializeField] private Transform generatedRoot;
        [SerializeField] private PlanetProxyVisual proxyVisual;
        [SerializeField] private PlanetOrbitalTerrainMesh orbitalTerrainMesh;

        [Header("Optional controllers")]
        [SerializeField] private PlanetTerrainTransitionController terrainTransitionController;
        [SerializeField] private PlanetAtmosphereFogController atmosphereFogController;
        [SerializeField] private PlanetFlightAtmosphereController flightAtmosphereController;
        [SerializeField] private PlanetCameraClipController cameraClipController;
        [SerializeField] private PlanetFrameDebugProbe debugProbe;

        [Header("Build options")]
        [SerializeField] private bool createMissingComponents = true;
        [SerializeField] private bool createTreadmillIfMissing = true;
        [SerializeField] private bool createOrbitalVisual = false;
        [SerializeField] private bool createDebugProbe = true;
        [SerializeField] private bool autoWireOnAwake = true;
        [Tooltip("Create/configure the one shared runtime floating origin for this scene's ship.")]
        [SerializeField] private bool enableFloatingOrigin = true;
        [SerializeField] private bool keepPlanetSurfaceNearWorldZero = true;

        const float AtmosphereHeightRadiusRatio = 1f / 4f;      // 60k radius => 15k atmosphere.
        const float TerrainPrepareRadiusRatio = 1f / 6f;        // 60k radius => 10k terrain preparation.
        const float SurfaceModeRadiusRatio = 1f / 2f;            // 30k radius => 15k Space-to-Surface frame swap.
        const float TreadmillRevealCapRadiusRatio = 1f / 12f;   // 60k radius => 5k reveal end.
        const float LowAtmosphereRadiusRatio = 1f / 240f;       // 60k radius => 250m lower atmosphere band.
        const float UpperAtmosphereRadiusRatio = 1f / 24f;      // 60k radius => 2500m upper atmosphere band.
        const float SurfaceExitMultiplier = 1.4f;               // 10k entry => 14k exit hysteresis.

        [Header("Planet scale - single source")]
        [FormerlySerializedAs("planetRadiusMeters")]
        [SerializeField, Min(1f)] private float defaultPlanetRadiusMeters = 60000f;
        [SerializeField, HideInInspector] private float defaultAtmosphereHeightMeters = 15000f;
        [SerializeField, HideInInspector] private float defaultTerrainPrepareAltitudeMeters = 10000f;
        [SerializeField, HideInInspector] private float defaultSurfaceModeAltitudeMeters = 10000f;
        [SerializeField] private Vector3 treadmillWorldPosition = Vector3.zero;

        public Rigidbody ShipRigidbody => shipRigidbody;
        public Transform ShipTarget => ResolveShipTarget();
        public PlanetDefinition Planet => planet;
        public PlanetTransitionState TransitionState => transitionState;
        public PlanetTerrainTreadmill Treadmill => treadmill;
        public ActivePlanetSelector ActivePlanetSelector => activePlanetSelector;
        public Transform CelestialSystemRoot => ResolveCelestialSystemRoot();
        public PlanetTerrainTransitionController TerrainTransitionController => terrainTransitionController;
        public PlanetProxyVisual ProxyVisual => proxyVisual;

        public void FollowMovingTarget(Transform movingTarget, Rigidbody movingBody)
        {
            if (!movingTarget) return;
            if (treadmill) treadmill.ConfigureTarget(movingTarget);
            if (transitionState) transitionState.SetMovingTarget(movingTarget);
            if (terrainTransitionController) terrainTransitionController.SetMovingTarget(movingTarget, movingBody);
            if (cameraClipController) cameraClipController.Configure(planet, movingTarget);
            if (treadmill)
            {
                var curvature = treadmill.GetComponent<PlanetTerrainCurvatureController>();
                if (curvature) curvature.Anchor = movingTarget;
            }
        }

        private void Reset()
        {
            ResolveSceneReferences();
            AutoWire();
        }

        private void Awake()
        {
            if (Application.isPlaying && autoWireOnAwake)
                AutoWire();
        }

        private void OnValidate()
        {
            defaultPlanetRadiusMeters = Mathf.Max(1f, defaultPlanetRadiusMeters);
            if (!Application.isPlaying && autoWireOnAwake)
            {
                ResolveSceneReferences();
                CascadeReferences();
            }
        }

        [ContextMenu("Auto Wire Planet System")]
        public void AutoWire()
        {
            ResolveSceneReferences();
            if (createMissingComponents)
                EnsureStackObjects();

            EnsureActivePlanetSelector();

            if (keepPlanetSurfaceNearWorldZero && planet != null && planet.transform == transform)
                transform.position = new Vector3(transform.position.x, -planet.RadiusMeters, transform.position.z);

            CascadeReferences();
            if (Application.isPlaying && enableFloatingOrigin && ResolveShipTarget() != null)
                FloatingOriginManager.EnsureFor(ResolveShipTarget());
        }

        void UpdateDerivedScaleValues()
        {
            defaultAtmosphereHeightMeters = defaultPlanetRadiusMeters * AtmosphereHeightRadiusRatio;
            defaultTerrainPrepareAltitudeMeters = defaultPlanetRadiusMeters * TerrainPrepareRadiusRatio;
            defaultSurfaceModeAltitudeMeters = defaultPlanetRadiusMeters * SurfaceModeRadiusRatio;
        }

        public void SetAllValuesFromRadius()
        {
            UpdateDerivedScaleValues();
            if (planet != null)
                planet.ConfigurePrototypeScale(defaultPlanetRadiusMeters, defaultAtmosphereHeightMeters, defaultTerrainPrepareAltitudeMeters, defaultSurfaceModeAltitudeMeters);

            float lowerAtmosphereAltitude = defaultPlanetRadiusMeters * LowAtmosphereRadiusRatio;
            float upperAtmosphereAltitude = defaultPlanetRadiusMeters * UpperAtmosphereRadiusRatio;
            float entryAltitude = defaultSurfaceModeAltitudeMeters;
            float exitAltitude = planet != null && planet.DeactivationAltitudeMeters > entryAltitude
                ? planet.DeactivationAltitudeMeters
                : entryAltitude * SurfaceExitMultiplier;

            if (transitionState != null)
                transitionState.ConfigureAltitudeBands(lowerAtmosphereAltitude, upperAtmosphereAltitude);
            if (terrainTransitionController != null)
            {
                terrainTransitionController.ConfigureAltitudeThresholds(entryAltitude, exitAltitude);
                terrainTransitionController.ConfigureTreadmillReveal(
                    defaultPlanetRadiusMeters * SurfaceModeRadiusRatio,
                    defaultPlanetRadiusMeters * TreadmillRevealCapRadiusRatio);
            }
            if (atmosphereFogController != null)
                atmosphereFogController.ConfigurePlanetScale(defaultPlanetRadiusMeters, entryAltitude, defaultAtmosphereHeightMeters, lowerAtmosphereAltitude, upperAtmosphereAltitude);
        }

        void CascadeReferences()
        {
            Transform target = ResolveShipTarget();
            if (activePlanetSelector != null)
            {
                activePlanetSelector.ConfigureTarget(target, shipRigidbody);
                activePlanetSelector.RefreshPlanetRigs();
            }
            if (treadmill != null)
                treadmill.ConfigureTarget(target);
            if (transitionState != null)
                transitionState.Configure(planet, target, activePlanetSelector, this);
            if (proxyVisual != null)
                proxyVisual.Configure(planet, transitionState);
            if (terrainTransitionController != null)
            {
                terrainTransitionController.Configure(transitionState, treadmill, generatedRoot != null ? generatedRoot.gameObject : treadmill != null ? treadmill.gameObject : null, activePlanetSelector, this, ResolveCelestialSystemRoot());
            }
            if (atmosphereFogController != null)
                atmosphereFogController.Configure(transitionState, activePlanetSelector, this);
            if (flightAtmosphereController != null)
                flightAtmosphereController.Configure(transitionState, ResolveFlightController(), activePlanetSelector, this);
            if (cameraClipController != null)
                cameraClipController.Configure(planet, target);
            if (orbitalTerrainMesh != null)
                orbitalTerrainMesh.gameObject.SetActive(false);
            if (debugProbe != null)
                debugProbe.Configure(planet, target, treadmill != null ? treadmill.TileSizeMeters : 1024f);
        }

        [ContextMenu("Build Missing Planet Children")]
        public void BuildMissingPlanetChildren()
        {
            createMissingComponents = true;
            EnsureStackObjects();
            AutoWire();
        }

        void ResolveSceneReferences()
        {
            if (shipRigidbody == null)
                shipRigidbody = FindFirstObjectByType<Rigidbody>();
            if (shipTarget == null && shipRigidbody != null)
                shipTarget = shipRigidbody.transform;
            if (flightController == null)
                flightController = shipTarget != null ? shipTarget.GetComponentInParent<FlightController>() : FindFirstObjectByType<FlightController>();

            if (planet == null)
                planet = GetComponent<PlanetDefinition>();
            ResolveCelestialSystemRoot();
            if (transitionState == null)
                transitionState = GetComponent<PlanetTransitionState>();
            if (proxyVisual == null)
                proxyVisual = GetComponent<PlanetProxyVisual>();
            if (terrainTransitionController == null)
                terrainTransitionController = GetComponent<PlanetTerrainTransitionController>();
            if (activePlanetSelector == null)
                activePlanetSelector = FindFirstObjectByType<ActivePlanetSelector>();
            if (atmosphereFogController == null)
                atmosphereFogController = GetComponent<PlanetAtmosphereFogController>();
            if (flightAtmosphereController == null)
                flightAtmosphereController = GetComponent<PlanetFlightAtmosphereController>();
            if (cameraClipController == null)
                cameraClipController = GetComponent<PlanetCameraClipController>();
            if (debugProbe == null)
                debugProbe = GetComponent<PlanetFrameDebugProbe>();
            if (treadmill == null)
                treadmill = FindLocalTreadmill();
            if (orbitalTerrainMesh == null)
                orbitalTerrainMesh = GetComponentInChildren<PlanetOrbitalTerrainMesh>(true);
        }

        Transform ResolveCelestialSystemRoot()
        {
            if (celestialSystemRoot != null || planet == null)
                return celestialSystemRoot;

            Transform ancestor = planet.transform;
            while (ancestor != null)
            {
                if (ancestor.name == "Planets")
                {
                    celestialSystemRoot = ancestor;
                    return celestialSystemRoot;
                }
                ancestor = ancestor.parent;
            }

            Transform hierarchyRoot = planet.transform.root;
            if (hierarchyRoot != planet.transform)
                celestialSystemRoot = hierarchyRoot;
            return celestialSystemRoot;
        }

        void EnsureActivePlanetSelector()
        {
            if (activePlanetSelector == null)
                activePlanetSelector = FindFirstObjectByType<ActivePlanetSelector>();

            if (activePlanetSelector == null)
            {
                GameObject selectorObject = new GameObject("ActivePlanetSelector");
                activePlanetSelector = selectorObject.AddComponent<ActivePlanetSelector>();
            }
        }

        PlanetTerrainTreadmill FindLocalTreadmill()
        {
            if (generatedRoot != null)
            {
                PlanetTerrainTreadmill local = generatedRoot.GetComponentInChildren<PlanetTerrainTreadmill>(true);
                if (local != null)
                    return local;
            }

            return GetComponentInChildren<PlanetTerrainTreadmill>(true);
        }

        PlanetTerrainTreadmill FindBestSceneTreadmill()
        {
            PlanetTerrainTreadmill[] treadmills = FindObjectsByType<PlanetTerrainTreadmill>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            PlanetTerrainTreadmill best = null;
            int bestScore = int.MinValue;

            foreach (PlanetTerrainTreadmill candidate in treadmills)
            {
                if (candidate == null)
                    continue;

                int score = 0;
                if (candidate.HasAuthoredTileSet)
                    score += 100;
                if (candidate.name == "TerrainTreadmill")
                    score += 50;
                if (candidate.transform.parent == generatedRoot)
                    score += 10;
                if (candidate.gameObject.activeInHierarchy)
                    score += 1;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        void EnsureStackObjects()
        {
            planet = EnsureComponent(planet);
            transitionState = EnsureComponent(transitionState);
            proxyVisual = EnsureComponent(proxyVisual);
            terrainTransitionController = EnsureComponent(terrainTransitionController);
            atmosphereFogController = EnsureComponent(atmosphereFogController);
            flightAtmosphereController = EnsureComponent(flightAtmosphereController);
            cameraClipController = EnsureComponent(cameraClipController);
            if (createDebugProbe)
                debugProbe = EnsureComponent(debugProbe);

            EnsureGeneratedRoot();

            PlanetTerrainTreadmill localTreadmill = generatedRoot.GetComponentInChildren<PlanetTerrainTreadmill>(true);
            if (localTreadmill != null && (treadmill == null || !treadmill.transform.IsChildOf(generatedRoot)))
                treadmill = localTreadmill;

            if (createTreadmillIfMissing && localTreadmill == null)
            {
                GameObject treadmillObject = new GameObject("PlanetTerrainTreadmill");
                treadmillObject.transform.SetParent(generatedRoot, false);
                treadmillObject.transform.position = treadmillWorldPosition;
                localTreadmill = treadmillObject.AddComponent<PlanetTerrainTreadmill>();
                treadmill = localTreadmill;
            }

            if (createOrbitalVisual && orbitalTerrainMesh == null)
            {
                GameObject orbitalObject = new GameObject("PlanetOrbitalTerrainVisual");
                orbitalObject.transform.SetParent(generatedRoot, false);
                orbitalTerrainMesh = orbitalObject.AddComponent<PlanetOrbitalTerrainMesh>();
            }
        }

        void EnsureGeneratedRoot()
        {
            if (generatedRoot != null)
            {
                if (generatedRoot.parent != transform)
                    generatedRoot.SetParent(transform, true);
                return;
            }

            Transform existing = transform.Find("PlanetGeneratedStack");
            generatedRoot = existing != null ? existing : new GameObject("PlanetGeneratedStack").transform;
            generatedRoot.SetParent(transform, false);
        }

        T EnsureComponent<T>(T component) where T : Component
        {
            return component != null ? component : gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();
        }

        Transform ResolveShipTarget()
        {
            // Planet transitions must use the physical ship pose. A stale scene reference
            // to an XR/cockpit child can make a body enter from the wrong position.
            if (shipRigidbody != null)
            {
                shipTarget = shipRigidbody.transform;
                return shipTarget;
            }

            if (shipTarget != null)
                return shipTarget;
            return null;
        }

        FlightController ResolveFlightController()
        {
            if (flightController != null)
                return flightController;

            Transform target = ResolveShipTarget();
            flightController = target != null ? target.GetComponentInParent<FlightController>() : FindFirstObjectByType<FlightController>();
            return flightController;
        }
    }
}
