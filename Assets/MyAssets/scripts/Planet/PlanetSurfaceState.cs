using MyAssets.PlanetTerrain;
using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Logical spherical surface state used while the player is in flat Terrain surface mode.
    /// This state is intentionally independent from Unity Terrain transforms: Terrain remains flat/Y-up.
    /// </summary>
    [System.Serializable]
    public struct PlanetSurfaceState
    {
        public bool valid;
        public Vector3 entrySurfaceUp;
        public Vector3 logicalSurfaceUp;
        public Vector3 logicalEast;
        public Vector3 logicalNorth;
        public PlanetTileCoord logicalTile;

        public Quaternion SurfaceToSpaceRotation => PlanetSurfaceFrameMath.BuildSurfaceToSpaceRotation(logicalSurfaceUp, logicalNorth);
        public Quaternion SpaceToSurfaceRotation => Quaternion.Inverse(SurfaceToSpaceRotation);

        public static PlanetSurfaceState FromSpacePosition(
            Vector3 planetCenter,
            Vector3 planetSpacePosition,
            Vector3 preferredForward,
            PlanetTileCoord initialTile = default)
        {
            Vector3 up = PlanetFrameMath.GetSurfaceUp(planetCenter, planetSpacePosition);
            PlanetSurfaceFrameMath.BuildStableTangentBasis(up, preferredForward, out Vector3 east, out Vector3 north);

            return new PlanetSurfaceState
            {
                valid = true,
                entrySurfaceUp = up,
                logicalSurfaceUp = up,
                logicalEast = east,
                logicalNorth = north,
                logicalTile = initialTile
            };
        }

        public void Normalize()
        {
            PlanetSurfaceFrameMath.OrthonormalizeBasis(ref logicalSurfaceUp, ref logicalEast, ref logicalNorth);
            if (entrySurfaceUp.sqrMagnitude <= 0.000001f)
                entrySurfaceUp = logicalSurfaceUp;
        }
    }
}
