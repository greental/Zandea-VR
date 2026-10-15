using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Slice-B manual POC for visually invariant Space <-> flat Surface frame swaps.
    /// Does not initialize or drive Terrain. Terrain integration belongs to a later slice.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Koombar/Planet/Planet Frame Swap Controller")]
    public sealed class PlanetFrameSwapController : MonoBehaviour, IFloatingOriginListener
    {
        [Header("Core references")]
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Rigidbody shipRigidbody;
        [SerializeField] private Transform shipRoot;
        [SerializeField] private Transform xrOriginRoot;
        [SerializeField] private SitInCockpit sitInCockpit;

        [Header("Universe/world frame participants")]
        [SerializeField] private PlanetUniverseFrameState universeFrameState;
        [SerializeField] private Transform[] universeTransforms;
        [SerializeField] private Light[] directionalLights;
        [SerializeField] private PlanetSkyFrameController skyFrameController;

        [Header("Manual debug controls")]
        [SerializeField] private bool enableManualKeys = true;
        [SerializeField] private KeyCode spaceToSurfaceKey = KeyCode.F7;
        [SerializeField] private KeyCode surfaceToSpaceKey = KeyCode.F8;
        [SerializeField] private bool logSwaps = true;

        [Header("Live state")]
        [SerializeField] private PlanetTravelMode travelMode = PlanetTravelMode.Space;
        [SerializeField] private PlanetSurfaceState surfaceState;
        [SerializeField] private Vector3 lastEntryPlanetCenter;
        [SerializeField] private float lastPlanetRadiusMeters;
        [SerializeField] private PlanetFrameTransform lastAppliedFrameTransform = PlanetFrameTransform.Identity;
        [SerializeField] private bool lastXrWasOwnedByShip;

        public PlanetTravelMode TravelMode => travelMode;
        public PlanetSurfaceState SurfaceState => surfaceState;
        public bool LastXrWasOwnedByShip => lastXrWasOwnedByShip;
        private FloatingOriginPosition surfaceFrameOrigin;

        public void OnOriginShift(Vector3 shift)
        {
            lastEntryPlanetCenter -= shift;
            if (travelMode == PlanetTravelMode.Surface) surfaceFrameOrigin.Add(-shift);
        }

        private void Reset()
        {
            planet = FindFirstObjectByType<PlanetDefinition>();
            shipRigidbody = FindFirstObjectByType<Rigidbody>();
            shipRoot = shipRigidbody != null ? shipRigidbody.transform : null;
            sitInCockpit = FindFirstObjectByType<SitInCockpit>();
            universeFrameState = FindFirstObjectByType<PlanetUniverseFrameState>();
            skyFrameController = FindFirstObjectByType<PlanetSkyFrameController>();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!enableManualKeys)
                return;

            if (Input.GetKeyDown(spaceToSurfaceKey))
                TryEnterSurface();
            if (Input.GetKeyDown(surfaceToSpaceKey))
                TryExitSurface();
        }

        [ContextMenu("Manual SPACE -> SURFACE Frame Swap")]
        public void ManualEnterSurfaceContextMenu()
        {
            TryEnterSurface();
        }

        [ContextMenu("Manual SURFACE -> SPACE Frame Swap")]
        public void ManualExitSurfaceContextMenu()
        {
            TryExitSurface();
        }

        public bool TryEnterSurface()
        {
            ResolveReferences();
            if (!HasCoreReferences() || travelMode == PlanetTravelMode.Surface)
                return false;

            surfaceFrameOrigin = default;

            lastEntryPlanetCenter = planet.Center;
            lastPlanetRadiusMeters = planet.RadiusMeters;

            surfaceState = PlanetSurfaceState.FromSpacePosition(
                lastEntryPlanetCenter,
                shipRoot.position,
                shipRoot.forward);
            surfaceState.Normalize();

            PlanetSurfaceBasis basis = PlanetFrameSwapMath.BasisFromState(surfaceState);
            PlanetFrameTransform frameTransform = PlanetFrameSwapMath.BuildSpaceToSurfaceTransform(
                lastEntryPlanetCenter,
                lastPlanetRadiusMeters,
                basis);

            ApplyAtomicFrameSwap(frameTransform, PlanetTravelMode.Surface);
            return true;
        }

        public bool TryExitSurface()
        {
            ResolveReferences();
            if (!HasCoreReferences() || travelMode == PlanetTravelMode.Space || !surfaceState.valid)
                return false;

            surfaceState.Normalize();
            PlanetSurfaceBasis logicalBasis = PlanetFrameSwapMath.BasisFromState(surfaceState);
            PlanetFrameTransform frameTransform = PlanetFrameSwapMath.BuildSurfaceToSpaceTransform(
                lastEntryPlanetCenter,
                lastPlanetRadiusMeters > 1f ? lastPlanetRadiusMeters : planet.RadiusMeters,
                logicalBasis);

            frameTransform.fromOrigin = surfaceFrameOrigin.ToVector3();

            ApplyAtomicFrameSwap(frameTransform, PlanetTravelMode.Space);
            return true;
        }

        void ApplyAtomicFrameSwap(PlanetFrameTransform frameTransform, PlanetTravelMode targetMode)
        {
            RigidbodyInterpolation cachedInterpolation = shipRigidbody.interpolation;
            if (cachedInterpolation != RigidbodyInterpolation.None)
                shipRigidbody.interpolation = RigidbodyInterpolation.None;

            PlanetFrameSwapMath.TransformRigidbodyState(
                frameTransform,
                shipRigidbody.position,
                shipRigidbody.rotation,
                shipRigidbody.linearVelocity,
                shipRigidbody.angularVelocity,
                out Vector3 newPosition,
                out Quaternion newRotation,
                out Vector3 newLinearVelocity,
                out Vector3 newAngularVelocity);

            shipRigidbody.position = newPosition;
            shipRigidbody.rotation = newRotation;
            shipRigidbody.linearVelocity = newLinearVelocity;
            shipRigidbody.angularVelocity = newAngularVelocity;

            Transform xrOwner = ResolveXrFrameRoot();
            lastXrWasOwnedByShip = IsTransformOwnedByShip(xrOwner);
            if (xrOwner != null && !lastXrWasOwnedByShip)
                TransformFrameParticipant(xrOwner, frameTransform);

            TransformUniverseParticipants(frameTransform);

            if (skyFrameController != null)
                skyFrameController.ApplyFrameTransform(frameTransform);

            if (universeFrameState != null)
                universeFrameState.ApplyDiscreteFrameTransform(frameTransform);

            // Do not call Physics.SyncTransforms by default. Enable only if testing proves stale contacts/colliders.
            shipRigidbody.interpolation = cachedInterpolation;
            travelMode = targetMode;
            lastAppliedFrameTransform = frameTransform;

            if (logSwaps)
            {
                Debug.Log(
                    $"[PlanetFrameSwapController] {targetMode} frame swap applied. " +
                    $"shipPos={shipRigidbody.position}, speed={shipRigidbody.linearVelocity.magnitude:0.###}, " +
                    $"xrOwner={(xrOwner != null ? xrOwner.name : "none")}, xrOwnedByShip={lastXrWasOwnedByShip}, " +
                    $"universeSwaps={(universeFrameState != null ? universeFrameState.DiscreteSwapCount : -1)}",
                    this);
            }
        }

        void TransformUniverseParticipants(PlanetFrameTransform frameTransform)
        {
            if (universeTransforms != null)
            {
                for (int i = 0; i < universeTransforms.Length; i++)
                {
                    Transform t = universeTransforms[i];
                    if (t == null || t == shipRoot || t == xrOriginRoot)
                        continue;
                    TransformFrameParticipant(t, frameTransform);
                }
            }

            if (directionalLights != null)
            {
                for (int i = 0; i < directionalLights.Length; i++)
                {
                    Light light = directionalLights[i];
                    if (light != null && light.type == LightType.Directional)
                        light.transform.rotation = frameTransform.TransformRotation(light.transform.rotation);
                }
            }
        }

        static void TransformFrameParticipant(Transform target, PlanetFrameTransform frameTransform)
        {
            target.SetPositionAndRotation(
                frameTransform.TransformPoint(target.position),
                frameTransform.TransformRotation(target.rotation));
        }

        Transform ResolveXrFrameRoot()
        {
            if (xrOriginRoot != null)
                return xrOriginRoot;

            if (sitInCockpit != null && sitInCockpit.xrOrigin != null)
            {
                xrOriginRoot = sitInCockpit.xrOrigin;
                return xrOriginRoot;
            }

            Unity.XR.CoreUtils.XROrigin xro = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Exclude);
            if (xro != null)
            {
                xrOriginRoot = xro.Origin != null ? xro.Origin.transform : xro.transform;
                return xrOriginRoot;
            }

            return null;
        }

        bool IsTransformOwnedByShip(Transform candidate)
        {
            if (candidate == null || shipRoot == null)
                return false;

            return candidate == shipRoot || candidate.IsChildOf(shipRoot);
        }

        void ResolveReferences()
        {
            if (planet == null)
                planet = FindFirstObjectByType<PlanetDefinition>();
            if (shipRigidbody == null)
                shipRigidbody = FindFirstObjectByType<Rigidbody>();
            if (shipRoot == null && shipRigidbody != null)
                shipRoot = shipRigidbody.transform;
            if (sitInCockpit == null)
                sitInCockpit = FindFirstObjectByType<SitInCockpit>();
            if (universeFrameState == null)
                universeFrameState = FindFirstObjectByType<PlanetUniverseFrameState>();
            if (skyFrameController == null)
                skyFrameController = FindFirstObjectByType<PlanetSkyFrameController>();
            ResolveXrFrameRoot();
        }

        bool HasCoreReferences()
        {
            if (planet != null && shipRigidbody != null && shipRoot != null)
                return true;

            Debug.LogWarning("[PlanetFrameSwapController] Missing PlanetDefinition or ship Rigidbody/root.", this);
            return false;
        }
    }
}
