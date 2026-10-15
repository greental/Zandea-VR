using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Slice-A debug probe for validating pure SPACE -> SURFACE -> SPACE conversion math.
    /// This component does not activate Terrain, move the treadmill, or manage travel mode.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Koombar/Planet/Planet Surface Round Trip Probe")]
    public sealed class PlanetSurfaceRoundTripProbe : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private Transform targetTransform;
        [SerializeField] private bool logOnStart;
        [SerializeField] private bool drawDebugBasis = true;
        [SerializeField, Min(0.1f)] private float gizmoScale = 25f;

        [Header("Last round-trip result")]
        [SerializeField] private PlanetSurfaceState lastSurfaceState;
        [SerializeField] private Vector3 lastSurfacePosition;
        [SerializeField] private Quaternion lastSurfaceRotation = Quaternion.identity;
        [SerializeField] private float lastPositionErrorMeters;
        [SerializeField] private float lastRotationErrorDegrees;
        [SerializeField] private float lastVelocityError;
        [SerializeField] private float lastAngularVelocityError;

        public PlanetSurfaceState LastSurfaceState => lastSurfaceState;
        public Vector3 LastSurfacePosition => lastSurfacePosition;
        public Quaternion LastSurfaceRotation => lastSurfaceRotation;
        public float LastPositionErrorMeters => lastPositionErrorMeters;
        public float LastRotationErrorDegrees => lastRotationErrorDegrees;
        public float LastVelocityError => lastVelocityError;
        public float LastAngularVelocityError => lastAngularVelocityError;

        private void Reset()
        {
            planet = FindFirstObjectByType<PlanetDefinition>();
            targetRigidbody = FindFirstObjectByType<Rigidbody>();
            targetTransform = targetRigidbody != null ? targetRigidbody.transform : transform;
        }

        private void Awake()
        {
            if (targetTransform == null && targetRigidbody != null)
                targetTransform = targetRigidbody.transform;
        }

        private void Start()
        {
            if (logOnStart)
                ValidateRoundTrip(true);
        }

        [ContextMenu("Validate SPACE -> SURFACE -> SPACE Round Trip")]
        public void ValidateRoundTripContextMenu()
        {
            ValidateRoundTrip(true);
        }

        public bool ValidateRoundTrip(bool logResult)
        {
            if (!ResolveReferences())
                return false;

            Vector3 originalPosition = targetTransform.position;
            Quaternion originalRotation = targetTransform.rotation;
            Vector3 originalVelocity = targetRigidbody != null ? targetRigidbody.linearVelocity : Vector3.zero;
            Vector3 originalAngularVelocity = targetRigidbody != null ? targetRigidbody.angularVelocity : Vector3.zero;

            lastSurfaceState = PlanetSurfaceFrameMath.CaptureSurfaceState(
                planet.Center,
                originalPosition,
                originalRotation);
            lastSurfaceState.Normalize();

            PlanetSurfaceBasis basis = PlanetFrameSwapMath.BasisFromState(lastSurfaceState);
            PlanetFrameTransform spaceToSurface = PlanetFrameSwapMath.BuildSpaceToSurfaceTransform(
                planet.Center,
                planet.RadiusMeters,
                basis);

            PlanetFrameSwapMath.TransformRigidbodyState(
                spaceToSurface,
                originalPosition,
                originalRotation,
                originalVelocity,
                originalAngularVelocity,
                out lastSurfacePosition,
                out lastSurfaceRotation,
                out Vector3 surfaceVelocity,
                out Vector3 surfaceAngularVelocity);

            PlanetFrameTransform surfaceToSpace = spaceToSurface.Inverse();
            PlanetFrameSwapMath.TransformRigidbodyState(
                surfaceToSpace,
                lastSurfacePosition,
                lastSurfaceRotation,
                surfaceVelocity,
                surfaceAngularVelocity,
                out Vector3 roundTripPosition,
                out Quaternion roundTripRotation,
                out Vector3 roundTripVelocity,
                out Vector3 roundTripAngularVelocity);

            lastPositionErrorMeters = Vector3.Distance(originalPosition, roundTripPosition);
            lastRotationErrorDegrees = Quaternion.Angle(originalRotation, roundTripRotation);
            lastVelocityError = Vector3.Distance(originalVelocity, roundTripVelocity);
            lastAngularVelocityError = Vector3.Distance(originalAngularVelocity, roundTripAngularVelocity);

            if (logResult)
            {
                Debug.Log(
                    $"[PlanetSurfaceRoundTripProbe] Round trip: " +
                    $"surfacePos={lastSurfacePosition}, " +
                    $"posError={lastPositionErrorMeters:0.######}m, " +
                    $"rotError={lastRotationErrorDegrees:0.######}deg, " +
                    $"velError={lastVelocityError:0.######}, " +
                    $"angVelError={lastAngularVelocityError:0.######}",
                    this);
            }

            return true;
        }

        bool ResolveReferences()
        {
            if (planet == null)
                planet = FindFirstObjectByType<PlanetDefinition>();
            if (targetRigidbody == null)
                targetRigidbody = FindFirstObjectByType<Rigidbody>();
            if (targetTransform == null)
                targetTransform = targetRigidbody != null ? targetRigidbody.transform : transform;

            if (planet != null && targetTransform != null)
                return true;

            Debug.LogWarning("[PlanetSurfaceRoundTripProbe] Missing PlanetDefinition or target Transform.", this);
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugBasis || !lastSurfaceState.valid)
                return;

            Vector3 origin = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + lastSurfaceState.logicalEast * gizmoScale);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + lastSurfaceState.logicalSurfaceUp * gizmoScale);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + lastSurfaceState.logicalNorth * gizmoScale);
        }
    }
}
