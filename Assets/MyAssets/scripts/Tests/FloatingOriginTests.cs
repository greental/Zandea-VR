using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class FloatingOriginTests
{
    private readonly List<GameObject> objects = new List<GameObject>();

    private GameObject Create(string name)
    {
        var go = new GameObject(name);
        objects.Add(go);
        return go;
    }

    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);

    [Test]
    public void DefaultRecenteringDistanceIsTenKilometres()
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        var distance = typeof(FloatingOriginManager).GetField("recenterDistance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.AreEqual(10000f, (float)distance.GetValue(manager));
    }

    [Test]
    public void RecenterPreservesPhysicsPoseWhenTransformIsInterpolated()
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        var hierarchy = Create("Ship hierarchy").transform;
        var ship = Create("Ship").AddComponent<Rigidbody>();
        ship.useGravity = false;
        ship.transform.SetParent(hierarchy, false);
        ship.position = new Vector3(12000f, 0f, 0f);
        ship.transform.position = ship.position;
        // Represent a render-interpolated Transform that is one short frame away from
        // the authoritative Rigidbody pose when the LateUpdate origin shift occurs.
        ship.transform.position += Vector3.right * 0.25f;
        manager.Configure(ship.transform);
        manager.RecenterNow();

        Assert.Less(ship.position.magnitude + ship.transform.position.magnitude, 0.001f);
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (GameObject go in objects)
            if (go != null) Object.DestroyImmediate(go);
        objects.Clear();
    }

    [Test]
    public void AccumulatedOffsetRetainsSmallTranslationsAtLargeDistances()
    {
        var offset = new FloatingOriginPosition(1e12, -1e12, 1e12);
        offset.Add(new Vector3(0.125f, 0.25f, -0.5f));
        Assert.AreEqual(1e12 + 0.125, offset.x);
        Assert.AreEqual(-1e12 + 0.25, offset.y);
        Assert.AreEqual(1e12 - 0.5, offset.z);
    }

    [UnityTest]
    public IEnumerator RecenterPreservesSeatedHandsNestedBodiesAndVelocity()
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        var shipHierarchy = Create("Ship hierarchy").transform;
        var ship = Create("Ship").AddComponent<Rigidbody>();
        ship.useGravity = false;
        ship.transform.SetParent(shipHierarchy, false);
        ship.position = new Vector3(100000f, 75000f, -100000f);
        ship.rotation = Quaternion.Euler(15f, 40f, 8f);
        ship.transform.SetPositionAndRotation(ship.position, ship.rotation);
        ship.linearVelocity = new Vector3(30f, 2f, -5f);
        ship.angularVelocity = new Vector3(0.1f, 0.2f, 0.3f);
        ship.interpolation = RigidbodyInterpolation.Interpolate;
        var xr = Create("Seated XR Origin").transform;
        xr.SetParent(ship.transform, false);
        xr.localPosition = new Vector3(0f, 1.5f, 0.2f);
        var hand = Create("Tracked hand").transform;
        hand.SetParent(xr, false);
        hand.localPosition = new Vector3(0.125f, -0.25f, 0.375f);
        var control = Create("Kinematic cockpit control").AddComponent<Rigidbody>();
        control.isKinematic = true;
        control.transform.SetParent(ship.transform, false);
        control.transform.localPosition = new Vector3(0.5f, 1f, 0.25f);
        var planetObject = Create("Planet");
        planetObject.transform.position = new Vector3(0f, -60000f, 0f);
        var planet = planetObject.AddComponent<PlanetDefinition>();
        planet.ConfigurePrototypeScale(60000f, 15000f, 10000f, 10000f);
        float altitudeBeforeShift = planet.GetAltitude(ship.position);
        var inactive = Create("Inactive planet");
        inactive.transform.position = new Vector3(120000f, 0f, 0f);
        inactive.SetActive(false);
        Physics.SyncTransforms();

        Vector3 shift = ship.position;
        Vector3 controlPosition = control.position;
        Vector3 velocity = ship.linearVelocity;
        Quaternion rotation = ship.rotation;
        Vector3 handLocal = hand.localPosition;
        Vector3 xrLocal = xr.localPosition;
        Vector3 controlLocal = control.transform.localPosition;
        Transform controlParent = control.transform.parent;
        manager.Configure(ship.transform);
        manager.RecenterNow();

        Assert.Less(ship.position.magnitude, 0.001f);
        Assert.AreEqual(velocity, ship.linearVelocity);
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), ship.angularVelocity);
        Assert.Less(Quaternion.Angle(rotation, ship.rotation), 0.001f);
        Assert.AreEqual(handLocal, hand.localPosition);
        Assert.AreEqual(xrLocal, xr.localPosition);
        Assert.AreSame(controlParent, control.transform.parent);
        Assert.AreEqual(controlLocal, control.transform.localPosition);
        Assert.Less(Vector3.Distance(controlPosition - shift, control.position), 0.02f);
        Assert.Less(Vector3.Distance(controlPosition - shift, control.transform.position), 0.02f);
        Assert.AreEqual(altitudeBeforeShift, planet.GetAltitude(ship.position), 0.02f);
        Assert.AreEqual(new Vector3(120000f, 0f, 0f) - shift, inactive.transform.position);
        manager.ShiftOrigin(new Vector3(1200f, -300f, 500f));
        Assert.AreEqual(altitudeBeforeShift, planet.GetAltitude(ship.position), 0.02f);
        yield return new WaitForFixedUpdate();
        Assert.AreEqual(RigidbodyInterpolation.Interpolate, ship.interpolation);
    }

    [TestCase(20000f)]
    [TestCase(100000f)]
    public void RecenterRecoversSubMillimetreHandMotion(float startingDistance)
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        var ship = Create("Ship").transform;
        ship.position = new Vector3(startingDistance, startingDistance, startingDistance);
        var hand = Create("Hand").transform;
        hand.SetParent(ship, false);
        hand.localPosition = Vector3.one * 0.125f;
        const float movement = 0.00025f;
        float before = hand.position.x;
        hand.localPosition += Vector3.right * movement;
        float distantMotion = hand.position.x - before;
        manager.Configure(ship);
        FloatingOriginPosition absoluteShip = manager.ToAbsolutePosition(ship.position);
        manager.RecenterNow();
        before = hand.position.x;
        hand.localPosition += Vector3.right * movement;
        float nearMotion = hand.position.x - before;
        Assert.Less(Mathf.Abs(nearMotion - movement), 0.000001f);
        Assert.Greater(Mathf.Abs(distantMotion - movement), 0.0001f);
        Assert.AreEqual(absoluteShip.x, manager.ToAbsolutePosition(ship.position).x);
        Assert.Less(manager.ToDisplayedPosition(absoluteShip).magnitude, 0.0001f);
    }

    [Test]
    public void SeatZoneAcceptsUntaggedXROriginAndTracksItsColliders()
    {
        var rig = Create("XR Origin");
        var body = rig.AddComponent<CapsuleCollider>();
        var controller = Create("Controller collider");
        controller.transform.SetParent(rig.transform, false);
        var hand = controller.AddComponent<BoxCollider>();
        var seat = Create("Seat").AddComponent<SitInCockpit>();
        seat.xrOrigin = rig.transform;

        var enter = typeof(SitInCockpit).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic);
        var exit = typeof(SitInCockpit).GetMethod("OnTriggerExit", BindingFlags.Instance | BindingFlags.NonPublic);
        var inZone = typeof(SitInCockpit).GetField("insideSeatZone", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.AreEqual("Untagged", rig.tag);
        enter.Invoke(seat, new object[] { body });
        enter.Invoke(seat, new object[] { hand });
        Assert.IsTrue((bool)inZone.GetValue(seat));
        exit.Invoke(seat, new object[] { body });
        Assert.IsTrue((bool)inZone.GetValue(seat));
        exit.Invoke(seat, new object[] { hand });
        Assert.IsFalse((bool)inZone.GetValue(seat));
    }

    [UnityTest]
    public IEnumerator RepeatedShiftsPreserveTerrainAddressAndWorldSpaceLines()
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        Set(manager, "recenterOnStart", false);
        var target = Create("Target").transform;
        target.position = new Vector3(2050f, 3000f, -100f);
        var terrain = Create("Terrain addressing").AddComponent<PlanetTerrainTreadmill>();
        terrain.enabled = false;
        PlanetTileCoord original = terrain.WorldToTile(target.position);
        var line = Create("World line").AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPositions(new[] { target.position, target.position + Vector3.right });

        foreach (Vector3 shift in new[] { target.position, new Vector3(-100000f, 75000f, 32768f), new Vector3(100000f, -75000f, -32768f) })
        {
            manager.ShiftOrigin(shift);
            Assert.AreEqual(original, terrain.WorldToTile(target.position));
            Assert.Less(Vector3.Distance(target.position, line.GetPosition(0)), 0.001f);
        }
        target.position += Vector3.right * terrain.TileSizeMeters;
        Assert.AreEqual(original.x + 1, terrain.WorldToTile(target.position).x);
        yield return null;
    }

    [UnityTest]
    public IEnumerator SpaceSurfaceRoundTripPreservesAltitudeAfterOriginShifts()
    {
        var manager = Create("Origin").AddComponent<FloatingOriginManager>();
        Set(manager, "recenterOnStart", false);
        var ship = Create("Ship").AddComponent<Rigidbody>();
        ship.useGravity = false;
        ship.position = new Vector3(0f, 67500f, 0f);
        ship.transform.position = ship.position;
        ship.linearVelocity = new Vector3(30f, 1f, 2f);
        var planet = Create("Planet").AddComponent<PlanetDefinition>();
        planet.ConfigurePrototypeScale(60000f, 15000f, 10000f, 10000f);
        var state = planet.gameObject.AddComponent<PlanetTransitionState>();
        state.Configure(planet, ship.transform);
        var terrain = Create("Treadmill").AddComponent<PlanetTerrainTreadmill>();
        Set(terrain, "visibleTileRadius", 1);
        Set(terrain, "heightmapResolution", 33);
        Set(terrain, "generateMissingRuntimeTiles", true);
        Set(terrain, "logTiming", false);
        var transition = planet.gameObject.AddComponent<PlanetTerrainTransitionController>();
        Set(transition, "logModeChanges", false);
        Set(transition, "logTileLogicalSteps", false);
        Set(transition, "showDebugOverlay", false);
        transition.Configure(state, terrain, terrain.gameObject);
        manager.Configure(ship.transform);
        manager.RecenterNow();
        Assert.AreEqual(7500f, planet.GetAltitude(ship.position), 0.02f);
        transition.ForceEnterSurfaceMode();
        manager.RecenterNow();
        Assert.AreEqual(7500f, transition.GetSurfaceAltitude(ship.position), 0.02f);
        PlanetTileCoord tile = terrain.WorldToTile(ship.position);
        manager.ShiftOrigin(new Vector3(1000f, -4000f, 2000f));
        Assert.AreEqual(tile, terrain.WorldToTile(ship.position));
        Assert.AreEqual(7500f, transition.GetSurfaceAltitude(ship.position), 0.02f);

        ship.position += new Vector3(300f, 0f, -200f);
        ship.transform.position = ship.position;
        terrain.SendMessage("Update");

        transition.ForceExitSurfaceMode();
        manager.RecenterNow();
        Assert.AreEqual(7500f, planet.GetAltitude(ship.position), 0.2f);
        Assert.AreEqual(new Vector3(30f, 1f, 2f).magnitude, ship.linearVelocity.magnitude, 0.001f);
        yield return null;
    }
}
