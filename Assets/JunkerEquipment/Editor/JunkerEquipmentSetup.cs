using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace JunkerEquipment.Editor
{
    public static class JunkerEquipmentSetup
    {
        public const string Folder="Assets/JunkerEquipment";
        public const string ScenePath="Assets/Scenes/JunkerBody.unity";
        static T Add<T>(GameObject go) where T:Component { var value=go.GetComponent<T>(); return value?value:go.AddComponent<T>(); }
        public static Transform Child(Transform parent,string name,Vector3 position=default)
        { var t=new GameObject(name).transform; t.SetParent(parent,false); t.localPosition=position; return t; }
        static void SaveAsset(UnityEngine.Object obj,string path)
        { var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path); if(old) { EditorUtility.CopySerialized(obj,old); UnityEngine.Object.DestroyImmediate(obj); AssetDatabase.SaveAssetIfDirty(old); } else AssetDatabase.CreateAsset(obj,path); }
        public static Scene Scene()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || SceneManager.sceneCount!=1 || scene.path!=ScenePath) throw new InvalidOperationException("Open only JunkerBody in Edit Mode.");
            return scene;
        }
        public static Bounds BoundsOf(GameObject go)
        { var rs=go.GetComponentsInChildren<Renderer>(); var b=rs[0].bounds; foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b; }
        static GameObject Primitive(PrimitiveType kind,Transform parent,string name,Vector3 pos,Vector3 scale,Color color,bool collider=true)
        {
            var go=GameObject.CreatePrimitive(kind); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=pos; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=Material(name,color);
            if(!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static Material Material(string name,Color color)
        {
            string path=Folder+"/Generated/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path); if(mat) return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color=color; AssetDatabase.CreateAsset(mat,path); return mat;
        }
        public static TextMesh Label(Transform parent,string name,string text,Vector3 pos,float size=.018f)
        { var label=Child(parent,name,pos).gameObject.AddComponent<TextMesh>(); label.text=text; label.characterSize=size; label.fontSize=48; label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; return label; }
        static GameObject Model(Transform parent,string path,float length,Vector3 euler,Vector3 center)
        {
            var wrapper=Child(parent,"Visual"); wrapper.localRotation=Quaternion.Euler(euler);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),wrapper);
            model.transform.localPosition=Vector3.zero;
            var bounds=BoundsOf(model); wrapper.localScale*=length/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            bounds=BoundsOf(model); wrapper.position+=parent.TransformPoint(center)-bounds.center;
            return wrapper.gameObject;
        }
        static JunkerEquipmentItem Item(GameObject go,string id,JunkerEquipmentRig rig,Vector3 grip,Vector3 rotation)
        {
            var rb=Add<Rigidbody>(go); rb.mass=id=="PKO"?.8f:.4f; rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            var grab=Add<XRGrabInteractable>(go); grab.useDynamicAttach=false; grab.retainTransformParent=false; grab.attachEaseInTime=.08f;
            grab.selectMode=InteractableSelectMode.Single; grab.movementType=XRBaseInteractable.MovementType.Kinematic;
            var item=Add<JunkerEquipmentItem>(go); item.itemId=id; item.rig=rig;
            item.gripPose=Child(go.transform,"Grip",grip); item.gripPose.localRotation=Quaternion.Euler(rotation); grab.attachTransform=item.gripPose;
            return item;
        }
        static JunkerEquipmentSlot Slot(JunkerEquipmentRig rig,string name,string id,Vector3 offset,Vector3 euler=default)
        { var slot=Child(rig.transform,name,offset).gameObject.AddComponent<JunkerEquipmentSlot>(); slot.acceptedItemId=id; slot.transform.localRotation=Quaternion.Euler(euler); return slot; }
        static void Place(JunkerEquipmentItem item,JunkerEquipmentSlot slot)
        { slot.startingItem=item; item.transform.SetPositionAndRotation(slot.transform.position,slot.transform.rotation); }

        [MenuItem("Tools/Junker Equipment/Configure gloves and equipment")]
        public static void Configure()
        {
            var scene=Scene();
            if(scene.GetRootGameObjects().Any(g=>g.name=="Junker Equipment")) throw new InvalidOperationException("Equipment is already configured; tune existing scene objects instead of duplicating them.");
            File.Copy(ScenePath,"Temp/JunkerBody.before-equipment.unity",true);
            if(!AssetDatabase.IsValidFolder(Folder+"/Generated")) AssetDatabase.CreateFolder(Folder,"Generated");
            var body=UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
            EnableHolsterQueries(body);
            var root=new GameObject("Junker Equipment").transform;
            var rig=Child(root,"PlayerEquipmentRig").gameObject.AddComponent<JunkerEquipmentRig>(); rig.body=body; rig.playerColliders=body.xrOrigin.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).ToArray();
            rig.transform.SetPositionAndRotation(body.xrOrigin.position,body.xrOrigin.rotation);
            var pistolSlot=Slot(rig,"Right hip — PKO","PKO",new Vector3(.24f,.93f,0),new Vector3(0,0,-90));
            var manualSlot=Slot(rig,"Left hip — screwdriver","screwdriver",new Vector3(-.25f,.93f,0),new Vector3(90,0,0));
            var binoSlot=Slot(rig,"Chest — binoculars","binoculars",new Vector3(0,1.29f,.23f));
            var primary=Slot(rig,"Right shoulder — future primary","primary",new Vector3(.25f,1.43f,-.23f));
            var backpackSlot=Slot(rig,"Left shoulder — backpack","backpack",new Vector3(-.18f,1.35f,-.23f));
            rig.slots=new[]{pistolSlot,manualSlot,binoSlot,primary,backpackSlot};
            var pistolGo=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<XRGrabInteractable>(true)).FirstOrDefault(g=>g.name=="PKO")?.gameObject;
            if(!pistolGo) pistolGo=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/PKO.prefab"),root);
            // Preserve the working prefab's geometry/colliders; attach points account for its +X barrel.
            var pistol=Item(pistolGo,"PKO",rig,new Vector3(-.028f,.012f,0),new Vector3(0,90,0));
            var gun=Add<JunkerPistol>(pistolGo); gun.muzzle=Child(pistolGo.transform,"Muzzle",new Vector3(.198f,.081f,-.005f)); gun.muzzle.localRotation=Quaternion.Euler(0,90,0); pistol.muzzle=gun.muzzle;
            gun.recoilVisual=pistolGo.transform.GetChild(0);
            gun.flash=Primitive(PrimitiveType.Sphere,gun.muzzle,"Muzzle flash",Vector3.zero,new Vector3(.015f,.015f,.04f),new Color(1,.7f,.1f),false); gun.flash.SetActive(false); Place(pistol,pistolSlot);
            var manualGo=Child(root,"Manual screwdriver").gameObject;
            Model(manualGo.transform,"Assets/MyAssets/mesh/tools/ScrewDriver/screwdriver__.rawscan..glb",.25f,new Vector3(0,-90,0),new Vector3(0,0,.06f));
            var mc=manualGo.AddComponent<CapsuleCollider>(); mc.direction=2; mc.radius=.016f; mc.height=.25f; mc.center=new Vector3(0,0,.06f);
            var manual=Item(manualGo,"screwdriver",rig,Vector3.zero,Vector3.zero); var driver=manualGo.AddComponent<JunkerScrewdriver>(); driver.tip=Child(manualGo.transform,"Tip",new Vector3(0,0,.185f)); Place(manual,manualSlot);
            var poweredGo=Child(root,"Powered screwdriver").gameObject;
            Model(poweredGo.transform,"Assets/MyAssets/mesh/tools/AutoScrewDriver/rusty drill 3d model.glb",.27f,new Vector3(0,-90,0),new Vector3(0,.045f,.025f));
            var pc=poweredGo.AddComponent<BoxCollider>(); pc.size=new Vector3(.075f,.25f,.27f); pc.center=new Vector3(0,.045f,.025f);
            var powered=Item(poweredGo,"powered-screwdriver",rig,Vector3.zero,Vector3.zero); var motor=poweredGo.AddComponent<JunkerScrewdriver>(); motor.powered=true; motor.tip=Child(poweredGo.transform,"Tip",new Vector3(0,.125f,.177f));
            motor.spinningBit=Primitive(PrimitiveType.Cube,poweredGo.transform,"Powered bit",new Vector3(0,.125f,.157f),new Vector3(.007f,.007f,.04f),Color.gray,false).transform;
            poweredGo.transform.position=body.xrOrigin.TransformPoint(new Vector3(-.6f,1.04f,1.2f));
            var binoGo=Child(root,"Binoculars").gameObject;
            Model(binoGo.transform,"Assets/MyAssets/mesh/tools/Binoculars/Binoculars.glb",.19f,Vector3.zero,new Vector3(0,0,.025f));
            var bc=binoGo.AddComponent<BoxCollider>(); bc.size=new Vector3(.18f,.08f,.19f); bc.center=new Vector3(0,0,.025f);
            var bino=Item(binoGo,"binoculars",rig,new Vector3(.055f,-.025f,-.02f),Vector3.zero); var optics=binoGo.AddComponent<JunkerBinoculars>(); optics.hmd=body.hmd.GetComponent<Camera>(); optics.eyepiece=Child(binoGo.transform,"Eyepiece",new Vector3(0,0,-.075f)); optics.viewingDirection=Child(binoGo.transform,"Viewing direction",new Vector3(0,0,.13f));
            optics.readout=Label(binoGo.transform,"Range readout","— m",new Vector3(0,.045f,.04f),.006f); optics.reticle=Label(binoGo.transform,"Reticle","+",new Vector3(0,.015f,.08f),.008f).gameObject; Place(bino,binoSlot);
            var bagGo=Child(root,"Backpack (storage extension)").gameObject;
            Model(bagGo.transform,"Assets/MyAssets/mesh/Armor/backpack/military_tactical_backpack.glb",.42f,Vector3.zero,new Vector3(0,-.12f,0));
            var bagBox=bagGo.AddComponent<BoxCollider>(); bagBox.size=new Vector3(.3f,.42f,.16f); bagBox.center=new Vector3(0,-.12f,0);
            Place(Item(bagGo,"backpack",rig,Vector3.zero,Vector3.zero),backpackSlot);
            BuildPanel(root,body,driver,motor);
            var target=Primitive(PrimitiveType.Cube,root,"Pistol test target",body.xrOrigin.TransformPoint(new Vector3(0,1.4f,4)),new Vector3(.6f,.6f,.08f),new Color(.4f,.1f,.06f)); var health=target.AddComponent<JunkerTarget>(); health.label=Label(target.transform,"Hits","HITS 0\n100 HP",new Vector3(0,0,-.52f),.08f);
            BuildGlove(root,body,true); BuildGlove(root,body,false); HideOriginalHands(body);
            Label(root,"Controls","GRIP: grab / release near holster\nTRIGGER: pistol / powered driver\nManual driver: turn wrist at screw",body.xrOrigin.TransformPoint(new Vector3(0,1.75f,2)),.022f);
            foreach(var component in pistolGo.GetComponents<Component>()) { EditorUtility.SetDirty(component); if(PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/JunkerEquipment.setup","Configured and saved JunkerBody: gloves, five slots, four tools, backpack, target, repair panel.");
        }
        [MenuItem("Tools/Junker Equipment/Retry incomplete setup")]
        public static void Retry()
        {
            Scene();
            if(File.Exists("Temp/JunkerEquipment.setup")) throw new InvalidOperationException("Setup already completed.");
            if(!File.ReadAllBytes(ScenePath).SequenceEqual(File.ReadAllBytes("Temp/JunkerBody.before-equipment.unity"))) throw new InvalidOperationException("Saved scene changed since setup began.");
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            Configure();
        }
        static void BuildPanel(Transform root,VRBodyController body,JunkerScrewdriver manual,JunkerScrewdriver powered)
        {
            var stand=Child(root,"Repair bench",body.xrOrigin.TransformPoint(new Vector3(-.6f,0,1.2f))); stand.rotation=body.xrOrigin.rotation;
            Primitive(PrimitiveType.Cube,stand,"Bench",new Vector3(0,.86f,0),new Vector3(.85f,.12f,.5f),new Color(.12f,.15f,.17f));
            var panelRoot=Child(stand,"Repair panel",new Vector3(0,1.2f,.13f)); panelRoot.localRotation=Quaternion.Euler(0,180,0);
            Primitive(PrimitiveType.Cube,panelRoot,"Panel frame",new Vector3(0,0,-.035f),new Vector3(.64f,.45f,.07f),Color.gray);
            var cover=Primitive(PrimitiveType.Cube,panelRoot,"Removable cover",Vector3.zero,new Vector3(.5f,.33f,.018f),new Color(.2f,.28f,.25f));
            var rb=cover.AddComponent<Rigidbody>(); rb.isKinematic=true; rb.useGravity=false;
            var grab=cover.AddComponent<XRGrabInteractable>(); grab.enabled=false; grab.retainTransformParent=false;
            var panel=panelRoot.gameObject.AddComponent<JunkerRepairPanel>(); panel.cover=grab;
            var screws=new List<JunkerScrew>();
            foreach(var p in new[]{new Vector3(-.20f,.12f,.035f),new Vector3(.20f,.12f,.035f),new Vector3(-.20f,-.12f,.035f),new Vector3(.20f,-.12f,.035f)})
            {
                var screw=Child(panelRoot,"Repair screw",p).gameObject.AddComponent<JunkerScrew>();
                screw.visual=Child(screw.transform,"Moving screw");
                Model(screw.visual,"Assets/MyAssets/mesh/tools/Screw/simple_rusty_screw.glb",.045f,new Vector3(0,90,0),new Vector3(0,0,-.018f));
                Primitive(PrimitiveType.Cube,screw.visual,"Screw slot",new Vector3(0,0,.001f),new Vector3(.012f,.002f,.003f),Color.black,false);
                screws.Add(screw);
            }
            panel.screws=screws.ToArray(); manual.screws=panel.screws; powered.screws=panel.screws;
            panel.status=Label(stand,"Panel instructions","Loosen all four screws",new Vector3(0,1.51f,.05f),.015f);
        }
        static void BuildGlove(Transform root,VRBodyController body,bool left)
        {
            string side=left?"Left":"Right";
            var hand=Child(root,side+" Junker glove").gameObject.AddComponent<JunkerGloveHand>(); hand.body=body; hand.left=left;
            hand.rotationOffset=new Vector3(0,0,left?90:-90);
            hand.positionOffset=new Vector3(left?-.02f:.02f,0,-.02f);
            var controller=left?body.leftController:body.rightController;
            hand.input=controller.GetComponentsInChildren<AnimateControllerHand>(true).FirstOrDefault(a=>a.handAnimator);
            if(!hand.input) throw new InvalidOperationException("Missing existing "+side+" grip/trigger input.");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Hands/Junker"+side+"GloveRig.fbx"),hand.transform);
            model.transform.localRotation=Quaternion.AngleAxis(180,Vector3.forward)*Quaternion.Euler(90,0,0);
            var joints=new List<JunkerGloveHand.Joint>();
            foreach(var bone in model.GetComponentsInChildren<Transform>().Where(t=>t.name!="Wrist" && char.IsDigit(t.name.Last())))
            {
                if(!int.TryParse(bone.name.Substring(bone.name.Length-1),out int joint) || joint<1 || joint>3) continue;
                var forward=bone.childCount>0?(bone.GetChild(0).position-bone.position).normalized:bone.up;
                var axis=Vector3.Cross(forward,-hand.transform.up).normalized;
                joints.Add(new JunkerGloveHand.Joint {bone=bone,rest=bone.localRotation,localAxis=bone.InverseTransformDirection(axis),index=bone.name.StartsWith("Index"),curlDegrees=bone.name.StartsWith("Thumb")?new[]{20f,35f,30f}[joint-1]:new[]{68f,80f,55f}[joint-1]});
            }
            hand.joints=joints.ToArray();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { skin.updateWhenOffscreen=true; skin.quality=SkinQuality.Bone4; }
            var arm=left?body.leftArm:body.rightArm;
            hand.transform.SetPositionAndRotation(arm.target.position,controller.rotation);
        }
        static Mesh TrimHands(Mesh source,SkinnedMeshRenderer renderer,VRBodyController body,string name)
        {
            var mesh=UnityEngine.Object.Instantiate(source); mesh.name=name;
            var handBones=new HashSet<int>();
            for(int i=0;i<renderer.bones.Length;i++) if(renderer.bones[i].IsChildOf(body.leftArm.tip) || renderer.bones[i].IsChildOf(body.rightArm.tip) || renderer.bones[i]==body.leftArm.tip || renderer.bones[i]==body.rightArm.tip) handBones.Add(i);
            var all=mesh.GetAllBoneWeights(); var counts=mesh.GetBonesPerVertex(); var hidden=new bool[mesh.vertexCount]; int k=0;
            for(int v=0;v<counts.Length;v++) { float weight=0; for(int j=0;j<counts[v];j++){var w=all[k++];if(handBones.Contains(w.boneIndex))weight+=w.weight;} hidden[v]=weight>.35f; }
            all.Dispose(); counts.Dispose();
            for(int s=0;s<mesh.subMeshCount;s++){var triangles=mesh.GetTriangles(s);var keep=new List<int>();for(int i=0;i<triangles.Length;i+=3) if(!hidden[triangles[i]]&&!hidden[triangles[i+1]]&&!hidden[triangles[i+2]]){keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);}mesh.SetTriangles(keep,s,false);}
            string path=Folder+"/Generated/"+name+".asset"; SaveAsset(mesh,path); return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        static void HideOriginalHands(VRBodyController body)
        {
            var visibility=body.GetComponentInChildren<JunkerLocalHead>();
            visibility.fullMesh=TrimHands(visibility.fullMesh,visibility.body,body,"JunkerBodyWithGloves");
            visibility.headlessMesh=TrimHands(visibility.headlessMesh,visibility.body,body,"JunkerLocalBodyWithGloves");
            visibility.body.sharedMesh=visibility.fullMesh;
            foreach(var shadow in body.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)) shadow.sharedMesh=visibility.fullMesh;
            EditorUtility.SetDirty(visibility);
        }
        [MenuItem("Tools/Junker Equipment/Refine glove and tool visuals")]
        public static void RefineVisuals()
        {
            var scene=Scene();
            EnableHolsterQueries(UnityEngine.Object.FindFirstObjectByType<VRBodyController>());
            var mat=Material("Junker glove leather",Color.white);
            mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Hands/GloveAlbedo.png");
            mat.SetFloat("_Smoothness",.22f);EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
            foreach(var hand in UnityEngine.Object.FindObjectsByType<JunkerGloveHand>(FindObjectsSortMode.None))
            {
                var old=hand.transform.GetChild(0).gameObject;
                UnityEngine.Object.DestroyImmediate(old);
                string side=hand.left?"Left":"Right";
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Hands/Junker"+side+"GloveRig.fbx"),hand.transform);
                model.transform.localRotation=Quaternion.AngleAxis(180,Vector3.forward)*Quaternion.Euler(90,0,0);
                var bindings=new List<JunkerGloveHand.Joint>();
                foreach(var bone in model.GetComponentsInChildren<Transform>())
                {
                    if(!int.TryParse(bone.name.Substring(bone.name.Length-1),out int joint) || joint<1 || joint>3)continue;
                    var direction=bone.childCount>0?(bone.GetChild(0).position-bone.position).normalized:bone.up;
                    var axis=Vector3.Cross(direction,-hand.transform.up).normalized;
                    bindings.Add(new JunkerGloveHand.Joint{bone=bone,rest=bone.localRotation,localAxis=bone.InverseTransformDirection(axis),index=bone.name.StartsWith("Index"),curlDegrees=bone.name.StartsWith("Thumb")?new[]{20f,35f,30f}[joint-1]:new[]{68f,80f,55f}[joint-1]});
                }
                hand.joints=bindings.ToArray();
                hand.rotationOffset=new Vector3(0,0,hand.left?90:-90);
                hand.positionOffset=new Vector3(hand.left?-.02f:.02f,0,-.02f);
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { skin.sharedMaterial=mat;skin.updateWhenOffscreen=true;skin.quality=SkinQuality.Bone4; PrefabUtility.RecordPrefabInstancePropertyModifications(skin); }
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);EditorUtility.SetDirty(hand);
            }
            foreach(var tool in UnityEngine.Object.FindObjectsByType<JunkerScrewdriver>(FindObjectsSortMode.None))
            {
                var vertices=tool.transform.Find("Visual").GetComponentsInChildren<MeshFilter>().SelectMany(m=>m.sharedMesh.vertices.Select(v=>tool.transform.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
                float z=vertices.Max(v=>v.z);var end=vertices.Where(v=>v.z>z-.004f).ToArray();var point=end.Aggregate(Vector3.zero,(sum,p)=>sum+p)/end.Length;
                tool.tip.localPosition=new Vector3(point.x,point.y,z+.003f);
                if(tool.spinningBit)tool.spinningBit.localPosition=new Vector3(point.x,point.y,z-.008f);
                EditorUtility.SetDirty(tool);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        static void EnableHolsterQueries(VRBodyController body)
        {
            var rig=UnityEngine.Object.FindFirstObjectByType<JunkerEquipmentRig>();
            if(rig) { rig.playerColliders=body.xrOrigin.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).ToArray();EditorUtility.SetDirty(rig); }
            foreach(var direct in body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true))
            {
                direct.physicsTriggerInteraction=QueryTriggerInteraction.Collide;
                EditorUtility.SetDirty(direct);PrefabUtility.RecordPrefabInstancePropertyModifications(direct);
            }
            foreach(var caster in body.xrOrigin.GetComponentsInChildren<SphereInteractionCaster>(true))
            {
                caster.physicsTriggerInteraction=QueryTriggerInteraction.Collide;
                EditorUtility.SetDirty(caster);PrefabUtility.RecordPrefabInstancePropertyModifications(caster);
            }
        }
    }
}
