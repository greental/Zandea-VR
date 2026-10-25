using System;
using System.IO;
using System.Linq;
using JunkerBody;
using JunkerScanner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalDemoBuilder
    {
        public const string ScenePath="Assets/Scenes/JunkerThermalDemo.unity";
        const string Folder="Assets/JunkerEquipment/Thermal/Generated";
        const string Request="Temp/JunkerThermal.build";
        static ThermalDemoBuilder() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Build(); File.WriteAllText("Temp/JunkerThermal.result","PASS: demo and prefabs built; scripts compiled.\n"+ScenePath); }
            catch(Exception e) { File.WriteAllText("Temp/JunkerThermal.result",e.ToString()); Debug.LogException(e); }
        }
        static Transform Child(Transform parent,string name,Vector3 pos=default)
        { var t=new GameObject(name).transform; t.SetParent(parent,false); t.localPosition=pos; return t; }
        static Material Mat(string name,Color color,bool unlit=false)
        {
            string path=Folder+"/"+name+".mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat) { mat=new Material(Shader.Find(unlit?"Universal Render Pipeline/Particles/Unlit":"Universal Render Pipeline/Lit")); mat.color=color; AssetDatabase.CreateAsset(mat,path); }
            return mat;
        }
        static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 size)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=pos; go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=Mat("Metal",new Color(.24f,.29f,.31f)); return go;
        }
        static Rigidbody Body(GameObject go,bool anchored=false)
        { var rb=go.AddComponent<Rigidbody>(); rb.mass=2; rb.isKinematic=anchored; rb.useGravity=!anchored; rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative; rb.interpolation=RigidbodyInterpolation.Interpolate; rb.solverIterations=12; return rb; }
        static XRGrabInteractable Grab(GameObject go)
        { var g=go.AddComponent<XRGrabInteractable>(); g.retainTransformParent=false; g.useDynamicAttach=true; g.movementType=XRBaseInteractable.MovementType.VelocityTracking; return g; }
        static ThermalPathProgress Path(Transform parent,string name,Vector3[] points,bool loop=false)
        {
            var go=Child(parent,name).gameObject; var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=Mat("Guide",Color.white,true); line.startWidth=line.endWidth=.006f; line.startColor=line.endColor=new Color(1,.35f,.02f); line.enabled=false;
            var path=go.AddComponent<AuthoredCutPath>(); path.loop=loop; path.controlPoints=points.Select((p,i)=>Child(go.transform,"Point "+i,p)).ToArray(); path.RefreshPath();
            var progress=go.AddComponent<ThermalPathProgress>(); progress.path=path;
            var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name="Start here"; marker.transform.SetParent(go.transform,false); marker.transform.localPosition=points[0]; marker.transform.localScale=Vector3.one*.015f; UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.GetComponent<Renderer>().sharedMaterial=Mat("Start marker",Color.green);
            return progress;
        }
        static void Scan(GameObject go,ThermalWorkTarget target)
        { var scan=go.AddComponent<ScannableObject>(); scan.displayName=go.name; scan.category="Thermal work"; scan.highlightKind=ScanHighlightKind.Cut; scan.providers=new ScanInfoProvider[]{target}; }
        static void Label(Transform parent,string text,Vector3 pos)
        { var t=Child(parent,"Instructions",pos).gameObject.AddComponent<TextMesh>(); t.text=text; t.characterSize=.025f; t.fontSize=48; t.anchor=TextAnchor.MiddleCenter; t.alignment=TextAlignment.Center; }
        [MenuItem("Zandea/Junker Equipment/Thermal/Build separate demo scene")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build in Edit mode.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new InvalidOperationException("Demo already exists; edit it directly. Builder will not overwrite it.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            if(!AssetDatabase.CopyAsset("Assets/Scenes/JunkerBody.unity",ScenePath)) throw new InvalidOperationException("Could not copy existing rig scene.");
            var previous=SceneManager.GetActiveScene(); var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var body=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<VRBodyController>(true)).First();
                var rig=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<JunkerEquipmentRig>(true)).FirstOrDefault();
                var root=new GameObject("Thermal work demonstration").transform;
                root.SetPositionAndRotation(body.xrOrigin.TransformPoint(new Vector3(2.8f,0,0)),body.xrOrigin.rotation);
                Box(root,"Work area floor",new Vector3(0,-.08f,1),new Vector3(3,.12f,3));
                var board=Child(root,"Cuttable metal board",new Vector3(-.65f,1.2f,1.1f));
                // Four bars form a real collidable hole; no full-board collider behind the plug.
                Box(board,"Left frame",new Vector3(-.4f,0,0),new Vector3(.2f,.9f,.07f));
                Box(board,"Right frame",new Vector3(.4f,0,0),new Vector3(.2f,.9f,.07f));
                Box(board,"Upper frame",new Vector3(0,.375f,0),new Vector3(.6f,.15f,.07f));
                Box(board,"Lower frame",new Vector3(0,-.375f,0),new Vector3(.6f,.15f,.07f));
                Box(root,"Board post",new Vector3(-1.09f,.4f,1.15f),new Vector3(.08f,.8f,.1f));
                var plug=Box(board,"Removable centre",Vector3.zero,new Vector3(.595f,.595f,.06f));
                var section=board.gameObject.AddComponent<CuttableSection>(); section.cutout=Body(plug,true); section.cutoutGrab=Grab(plug); section.cutoutGrab.enabled=false;
                // A slight forward tilt lets the released plug slide away from the lower frame.
                board.localRotation=Quaternion.Euler(-12,0,0);
                section.paths=new[]{Path(board,"Rectangle cut",new[]{new Vector3(-.295f,.295f,-.037f),new Vector3(.295f,.295f,-.037f),new Vector3(.295f,-.295f,-.037f),new Vector3(-.295f,-.295f,-.037f)},true)};
                Scan(board.gameObject,section);
                PrefabUtility.SaveAsPrefabAsset(board.gameObject,Folder+"/CuttableBoard.prefab");
                var pipes=Child(root,"Reversible pipe weld",new Vector3(.65f,1.1f,1));
                var a=Pipe(pipes,"Anchored pipe A",new Vector3(-.25f,0,0)); var b=Pipe(pipes,"Loose pipe B",new Vector3(.3f,0,-.02f));
                var connection=a.AddComponent<WeldConnection>(); connection.bodyA=Body(a); connection.bodyB=Body(b);
                connection.grabA=Grab(a); connection.grabB=Grab(b); connection.startWelded=true;
                connection.anchorA=Child(a.transform,"Joint frame A",new Vector3(.25f,0,0)); connection.anchorB=Child(b.transform,"Joint frame B",new Vector3(-.25f,0,0));
                b.AddComponent<ThermalTargetLink>().target=connection;
                // Front half arc, reachable without moving behind the fixture.
                var points=Enumerable.Range(0,9).Select(i=>{ float t=Mathf.Lerp(-65,65,i/8f)*Mathf.Deg2Rad; return new Vector3(.249f,.085f*Mathf.Sin(t),-.085f*Mathf.Cos(t)); }).ToArray();
                connection.paths=new[]{Path(a.transform,"Accessible weld seam",points)}; connection.paths[0].tolerance=.025f;
                Scan(a,connection); Scan(b,connection);
                Box(root,"Separate pipe fixture",new Vector3(.4f,.5f,1),new Vector3(.2f,1,.18f));
                // Catch shelf is below the seam: severed pipe visibly drops onto it.
                Box(root,"Pipe catch shelf",new Vector3(.95f,.82f,1),new Vector3(.75f,.06f,.5f));
                PrefabUtility.SaveAsPrefabAsset(pipes.gameObject,Folder+"/WeldablePipes.prefab");
                Box(root,"Tool table",new Vector3(0,.72f,.15f),new Vector3(.75f,.08f,.55f));
                var tool=BuildTool(root,rig); tool.transform.localPosition=new Vector3(0,.9f,.15f);
                ThermalDemoUpgrade.ConfigureTool(tool); ThermalDemoUpgrade.ConfigureRig(rig);
                PrefabUtility.SaveAsPrefabAsset(tool,Folder+"/ScrapCutter.prefab");
                Label(root,"THERMAL WORK\nGrip: pick up PLS-T | Holding-hand stick CLICK: CUT / WELD\nTrigger: beam (release after mode switch)\nStart at green dot; trace orange path at close range",new Vector3(0,1.95f,1.15f));
                Label(root,"CUT seam to release pipe B\nAlign and WELD seam to rejoin",new Vector3(.65f,1.48f,1));
                EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
        }
        static GameObject Pipe(Transform parent,string name,Vector3 position)
        {
            var root=Child(parent,name,position).gameObject;
            var cylinder=GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylinder.name="Authored pipe geometry"; cylinder.transform.SetParent(root.transform,false); cylinder.transform.localRotation=Quaternion.Euler(0,0,90); cylinder.transform.localScale=new Vector3(.16f,.25f,.16f);
            UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            var collider=cylinder.AddComponent<MeshCollider>(); collider.sharedMesh=cylinder.GetComponent<MeshFilter>().sharedMesh; collider.convex=true;
            cylinder.GetComponent<Renderer>().sharedMaterial=Mat("Pipe",new Color(.35f,.22f,.1f)); return root;
        }
        static GameObject BuildTool(Transform parent,JunkerEquipmentRig rig)
        {
            var go=Child(parent,"PLS-T ScrapCutter").gameObject;
            var visual=Child(go.transform,"PLS-T real mesh");
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/mesh/tools/ScrapCutter/PLS-T.glb");
            if(!asset) throw new InvalidOperationException("PLS-T mesh import missing.");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,visual);
            // glTF import mirrors X; the original -X nozzle becomes Unity +X.
            visual.localRotation=Quaternion.Euler(0,-90,0);
            var renderers=model.GetComponentsInChildren<Renderer>(); var bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            visual.localScale=Vector3.one*(.42f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z));
            bounds=renderers[0].bounds; foreach(var r in renderers) bounds.Encapsulate(r.bounds);
            visual.position+=go.transform.TransformPoint(new Vector3(0,.065f,.10f))-bounds.center;
            var box=go.AddComponent<BoxCollider>(); box.center=new Vector3(0,.065f,.1f); box.size=new Vector3(.085f,.22f,.42f);
            Body(go); Grab(go); var item=go.AddComponent<JunkerEquipmentItem>(); item.itemId="PLS-T"; item.rig=rig; item.gripPose=Child(go.transform,"Grip",new Vector3(0,0,-.03f));
            var tool=go.AddComponent<ThermalToolController>(); tool.nozzle=Child(go.transform,"Nozzle +Z",new Vector3(0,.107f,.312f)); item.muzzle=tool.nozzle;
            var fx=go.AddComponent<ThermalFXController>(); tool.effects=fx;
            fx.beam=Child(tool.nozzle,"Thermal beam").gameObject.AddComponent<LineRenderer>(); fx.beam.sharedMaterial=Mat("Beam",Color.white,true); fx.beam.startWidth=.006f; fx.beam.endWidth=.002f; fx.beam.enabled=false;
            fx.loopSource=go.AddComponent<AudioSource>(); fx.oneShotSource=go.AddComponent<AudioSource>(); fx.contactSource=go.AddComponent<AudioSource>();
            foreach(var source in go.GetComponents<AudioSource>()) { source.playOnAwake=false; source.spatialBlend=1; }
            fx.cutNozzle=Particles(tool.nozzle,"Cut plasma",fx.cutColor); fx.weldNozzle=Particles(tool.nozzle,"Weld arc",fx.weldColor);
            fx.cutContact=Particles(go.transform,"Cut sparks",fx.cutColor); fx.weldContact=Particles(go.transform,"Weld sparks",fx.weldColor);
            return go;
        }
        static ParticleSystem Particles(Transform parent,string name,Color color)
        {
            var ps=Child(parent,name).gameObject.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.playOnAwake=false; main.startLifetime=.08f; main.startSpeed=.25f; main.startSize=.012f; main.startColor=color; main.maxParticles=32; main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var emission=ps.emission; emission.rateOverTime=40; var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.radius=.003f; shape.angle=12;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=Mat("Particles",Color.white,true); return ps;
        }
    }
}
