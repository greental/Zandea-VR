using UnityEngine;
using MyAssets.PlanetTerrain;

namespace MyAssets.Planet
{
    /// <summary>
    /// Converts spherical planet directions into regular cube-face UV/tile addresses.
    /// This is intentionally math-only and does not move or rotate Unity Terrain objects.
    /// </summary>
    public static class PlanetSurfaceMapper
    {
        public static PlanetSurfaceAddress DirectionToAddress(Vector3 surfaceDirection, float tileSizeMeters, float planetRadiusMeters)
        {
            Vector3 dir = surfaceDirection.sqrMagnitude > 0.000001f ? surfaceDirection.normalized : Vector3.up;
            PlanetCubeFace face = SelectFace(dir);
            Vector2 uv = DirectionToFaceUv(dir, face);
            PlanetTileCoord tile = FaceUvToTile(uv, tileSizeMeters, planetRadiusMeters);
            return new PlanetSurfaceAddress(face, uv, tile);
        }

        public static PlanetSurfaceAddress PlanetPositionToAddress(Vector3 planetCenter, Vector3 planetSpacePosition, float tileSizeMeters, float planetRadiusMeters)
        {
            return DirectionToAddress(PlanetFrameMath.GetSurfaceUp(planetCenter, planetSpacePosition), tileSizeMeters, planetRadiusMeters);
        }

        static PlanetCubeFace SelectFace(Vector3 dir)
        {
            float ax = Mathf.Abs(dir.x);
            float ay = Mathf.Abs(dir.y);
            float az = Mathf.Abs(dir.z);

            if (ax >= ay && ax >= az)
                return dir.x >= 0f ? PlanetCubeFace.PositiveX : PlanetCubeFace.NegativeX;
            if (ay >= ax && ay >= az)
                return dir.y >= 0f ? PlanetCubeFace.PositiveY : PlanetCubeFace.NegativeY;
            return dir.z >= 0f ? PlanetCubeFace.PositiveZ : PlanetCubeFace.NegativeZ;
        }

        static Vector2 DirectionToFaceUv(Vector3 dir, PlanetCubeFace face)
        {
            float u;
            float v;
            switch (face)
            {
                case PlanetCubeFace.PositiveX:
                    u = -dir.z / Mathf.Abs(dir.x);
                    v = dir.y / Mathf.Abs(dir.x);
                    break;
                case PlanetCubeFace.NegativeX:
                    u = dir.z / Mathf.Abs(dir.x);
                    v = dir.y / Mathf.Abs(dir.x);
                    break;
                case PlanetCubeFace.PositiveY:
                    u = dir.x / Mathf.Abs(dir.y);
                    v = -dir.z / Mathf.Abs(dir.y);
                    break;
                case PlanetCubeFace.NegativeY:
                    u = dir.x / Mathf.Abs(dir.y);
                    v = dir.z / Mathf.Abs(dir.y);
                    break;
                case PlanetCubeFace.PositiveZ:
                    u = dir.x / Mathf.Abs(dir.z);
                    v = dir.y / Mathf.Abs(dir.z);
                    break;
                default:
                    u = -dir.x / Mathf.Abs(dir.z);
                    v = dir.y / Mathf.Abs(dir.z);
                    break;
            }

            return new Vector2(Mathf.Clamp(u, -1f, 1f), Mathf.Clamp(v, -1f, 1f));
        }

        static PlanetTileCoord FaceUvToTile(Vector2 uv, float tileSizeMeters, float planetRadiusMeters)
        {
            float safeTileSize = Mathf.Max(1f, tileSizeMeters);
            float faceMeters = Mathf.Max(safeTileSize, Mathf.Max(1f, planetRadiusMeters) * 2f);
            float xMeters = (uv.x + 1f) * 0.5f * faceMeters;
            float zMeters = (uv.y + 1f) * 0.5f * faceMeters;
            return new PlanetTileCoord(Mathf.FloorToInt(xMeters / safeTileSize), Mathf.FloorToInt(zMeters / safeTileSize));
        }
    }
}