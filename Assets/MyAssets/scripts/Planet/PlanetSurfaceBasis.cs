using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Orthonormal tangent basis for a logical spherical surface point.
    /// Surface convention: +X=east, +Y=radial up, +Z=north.
    /// </summary>
    [System.Serializable]
    public struct PlanetSurfaceBasis
    {
        public Vector3 surfaceUp;
        public Vector3 surfaceEast;
        public Vector3 surfaceNorth;

        public Quaternion SurfaceToSpaceRotation => Quaternion.LookRotation(surfaceNorth, surfaceUp);
        public Quaternion SpaceToSurfaceRotation => Quaternion.Inverse(SurfaceToSpaceRotation);

        public PlanetSurfaceBasis(Vector3 surfaceUp, Vector3 surfaceEast, Vector3 surfaceNorth)
        {
            this.surfaceUp = surfaceUp;
            this.surfaceEast = surfaceEast;
            this.surfaceNorth = surfaceNorth;
            Normalize();
        }

        public static PlanetSurfaceBasis FromUpAndPreferredForward(Vector3 surfaceUp, Vector3 preferredForward)
        {
            PlanetSurfaceFrameMath.BuildStableTangentBasis(surfaceUp, preferredForward, out Vector3 east, out Vector3 north);
            return new PlanetSurfaceBasis(surfaceUp, east, north);
        }

        public void Normalize()
        {
            PlanetSurfaceFrameMath.OrthonormalizeBasis(ref surfaceUp, ref surfaceEast, ref surfaceNorth);
        }

        public bool IsValid(float tolerance = 0.01f)
        {
            if (surfaceUp.sqrMagnitude < 0.5f || surfaceEast.sqrMagnitude < 0.5f || surfaceNorth.sqrMagnitude < 0.5f)
                return false;

            float dotUE = Mathf.Abs(Vector3.Dot(surfaceUp.normalized, surfaceEast.normalized));
            float dotUN = Mathf.Abs(Vector3.Dot(surfaceUp.normalized, surfaceNorth.normalized));
            float dotEN = Mathf.Abs(Vector3.Dot(surfaceEast.normalized, surfaceNorth.normalized));
            return dotUE <= tolerance && dotUN <= tolerance && dotEN <= tolerance;
        }
    }
}
