using UnityEngine;
using MyAssets.PlanetTerrain;

namespace MyAssets.Planet
{
    /// <summary>
    /// Non-invasive Task 1 debug/proof component. Computes spherical altitude, tangent frame and cube-face tile address
    /// for a target without moving the ship, XR Origin, camera or Terrain hierarchy.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Frame Debug Probe")]
    public sealed class PlanetFrameDebugProbe : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Transform target;
        [SerializeField, Min(1f)] private float tileSizeMeters = 1024f;
        [SerializeField] private bool logOnTileChange = true;
        [SerializeField] private bool drawGizmos = true;

        [Header("Live values")]
        [SerializeField] private float altitudeMeters;
        [SerializeField] private PlanetCubeFace cubeFace;
        [SerializeField] private Vector2 faceUv;
        [SerializeField] private PlanetTileCoord logicalTile;

        private bool hasLastTile;
        private PlanetTileCoord lastTile;

        public float AltitudeMeters => altitudeMeters;
        public PlanetSurfaceAddress CurrentAddress => new PlanetSurfaceAddress(cubeFace, faceUv, logicalTile);
        public PlanetLocalFrame CurrentFrame => planet != null && target != null ? planet.BuildLocalFrame(target.position) : default;

        public void Configure(PlanetDefinition planetDefinition, Transform targetTransform, float tileSize)
        {
            planet = planetDefinition;
            target = targetTransform;
            tileSizeMeters = Mathf.Max(1f, tileSize);
            hasLastTile = false;
        }

        private void Reset()
        {
            planet = FindFirstObjectByType<PlanetDefinition>();
            Rigidbody rb = FindFirstObjectByType<Rigidbody>();
            if (rb != null)
                target = rb.transform;
        }

        private void Update()
        {
            if (planet == null || target == null)
                return;

            altitudeMeters = planet.GetAltitude(target.position);
            PlanetSurfaceAddress address = PlanetSurfaceMapper.PlanetPositionToAddress(planet.Center, target.position, tileSizeMeters, planet.RadiusMeters);
            cubeFace = address.face;
            faceUv = address.faceUv;
            logicalTile = address.tile;

            if (logOnTileChange && (!hasLastTile || !logicalTile.Equals(lastTile)))
            {
                Debug.Log($"[PlanetFrameDebugProbe] altitude={altitudeMeters:0.0}m address={address}", this);
                lastTile = logicalTile;
                hasLastTile = true;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || planet == null || target == null)
                return;

            PlanetLocalFrame frame = planet.BuildLocalFrame(target.position);
            Vector3 origin = target.position;
            float length = Mathf.Max(10f, tileSizeMeters * 0.15f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + frame.up * length);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + frame.right * length);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + frame.forward * length);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(frame.surfacePoint, Mathf.Max(2f, length * 0.04f));
        }
    }
}