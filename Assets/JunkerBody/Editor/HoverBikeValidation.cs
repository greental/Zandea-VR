using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Exercises the actual scene interactables and XRI selection lifecycle in Play mode.
[InitializeOnLoad]
public static class HoverBikeValidation
{
    const string Key = "Junker.HoverBikeValidation";
    static HoverBikeController bike;
    static XRDirectInteractor left, right, ignitionHand;
    static int phase;
    static int previousFrame;
    static double next;
    static float simulationDeadline;
    static double timeout;
    static Vector3 leftHandPose, rightHandPose, driveStart, driveForward;
    static readonly List<string> report = new List<string>();

    static HoverBikeValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                phase = 0; previousFrame = -10; report.Clear(); next = EditorApplication.timeSinceStartup + 1;
                simulationDeadline=0;timeout=EditorApplication.timeSinceStartup+150;
                Application.runInBackground = true;
                EditorApplication.update += Tick;
            }
        };
    }

    [MenuItem("Tools/JunkerBody/Test Pyxol2 interaction")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/JunkerBody/Inspect Pyxol2 physics")]
    public static void InspectPhysics()
    {
        var vehicle=UnityEngine.Object.FindFirstObjectByType<HoverBikeController>();
        var lines=new List<string>();
        if(!vehicle)return;
        Physics.SyncTransforms();
        lines.Add($"Root {vehicle.transform.position} rotation={vehicle.transform.eulerAngles} scale={vehicle.transform.lossyScale}; probe={vehicle.hoverProbe.position}");
        var rb=vehicle.body;
        lines.Add($"Body kin={rb.isKinematic} gravity={rb.useGravity} constraints={rb.constraints} velocity={rb.linearVelocity}; ground={vehicle.groundDetected} height={vehicle.measuredGroundHeight}");
        foreach(var c in vehicle.GetComponentsInChildren<Collider>(true))
            lines.Add($"Collider {c.name} enabled={c.enabled} trigger={c.isTrigger} bounds={c.bounds} body={c.attachedRigidbody?.name}");
        var hull=vehicle.GetComponent<BoxCollider>();
        foreach(var c in vehicle.GetComponentsInChildren<Collider>(true))
            if(c!=hull&&!c.isTrigger&&Physics.ComputePenetration(hull,hull.transform.position,hull.transform.rotation,c,c.transform.position,c.transform.rotation,out var direction,out var depth))
                lines.Add($"Hull overlap: {c.name} depth={depth} direction={direction} ignored={Physics.GetIgnoreCollision(hull,c)}");
        foreach(var hit in Physics.RaycastAll(vehicle.transform.position+Vector3.up*20,Vector3.down,100,~0,QueryTriggerInteraction.Ignore))
            lines.Add($"Ground ray: {hit.collider.name} point={hit.point} distance={hit.distance} self={hit.transform.IsChildOf(vehicle.transform)}");
        foreach(var stick in vehicle.GetComponentsInChildren<HoverBikeStick>())
        {
            lines.Add($"Grip {stick.name} position={stick.transform.position} serialized anchor world={stick.wheelVisual.TransformPoint(stick.wheelLocalAnchor)} rotation={stick.transform.eulerAngles}");
            var swap=stick.GetComponent<HandSwitcher_NoGrabInteractable>();
            if(swap&&swap.fakeGrabHandModel)lines.Add($"Fake {swap.fakeGrabHandModel.name}: localPos={swap.fakeGrabHandModel.transform.localPosition} localRot={swap.fakeGrabHandModel.transform.localEulerAngles}");
        }
        Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/HoverBikePhysicsInspection.txt",lines);
    }

    static void Check(bool ok, string message)
    {
        report.Add((ok ? "PASS " : "FAIL ") + message);
        if (!ok) throw new InvalidOperationException(message);
    }

    static XRDirectInteractor Hand(string name, HoverBikeStick stick)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetParent(stick.bikeFrame, false);
        go.transform.position = stick.transform.position;
        var sphere = go.AddComponent<SphereCollider>(); sphere.radius = .025f; sphere.isTrigger = true;
        var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var hand = go.AddComponent<XRDirectInteractor>();
        hand.improveAccuracyWithSphereCollider = false; // Match the project's XR Origin Pos direct hands.
        hand.physicsTriggerInteraction = QueryTriggerInteraction.Ignore;
        go.SetActive(true);
        return hand;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Finish(); return; }
        if(EditorApplication.timeSinceStartup>timeout){report.Add("FAIL Physics validation timed out");Finish();return;}
        if (EditorApplication.timeSinceStartup < next) return;
        if(Time.time<simulationDeadline)return;
        if (Time.frameCount < previousFrame + 3) return;
        previousFrame = Time.frameCount;
        next = EditorApplication.timeSinceStartup + .2;
        try
        {
            switch (phase++)
            {
                case 0:
                    bike = UnityEngine.Object.FindFirstObjectByType<HoverBikeController>();
                    InspectPhysics();
                    report.AddRange(File.ReadAllLines("Logs/HoverBikePhysicsInspection.txt"));
                    Check(bike && bike.leftStick && bike.rightStick && bike.ignition && bike.seat, "Bike references resolve");
                    var hull=bike.GetComponent<BoxCollider>();
                    float floor=float.NegativeInfinity;
                    foreach(var hit in Physics.RaycastAll(hull.bounds.center+Vector3.up*20,Vector3.down,80,~0,QueryTriggerInteraction.Ignore))
                        if(!hit.transform.IsChildOf(bike.transform)&&hit.normal.y>.5f)floor=Mathf.Max(floor,hit.point.y);
                    Check(!float.IsNegativeInfinity(floor),"Actual terrain collider exists below bike");
                    bike.body.position+=Vector3.up*(floor-hull.bounds.min.y+.01f);
                    bike.body.linearVelocity=Vector3.zero;bike.body.angularVelocity=Vector3.zero;
                    bike.targetHeight=1;
                    simulationDeadline=Time.time+5f;
                    break;
                case 1:
                    CheckHover(.7f,1.3f,"Rises from ground to one metre hull clearance");
                    Check(Physics.GetIgnoreCollision(bike.GetComponent<BoxCollider>(),bike.ignition.GetComponent<Collider>()),"Ignition cannot push the hull into the floor");
                    bike.targetHeight=.5f;simulationDeadline=Time.time+4f;
                    break;
                case 2:
                    CheckHover(.35f,.7f,"Maintains minimum hover clearance");
                    bike.targetHeight=2;simulationDeadline=Time.time+4f;
                    break;
                case 3:
                    CheckHover(1.7f,2.3f,"Rises to maximum hover clearance");
                    bike.targetHeight=1;
                    bike.body.rotation=Quaternion.Euler(12,0,-10)*bike.body.rotation;
                    bike.body.angularVelocity=new Vector3(.6f,0,-.6f);
                    simulationDeadline=Time.time+5f;
                    break;
                case 4:
                    CheckHover(.7f,1.3f,"Recovers from pitch and roll disturbance");
                    left = Hand("Validation left hand", bike.leftStick);
                    right = Hand("Validation right hand", bike.rightStick);
                    bike.ignition.SetOff();
                    ignitionHand = Hand("Validation ignition hand", bike.leftStick);
                    ignitionHand.transform.position = bike.ignition.transform.position;
                    leftHandPose=bike.leftStick.GetComponent<HandSwitcher_NoGrabInteractable>().fakeGrabHandModel.transform.localPosition;
                    rightHandPose=bike.rightStick.GetComponent<HandSwitcher_NoGrabInteractable>().fakeGrabHandModel.transform.localPosition;
                    break;
                case 5:
                    var targets = new List<IXRInteractable>();
                    left.GetValidTargets(targets);
                    report.Add($"Left targets={targets.Count}; hand={left.transform.position}; grip={bike.leftStick.transform.position}; collider={bike.leftStick.GetComponent<SphereCollider>().bounds}; trigger={bike.leftStick.GetComponent<SphereCollider>().isTrigger}");
                    report.Add($"Registered colliders={bike.leftStick.GetComponent<XRGrabInteractable>().colliders.Count}");
                    Check(targets.Contains(bike.leftStick.GetComponent<XRGrabInteractable>()), "Left grip detected by direct hand");
                    targets.Clear(); right.GetValidTargets(targets);
                    Check(targets.Contains(bike.rightStick.GetComponent<XRGrabInteractable>()), "Right grip detected by direct hand");
                    targets.Clear(); ignitionHand.GetValidTargets(targets);
                    Check(targets.Contains(bike.ignition.interactable), "Ignition detected by direct hand");
                    ignitionHand.StartManualInteraction((IXRSelectInteractable)bike.ignition.interactable);
                    left.StartManualInteraction((IXRSelectInteractable)bike.leftStick.GetComponent<XRGrabInteractable>());
                    right.StartManualInteraction((IXRSelectInteractable)bike.rightStick.GetComponent<XRGrabInteractable>());
                    break;
                case 6:
                    Check(bike.leftStick.IsGrabbed && bike.rightStick.IsGrabbed, "Both grips selected simultaneously");
                    Check(bike.ignition.isOn, "Grabbing ignition switches power on");
                    ignitionHand.EndManualInteraction();
                    Check(bike.leftStick.transform.parent == bike.transform, "Selected wheel grip stays attached to bike");
                    // Parent synthetic tracking to the bike, like the seated XR Origin.
                    left.transform.SetParent(bike.transform, true); right.transform.SetParent(bike.transform, true);
                    Check(Vector3.Distance(leftHandPose,bike.leftStick.GetComponent<HandSwitcher_NoGrabInteractable>().fakeGrabHandModel.transform.localPosition)<.001f,"Left grab hand retains authored grip position");
                    Check(Vector3.Distance(rightHandPose,bike.rightStick.GetComponent<HandSwitcher_NoGrabInteractable>().fakeGrabHandModel.transform.localPosition)<.001f,"Right grab hand retains authored grip position");
                    left.transform.position -= bike.transform.forward * .1f;
                    right.transform.position += bike.transform.forward * .1f;
                    driveStart=bike.body.position;driveForward=bike.transform.forward;
                    break;
                case 7:
                    Check(bike.leftStick.Throttle > .8f && bike.rightStick.Throttle > .8f, "Push hands forward produces throttle");
                    Check(bike.seat.StartSeated(), "Pilot can enter seat");
                    Check(bike.ignition.isOn, "Ignition enables propulsion");
                    simulationDeadline=Time.time+1.5f;
                    break;
                case 8:
                    Check(Vector3.Dot(bike.body.linearVelocity, bike.transform.forward) < -.5f, "Positive wheel input drives rigidbody backward");
                    Check(Vector3.Dot(bike.body.position-driveStart,driveForward)<-.5f,"Bike actually travels backward above terrain");
                    CheckHover(.3f,1.6f,"Stays level and above ground while driving");
                    left.transform.position += bike.transform.forward * .2f;
                    right.transform.position -= bike.transform.forward * .2f;
                    simulationDeadline=Time.time+3f;
                    break;
                case 9:
                    Check(bike.leftStick.Throttle<-.8f&&bike.rightStick.Throttle<-.8f,"Pulling handles produces negative wheel input");
                    Check(Vector3.Dot(bike.body.linearVelocity,bike.transform.forward)>.5f,"Bike brakes then travels forward");
                    CheckHover(.3f,1.6f,"Reverse travel retains ground clearance and level attitude");
                    right.EndManualInteraction();
                    left.transform.position+=bike.transform.up*.1f;
                    simulationDeadline=Time.time+.7f;
                    break;
                case 10:
                    Check(bike.leftStick.Steering>.8f&&bike.body.angularVelocity.y>.02f,"Left hand wheel input turns bike in reversed direction");
                    left.EndManualInteraction();
                    right.StartManualInteraction((IXRSelectInteractable)bike.rightStick.GetComponent<XRGrabInteractable>());
                    right.transform.position+=bike.transform.up*.1f;
                    simulationDeadline=Time.time+1f;
                    break;
                case 11:
                    Check(bike.rightStick.Steering<-.8f&&bike.body.angularVelocity.y<-.02f,"Right hand wheel input turns bike in reversed direction");
                    right.EndManualInteraction();
                    break;
                case 12:
                    Check(!bike.rightStick.IsGrabbed&&!bike.leftStick.IsGrabbed&&bike.rightStick.Throttle==0&&bike.leftStick.Throttle==0,"Releasing wheel clears drive input");
                    Finish();
                    break;
            }
        }
        catch (Exception e) { report.Add(e.ToString()); Finish(); }
    }

    static void CheckHover(float minimum,float maximum,string description)
    {
        float tilt=Vector3.Angle(bike.body.rotation*Vector3.up,Vector3.up);
        report.Add($"Clearance={bike.measuredGroundHeight:F3}m, target={bike.targetHeight:F2}m, tilt={tilt:F2} degrees, velocity={bike.body.linearVelocity}");
        Check(bike.groundDetected&&bike.measuredGroundHeight>=minimum&&bike.measuredGroundHeight<=maximum&&tilt<4,description);
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/HoverBikeValidation.txt", report);
        Debug.Log(string.Join("\n", report));
        EditorApplication.isPlaying = false;
    }
}
