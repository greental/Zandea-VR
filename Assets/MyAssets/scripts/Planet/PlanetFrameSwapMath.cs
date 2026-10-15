using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// Pure math for the Space <-> flat Surface frame swap. Does not touch scene objects.
    /// </summary>
    public static class PlanetFrameSwapMath
    {
        public static PlanetSurfaceBasis BuildBasis(Vector3 planetCenter, Vector3 spacePosition, Quaternion shipRotation)
        {
            Vector3 surfaceUp = PlanetFrameMath.GetSurfaceUp(planetCenter, spacePosition);
            return PlanetSurfaceBasis.FromUpAndPreferredForward(surfaceUp, shipRotation * Vector3.forward);
        }

        public static PlanetFrameTransform BuildSpaceToSurfaceTransform(Vector3 planetCenter, float planetRadiusMeters, PlanetSurfaceBasis basis)
        {
            Vector3 surfaceAnchor = planetCenter + basis.surfaceUp.normalized * Mathf.Max(1f, planetRadiusMeters);
            return new PlanetFrameTransform(surfaceAnchor, Vector3.zero, basis.SpaceToSurfaceRotation);
        }

        public static PlanetFrameTransform BuildSurfaceToSpaceTransform(Vector3 planetCenter, float planetRadiusMeters, PlanetSurfaceBasis logicalBasis)
        {
            return BuildSpaceToSurfaceTransform(planetCenter, planetRadiusMeters, logicalBasis).Inverse();
        }

        public static PlanetSurfaceBasis BasisFromState(PlanetSurfaceState state)
        {
            return new PlanetSurfaceBasis(state.logicalSurfaceUp, state.logicalEast, state.logicalNorth);
        }

        public static void TransformRigidbodyState(
            PlanetFrameTransform frameTransform,
            Vector3 position,
            Quaternion rotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            out Vector3 transformedPosition,
            out Quaternion transformedRotation,
            out Vector3 transformedLinearVelocity,
            out Vector3 transformedAngularVelocity)
        {
            transformedPosition = frameTransform.TransformPoint(position);
            transformedRotation = frameTransform.TransformRotation(rotation);
            transformedLinearVelocity = frameTransform.TransformDirection(linearVelocity);
            transformedAngularVelocity = frameTransform.TransformDirection(angularVelocity);
        }
    }
}
