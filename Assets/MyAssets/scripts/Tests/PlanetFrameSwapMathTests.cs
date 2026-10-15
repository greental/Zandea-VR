using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using NUnit.Framework;
using UnityEngine;

public sealed class PlanetFrameSwapMathTests
{
    const float k_PositionTolerance = 0.001f;
    const float k_DirectionTolerance = 0.0001f;
    const float k_RotationToleranceDeg = 0.001f;

    [Test]
    public void FrameTransform_InverseRoundTripsPointDirectionAndRotation()
    {
        PlanetFrameTransform transform = new PlanetFrameTransform(
            new Vector3(10f, -3f, 42f),
            new Vector3(-5f, 7f, 2f),
            Quaternion.Euler(31f, 123f, -17f));
        PlanetFrameTransform inverse = transform.Inverse();

        Vector3 point = new Vector3(100f, 12.5f, -77f);
        Vector3 direction = new Vector3(0.25f, -0.5f, 0.75f).normalized;
        Quaternion rotation = Quaternion.Euler(-12f, 88f, 44f);

        Vector3 pointRoundTrip = inverse.TransformPoint(transform.TransformPoint(point));
        Vector3 directionRoundTrip = inverse.TransformDirection(transform.TransformDirection(direction));
        Quaternion rotationRoundTrip = inverse.TransformRotation(transform.TransformRotation(rotation));

        AssertVectorClose(point, pointRoundTrip, k_PositionTolerance, "point");
        AssertVectorClose(direction, directionRoundTrip, k_DirectionTolerance, "direction");
        Assert.LessOrEqual(Quaternion.Angle(rotation, rotationRoundTrip), k_RotationToleranceDeg, "rotation");
    }

    [Test]
    public void SpaceToSurface_UsesTranslationAndRotationForPositions()
    {
        Vector3 planetCenter = new Vector3(1000f, 200f, -300f);
        float radius = 50000f;
        PlanetSurfaceBasis basis = PlanetSurfaceBasis.FromUpAndPreferredForward(Vector3.up, Vector3.forward);
        PlanetFrameTransform spaceToSurface = PlanetFrameSwapMath.BuildSpaceToSurfaceTransform(planetCenter, radius, basis);

        Vector3 anchor = planetCenter + Vector3.up * radius;
        Vector3 eastPoint = anchor + Vector3.right * 25f;
        Vector3 altitudePoint = anchor + Vector3.up * 125f;
        Vector3 northPoint = anchor + Vector3.forward * 40f;

        AssertVectorClose(Vector3.zero, spaceToSurface.TransformPoint(anchor), k_PositionTolerance, "anchor");
        AssertVectorClose(Vector3.right * 25f, spaceToSurface.TransformPoint(eastPoint), k_PositionTolerance, "east offset");
        AssertVectorClose(Vector3.up * 125f, spaceToSurface.TransformPoint(altitudePoint), k_PositionTolerance, "altitude offset");
        AssertVectorClose(Vector3.forward * 40f, spaceToSurface.TransformPoint(northPoint), k_PositionTolerance, "north offset");
    }

    [Test]
    public void RigidbodyState_RoundTripsThroughSpaceSurfaceFrame()
    {
        Vector3 planetCenter = Vector3.zero;
        float radius = 50000f;
        Vector3 spacePosition = new Vector3(15000f, 47000f, 9000f);
        Quaternion spaceRotation = Quaternion.Euler(12f, 80f, 33f);
        Vector3 linearVelocity = new Vector3(120f, -30f, 400f);
        Vector3 angularVelocity = new Vector3(0.2f, -0.8f, 0.45f);

        PlanetSurfaceBasis basis = PlanetFrameSwapMath.BuildBasis(planetCenter, spacePosition, spaceRotation);
        PlanetFrameTransform spaceToSurface = PlanetFrameSwapMath.BuildSpaceToSurfaceTransform(planetCenter, radius, basis);
        PlanetFrameTransform surfaceToSpace = spaceToSurface.Inverse();

        PlanetFrameSwapMath.TransformRigidbodyState(
            spaceToSurface,
            spacePosition,
            spaceRotation,
            linearVelocity,
            angularVelocity,
            out Vector3 surfacePosition,
            out Quaternion surfaceRotation,
            out Vector3 surfaceLinearVelocity,
            out Vector3 surfaceAngularVelocity);

        PlanetFrameSwapMath.TransformRigidbodyState(
            surfaceToSpace,
            surfacePosition,
            surfaceRotation,
            surfaceLinearVelocity,
            surfaceAngularVelocity,
            out Vector3 roundTripPosition,
            out Quaternion roundTripRotation,
            out Vector3 roundTripLinearVelocity,
            out Vector3 roundTripAngularVelocity);

        AssertVectorClose(spacePosition, roundTripPosition, 0.01f, "position");
        Assert.LessOrEqual(Quaternion.Angle(spaceRotation, roundTripRotation), 0.01f, "rotation");
        AssertVectorClose(linearVelocity, roundTripLinearVelocity, 0.001f, "linear velocity");
        AssertVectorClose(angularVelocity, roundTripAngularVelocity, 0.001f, "angular velocity");
    }

    [Test]
    public void SurfaceExitFrame_IncludesPartialTileOffsetAfterTreadmillRecenter()
    {
        const float radius = 60000f;
        const float tileSize = 1024f;
        Vector3 center = new Vector3(120f, -60000f, -75f);
        Vector3 entryPosition = center + Vector3.right * (radius + 9000f);
        PlanetSurfaceState state = PlanetSurfaceFrameMath.CaptureSurfaceState(
            center, entryPosition, Quaternion.LookRotation(Vector3.forward, Vector3.right));
        state.Normalize();

        PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTileDelta(ref state, new PlanetTileCoord(1, 0), tileSize, radius);
        Vector3 tileCenterUp = state.logicalSurfaceUp;
        PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTangentOffset(ref state, new Vector2(300f, -200f), radius);

        Assert.AreEqual(1, state.logicalTile.x);
        Assert.AreEqual(0, state.logicalTile.z);
        Assert.Greater(Vector3.Angle(tileCenterUp, state.logicalSurfaceUp), 0.1f);
        Vector3 planetSpaceExit = PlanetSurfaceFrameMath.SurfaceToSpacePosition(center, radius, state.logicalSurfaceUp, Vector3.up * 14000f);
        Assert.AreEqual(radius + 14000f, Vector3.Distance(center, planetSpaceExit), 0.1f);
        Assert.Greater(Vector3.Angle(tileCenterUp, planetSpaceExit - center), 0.1f);
    }

    [Test]
    public void PositiveSurfaceX_AdvancesAlongLogicalEast()
    {
        const float radius = 30000f;
        const float travelMeters = 1000f;
        PlanetSurfaceState state = PlanetSurfaceFrameMath.CaptureSurfaceState(
            Vector3.zero,
            Vector3.up * (radius + 15000f),
            Quaternion.LookRotation(Vector3.forward, Vector3.up));
        state.Normalize();

        Vector3 initialEast = state.logicalEast;
        Assert.Greater(Vector3.Dot(initialEast, Vector3.right), 0.999f,
            "Positive flat X must be the east direction.");

        PlanetSurfaceFrameMath.AdvanceLogicalSurfaceByTangentOffset(
            ref state, new Vector2(travelMeters, 0f), radius);

        Assert.Greater(Vector3.Dot(state.logicalSurfaceUp, initialEast), 0f,
            "Positive flat X travel must move the radial up direction eastward.");
    }

    [Test]
    public void UniverseFrameState_ComposesOnlyDiscreteFrameTransforms()
    {
        GameObject go = new GameObject("UniverseFrameStateTest");
        try
        {
            PlanetUniverseFrameState state = go.AddComponent<PlanetUniverseFrameState>();
            PlanetFrameTransform first = new PlanetFrameTransform(Vector3.zero, new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            PlanetFrameTransform second = new PlanetFrameTransform(Vector3.zero, new Vector3(0f, 5f, 0f), Quaternion.Euler(30f, 0f, 0f));

            state.ApplyDiscreteFrameTransform(first);
            state.ApplyDiscreteFrameTransform(second);

            Vector3 expectedOrigin = second.TransformPoint(first.TransformPoint(Vector3.zero));
            Quaternion expectedRotation = PlanetFrameTransform.Normalize(second.rotation * first.rotation);

            Assert.AreEqual(2, state.DiscreteSwapCount);
            AssertVectorClose(expectedOrigin, state.CurrentOrigin, k_PositionTolerance, "composed origin");
            Assert.LessOrEqual(Quaternion.Angle(expectedRotation, state.CurrentRotation), k_RotationToleranceDeg, "composed rotation");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void AssertVectorClose(Vector3 expected, Vector3 actual, float tolerance, string label)
    {
        Assert.LessOrEqual(Vector3.Distance(expected, actual), tolerance, $"{label}: expected {expected}, actual {actual}");
    }
}
