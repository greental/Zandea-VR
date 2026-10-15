using MyAssets.PlanetTerrain;
using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Coordinates the prototype handoff between real spherical Space mode and flat Unity Terrain Surface mode.
    /// Unity Terrain is never rotated or curved: spherical traversal while on the surface is logical state only.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Terrain Transition Controller")]
    public sealed class PlanetTerrainTransitionController : MonoBehaviour, IFloatingOriginListener
    {
        [Header("References")]
        [SerializeField] private PlanetTransitionState transitionState;
        [SerializeField] private PlanetTerrainTreadmill treadmill;
        [SerializeField] private GameObject terrainRoot;
        [SerializeField] private ActivePlanetSelector activePlanetSelector;
        [SerializeField] private PlanetSystemRig owningRig;
        [SerializeField] private Transform celestialSystemRoot;
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private Transform target;

        [Header("Mode thresholds")]
        [SerializeField, Min(0f)] private float surfaceExitAltitudeMeters = 3500f;
        [Header("Flat surface session")]
        [SerializeField] private bool driveTerrainRootVisibility = true;
        [Tooltip("Root position for the active flat Surface-mode terrain frame. Keep this near world zero; the ship is converted into the same flat frame on entry.")]
        [SerializeField] private Vector3 flatTerrainRootPosition = Vector3.zero;
        [Tooltip("Meters to offset the treadmill root from the ship when Surface mode starts. X = right/left, Z = forward/back in the flat surface frame. Use this to make terrain pop in farther away from the ship.")]
        [SerializeField] private Vector2 treadmillPopOffsetFromShipMeters = Vector2.zero;
        // [SerializeField] private float treadmillPopVerticalOffsetMeters = 1000f; // Disabled: approach reveal owns terrain Y.
        [Header("Approach reveal")]
        [Tooltip("Ship altitude where the terrain begins rising from below the surface. Preserved during normal Auto Wire; Set For All supplies a radius-based default.")]
        [SerializeField, Min(0f)] private float treadmillRevealStartAltitudeMeters = 7000f;
        [Tooltip("Ship altitude where the treadmill reaches its final height. This does not control the Space-to-Surface frame swap.")]
        [SerializeField, Min(0f)] private float treadmillRevealCapAltitudeMeters = 2500f;
        [Tooltip("Treadmill tile Y at reveal start, relative to the local altitude-zero surface plane.")]
        [SerializeField] private float treadmillRevealBelowSurfaceY = -1000f;
        [Tooltip("Treadmill tile Y when the reveal reaches its cap altitude.")]
        [SerializeField, Min(0f)] private float treadmillRevealFinalHeightMeters = 1000f;
        [SerializeField] private bool logTreadmillReveal = true;
        [Tooltip("Extra meters above PlanetDefinition.RadiusMeters used only for debug/logging the equivalent planet-space surface point.")]
        [SerializeField] private float planetSurfaceDebugOffsetMeters = 25f;
        [SerializeField] private PlanetTileCoord surfaceSessionStartTile = default;

        [Header("Debug")]
        [SerializeField] private bool logModeChanges = true;
        [SerializeField] private bool logTileLogicalSteps = true;
        [SerializeField] private bool showDebugOverlay = true;

        [Header("Live values")]
        [SerializeField] private PlanetTravelMode travelMode = PlanetTravelMode.Space;
        [SerializeField] private PlanetSurfaceState surfaceState;
        [SerializeField] private float currentAltitudeMeters;
        [SerializeField] private float effectiveSurfaceEnterAltitudeMeters;
        [SerializeField] private PlanetTileCoord entryTile;
        [SerializeField] private PlanetTileCoord logicalTilesTraveled;
        [SerializeField, HideInInspector] private float liveTreadmillRevealStartY;
        [SerializeField, HideInInspector] private float liveTreadmillRevealEndY;
        [SerializeField, HideInInspector] private float liveTreadmillRevealMetersPerAltitudeMeter;
        [SerializeField, HideInInspector] private float liveTreadmillCurrentY;
        [SerializeField, HideInInspector] private float liveTreadmillYChange;

        private PlanetTerrainTreadmill subscribedTreadmill;
        private FloatingOriginPosition surfaceOrigin;
        private bool hasCelestialEntryPose;
        private Vector3 spaceCelestialRootPosition;
        private Quaternion spaceCelestialRootRotation;
        private Vector3 entryPlanetCenterInSurfaceFrame;
        private Vector3 entryCelestialRootPositionInSurfaceFrame;
        private Quaternion entryCelestialRootRotationInSurfaceFrame;
        private Quaternion entrySpaceToSurfaceRotation;
        private Transform treadmillOriginalParent;
        private Vector3 treadmillOriginalLocalPosition;
        private Quaternion treadmillOriginalLocalRotation;
        private Vector3 treadmillOriginalLocalScale;
        private bool treadmillOriginalActiveSelf;
        private bool treadmillDetachedForSurfaceFrame;
        public float TreadmillRevealFinalHeightMeters => treadmillRevealFinalHeightMeters;
        private const float TreadmillRevealLogIntervalSeconds = 0.5f;
        private float nextTreadmillRevealLogTime;
        private bool hasPreviousTreadmillRevealY;
        private float previousTreadmillRevealY;
        private bool hasLoggedTreadmillRevealY;
        private float lastLoggedTreadmillRevealY;

        public float SurfaceOriginY => (float)surfaceOrigin.y;
        public float GetSurfaceAltitude(Vector3 displayedPosition) => (float)((double)displayedPosition.y - surfaceOrigin.y);

        public void OnOriginShift(Vector3 shift)
        {
            if (travelMode == PlanetTravelMode.Surface)
            {
                surfaceOrigin.Add(-shift);
                if (hasCelestialEntryPose)
                {
                    spaceCelestialRootPosition -= shift;
                    entryPlanetCenterInSurfaceFrame -= shift;
                    entryCelestialRootPositionInSurfaceFrame -= shift;
                }
            }
        }

        public PlanetTravelMode TravelMode => travelMode;
        public PlanetSurfaceState SurfaceState => surfaceState;
        public float CurrentAltitudeMeters => currentAltitudeMeters;
        public PlanetTileCoord LogicalTilesTraveled => logicalTilesTraveled;
        public float TreadmillRevealStartY => liveTreadmillRevealStartY;
        public float TreadmillRevealEndY => liveTreadmillRevealEndY;
        public float TreadmillRevealMetersPerAltitudeMeter => liveTreadmillRevealMetersPerAltitudeMeter;
        public float TreadmillCurrentY => liveTreadmillCurrentY;
        public float TreadmillYChange => liveTreadmillYChange;
        public Vector3 CurrentPlanetaryUp => travelMode == PlanetTravelMode.Surface && surfaceState.valid
            ? surfaceState.logicalSurfaceUp
            : ResolvePlanetaryUpInSpace();
        public Vector3 CurrentPlanetaryDown => -CurrentPlanetaryUp;

        public void Configure(PlanetTransitionState state, PlanetTerrainTreadmill terrainTreadmill, GameObject root)
        {
            Configure(state, terrainTreadmill, root, activePlanetSelector, owningRig);
        }

        public void Configure(PlanetTransitionState state, PlanetTerrainTreadmill terrainTreadmill, GameObject root, ActivePlanetSelector selector, PlanetSystemRig rig)
        {
            Configure(state, terrainTreadmill, root, selector, rig, celestialSystemRoot);
        }

        public void Configure(PlanetTransitionState state, PlanetTerrainTreadmill terrainTreadmill, GameObject root, ActivePlanetSelector selector, PlanetSystemRig rig, Transform systemRoot)
        {
            transitionState = state;
            SetTreadmill(terrainTreadmill);
            terrainRoot = root != null ? root : terrainTreadmill != null ? terrainTreadmill.gameObject : null;
            activePlanetSelector = selector;
            owningRig = rig;
            celestialSystemRoot = systemRoot;
            if (rig != null && rig.ShipRigidbody != null)
            {
                targetRigidbody = rig.ShipRigidbody;
                target = targetRigidbody.transform;
            }
            else if (state != null && state.Target != null)
            {
                target = state.Target;
                if (targetRigidbody == null)
                    targetRigidbody = target.GetComponentInParent<Rigidbody>();
            }
            ResolveTargetReferences();
            if (transitionState != null)
                transitionState.SetTravelMode(travelMode);
            // PlanetSystemRig can replace the serialized treadmill/root during its
            // Awake after this controller's Awake has already hidden the old root.
            // Hide the newly wired root before any treadmill Start can show tiles.
            if (Application.isPlaying && travelMode == PlanetTravelMode.Space)
                SetTerrainRootActive(false);
        }

        public void SetMovingTarget(Transform movingTarget, Rigidbody movingBody)
        {
            target = movingTarget;
            targetRigidbody = movingBody;
        }

        public void ConfigureAltitudeThresholds(float enterAltitudeMeters, float exitAltitudeMeters)
        {
            surfaceExitAltitudeMeters = Mathf.Max(Mathf.Max(0f, enterAltitudeMeters) + 1f, exitAltitudeMeters);
        }

        public void ConfigureTreadmillReveal(float revealStartAltitudeMeters, float revealCapAltitudeMeters)
        {
            treadmillRevealCapAltitudeMeters = Mathf.Max(0f, revealCapAltitudeMeters);
            treadmillRevealStartAltitudeMeters = Mathf.Max(treadmillRevealCapAltitudeMeters + 1f, revealStartAltitudeMeters);
        }

        private void Reset()
        {
            transitionState = GetComponent<PlanetTransitionState>();
            SetTreadmill(GetComponentInChildren<PlanetTerrainTreadmill>(true));
            terrainRoot = treadmill != null ? treadmill.gameObject : null;
            ResolveTargetReferences();
        }

        private void Awake()
        {
            ResolveTargetReferences();
            SetTerrainRootActive(travelMode == PlanetTravelMode.Surface);
            if (transitionState != null)
                transitionState.SetTravelMode(travelMode);
        }

        private void OnEnable()
        {
            SubscribeTreadmill();
        }

        private void OnDisable()
        {
            UnsubscribeTreadmill();
            RestoreCelestialSpaceFrame();
            RestoreTreadmillParent();
        }

        private void Update()
        {
            ResolveReferences();
            if (!HasRequiredReferences())
                return;

            if (!IsActivePlanetRig())
            {
                currentAltitudeMeters = 0f;
                if (travelMode == PlanetTravelMode.Surface)
                    ExitSurfaceMode();
                else
                    SetTerrainRootActive(false);

                if (transitionState != null)
                    transitionState.SetTravelMode(PlanetTravelMode.Space);
                return;
            }

            currentAltitudeMeters = CalculateModeAltitude();
            if (transitionState != null)
                transitionState.SetTravelMode(travelMode);

            if (travelMode == PlanetTravelMode.Space)
            {
                effectiveSurfaceEnterAltitudeMeters = ResolveSurfaceEnterAltitude();
                UpdateTreadmillApproachReveal(currentAltitudeMeters);
                if (currentAltitudeMeters <= effectiveSurfaceEnterAltitudeMeters)
                    EnterSurfaceMode();
            }
            else if (currentAltitudeMeters >= Mathf.Max(effectiveSurfaceEnterAltitudeMeters, surfaceExitAltitudeMeters))
            {
                UpdateTreadmillApproachReveal(currentAltitudeMeters);
                ExitSurfaceMode();
            }
            else if (travelMode == PlanetTravelMode.Surface)
            {
                // Preserve the exact altitude-to-height mapping after the mode handoff,
                // so the treadmill cannot be pinned at its entry height while descending.
                UpdateTreadmillApproachReveal(currentAltitudeMeters);
            }
        }

        [ContextMenu("Force Enter Surface Mode")]
        public void ForceEnterSurfaceMode()
        {
            ResolveReferences();
            if (HasRequiredReferences() && travelMode == PlanetTravelMode.Space)
                EnterSurfaceMode();
        }

        [ContextMenu("Force Exit Surface Mode")]
        public void ForceExitSurfaceMode()
        {
            ResolveReferences();
            if (HasRequiredReferences() && travelMode == PlanetTravelMode.Surface)
                ExitSurfaceMode();
        }

        void EnterSurfaceMode()
        {
            surfaceOrigin = default;
            PlanetDefinition planet = transitionState.Planet;
            Vector3 spacePosition = target.position;
            Quaternion spaceRotation = target.rotation;
            Vector3 spaceVelocity = targetRigidbody != null ? targetRigidbody.linearVelocity : Vector3.zero;
            Vector3 spaceAngularVelocity = targetRigidbody != null ? targetRigidbody.angularVelocity : Vector3.zero;
            // Capture this before rotating the celestial root. Planet.Center is a live
            // transform query and will move with that root during entry.
            Vector3 spacePlanetCenter = planet.Center;
            float measuredEntryAltitude = PlanetFrameMath.GetAltitude(spacePlanetCenter, planet.RadiusMeters, spacePosition);

            surfaceState = PlanetSurfaceFrameMath.CaptureSurfaceState(spacePlanetCenter, spacePosition, spaceRotation, surfaceSessionStartTile);
            surfaceState.Normalize();
            DetachTreadmillFromCelestialSystem();
            PlanetFrameTransform spaceToSurface = BuildEntrySpaceToSurfaceTransform(planet, spacePlanetCenter);
            CaptureAndApplyCelestialEntryFrame(planet, spacePlanetCenter, spaceToSurface);
            entryTile = surfaceSessionStartTile;
            logicalTilesTraveled = default;

            Vector3 surfacePosition = spaceToSurface.TransformPoint(spacePosition);
            Quaternion surfaceRotation = spaceToSurface.TransformRotation(spaceRotation);
            Vector3 surfaceVelocity = spaceToSurface.TransformDirection(spaceVelocity);
            Vector3 surfaceAngularVelocity = spaceToSurface.TransformDirection(spaceAngularVelocity);

            ApplyTargetTransform(surfacePosition, surfaceRotation, surfaceVelocity, surfaceAngularVelocity);
            ReinitializeFlatTreadmill(surfacePosition);
            SetTerrainRootActive(true);
            travelMode = PlanetTravelMode.Surface;
            if (transitionState != null)
                transitionState.SetTravelMode(travelMode);

            if (logModeChanges)
            {
                Debug.Log(
                    $"[PlanetTerrainTransitionController] Entered Surface mode. " +
                    $"spaceShip={spacePosition}, spacePlanetCenter={spacePlanetCenter}, " +
                    $"measuredEntryAltitude={measuredEntryAltitude:0.##}m, localPos={surfacePosition}, altitude={surfacePosition.y:0.##}m, " +
                    $"frameSwapThreshold={effectiveSurfaceEnterAltitudeMeters:0.##}m, " +
                    $"treadmillRevealCap={treadmillRevealCapAltitudeMeters:0.##}m, " +
                    $"up={surfaceState.logicalSurfaceUp}, east={surfaceState.logicalEast}, north={surfaceState.logicalNorth}",
                    this);
            }
        }

        void ExitSurfaceMode()
        {
            PlanetDefinition planet = transitionState.Planet;
            surfaceState.Normalize();

            // Recover surface-session coordinates before the existing spherical conversion.
            Vector3 surfacePosition = new Vector3(
                (float)((double)target.position.x - surfaceOrigin.x),
                GetSurfaceAltitude(target.position),
                (float)((double)target.position.z - surfaceOrigin.z));
            Quaternion surfaceRotation = target.rotation;
            Vector3 surfaceVelocity = targetRigidbody != null ? targetRigidbody.linearVelocity : Vector3.zero;
            Vector3 surfaceAngularVelocity = targetRigidbody != null ? targetRigidbody.angularVelocity : Vector3.zero;

            // Recenter events advance the radial frame by whole tiles. Fold the ship's
            // remaining in-tile X/Z offset into that frame before returning to the sphere.
            float tileSize = treadmill != null ? treadmill.TileSizeMeters : 0f;
            Vector2 inTileOffset = new Vector2(
                surfacePosition.x - surfaceState.logicalTile.x * tileSize,
                surfacePosition.z - surfaceState.logicalTile.z * tileSize);
            PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTangentOffset(
                ref surfaceState,
                inTileOffset,
                planet.RadiusMeters);
            surfacePosition.x = 0f;
            surfacePosition.z = 0f;

            PlanetSurfaceFrameMath.SurfaceToSpaceTransform(
                hasCelestialEntryPose ? ResolveEntryPlanetCenterInSpace() : planet.Center,
                planet.RadiusMeters,
                surfacePosition,
                surfaceRotation,
                surfaceVelocity,
                surfaceAngularVelocity,
                surfaceState,
                out Vector3 spacePosition,
                out Quaternion spaceRotation,
                out Vector3 spaceVelocity,
                out Vector3 spaceAngularVelocity);

            SetTerrainRootActive(false);
            ApplyTargetTransform(spacePosition, spaceRotation, spaceVelocity, spaceAngularVelocity);
            RestoreCelestialSpaceFrame();
            RestoreTreadmillParent();
            travelMode = PlanetTravelMode.Space;
            if (transitionState != null)
                transitionState.SetTravelMode(travelMode);

            if (logModeChanges)
            {
                Debug.Log(
                    $"[PlanetTerrainTransitionController] Exited Surface mode. " +
                    $"spacePos={spacePosition}, logicalUp={surfaceState.logicalSurfaceUp}, " +
                    $"tilesTraveled={logicalTilesTraveled}",
                    this);
            }
        }

        void ReinitializeFlatTreadmill(Vector3 surfacePosition)
        {
            if (treadmill == null)
                return;

            Vector3 popOffset = new Vector3(treadmillPopOffsetFromShipMeters.x, 0f, treadmillPopOffsetFromShipMeters.y);
            treadmill.transform.SetPositionAndRotation(flatTerrainRootPosition + popOffset, Quaternion.identity);
            treadmill.transform.localScale = Vector3.one;
            // Reinitialization must preserve the reveal's vertical result. Surface mode
            // starts only after the approach animation reaches its fixed final height.
            float revealHeight = CalculateTreadmillRevealY(currentAltitudeMeters);
            treadmill.SetTileVerticalOffset(revealHeight);
            treadmill.SetLocalTileFrameEnabled(false);
            treadmill.ConfigureTarget(target);
            treadmill.ReinitializeFlatSession(new Vector3(surfacePosition.x, 0f, surfacePosition.z), target, surfaceSessionStartTile);
            RefreshTreadmillRevealDiagnostics(currentAltitudeMeters, revealHeight);
        }

        void UpdateTreadmillApproachReveal(float altitude)
        {
            if (treadmill == null)
                return;

            if (travelMode == PlanetTravelMode.Space && transitionState != null && transitionState.Planet != null && target != null)
            {
                // In orbital space, Y=0 is not necessarily the altitude-zero surface
                // beneath the approaching ship. Anchor the treadmill's height plane to
                // the ship's radial surface point while preserving its independent X/Z frame.
                Vector3 localSurfacePoint = transitionState.Planet.GetSurfacePoint(target.position);
                treadmill.SetTileFrameBaseY(localSurfacePoint.y);
            }

            float start = Mathf.Max(treadmillRevealCapAltitudeMeters + 1f, treadmillRevealStartAltitudeMeters);
            float verticalOffset = CalculateTreadmillRevealY(altitude);
            treadmill.SetTileVerticalOffset(verticalOffset);
            RefreshTreadmillRevealDiagnostics(altitude, verticalOffset);

            // Apply the below-surface height before making the terrain visible. This
            // avoids a one-frame flash at its previous session's final height.
            SetTerrainRootActive(altitude <= start);
        }

        float CalculateTreadmillRevealY(float altitude)
        {
            float start = Mathf.Max(treadmillRevealCapAltitudeMeters + 1f, treadmillRevealStartAltitudeMeters);
            float progress = 1f - Mathf.InverseLerp(treadmillRevealCapAltitudeMeters, start, altitude);
            return Mathf.Lerp(treadmillRevealBelowSurfaceY, treadmillRevealFinalHeightMeters, progress);
        }

        void RefreshTreadmillRevealDiagnostics(float altitude, float requestedY)
        {
            float start = Mathf.Max(treadmillRevealCapAltitudeMeters + 1f, treadmillRevealStartAltitudeMeters);
            float altitudeSpan = Mathf.Max(1f, start - treadmillRevealCapAltitudeMeters);
            liveTreadmillRevealStartY = treadmillRevealBelowSurfaceY;
            liveTreadmillRevealEndY = treadmillRevealFinalHeightMeters;
            liveTreadmillRevealMetersPerAltitudeMeter = Mathf.Abs(liveTreadmillRevealEndY - liveTreadmillRevealStartY) / altitudeSpan;
            liveTreadmillCurrentY = treadmill != null ? treadmill.TileVerticalOffsetMeters : requestedY;

            if (hasPreviousTreadmillRevealY)
                liveTreadmillYChange = liveTreadmillCurrentY - previousTreadmillRevealY;
            previousTreadmillRevealY = liveTreadmillCurrentY;
            hasPreviousTreadmillRevealY = true;

            if (!logTreadmillReveal || target == null || Time.unscaledTime < nextTreadmillRevealLogTime)
                return;

            nextTreadmillRevealLogTime = Time.unscaledTime + TreadmillRevealLogIntervalSeconds;
            float logChange = hasLoggedTreadmillRevealY ? liveTreadmillCurrentY - lastLoggedTreadmillRevealY : 0f;
            lastLoggedTreadmillRevealY = liveTreadmillCurrentY;
            hasLoggedTreadmillRevealY = true;
            Vector3 currentTilePosition = treadmill != null
                ? treadmill.GetTileWorldPosition(treadmill.CenterTile)
                : Vector3.zero;
            float tileFrameBaseY = currentTilePosition.y - liveTreadmillCurrentY;
            Vector3 treadmillStart = new Vector3(currentTilePosition.x, tileFrameBaseY + liveTreadmillRevealStartY, currentTilePosition.z);
            Vector3 treadmillStop = new Vector3(currentTilePosition.x, tileFrameBaseY + liveTreadmillRevealEndY, currentTilePosition.z);
            float shipToCurrentTerrainY = currentTilePosition.y - target.position.y;
            Debug.Log(
                $"[PlanetTerrainTransitionController] Treadmill reveal | mode={travelMode} " +
                $"ship={target.position} altitude={altitude:0.0}m | " +
                $"treadmill start={treadmillStart} @ {start:0.0}m altitude | " +
                $"stop={treadmillStop} @ {treadmillRevealCapAltitudeMeters:0.0}m altitude | " +
                $"current={currentTilePosition} surfacePlaneY={tileFrameBaseY:0.0}m " +
                $"offsetY={liveTreadmillCurrentY:0.0}m shipToTerrainY={shipToCurrentTerrainY:+0.0;-0.0;0.0}m " +
                $"changeY={logChange:+0.00;-0.00;0.00}m/{TreadmillRevealLogIntervalSeconds:0.0}s | " +
                $"travel={liveTreadmillRevealMetersPerAltitudeMeter:0.000}m per 1m altitude descent",
                this);
        }

        Vector3 ResolvePlanetSurfaceDebugPosition()
        {
            PlanetDefinition planet = transitionState != null ? transitionState.Planet : null;
            if (planet == null || !surfaceState.valid)
                return Vector3.zero;

            Vector3 up = surfaceState.logicalSurfaceUp.sqrMagnitude > 0.000001f
                ? surfaceState.logicalSurfaceUp.normalized
                : ResolvePlanetaryUpInSpace();
            return planet.Center + up * (planet.RadiusMeters + planetSurfaceDebugOffsetMeters);
        }

        void ApplyTargetTransform(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            if (targetRigidbody != null)
            {
                RigidbodyInterpolation interpolation = targetRigidbody.interpolation;
                targetRigidbody.interpolation = RigidbodyInterpolation.None;
                targetRigidbody.position = position;
                targetRigidbody.rotation = rotation;
                targetRigidbody.transform.SetPositionAndRotation(position, rotation);
                targetRigidbody.linearVelocity = velocity;
                targetRigidbody.angularVelocity = angularVelocity;
                targetRigidbody.interpolation = interpolation;
                return;
            }

            target.SetPositionAndRotation(position, rotation);
        }

        void OnTreadmillCenterTileChanged(PlanetTileCoord oldCenter, PlanetTileCoord newCenter, PlanetTileCoord delta)
        {
            if (travelMode != PlanetTravelMode.Surface || !surfaceState.valid || treadmill == null || transitionState == null || transitionState.Planet == null)
                return;

            PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTileDelta(
                ref surfaceState,
                delta,
                treadmill.TileSizeMeters,
                transitionState.Planet.RadiusMeters);

            logicalTilesTraveled = new PlanetTileCoord(newCenter.x - entryTile.x, newCenter.z - entryTile.z);

            if (logTileLogicalSteps)
            {
                Debug.Log(
                    $"[PlanetTerrainTransitionController] Surface tile step old={oldCenter}, new={newCenter}, " +
                    $"delta={delta}, traveled={logicalTilesTraveled}, logicalUp={surfaceState.logicalSurfaceUp}",
                    this);
            }
        }

        float CalculateModeAltitude()
        {
            if (travelMode == PlanetTravelMode.Surface)
                return target != null ? GetSurfaceAltitude(target.position) : 0f;

            return transitionState != null && transitionState.Planet != null && target != null
                ? transitionState.Planet.GetAltitude(target.position)
                : 0f;
        }

        float ResolveSurfaceEnterAltitude()
        {
            // The coordinate-frame flip happens at the planet's configured Surface Mode
            // altitude. The treadmill reveal cap is only its final height altitude.
            return transitionState != null && transitionState.Planet != null
                ? Mathf.Max(0f, transitionState.Planet.SurfaceModeAltitudeMeters)
                : Mathf.Max(0f, treadmillRevealStartAltitudeMeters);
        }

        PlanetFrameTransform BuildEntrySpaceToSurfaceTransform(PlanetDefinition planet, Vector3 spacePlanetCenter)
        {
            PlanetSurfaceBasis basis = PlanetFrameSwapMath.BasisFromState(surfaceState);
            return PlanetFrameSwapMath.BuildSpaceToSurfaceTransform(spacePlanetCenter, planet.RadiusMeters, basis);
        }

        void CaptureAndApplyCelestialEntryFrame(PlanetDefinition planet, Vector3 spacePlanetCenter, PlanetFrameTransform toSurface)
        {
            hasCelestialEntryPose = false;
            if (celestialSystemRoot == null || celestialSystemRoot == target || celestialSystemRoot.IsChildOf(target) || target.IsChildOf(celestialSystemRoot))
                return;

            spaceCelestialRootPosition = celestialSystemRoot.position;
            spaceCelestialRootRotation = celestialSystemRoot.rotation;
            entryPlanetCenterInSurfaceFrame = toSurface.TransformPoint(spacePlanetCenter);
            entryCelestialRootPositionInSurfaceFrame = toSurface.TransformPoint(spaceCelestialRootPosition);
            entryCelestialRootRotationInSurfaceFrame = toSurface.TransformRotation(spaceCelestialRootRotation);
            entrySpaceToSurfaceRotation = toSurface.rotation;
            celestialSystemRoot.SetPositionAndRotation(entryCelestialRootPositionInSurfaceFrame, entryCelestialRootRotationInSurfaceFrame);
            hasCelestialEntryPose = true;

            if (logModeChanges)
                Debug.Log($"[PlanetTerrainTransitionController] Rotated celestial system into surface frame. root={celestialSystemRoot.name}, planetCenter={entryPlanetCenterInSurfaceFrame}, moon and sibling bodies follow the Planets root.", this);
        }

        void DetachTreadmillFromCelestialSystem()
        {
            treadmillDetachedForSurfaceFrame = false;
            if (treadmill == null || celestialSystemRoot == null || !treadmill.transform.IsChildOf(celestialSystemRoot))
                return;

            treadmillOriginalParent = treadmill.transform.parent;
            treadmillOriginalLocalPosition = treadmill.transform.localPosition;
            treadmillOriginalLocalRotation = treadmill.transform.localRotation;
            treadmillOriginalLocalScale = treadmill.transform.localScale;
            treadmillOriginalActiveSelf = treadmill.gameObject.activeSelf;
            treadmill.transform.SetParent(null, true);
            treadmillDetachedForSurfaceFrame = true;
        }

        void RestoreTreadmillParent()
        {
            if (!treadmillDetachedForSurfaceFrame || treadmill == null)
                return;

            treadmill.transform.SetParent(treadmillOriginalParent, false);
            treadmill.transform.localPosition = treadmillOriginalLocalPosition;
            treadmill.transform.localRotation = treadmillOriginalLocalRotation;
            treadmill.transform.localScale = treadmillOriginalLocalScale;
            treadmill.gameObject.SetActive(treadmillOriginalActiveSelf);
            treadmillOriginalParent = null;
            treadmillDetachedForSurfaceFrame = false;
        }

        Vector3 ResolveEntryPlanetCenterInSpace()
        {
            // The original-space center is tracked through floating-origin shifts separately
            // from the now-moving PlanetDefinition transform.
            Quaternion surfaceToSpace = Quaternion.Inverse(entrySpaceToSurfaceRotation);
            return spaceCelestialRootPosition + surfaceToSpace * (entryPlanetCenterInSurfaceFrame - entryCelestialRootPositionInSurfaceFrame);
        }

        void UpdateCelestialSurfaceFrame()
        {
            if (!hasCelestialEntryPose || celestialSystemRoot == null || travelMode != PlanetTravelMode.Surface || treadmill == null || target == null)
                return;

            PlanetDefinition planet = transitionState != null ? transitionState.Planet : null;
            if (planet == null)
                return;

            // surfaceState accumulates each tile step in order, preserving curved paths
            // and turns. Add the ship's remaining offset inside the current tile so the
            // planet responds smoothly instead of using a straight chord from entry.
            PlanetSurfaceState currentState = surfaceState;
            float tileSize = treadmill.TileSizeMeters;
            Vector2 inTileOffset = new Vector2(
                (float)((double)target.position.x - surfaceOrigin.x - currentState.logicalTile.x * tileSize),
                (float)((double)target.position.z - surfaceOrigin.z - currentState.logicalTile.z * tileSize));
            PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTangentOffset(ref currentState, inTileOffset, planet.RadiusMeters);
            currentState.Normalize();

            Quaternion currentSpaceToSurface = currentState.SpaceToSurfaceRotation;
            // Both values map the same space frame into a flat frame. Their relative
            // rotation is the counter-motion of the celestial system around the planet.
            Quaternion deltaRotation = currentSpaceToSurface * Quaternion.Inverse(entrySpaceToSurfaceRotation);
            float altitude = GetSurfaceAltitude(target.position);
            Vector3 displayedPlanetCenter = target.position - Vector3.up * (planet.RadiusMeters + altitude);
            Vector3 displayedRootPosition = displayedPlanetCenter + deltaRotation * (entryCelestialRootPositionInSurfaceFrame - entryPlanetCenterInSurfaceFrame);
            Quaternion displayedRootRotation = deltaRotation * entryCelestialRootRotationInSurfaceFrame;
            celestialSystemRoot.SetPositionAndRotation(displayedRootPosition, displayedRootRotation);
        }

        void RestoreCelestialSpaceFrame()
        {
            if (!hasCelestialEntryPose)
                return;

            if (celestialSystemRoot != null)
                celestialSystemRoot.SetPositionAndRotation(spaceCelestialRootPosition, spaceCelestialRootRotation);
            hasCelestialEntryPose = false;
        }

        void LateUpdate()
        {
            UpdateCelestialSurfaceFrame();
        }

        Vector3 ResolvePlanetaryUpInSpace()
        {
            return transitionState != null && transitionState.Planet != null && target != null
                ? transitionState.Planet.GetSurfaceUp(target.position)
                : Vector3.up;
        }

        void ResolveReferences()
        {
            if (owningRig != null && owningRig.Treadmill != null && treadmill != owningRig.Treadmill)
                SetTreadmill(owningRig.Treadmill);
            if (terrainRoot == null && treadmill != null)
                terrainRoot = treadmill.gameObject;
            ResolveTargetReferences();
        }

        bool IsActivePlanetRig()
        {
            if (activePlanetSelector == null)
                return true;

            return owningRig != null
                ? activePlanetSelector.IsActiveRig(owningRig)
                : transitionState != null && activePlanetSelector.ActivePlanet == transitionState.Planet;
        }

        void ResolveTargetReferences()
        {
            if (target == null && transitionState != null)
                target = transitionState.Target;
            if (targetRigidbody == null && target != null)
                targetRigidbody = target.GetComponentInParent<Rigidbody>();
            if (targetRigidbody == null && target == null)
                targetRigidbody = FindFirstObjectByType<Rigidbody>();
            if (target == null && targetRigidbody != null)
                target = targetRigidbody.transform;
        }

        bool HasRequiredReferences()
        {
            return transitionState != null && transitionState.Planet != null && target != null && treadmill != null;
        }

        void SetTreadmill(PlanetTerrainTreadmill terrainTreadmill)
        {
            if (treadmill == terrainTreadmill)
                return;

            UnsubscribeTreadmill();
            treadmill = terrainTreadmill;
            SubscribeTreadmill();
        }

        void SubscribeTreadmill()
        {
            if (subscribedTreadmill == treadmill || treadmill == null)
                return;

            UnsubscribeTreadmill();
            subscribedTreadmill = treadmill;
            subscribedTreadmill.CenterTileChanged += OnTreadmillCenterTileChanged;
        }

        void UnsubscribeTreadmill()
        {
            if (subscribedTreadmill == null)
                return;

            subscribedTreadmill.CenterTileChanged -= OnTreadmillCenterTileChanged;
            subscribedTreadmill = null;
        }

        void SetTerrainRootActive(bool active)
        {
            if (driveTerrainRootVisibility && terrainRoot != null && terrainRoot.activeSelf != active)
                terrainRoot.SetActive(active);

            if (driveTerrainRootVisibility && treadmillDetachedForSurfaceFrame && treadmill != null && treadmill.gameObject.activeSelf != active)
                treadmill.gameObject.SetActive(active);
        }

        private void OnGUI()
        {
            if (!showDebugOverlay || travelMode != PlanetTravelMode.Surface || target == null)
                return;

            GUILayout.BeginArea(new Rect(10f, 240f, 520f, 170f), GUI.skin.box);
            GUILayout.Label($"Planet travel mode: {travelMode}");
            string centerTileText = treadmill != null ? treadmill.CenterTile.ToString() : "none";
            GUILayout.Label($"Treadmill center tile: {centerTileText}");
            GUILayout.Label($"Ship local surface position: {target.position}");
            GUILayout.Label($"Local altitude: {GetSurfaceAltitude(target.position):0.##} m");
            GUILayout.Label($"Logical radial up: {surfaceState.logicalSurfaceUp}");
            GUILayout.Label($"Logical tiles traveled from entry: {logicalTilesTraveled}");
            GUILayout.EndArea();
        }
    }
}
