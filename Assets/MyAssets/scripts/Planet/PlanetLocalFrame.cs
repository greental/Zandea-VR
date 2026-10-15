using System;
using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// A stable tangent-space frame anchored to one point on a spherical planet.
    /// Local Unity axes are: X = surface right/east, Y = surface up, Z = surface forward/north.
    /// </summary>
    [Serializable]
    public readonly struct PlanetLocalFrame
    {
        public readonly Vector3 planetCenter;
        public readonly float planetRadiusMeters;
        public readonly Vector3 surfacePoint;
        public readonly Vector3 up;
        public readonly Vector3 right;
        public readonly Vector3 forward;
        public readonly Quaternion localToPlanetRotation;
        public readonly Quaternion planetToLocalRotation;

        public PlanetLocalFrame(
            Vector3 planetCenter,
            float planetRadiusMeters,
            Vector3 surfacePoint,
            Vector3 up,
            Vector3 right,
            Vector3 forward)
        {
            this.planetCenter = planetCenter;
            this.planetRadiusMeters = Mathf.Max(1f, planetRadiusMeters);
            this.surfacePoint = surfacePoint;
            this.up = up.normalized;
            this.right = right.normalized;
            this.forward = forward.normalized;
            localToPlanetRotation = Quaternion.LookRotation(this.forward, this.up);
            planetToLocalRotation = Quaternion.Inverse(localToPlanetRotation);
        }

        public static PlanetLocalFrame FromPlanetPosition(Vector3 planetCenter, float planetRadiusMeters, Vector3 anchorPlanetSpacePosition)
        {
            PlanetFrameMath.BuildTangentBasis(planetCenter, anchorPlanetSpacePosition, out Vector3 frameUp, out Vector3 frameRight, out Vector3 frameForward);
            Vector3 frameSurfacePoint = planetCenter + frameUp * Mathf.Max(1f, planetRadiusMeters);
            return new PlanetLocalFrame(planetCenter, planetRadiusMeters, frameSurfacePoint, frameUp, frameRight, frameForward);
        }

        public float GetAltitude(Vector3 planetSpacePosition)
        {
            return PlanetFrameMath.GetAltitude(planetCenter, planetRadiusMeters, planetSpacePosition);
        }

        public Vector3 PlanetToLocalPosition(Vector3 planetSpacePosition)
        {
            return planetToLocalRotation * (planetSpacePosition - surfacePoint);
        }

        public Vector3 LocalToPlanetPosition(Vector3 localPosition)
        {
            return surfacePoint + localToPlanetRotation * localPosition;
        }

        public Vector3 PlanetToLocalDirection(Vector3 planetSpaceDirection)
        {
            return planetToLocalRotation * planetSpaceDirection;
        }

        public Vector3 LocalToPlanetDirection(Vector3 localDirection)
        {
            return localToPlanetRotation * localDirection;
        }

        public Quaternion PlanetToLocalRotation(Quaternion planetSpaceRotation)
        {
            return planetToLocalRotation * planetSpaceRotation;
        }

        public Quaternion LocalToPlanetRotation(Quaternion localRotation)
        {
            return localToPlanetRotation * localRotation;
        }

        public Vector3 PlanetToLocalVelocity(Vector3 planetSpaceVelocity)
        {
            return PlanetToLocalDirection(planetSpaceVelocity);
        }

        public Vector3 LocalToPlanetVelocity(Vector3 localVelocity)
        {
            return LocalToPlanetDirection(localVelocity);
        }
    }
}
