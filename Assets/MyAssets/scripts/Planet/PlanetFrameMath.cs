using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Pure math helpers for converting between spherical planet-space and local tangent frames.
    /// </summary>
    public static class PlanetFrameMath
    {
        private const float CenterEpsilon = 0.0001f;

        public static Vector3 GetSurfaceUp(Vector3 planetCenter, Vector3 planetSpacePosition)
        {
            Vector3 fromCenter = planetSpacePosition - planetCenter;
            if (fromCenter.sqrMagnitude <= CenterEpsilon * CenterEpsilon)
                return Vector3.up;

            return fromCenter.normalized;
        }

        public static Vector3 GetSurfaceDown(Vector3 planetCenter, Vector3 planetSpacePosition)
        {
            return -GetSurfaceUp(planetCenter, planetSpacePosition);
        }

        public static float GetAltitude(Vector3 planetCenter, float planetRadiusMeters, Vector3 planetSpacePosition)
        {
            return Vector3.Distance(planetCenter, planetSpacePosition) - Mathf.Max(1f, planetRadiusMeters);
        }

        public static Vector3 GetSurfacePoint(Vector3 planetCenter, float planetRadiusMeters, Vector3 planetSpacePosition)
        {
            return planetCenter + GetSurfaceUp(planetCenter, planetSpacePosition) * Mathf.Max(1f, planetRadiusMeters);
        }

        public static void BuildTangentBasis(
            Vector3 planetCenter,
            Vector3 planetSpacePosition,
            out Vector3 up,
            out Vector3 right,
            out Vector3 forward)
        {
            up = GetSurfaceUp(planetCenter, planetSpacePosition);

            // Pick a reference axis that is not parallel to up. This avoids pole/axis degeneracy.
            Vector3 referenceForward = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) > 0.95f
                ? Vector3.right
                : Vector3.forward;

            right = Vector3.Cross(referenceForward, up);
            if (right.sqrMagnitude <= CenterEpsilon * CenterEpsilon)
                right = Vector3.Cross(Vector3.up, up);
            if (right.sqrMagnitude <= CenterEpsilon * CenterEpsilon)
                right = Vector3.right;
            right.Normalize();

            forward = Vector3.Cross(up, right);
            if (forward.sqrMagnitude <= CenterEpsilon * CenterEpsilon)
                forward = Vector3.forward;
            forward.Normalize();
        }

        public static Quaternion BuildLocalToPlanetRotation(Vector3 up, Vector3 forward)
        {
            return Quaternion.LookRotation(forward.normalized, up.normalized);
        }

        public static Quaternion BuildPlanetToLocalRotation(Vector3 up, Vector3 forward)
        {
            return Quaternion.Inverse(BuildLocalToPlanetRotation(up, forward));
        }
    }
}
