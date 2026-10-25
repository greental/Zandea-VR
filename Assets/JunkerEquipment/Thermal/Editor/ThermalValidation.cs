using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using JunkerScanner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalValidation
    {
        const string Flag="JunkerThermal.Validation";
        static ThermalValidation() { EditorApplication.update+=Poll; EditorApplication.playModeStateChanged+=Changed; }
        static void Poll()
        {
            if(File.Exists("Temp/JunkerThermal.stop"))
            {
                File.Delete("Temp/JunkerThermal.stop"); SessionState.SetBool(Flag,false); EditorApplication.isPlaying=false; return;
            }
            if(!File.Exists("Temp/JunkerThermal.validate") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/JunkerThermal.validate");
            try
            {
                if(File.Exists("Temp/JunkerThermal.colliders")) { File.Delete("Temp/JunkerThermal.colliders"); RefreshPipeColliders(); }
                ValidateAssets(); Run();
            }
            catch(Exception e) { File.WriteAllText("Temp/JunkerThermal.validation",e.ToString()); }
        }
        static void Changed(PlayModeStateChange state)
        { if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag,false)) { SessionState.SetBool(Flag,false); new GameObject("Thermal synthetic validation").AddComponent<ThermalValidationRunner>(); } }
        [MenuItem("Zandea/Junker Equipment/Thermal/Run Play Mode validation")]
        public static void Run() { if(EditorApplication.isPlayingOrWillChangePlaymode) return; SessionState.SetBool(Flag,true); EditorApplication.isPlaying=true; }
        [MenuItem("Zandea/Junker Equipment/Thermal/Validate assets and render preview")]
        public static void ValidateAssets()
        {
            foreach(string name in new[]{"ScrapCutter","CuttableBoard","WeldablePipes"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JunkerEquipment/Thermal/Generated/"+name+".prefab");
                if(!prefab) throw new Exception("Missing "+name);
                foreach(var t in prefab.GetComponentsInChildren<Transform>(true)) if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) throw new Exception("Missing script on "+t.name);
                foreach(var p in prefab.GetComponentsInChildren<ThermalPathProgress>(true)) if(!p.Valid) throw new Exception("Invalid path "+p.name);
                var copy=UnityEngine.Object.Instantiate(prefab); copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                if(name=="ScrapCutter")
                {
                    foreach(var ps in copy.GetComponentsInChildren<ParticleSystem>()) UnityEngine.Object.DestroyImmediate(ps.gameObject);
                    foreach(var line in copy.GetComponentsInChildren<LineRenderer>()) UnityEngine.Object.DestroyImmediate(line.gameObject);
                    var nozzle=copy.GetComponent<ThermalToolController>().nozzle;
                    var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.transform.SetParent(nozzle,false); marker.transform.localScale=Vector3.one*.015f;
                }
                JunkerEquipment.Editor.JunkerEquipmentValidation.Render(copy,"Temp/Thermal-"+name+".png",new Vector3(1,.35f,-1));
                UnityEngine.Object.DestroyImmediate(copy);
            }
            var tool=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/Tools/PLS-T ScrapCutter.prefab");
            var effects=tool.GetComponent<ThermalFXController>();
            if(!effects || !effects.beamCore || !effects.beamHalo || !effects.cutContactFlash || !effects.weldContactFlash ||
               !effects.cutContact || !effects.weldContact || !effects.cutMicroSpatter || !effects.weldMicroSpatter ||
               !effects.cutStart || !effects.cutLoop || !effects.weldStart || !effects.weldLoop ||
               !effects.cutContactClip || !effects.weldContactClip || !effects.loopSource || !effects.contactSource)
                throw new Exception("PLS-T prefab lacks layered plasma VFX or burner/contact audio.");
            var inventory=tool.GetComponent<VRInventoryItem>();
            if(!inventory || !inventory.CanStoreInBackpack || !inventory.InventoryAnchor)
                throw new Exception("PLS-T prefab lacks backpack inventory configuration.");
            foreach(var path in new[]{"Assets/MyAssets/prefabs/Player/Junker.prefab","Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path); if(!prefab) continue;
                var rig=prefab.GetComponentInChildren<JunkerEquipmentRig>(true);
                if(!rig || !rig.slots.Any(s=>s && s.name=="Right hip - PLS-T" && s.Accepts(tool.GetComponent<JunkerEquipmentItem>())))
                    throw new Exception("Player prefab lacks PLS-T right hip slot: "+path);
            }
            var demo=SceneManager.GetSceneByPath(ThermalDemoBuilder.ScenePath);
            if(demo.isLoaded)
            {
                var sceneTool=demo.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ThermalToolController>(true)).FirstOrDefault();
                if(!sceneTool || sceneTool.GetComponents<VRInventoryItem>().Length!=1 || !sceneTool.GetComponent<JunkerEquipmentItem>().rig)
                    throw new Exception($"Demo PLS-T inventory/rig: found={!!sceneTool}, name={ (sceneTool?sceneTool.name:"null") }, source={ (sceneTool?PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sceneTool.gameObject):"null") }, inventory={ (sceneTool?sceneTool.GetComponents<VRInventoryItem>().Length:0) }, rig={ (sceneTool && sceneTool.GetComponent<JunkerEquipmentItem>().rig?sceneTool.GetComponent<JunkerEquipmentItem>().rig.name:"null") }.");
            }
        }
        // Upgrade only this generated prototype's cylinder collider, preserving scene edits.
        [MenuItem("Zandea/Junker Equipment/Thermal/Refresh prototype pipe colliders")]
        public static void RefreshPipeColliders()
        {
            string path="Assets/JunkerEquipment/Thermal/Generated/WeldablePipes.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            Fix(root); PrefabUtility.SaveAsPrefabAsset(root,path); PrefabUtility.UnloadPrefabContents(root);
            path="Assets/JunkerEquipment/Thermal/Generated/ScrapCutter.prefab";
            root=PrefabUtility.LoadPrefabContents(path);
            Fix(root); PrefabUtility.SaveAsPrefabAsset(root,path); PrefabUtility.UnloadPrefabContents(root);
            var scene=EditorSceneManager.OpenScene(ThermalDemoBuilder.ScenePath,OpenSceneMode.Additive);
            foreach(var go in scene.GetRootGameObjects().Where(g=>g.name=="Thermal work demonstration")) Fix(go);
            EditorSceneManager.SaveScene(scene); EditorSceneManager.CloseScene(scene,true);
        }
        static void Fix(GameObject root)
        {
            foreach(var tool in root.GetComponentsInChildren<ThermalToolController>())
            {
                tool.nozzle.localPosition=new Vector3(0,.107f,.312f);
                var transformer=tool.GetComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
                if(!transformer) transformer=tool.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
                transformer.allowOneHandedScaling=false; transformer.allowTwoHandedScaling=false;
            }
            foreach(var cylinder in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Authored pipe geometry"))
            {
                var old=cylinder.GetComponent<CapsuleCollider>(); if(!old) continue;
                UnityEngine.Object.DestroyImmediate(old); var shape=cylinder.gameObject.AddComponent<MeshCollider>(); shape.sharedMesh=cylinder.GetComponent<MeshFilter>().sharedMesh; shape.convex=true;
            }
        }
    }

    public sealed class ThermalValidationRunner : MonoBehaviour
    {
        readonly StringBuilder report=new("Junker Thermal synthetic Play Mode validation\n");
        GameObject root;
        InputDevice leftDevice,rightDevice;
        bool wasBackground;
        void Check(bool condition,string message) { if(!condition) throw new Exception(message); report.AppendLine("PASS "+message); File.WriteAllText("Temp/JunkerThermal.validation",report.ToString()); }
        IEnumerator Start()
        {
            wasBackground=Application.runInBackground; Application.runInBackground=true;
            // Test fixtures run alone. Saved/unsaved edit-mode objects are restored by Unity
            // on exit; unrelated active scene simulation must not affect these checks.
            for(int i=0;i<SceneManager.sceneCount;i++)
                foreach(var existing in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if(existing!=gameObject) existing.SetActive(false);
            var run=Checks();
            while(true)
            {
                object current;
                try { if(!run.MoveNext()) break; current=run.Current; }
                catch(Exception e) { report.AppendLine("FAIL "+e); Finish(); yield break; }
                yield return current;
            }
            report.AppendLine("All synthetic checks passed. Hardware VR verification remains required."); Finish();
        }
        GameObject Spawn(string name,Vector3 position)
        {
            var go=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/JunkerEquipment/Thermal/Generated/"+name+".prefab"),root.transform);
            go.transform.localPosition=position; go.transform.localRotation=Quaternion.identity; return go;
        }
        IEnumerator Checks()
        {
            root=new GameObject("Thermal validation fixtures"); root.transform.position=new Vector3(100,100,100);
            var board=Spawn("CuttableBoard",Vector3.zero).GetComponent<CuttableSection>();
            var path=board.paths[0]; yield return null;
            Vector3 start=path.WorkLocation;
            for(int i=0;i<2000;i++) path.Advance(start,.02f,100);
            Check(path.Fraction<.05f,"stationary cutting does not complete a path");
            float partial=path.Fraction;
            Check(!path.Advance(start+Vector3.forward,.1f,100) && path.Fraction==partial,"out-of-contact progress is preserved");
            Check(!path.Advance(path.path.controlPoints[2].position,.1f,100),"cannot skip to a later corner");
            var ownerA=new object(); var ownerB=new object(); path.path.RequestVisibility(ownerA,true); path.path.RequestVisibility(ownerB,true); path.path.RequestVisibility(ownerA,false);
            Check(path.path.IsVisible,"tool keeps guide visible after scanner releases it"); path.path.RequestVisibility(ownerB,false); Check(!path.path.IsVisible,"last visibility owner hides guide");
            path.Clear();
            Check(!board.Work(ThermalMode.Weld,start,1,"",.02f,100),"wrong mode rejected");
            Check(!board.Work(ThermalMode.Cut,start,0,"",.02f,100),"insufficient power rejected");
            Check(!board.Work(ThermalMode.Cut,start,1,"wood",.02f,100),"incompatible material rejected");
            Check(board.Work(ThermalMode.Cut,start,1,"steel",.02f,100),"valid cutting starts");
            Check(!board.Work(ThermalMode.Cut,start,1,"steel",.02f,100),"two tools cannot double-process a target in one frame");
            int released=0; board.completed.AddListener(()=>released++);
            for(int i=0;i<600 && !board.Released;i++) { yield return null; board.Work(ThermalMode.Cut,path.WorkLocation,1,"",.02f,100); }
            Check(board.Released && released==1 && !board.cutout.isKinematic && board.cutout.useGravity && board.cutoutGrab.enabled,"traced board releases one physical, grabbable cutout");
            float cutoutY=board.cutout.position.y;
            yield return new WaitForSeconds(.65f);
            Check(board.cutout.position.y<cutoutY-.15f,"released board centre visibly falls");
            Check(!Physics.Raycast(board.transform.position-Vector3.forward,Vector3.forward,2),"remaining board has a real collision hole");
            Check(!board.Work(ThermalMode.Cut,start,1,"",1,100) && released==1,"completed board cannot release twice");

            var pipes=Spawn("WeldablePipes",new Vector3(2,0,0)); var weld=pipes.GetComponentInChildren<WeldConnection>(); weld.snapOnRelease=false; weld.bodyB.useGravity=false;
            yield return null;
            Check(!weld.Accepts(ThermalMode.Weld),"misaligned pipes reject welding");
            Align(weld); yield return null;
            Check(weld.Aligned && !weld.Attached,$"aligned pipes do not weld from proximity (distance {Vector3.Distance(weld.anchorA.position,weld.anchorB.position):F4}, angle {Quaternion.Angle(weld.anchorA.rotation,weld.anchorB.rotation):F2})");
            for(int i=0;i<150 && !weld.Attached;i++) { yield return null; weld.Work(ThermalMode.Weld,weld.Current.WorkLocation,1,"",.02f,100); }
            var pipeCollider=weld.bodyB.GetComponentInChildren<Collider>();
            Check(weld.Attached && weld.bodyB.isKinematic && weld.bodyB.transform.parent==weld.bodyA.transform && !pipeCollider.enabled && !weld.grabB.enabled,
                "traced seam turns pipe B into one collision-free part of pipe A");
            yield return new WaitForSeconds(.4f);
            Check(weld.Aligned && weld.bodyA.isKinematic && weld.bodyB.transform.parent==weld.bodyA.transform,"anchored assembly stays aligned under incidental gravity");
            pipes.transform.position-=new Vector3(50,0,0); Physics.SyncTransforms(); yield return new WaitForFixedUpdate();
            Check(weld.Aligned,"welded assembly survives shared origin translation");
            int broken=0; weld.weldBroken.AddListener(()=>broken++);
            for(int i=0;i<150 && weld.Attached;i++) { yield return null; weld.Work(ThermalMode.Cut,weld.Current.WorkLocation,1,"",.02f,100); }
            yield return null;
            Check(!weld.Attached && broken==1 && weld.bodyB.transform.parent==pipes.transform && !weld.bodyB.isKinematic && pipeCollider.enabled && weld.grabB.enabled,
                "cut seam restores independent physics, collisions and grabbing");
            weld.bodyB.useGravity=true;
            float pipeY=weld.bodyB.position.y; yield return new WaitForSeconds(.3f);
            Check(weld.bodyB.position.y<pipeY-.1f,"severed pipe separates physically");
            weld.bodyB.useGravity=false;
            weld.snapOnRelease=true; weld.RearmSnap();
            weld.bodyB.rotation=Quaternion.Euler(30,110,25);
            weld.bodyB.position=weld.anchorA.position+Vector3.right*.05f;
            weld.bodyB.transform.SetPositionAndRotation(weld.bodyB.position,weld.bodyB.rotation);
            Physics.SyncTransforms(); yield return new WaitForFixedUpdate(); yield return null;
            Check(weld.Aligned && Quaternion.Angle(weld.anchorA.rotation,weld.anchorB.rotation)<1,
                "surface contact snaps pipe B without matching its rotation by hand");
            weld.snapOnRelease=false;
            for(int i=0;i<150 && !weld.Attached;i++) { yield return null; weld.Work(ThermalMode.Weld,weld.Current.WorkLocation,1,"",.02f,100); }
            Check(weld.Attached,"cut connection can be aligned and welded again");
            weld.bodyA.isKinematic=false; weld.bodyA.useGravity=false; weld.bodyA.linearVelocity=Vector3.right;
            float x=weld.bodyB.position.x; yield return new WaitForSeconds(.4f);
            Check(weld.bodyB.position.x>x+.05f && weld.Aligned,"two dynamic welded bodies move as one assembly");

            var toolObject=Spawn("ScrapCutter",new Vector3(0,2,0)); var tool=toolObject.GetComponent<ThermalToolController>(); var item=toolObject.GetComponent<JunkerEquipmentItem>();
            tool.modeCooldown=0;
            InputSystem.RegisterLayout("{\"name\":\"ThermalValidationController\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"thumbstickClicked\",\"layout\":\"Button\",\"usages\":[\"Primary2DAxisClick\"]}]}");
            leftDevice=InputSystem.AddDevice("ThermalValidationController"); InputSystem.SetDeviceUsage(leftDevice,"LeftHand");
            rightDevice=InputSystem.AddDevice("ThermalValidationController"); InputSystem.SetDeviceUsage(rightDevice,"RightHand");
            var manager=new GameObject("Thermal validation XR manager").AddComponent<XRInteractionManager>(); manager.transform.SetParent(root.transform);
            var left=Hand(manager,InteractorHandedness.Left); var right=Hand(manager,InteractorHandedness.Right);
            item.Grab.interactionManager=manager;
            yield return null;
            Press(Key.F9); yield return null; Check(tool.mode==ThermalMode.Cut,"unheld tool ignores mode clicks"); Release(); yield return null;
            left.StartManualInteraction((IXRSelectInteractable)item.Grab); yield return null;
            Check(JunkerBody.JunkerSprint.IsStickClickReserved(true) && !JunkerBody.JunkerSprint.IsStickClickReserved(false),"only holding-hand locomotion click is reserved");
            Press(Key.F10); yield return null; Check(tool.mode==ThermalMode.Cut,"opposite-hand click ignored"); Release(); yield return null;
            Press(Key.F9); yield return null; Check(tool.mode==ThermalMode.Weld,"left holding-hand click selects WELD");
            yield return null; Check(tool.mode==ThermalMode.Weld && !tool.effects.beam.enabled,"held click toggles once and never fires"); Release(); yield return null;
            left.EndManualInteraction(); yield return null; right.StartManualInteraction((IXRSelectInteractable)item.Grab); yield return null;
            Press(Key.F10); yield return null; Check(tool.mode==ThermalMode.Cut,"mode input follows transfer to right hand"); Release(); yield return null;
            Trigger(right,true); yield return null; yield return null;
            Check(item.Activated && tool.effects.beam.enabled,"holding-hand XRI trigger fires beam");
            Press(Key.F10); yield return null; Check(!tool.effects.beam.enabled,"mode switch while firing stops beam until trigger is released"); Release();
            Trigger(right,false); yield return null; yield return null;
            Trigger(right,true); yield return null; yield return null;
            Check(tool.effects.beam.enabled,"release and repress resumes beam in new mode");
            right.EndManualInteraction(); yield return null;
            Check(!tool.effects.beam.enabled && !tool.effects.cutNozzle.isPlaying && !tool.effects.weldNozzle.isPlaying,"drop stops beam and nozzle effects");
            Check(!JunkerBody.JunkerSprint.IsStickClickReserved(true) && !JunkerBody.JunkerSprint.IsStickClickReserved(false),"drop restores both locomotion click bindings");
            tool.enabled=false; Check(!tool.effects.beam.enabled,"disable stops beam");
        }
        static void Align(WeldConnection weld)
        {
            var rotation=weld.anchorA.rotation*Quaternion.Inverse(weld.anchorB.rotation);
            var offset=weld.anchorB.position-weld.bodyB.position;
            var position=weld.anchorA.position-rotation*offset;
            var orientation=rotation*weld.bodyB.rotation;
            weld.bodyB.position=position; weld.bodyB.rotation=orientation;
            weld.bodyB.transform.SetPositionAndRotation(position,orientation);
            weld.bodyB.linearVelocity=Vector3.zero; weld.bodyB.angularVelocity=Vector3.zero; Physics.SyncTransforms();
        }
        XRDirectInteractor Hand(XRInteractionManager manager,InteractorHandedness side)
        {
            var go=new GameObject("Validation "+side); go.transform.SetParent(root.transform,false); go.transform.localPosition=new Vector3(0,2,0);
            var sphere=go.AddComponent<SphereCollider>(); sphere.isTrigger=true; sphere.radius=.1f;
            var rb=go.AddComponent<Rigidbody>(); rb.isKinematic=true; rb.useGravity=false;
            var hand=go.AddComponent<XRDirectInteractor>(); hand.interactionManager=manager; hand.handedness=side;
            hand.selectInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            hand.activateInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            return hand;
        }
        static void Trigger(XRDirectInteractor hand,bool value) { hand.activateInput.QueueManualState(value,value?1f:0f); }
        void Press(Key key) { InputSystem.QueueDeltaStateEvent((key==Key.F9?leftDevice:rightDevice).GetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("thumbstickClicked"),(byte)1); InputSystem.Update(); }
        void Release() { InputSystem.QueueDeltaStateEvent(leftDevice.GetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("thumbstickClicked"),(byte)0); InputSystem.QueueDeltaStateEvent(rightDevice.GetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("thumbstickClicked"),(byte)0); InputSystem.Update(); }
        void Finish()
        {
            if(leftDevice!=null) InputSystem.RemoveDevice(leftDevice); if(rightDevice!=null) InputSystem.RemoveDevice(rightDevice);
            if(root) Destroy(root); Application.runInBackground=wasBackground;
            File.WriteAllText("Temp/JunkerThermal.validation",report.ToString()); EditorApplication.isPlaying=false;
        }
    }
}
