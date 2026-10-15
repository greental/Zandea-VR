using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Optional planet-space gravity applicator. Assign explicit bodies to avoid double-applying gravity to systems
    /// that already manage their own local-surface gravity/damping.
    /// </summary>
    [AddComponentMenu("Koombar/Planet/Planet Gravity Source")]
    public sealed class PlanetGravitySource : MonoBehaviour
    {
        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private Rigidbody[] bodies;
        [SerializeField] private bool applyInLocalSurfaceMode;

        private void Reset()
        {
            planet = GetComponent<PlanetDefinition>();
        }

        private void FixedUpdate()
        {
            if (planet == null || bodies == null)
                return;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                    continue;

                float altitude = planet.GetAltitude(body.position);
                if (!applyInLocalSurfaceMode && altitude <= planet.SurfaceModeAltitudeMeters)
                    continue;

                Vector3 down = PlanetFrameMath.GetSurfaceDown(planet.Center, body.position);
                body.AddForce(down * planet.GravityAcceleration, ForceMode.Acceleration);
            }
        }
    }
}