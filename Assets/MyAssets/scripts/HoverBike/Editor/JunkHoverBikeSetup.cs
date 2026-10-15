#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class JunkHoverBikeSetup
{
    const string Folder="Assets/MyAssets/mesh/HoverBikes/JunkHoverBike/Rigged";
    const string PrefabPath="Assets/MyAssets/prefabs/ground vehichles/Junk Hover Bike.prefab";
    static string WorkFolder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/JunkHoverBikeWork"));
    static string Request=>Path.Combine(WorkFolder,"build.request");
    static JunkHoverBikeSetup()
    {
        if(File.Exists(Request))
        {
            EditorApplication.delayCall+=RequestedBuild;
            EditorApplication.update+=RequestedBuild;
        }
    }
    static void RequestedBuild()
    {
        if(!File.Exists(Request))return;
        EditorApplication.update-=RequestedBuild;
        File.Delete(Request);
        try{Build();}catch(Exception e){File.WriteAllText(Path.Combine(WorkFolder,"build-result.txt"),e.ToString());Debug.LogException(e);}
    }

    [MenuItem("Tools/Hover Bikes/Build Junk Hover Bike")]
    public static void Build()
    {
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/JunkHoverBikeRigged.fbx");
        var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/ground vehichles/Pyxol2.prefab");
        if(!model||!template)throw new InvalidOperationException("Rigged model or Pyxol template has not imported.");
        // Build in an isolated preview scene, so active gameplay and user edits
        // are unaffected and no vehicle lifecycle runs during construction.
        var preview=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Junk Hover Bike");
        SceneManager.MoveGameObjectToScene(root,preview);
        try
        {
            var artwork=UnityEngine.Object.Instantiate(model,root.transform);artwork.name="Artwork";
            var front=artwork.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Junk Front Body");
            var rear=artwork.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Junk Rear Body");
            var bar=artwork.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Motorcycle Handlebar");
            if(front.bounds.center.z<rear.bounds.center.z)artwork.transform.localRotation=Quaternion.Euler(0,180,0);
            Bounds bounds=BoundsOf(artwork);
            artwork.transform.localScale*=2.75f/bounds.size.z;
            bounds=BoundsOf(artwork);
            artwork.transform.localPosition+=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z);
            var frontMat=Finish("Junk Front Finish","JunkFront.png");
            var rearMat=Finish("Junk Rear Finish","JunkRear.png");
            var metal=Finish("Handlebar Aged Metal",null);
            front.sharedMaterial=frontMat;rear.sharedMaterial=rearMat;
            bar.sharedMaterials=bar.sharedMaterials.Select(m=>m&&m.name.Contains("Aged")?metal:frontMat).ToArray();
            // Flatten imported transforms without changing the authored geometry.
            foreach(var renderer in artwork.GetComponentsInChildren<MeshRenderer>())renderer.transform.SetParent(root.transform,true);
            UnityEngine.Object.DestroyImmediate(artwork);
            var steering=Child(root.transform,"Handlebar Steering Pivot",bar.transform.position);
            bar.transform.SetParent(steering,true);
            var body=root.AddComponent<Rigidbody>();body.mass=280;body.useGravity=true;body.linearDamping=.35f;body.angularDamping=3.5f;
            body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            bounds=BoundsOf(root);
            var hull=root.AddComponent<BoxCollider>();hull.center=new Vector3(0,bounds.size.y*.32f,0);hull.size=new Vector3(bounds.size.x*.68f,bounds.size.y*.64f,bounds.size.z*.94f);
            var bike=root.AddComponent<HoverBikeController>();bike.body=body;bike.drivingSticks=steering;bike.motorcycleHandlebar=true;bike.handlebarSteeringAngle=28f;
            bike.hoverProbe=Child(root.transform,"Hover Probe",Vector3.zero);bike.targetHeight=.8f;
            root.AddComponent<HoverBikeCosmeticLean>();
            var source=template.GetComponent<HoverBikeController>();
            Bounds grips=bar.bounds;
            bike.leftStick=Grip(source.leftStick,root.transform,steering,new Vector3(grips.min.x+grips.size.x*.09f,grips.max.y-.08f,grips.center.z),false);
            bike.rightStick=Grip(source.rightStick,root.transform,steering,new Vector3(grips.max.x-grips.size.x*.09f,grips.max.y-.08f,grips.center.z),true);
            var anchor=Child(root.transform,"HoverBike Seat Anchor",new Vector3(0,grips.center.y+.48f,steering.localPosition.z-.55f));
            var zone=Child(root.transform,"HoverBike Seat Zone",anchor.position);
            var zoneCollider=zone.gameObject.AddComponent<BoxCollider>();zoneCollider.isTrigger=true;zoneCollider.size=new Vector3(1f,1.4f,1.1f);
            bike.seat=zone.gameObject.AddComponent<SitInCockpit>();bike.seat.seatAnchor=anchor;bike.seat.sitDistance=1.2f;bike.seat.lockMovementWhenSeated=true;
            // Keep scene-specific rig references out of the reusable prefab.
            bike.seat.xrOrigin=null;bike.seat.mainCamera=null;bike.seat.locomotionMediator=null;
            var ignition=UnityEngine.Object.Instantiate(source.ignition.gameObject,root.transform);
            ignition.name="24V Ignition";ignition.transform.localPosition=new Vector3(.13f,steering.localPosition.y-.015f,steering.localPosition.z+.13f);
            ignition.transform.localRotation=Quaternion.identity;ignition.transform.localScale=new Vector3(.055f,.09f,.035f);
            bike.ignition=ignition.GetComponent<XRFlipSwitch>();bike.ignition.isOn=false;
            bike.engineLoop=source.engineLoop;
            var audio=Child(root.transform,"Junk Bike Engine Audio",Vector3.zero).gameObject.AddComponent<AudioSource>();
            audio.clip=bike.engineLoop;audio.playOnAwake=false;audio.loop=true;audio.spatialBlend=1;audio.minDistance=2;audio.maxDistance=30;audio.volume=0;bike.engineSource=audio;
            Validate(bike);
            JunkHoverBikeValidation.CheckControls(bike);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            if(!prefab)throw new Exception("Prefab save failed.");
            SavePreview(root,preview);
            AssetDatabase.SaveAssets();
            string report="PASS: Junk Hover Bike prefab created.\n";
            report+=$"Bounds {bounds}; steering pivot {steering.localPosition}; left grip {bike.leftStick.transform.localPosition}; right grip {bike.rightStick.transform.localPosition}\n";
            report+="Three textured meshes; separate pivot; two XRI grips; right wrist throttle; ignition; seat; hover; audio; cosmetic lean.\nPASS: left/right steering, twist throttle, yaw/throttle isolation, grip attachment, stable steering pivot over repeated updates.\n";
            // Add one instance beside the existing bike; do not duplicate it on rebuild.
            var existing=UnityEngine.Object.FindObjectsByType<HoverBikeController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                .FirstOrDefault(b=>b!=bike&&b.name=="Junk Hover Bike");
            if(!existing && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var neighbour=UnityEngine.Object.FindObjectsByType<HoverBikeController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                    .FirstOrDefault(b=>b!=bike&&b.gameObject.scene.IsValid());
                if(neighbour)
                {
                    var scene=neighbour.gameObject.scene;bool wasDirty=scene.isDirty;
                    var placed=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                    Undo.RegisterCreatedObjectUndo(placed,"Add Junk Hover Bike");
                    placed.transform.SetPositionAndRotation(neighbour.transform.position+neighbour.transform.right*3f,neighbour.transform.rotation);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if(!wasDirty){EditorSceneManager.SaveScene(scene);report+="Placed beside "+neighbour.name+" and saved "+scene.path+".\n";}
                    else report+="Placed beside "+neighbour.name+"; scene already had unsaved edits, so placement is unsaved.\n";
                    Selection.activeGameObject=placed;
                }
            }
            Directory.CreateDirectory(WorkFolder);
            File.WriteAllText(Path.Combine(WorkFolder,"build-result.txt"),report);Debug.Log(report);
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }
    static HoverBikeStick Grip(HoverBikeStick source,Transform root,Transform steering,Vector3 position,bool right)
    {
        var go=UnityEngine.Object.Instantiate(source.gameObject,root);go.name=right?"Right Handlebar Grip":"Left Handlebar Grip";
        go.transform.SetPositionAndRotation(position,Quaternion.identity);go.transform.localScale=Vector3.one;
        var grip=go.GetComponent<HoverBikeStick>();grip.handle=go.transform;grip.bikeFrame=root;grip.wheelVisual=steering;
        grip.wheelLocalAnchor=steering.InverseTransformPoint(position);grip.applyVisual=false;grip.motorcycleGrip=true;grip.twistThrottle=right;
        grip.steeringAngle=28;grip.steeringDirection=1;grip.throttleDirection=1;grip.grabRadius=.07f;
        var sphere=go.GetComponent<SphereCollider>();sphere.radius=.07f;sphere.isTrigger=false;
        var grab=go.GetComponent<XRGrabInteractable>();grab.colliders.Clear();grab.colliders.Add(sphere);
        return grip;
    }
    static Transform Child(Transform root,string name,Vector3 position)
    {var t=new GameObject(name).transform;t.SetParent(root,false);t.position=position;return t;}
    static Bounds BoundsOf(GameObject root)
    {var rs=root.GetComponentsInChildren<MeshRenderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
    static Material Finish(string name,string image)
    {
        string path=Folder+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
        material.name=name;material.color=image!=null?Color.white:new Color(.15f,.10f,.06f);
        material.mainTexture=image!=null?AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+image):null;
        material.SetFloat("_Metallic",image!=null?.25f:.75f);material.SetFloat("_Smoothness",.28f);EditorUtility.SetDirty(material);return material;
    }
    static void SavePreview(GameObject root,Scene scene)
    {
        var cameraObject=new GameObject("Preview Camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var camera=cameraObject.AddComponent<Camera>();camera.cameraType=CameraType.Preview;camera.scene=scene;
        camera.transform.position=new Vector3(3f,2.3f,4f);camera.transform.LookAt(new Vector3(0,.5f,0));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.17f);
        camera.fieldOfView=35;camera.nearClipPlane=.05f;
        var lightObject=new GameObject("Preview Light");SceneManager.MoveGameObjectToScene(lightObject,scene);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var target=new RenderTexture(1200,900,24);var prior=RenderTexture.active;
        var image=new Texture2D(1200,900,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
            Directory.CreateDirectory(WorkFolder);File.WriteAllBytes(Path.Combine(WorkFolder,"unity-preview.png"),image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }
    public static void Validate(HoverBikeController bike)
    {
        if(!bike.body||!bike.seat||!bike.ignition||!bike.engineLoop||!bike.drivingSticks||!bike.motorcycleHandlebar)throw new Exception("Missing bike dependency.");
        if(bike.leftStick.transform.localPosition.x>=bike.rightStick.transform.localPosition.x)throw new Exception("Grip sides reversed.");
        foreach(var grip in new[]{bike.leftStick,bike.rightStick})
            if(!grip.motorcycleGrip||grip.wheelVisual!=bike.drivingSticks||grip.bikeFrame!=bike.transform||!grip.GetComponent<XRGrabInteractable>())throw new Exception("Grip wiring invalid.");
        if(bike.drivingSticks.GetComponentsInChildren<MeshRenderer>().Length!=1)throw new Exception("Handlebar must be separate from body.");
        if(bike.drivingSticks.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Physics hull must stay outside cosmetic steering.");
    }
}
#endif
