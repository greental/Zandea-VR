#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class PyxolHoverBikeSetup
{
    static PyxolHoverBikeSetup()
    {
        EditorApplication.delayCall+=()=>
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var root=GameObject.Find("Pyxol2");
            var controller=root?root.GetComponent<HoverBikeController>():null;
            if(root&&(!controller||!controller.drivingSticks||root.GetComponentsInChildren<HoverBikeStick>(true).Any(s=>!s.GetComponent<XRGrabInteractable>()||!s.wheelVisual||s.transform.parent!=root.transform)||root.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="24V Ignition")>1)) Configure();
            controller=root?root.GetComponent<HoverBikeController>():null;
            if(controller&&(!controller.engineLoop||!controller.engineSource))AssignEngineAudio();
            RepairGripColliders();
        };
    }

    [MenuItem("Zandea/JunkerBody/Repair Pyxol2 grab colliders")]
    public static void RepairGripColliders()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        foreach(var stick in Object.FindObjectsByType<HoverBikeStick>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var collider=stick.GetComponent<SphereCollider>();
            if(!collider)continue;
            var scale=stick.transform.lossyScale;
            float radius=stick.grabRadius/Mathf.Max(.0001f,Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            var grab=stick.GetComponent<XRGrabInteractable>();
            if(grab&&!grab.colliders.Contains(collider))
            {
                Undo.RecordObject(grab,"Register hover bike grip collider");
                grab.colliders.Add(collider);EditorUtility.SetDirty(grab);EditorSceneManager.MarkSceneDirty(stick.gameObject.scene);
            }
            if(!collider.isTrigger&&Mathf.Approximately(collider.radius,radius))continue;
            Undo.RecordObject(collider,"Repair hover bike grab detection");
            collider.isTrigger=false;collider.radius=radius;
            EditorUtility.SetDirty(collider);EditorSceneManager.MarkSceneDirty(stick.gameObject.scene);
        }
    }

    [MenuItem("Zandea/JunkerBody/Configure Pyxol2 hover bike")]
    public static void Configure()
    {
        var root=GameObject.Find("Pyxol2");
        if(!root){Debug.LogError("Pyxol2 was not found in the active scene.");return;}
        Undo.RegisterFullObjectHierarchyUndo(root,"Configure Pyxol2 hover bike");

        var rb=GetOrAdd<Rigidbody>(root);rb.mass=320f;rb.useGravity=true;rb.linearDamping=.35f;rb.angularDamping=3.5f;rb.interpolation=RigidbodyInterpolation.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        var bounds=CombinedBounds(root);
        var hull=GetOrAdd<BoxCollider>(root);hull.center=root.transform.InverseTransformPoint(bounds.center);
        Vector3 localSize=root.transform.InverseTransformVector(bounds.size);hull.size=new Vector3(Mathf.Abs(localSize.x)*.82f,Mathf.Abs(localSize.y)*.55f,Mathf.Abs(localSize.z)*.85f);

        foreach(var wrong in root.GetComponentsInChildren<HoverBikeStick>(true).ToArray())
        {
            if(wrong.transform.parent!=root.transform && (wrong.name=="Left Driving Stick Grab"||wrong.name=="Right Driving Stick Grab"))
                Undo.DestroyObjectImmediate(wrong.gameObject);
            else Undo.DestroyObjectImmediate(wrong);
        }
        var driving=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="tmpyapy7qqp.ply");
        if(!driving){Debug.LogError("Pyxol2 connected control tmpyapy7qqp.ply was not found.",root);return;}
        var drivingRenderer=driving.GetComponent<Renderer>();
        Bounds drivingBounds=drivingRenderer.bounds;
        Vector3 controlCenter=drivingBounds.center;
        var leftZone=Child(root.transform,"Left Driving Stick Grab");
        var rightZone=Child(root.transform,"Right Driving Stick Grab");
        var leftPoint=new Vector3(drivingBounds.min.x+drivingBounds.size.x*.18f,controlCenter.y,controlCenter.z);
        var rightPoint=new Vector3(drivingBounds.max.x-drivingBounds.size.x*.18f,controlCenter.y,controlCenter.z);
        leftZone.position=leftPoint;rightZone.position=rightPoint;
        leftZone.rotation=rightZone.rotation=root.transform.rotation;
        var left=Stick(leftZone,root.transform);var right=Stick(rightZone,root.transform);
        left.handle=leftZone;right.handle=rightZone;left.wheelVisual=right.wheelVisual=driving;
        left.wheelLocalAnchor=driving.InverseTransformPoint(leftPoint);right.wheelLocalAnchor=driving.InverseTransformPoint(rightPoint);
        left.applyVisual=false;right.applyVisual=false;
        left.throttleDirection=-1f;left.steeringDirection=1f;
        right.throttleDirection=1f;right.steeringDirection=-1f;

        var anchor=Child(root.transform,"HoverBike Seat Anchor");
        anchor.position=controlCenter-root.transform.forward*.62f+root.transform.up*.32f;
        anchor.rotation=root.transform.rotation;
        var zone=Child(root.transform,"HoverBike Seat Zone");zone.position=anchor.position;zone.rotation=anchor.rotation;
        var zoneCollider=GetOrAdd<BoxCollider>(zone.gameObject);zoneCollider.isTrigger=true;zoneCollider.size=new Vector3(1.1f,1.35f,1.2f);
        var seat=GetOrAdd<SitInCockpit>(zone.gameObject);seat.seatAnchor=anchor;seat.sitDistance=1.2f;seat.lockMovementWhenSeated=true;

        var ignitionObjects=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="24V Ignition").ToArray();
        GameObject ignitionObject;
        if(ignitionObjects.Length>0) ignitionObject=ignitionObjects[0].gameObject;
        else { ignitionObject=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(ignitionObject,"Create hover bike ignition");ignitionObject.name="24V Ignition";ignitionObject.transform.SetParent(root.transform); }
        for(int i=1;i<ignitionObjects.Length;i++) Undo.DestroyObjectImmediate(ignitionObjects[i].gameObject);
        ignitionObject.transform.position=controlCenter+root.transform.right*.22f-root.transform.forward*.12f;ignitionObject.transform.rotation=root.transform.rotation;ignitionObject.transform.localScale=new Vector3(.055f,.09f,.035f);
        var ignitionGrab=GetOrAdd<XRGrabInteractable>(ignitionObject);ignitionGrab.trackPosition=false;ignitionGrab.trackRotation=false;ignitionGrab.throwOnDetach=false;ignitionGrab.retainTransformParent=true;
        var ignitionBody=GetOrAdd<Rigidbody>(ignitionObject);ignitionBody.isKinematic=true;ignitionBody.useGravity=false;
        var oldSimple=ignitionObject.GetComponent<XRSimpleInteractable>();if(oldSimple)Undo.DestroyObjectImmediate(oldSimple);
        var ignition=GetOrAdd<XRFlipSwitch>(ignitionObject);ignition.interactable=ignitionGrab;ignition.handle=ignitionObject.transform;ignition.pivot=ignitionObject.transform;ignition.axis=XRFlipSwitch.RotationAxis.X;ignition.offAngle=-25f;ignition.onAngle=25f;

        var probe=Child(root.transform,"Hover Probe");probe.localPosition=Vector3.zero;
        var controller=GetOrAdd<HoverBikeController>(root);controller.body=rb;controller.leftStick=left;controller.rightStick=right;controller.drivingSticks=driving;controller.ignition=ignition;controller.seat=seat;controller.hoverProbe=probe;controller.targetHeight=1f;controller.minimumHeight=.5f;controller.maximumHeight=2f;controller.hoverSpring=24f;controller.hoverDamping=10f;controller.maxHoverAcceleration=35f;controller.groundProbeLift=1.5f;controller.uprightTorque=18f;controller.uprightDamping=10f;
        AssignEngineAudio();

        foreach(var o in root.GetComponentsInChildren<Component>(true))if(o)EditorUtility.SetDirty(o);
        EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);AssetDatabase.SaveAssets();
        Debug.Log("Pyxol2 configured: dual sticks, 24V ignition, A-button seat, bounded hover and B-button boost.",root);
    }
    [MenuItem("Zandea/JunkerBody/Assign Pyxol2 engine audio")]
    public static void AssignEngineAudio()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var root=GameObject.Find("Pyxol2");
        var bike=root?root.GetComponent<HoverBikeController>():null;
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/Pyxol/PyxolEngine.wav");
        if(!bike||!clip){Debug.LogError("Pyxol2 controller or PyxolEngine.wav was not found.");return;}
        Undo.RecordObject(bike,"Assign Pyxol2 engine audio");
        var audioTransform=root.transform.Find("Pyxol Engine Audio");
        if(!audioTransform)
        {
            var audioObject=new GameObject("Pyxol Engine Audio");
            Undo.RegisterCreatedObjectUndo(audioObject,"Create Pyxol2 engine audio");
            audioTransform=audioObject.transform;
            audioTransform.SetParent(root.transform,false);
        }
        var source=GetOrAdd<AudioSource>(audioTransform.gameObject);
        Undo.RecordObject(source,"Configure Pyxol2 engine audio");
        source.clip=clip;source.loop=true;source.playOnAwake=false;
        source.spatialBlend=1f;source.rolloffMode=AudioRolloffMode.Logarithmic;
        source.minDistance=2f;source.maxDistance=30f;source.volume=0f;
        bike.engineLoop=clip;bike.engineSource=source;
        EditorUtility.SetDirty(source);EditorUtility.SetDirty(bike);
        EditorSceneManager.MarkSceneDirty(root.scene);
    }
    static HoverBikeStick Stick(Transform handle,Transform bike)
    {
        var collider=GetOrAdd<SphereCollider>(handle.gameObject);collider.radius=.06f/Mathf.Max(.0001f,Mathf.Abs(handle.lossyScale.x));collider.isTrigger=false;
        var grab=GetOrAdd<XRGrabInteractable>(handle.gameObject);grab.trackPosition=false;grab.trackRotation=false;grab.throwOnDetach=false;grab.retainTransformParent=true;
        grab.colliders.Clear();grab.colliders.Add(collider);
        var controlBody=GetOrAdd<Rigidbody>(handle.gameObject);controlBody.isKinematic=true;controlBody.useGravity=false;
        var stick=GetOrAdd<HoverBikeStick>(handle.gameObject);stick.handle=handle;stick.bikeFrame=bike;stick.steeringAngle=31f;stick.localYBack=-.004f;stick.localYForward=-.003f;stick.handTravel=.10f;return stick;
    }
    [MenuItem("Zandea/JunkerBody/Apply Pyxol2 hover fixes")]
    public static void ApplyHoverFixes()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        foreach(var bike in Object.FindObjectsByType<HoverBikeController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
        {
            Undo.RecordObject(bike,"Repair Pyxol2 hover");
            bike.groundSearchDistance=40f;
            foreach(var stick in new[]{bike.leftStick,bike.rightStick})
            {
                if(!stick)continue;
                Undo.RecordObject(stick,"Set per-handle control directions");
                stick.throttleDirection=stick==bike.leftStick?-1f:1f;
                stick.steeringDirection=stick==bike.leftStick?1f:-1f;
                var swap=stick.GetComponent<HandSwitcher_NoGrabInteractable>();
                if(swap&&swap.fakeGrabHandModel)
                {
                    Undo.RecordObject(swap,"Use complete grab hand root");
                    for(var t=swap.fakeGrabHandModel.transform;t&&t!=stick.transform;t=t.parent)
                        if(t.GetComponents<MonoBehaviour>().Any(b=>b is IGrabPoseOverride))swap.fakeGrabHandModel=t.gameObject;
                    EditorUtility.SetDirty(swap);
                }
                EditorUtility.SetDirty(stick);
            }
            EditorUtility.SetDirty(bike);EditorSceneManager.MarkSceneDirty(bike.gameObject.scene);
        }
    }
    static Bounds CombinedBounds(GameObject root)
    {
        var rs=root.GetComponentsInChildren<Renderer>(true);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
    }
    static Transform Child(Transform parent,string name){var t=parent.Find(name);if(!t){t=new GameObject(name).transform;t.SetParent(parent,false);}return t;}
    static T GetOrAdd<T>(GameObject go)where T:Component{var c=go.GetComponent<T>();return c?c:Undo.AddComponent<T>(go);}
}
#endif
