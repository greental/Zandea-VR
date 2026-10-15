using MyAssets.PlanetTerrain;
using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Pure coordinate conversion helpers for handoff between spherical planet-space and flat Y-up surface-space.
    /// These helpers do not touch Terrain or scene hierarchy state.
    /// </summary>
    public static class PlanetSurfaceFrameMath
    {
        private const float Epsilon = 0.000001f;

        public static void BuildStableTangentBasis(Vector3 surfaceUp, Vector3 preferredForward, out Vector3 east, out Vector3 north)
        {
            surfaceUp = SafeNormalize(surfaceUp, Vector3.up);

            north = Vector3.ProjectOnPlane(preferredForward, surfaceUp);
            if (north.sqrMagnitude <= Epsilon)
                north = Vector3.ProjectOnPlane(Vector3.forward, surfaceUp);
            if (north.sqrMagnitude <= Epsilon)
                north = Vector3.ProjectOnPlane(Vector3.right, surfaceUp);
            if (north.sqrMagnitude <= Epsilon)
            {
                Vector3 fallback = Mathf.Abs(Vector3.Dot(surfaceUp, Vector3.up)) < 0.95f ? Vector3.up : Vector3.right;
                north = Vector3.ProjectOnPlane(fallback, surfaceUp);
            }

            north = SafeNormalize(north, Vector3.forward);
            // Surface convention is +X=east, +Y=up, +Z=north. With Unity's
            // LookRotation(north, up), east is up x north (north x up reverses X).
            east = Vector3.Cross(surfaceUp, north);
            east = SafeNormalize(east, Vector3.right);
            north = Vector3.Cross(east, surfaceUp);
            north = SafeNormalize(north, Vector3.forward);
        }

        public static void OrthonormalizeBasis(ref Vector3 surfaceUp, ref Vector3 east, ref Vector3 north)
        {
            surfaceUp = SafeNormalize(surfaceUp, Vector3.up);

            Vector3 projectedNorth = Vector3.ProjectOnPlane(north, surfaceUp);
            if (projectedNorth.sqrMagnitude > Epsilon)
            {
                north = projectedNorth.normalized;
                east = Vector3.Cross(surfaceUp, north);
                east = SafeNormalize(east, Vector3.right);
                north = Vector3.Cross(east, surfaceUp);
                north = SafeNormalize(north, Vector3.forward);
                return;
            }

            Vector3 projectedEast = Vector3.ProjectOnPlane(east, surfaceUp);
            if (projectedEast.sqrMagnitude > Epsilon)
            {
                east = projectedEast.normalized;
                north = Vector3.Cross(east, surfaceUp);
                north = SafeNormalize(north, Vector3.forward);
                east = Vector3.Cross(surfaceUp, north);
                east = SafeNormalize(east, Vector3.right);
                return;
            }

            BuildStableTangentBasis(surfaceUp, Vector3.forward, out east, out north);
        }

        public static Quaternion BuildSurfaceToSpaceRotation(Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            Vector3 up = SafeNormalize(surfaceUp, Vector3.up);
            BuildStableTangentBasis(up, surfaceNorth, out _, out Vector3 north);
            return Quaternion.LookRotation(north, up);
        }

        public static Quaternion BuildSpaceToSurfaceRotation(Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            return Quaternion.Inverse(BuildSurfaceToSpaceRotation(surfaceUp, surfaceNorth));
        }

        public static Vector3 SpaceToSurfacePosition(Vector3 planetCenter, float planetRadiusMeters, Vector3 planetSpacePosition)
        {
            float altitude = PlanetFrameMath.GetAltitude(planetCenter, Mathf.Max(1f, planetRadiusMeters), planetSpacePosition);
            return new Vector3(0f, altitude, 0f);
        }

        public static Vector3 SurfaceToSpacePosition(Vector3 planetCenter, float planetRadiusMeters, Vector3 logicalSurfaceUp, Vector3 surfacePosition)
        {
            Vector3 up = SafeNormalize(logicalSurfaceUp, Vector3.up);
            return planetCenter + up * (Mathf.Max(1f, planetRadiusMeters) + surfacePosition.y);
        }

        public static Quaternion SpaceToSurfaceRotation(Quaternion spaceRotation, Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            return BuildSpaceToSurfaceRotation(surfaceUp, surfaceNorth) * spaceRotation;
        }

        public static Quaternion SurfaceToSpaceRotation(Quaternion surfaceRotation, Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            return BuildSurfaceToSpaceRotation(surfaceUp, surfaceNorth) * surfaceRotation;
        }

        public static Vector3 SpaceToSurfaceVector(Vector3 spaceVector, Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            return BuildSpaceToSurfaceRotation(surfaceUp, surfaceNorth) * spaceVector;
        }

        public static Vector3 SurfaceToSpaceVector(Vector3 surfaceVector, Vector3 surfaceUp, Vector3 surfaceNorth)
        {
            return BuildSurfaceToSpaceRotation(surfaceUp, surfaceNorth) * surfaceVector;
        }

        public static PlanetSurfaceState CaptureSurfaceState(
            Vector3 planetCenter,
            Vector3 planetSpacePosition,
            Quaternion shipSpaceRotation,
            PlanetTileCoord initialTile = default)
        {
            Vector3 preferredForward = shipSpaceRotation * Vector3.forward;
            return PlanetSurfaceState.FromSpacePosition(planetCenter, planetSpacePosition, preferredForward, initialTile);
        }

        public static void SpaceToSurfaceTransform(
            Vector3 planetCenter,
            float planetRadiusMeters,
            Vector3 spacePosition,
            Quaternion spaceRotation,
            Vector3 spaceVelocity,
            Vector3 spaceAngularVelocity,
            PlanetSurfaceState surfaceState,
            out Vector3 surfacePosition,
            out Quaternion surfaceRotation,
            out Vector3 surfaceVelocity,
            out Vector3 surfaceAngularVelocity)
        {
            surfacePosition = SpaceToSurfacePosition(planetCenter, planetRadiusMeters, spacePosition);
            surfaceRotation = SpaceToSurfaceRotation(spaceRotation, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
            surfaceVelocity = SpaceToSurfaceVector(spaceVelocity, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
            surfaceAngularVelocity = SpaceToSurfaceVector(spaceAngularVelocity, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
        }

        public static void SurfaceToSpaceTransform(
            Vector3 planetCenter,
            float planetRadiusMeters,
            Vector3 surfacePosition,
            Quaternion surfaceRotation,
            Vector3 surfaceVelocity,
            Vector3 surfaceAngularVelocity,
            PlanetSurfaceState surfaceState,
            out Vector3 spacePosition,
            out Quaternion spaceRotation,
            out Vector3 spaceVelocity,
            out Vector3 spaceAngularVelocity)
        {
            spacePosition = SurfaceToSpacePosition(planetCenter, planetRadiusMeters, surfaceState.logicalSurfaceUp, surfacePosition);
            spaceRotation = SurfaceToSpaceRotation(surfaceRotation, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
            spaceVelocity = SurfaceToSpaceVector(surfaceVelocity, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
            spaceAngularVelocity = SurfaceToSpaceVector(surfaceAngularVelocity, surfaceState.logicalSurfaceUp, surfaceState.logicalNorth);
        }

        public static void AdvanceLogicalSurfaceByTileDelta(ref PlanetSurfaceState state, PlanetTileCoord delta, float tileSizeMeters, float planetRadiusMeters)
        {
            if (!state.valid || (delta.x == 0 && delta.z == 0))
                return;

            float angleDegrees = (Mathf.Max(0f, tileSizeMeters) / Mathf.Max(1f, planetRadiusMeters)) * Mathf.Rad2Deg;
            if (Mathf.Approximately(angleDegrees, 0f))
                return;

            int stepsX = Mathf.Abs(delta.x);
            int signX = delta.x >= 0 ? 1 : -1;
            for (int i = 0; i < stepsX; i++)
                AdvanceOneStep(ref state, state.logicalEast * signX, angleDegrees);

            int stepsZ = Mathf.Abs(delta.z);
            int signZ = delta.z >= 0 ? 1 : -1;
            for (int i = 0; i < stepsZ; i++)
                AdvanceOneStep(ref state, state.logicalNorth * signZ, angleDegrees);

            state.logicalTile = new PlanetTileCoord(state.logicalTile.x + delta.x, state.logicalTile.z + delta.z);
        }

        /// <summary>
        /// Advances the spherical frame from the current logical tile center to a position
        /// within that tile. Used when converting back to planet space so the final partial
        /// tile offset is preserved along with whole-tile treadmill recentering.
        /// </summary>
        public static void AdvanceLogicalSurfaceByTangentOffset(ref PlanetSurfaceState state, Vector2 offsetMeters, float planetRadiusMeters)
        {
            if (!state.valid)
                return;

            Vector3 tangent = state.logicalEast * offsetMeters.x + state.logicalNorth * offsetMeters.y;
            float distance = tangent.magnitude;
            if (distance <= Epsilon)
                return;

            float angleDegrees = distance / Mathf.Max(1f, planetRadiusMeters) * Mathf.Rad2Deg;
            AdvanceOneStep(ref state, tangent / distance, angleDegrees);
        }

        static void AdvanceOneStep(ref PlanetSurfaceState state, Vector3 tangentDirection, float angleDegrees)
        {
            OrthonormalizeBasis(ref state.logicalSurfaceUp, ref state.logicalEast, ref state.logicalNorth);

            tangentDirection = Vector3.ProjectOnPlane(tangentDirection, state.logicalSurfaceUp);
            if (tangentDirection.sqrMagnitude <= Epsilon)
                return;
            tangentDirection.Normalize();

            Vector3 axis = Vector3.Cross(state.logicalSurfaceUp, tangentDirection);
            if (axis.sqrMagnitude <= Epsilon)
                return;

            Quaternion rotation = Quaternion.AngleAxis(angleDegrees, axis.normalized);
            Vector3 previousNorth = state.logicalNorth;
            state.logicalSurfaceUp = (rotation * state.logicalSurfaceUp).normalized;
            state.logicalNorth = Vector3.ProjectOnPlane(rotation * previousNorth, state.logicalSurfaceUp);
            OrthonormalizeBasis(ref state.logicalSurfaceUp, ref state.logicalEast, ref state.logicalNorth);
        }

        static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > Epsilon ? value.normalized : fallback.normalized;
        }
    }
}
