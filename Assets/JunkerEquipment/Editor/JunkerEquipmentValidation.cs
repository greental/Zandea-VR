#pragma warning disable 0618, 0619
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace JunkerEquipment.Editor
{
    [InitializeOnLoad]
    public static class JunkerEquipmentValidation
    {
        const string Flag="JunkerEquipment.Validation";
        const string FloorFlag="JunkerEquipment.FloorPickupValidation";
        static JunkerEquipmentValidation() { EditorApplication.playModeStateChanged+=Changed; }
        static void Changed(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag,false))
            { SessionState.SetBool(Flag,false); new GameObject("Equipment synthetic validation").AddComponent<JunkerEquipmentValidationRunner>(); }
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(FloorFlag,false))
            { SessionState.SetBool(FloorFlag,false); new GameObject("Floor pickup validation").AddComponent<JunkerFloorPickupValidationRunner>(); }
        }
        [MenuItem("Tools/Junker Equipment/Validate saved equipment")]
        public static void Validate()
        {
            var scene=JunkerEquipmentSetup.Scene(); var log=new StringBuilder();
            foreach(var go in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject)>0) throw new Exception("Missing script "+go.name);
            foreach(var hand in UnityEngine.Object.FindObjectsByType<JunkerGloveHand>(FindObjectsSortMode.None))
            {
                if(!hand.body || !hand.input || hand.joints.Length!=15 || hand.joints.Any(j=>!j.bone)) throw new Exception("Incomplete glove: "+hand.name);
                var skin=hand.GetComponentInChildren<SkinnedMeshRenderer>(); if(skin.bones.Length!=16 || skin.sharedMesh.bindposes.Length!=16) throw new Exception("Invalid glove skin: "+hand.name);
                log.AppendLine($"PASS {hand.name}: {skin.sharedMesh.vertexCount} vertices, 16 skin bones, 15 driven finger joints; existing grip/trigger actions.");
            }
            var rig=UnityEngine.Object.FindFirstObjectByType<JunkerEquipmentRig>(); if(!rig || rig.slots.Length!=5 || !rig.body) throw new Exception("Incomplete rig.");
            foreach(var item in UnityEngine.Object.FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None))
            {
                if(!item.rig || !item.gripPose || !item.GetComponent<Rigidbody>() || !item.GetComponent<XRGrabInteractable>() || item.GetComponentsInChildren<Collider>().Length==0) throw new Exception("Incomplete item "+item.name);
                log.AppendLine("PASS item references: "+item.itemId);
            }
            log.AppendLine("PASS five slots, existing body references, no missing scripts.");
            File.WriteAllText("Temp/JunkerEquipment.validation",log.ToString());
        }
        [MenuItem("Tools/Junker Equipment/Run equipment Play Mode validation")]
        public static void Play()
        {
            Validate(); var scene=SceneManager.GetActiveScene();
            if(scene.isDirty) EditorSceneManager.SaveScene(scene);
            SessionState.SetBool(Flag,true); EditorApplication.isPlaying=true;
        }
        [MenuItem("Tools/Junker Equipment/Test dropped pistol pickup")]
        public static void TestFloorPickup()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            SessionState.SetBool(FloorFlag,true);
            EditorApplication.isPlaying=true;
        }
        [MenuItem("Tools/Junker Equipment/Render glove and tool previews")]
        public static void Previews()
        {
            JunkerEquipmentSetup.Scene();
            foreach(var hand in UnityEngine.Object.FindObjectsByType<JunkerGloveHand>(FindObjectsSortMode.None))
                foreach(var curl in new[]{0f,1f})
                {
                    var copy=UnityEngine.Object.Instantiate(hand.gameObject); copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    copy.GetComponent<JunkerGloveHand>().SetCurl(curl,curl);
                    Render(copy,"Temp/"+hand.name+"-"+curl+".png",new Vector3(.7f,1,-1));
                }
            foreach(var item in UnityEngine.Object.FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None))
            { var copy=UnityEngine.Object.Instantiate(item.gameObject); copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); Render(copy,"Temp/item-"+item.itemId+".png",new Vector3(.7f,.5f,-1)); }
        }
        [MenuItem("Tools/Junker Equipment/Render held tool previews")]
        public static void HeldPreviews()
        {
            JunkerEquipmentSetup.Scene();
            foreach(var hand in UnityEngine.Object.FindObjectsByType<JunkerGloveHand>(FindObjectsSortMode.None))
            foreach(var item in UnityEngine.Object.FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None).Where(i=>i.itemId!="backpack"))
            {
                var root=new GameObject("Held preview");var glove=UnityEngine.Object.Instantiate(hand.gameObject,root.transform);
                var arm=hand.left?hand.body.leftArm:hand.body.rightArm;
                glove.transform.SetPositionAndRotation(arm.wristPositionOffset+hand.positionOffset,Quaternion.Euler(hand.rotationOffset));glove.GetComponent<JunkerGloveHand>().SetCurl(1,item.itemId=="PKO"?.4f:1);
                var tool=UnityEngine.Object.Instantiate(item.gameObject,root.transform);tool.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var grip=tool.GetComponent<JunkerEquipmentItem>().gripPose;
                tool.transform.rotation=Quaternion.Inverse(grip.rotation);tool.transform.position-=grip.position;
                Render(root,"Temp/held-"+(hand.left?"left":"right")+"-"+item.itemId+".png",new Vector3(hand.left?.7f:-.7f,.6f,-1));
            }
        }
        public static void Render(GameObject go,string path,Vector3 view)
        {
            var preview=new PreviewRenderUtility();
            try
            {
                preview.AddSingleGO(go); var bounds=JunkerEquipmentSetup.BoundsOf(go);
                preview.camera.transform.position=bounds.center+view.normalized*bounds.size.magnitude*2;
                preview.camera.transform.LookAt(bounds.center);
                preview.camera.nearClipPlane=.001f; preview.camera.farClipPlane=100;
                preview.camera.orthographic=true; preview.camera.orthographicSize=bounds.size.magnitude*.56f;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.13f,.15f,.18f);
                preview.lights[0].intensity=1.5f; preview.lights[0].transform.rotation=Quaternion.Euler(30,30,0); preview.lights[1].intensity=.8f;
                preview.BeginStaticPreview(new Rect(0,0,800,800)); preview.Render(true);
                var texture=preview.EndStaticPreview(); File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            }
            finally { preview.Cleanup(); }
        }
    }
    public sealed class JunkerFloorPickupValidationRunner : MonoBehaviour
    {
        readonly StringBuilder report=new StringBuilder("Dropped pistol pickup Play Mode validation\n");
        IEnumerator Start()
        {
            Application.runInBackground=true;
            var run=Run();
            while(true)
            {
                object current;
                try { if(!run.MoveNext())break;current=run.Current; }
                catch(Exception e){report.AppendLine("FAIL "+e);Finish();yield break;}
                yield return current;
            }
            Finish();
        }
        void Check(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            report.AppendLine("PASS "+message);
        }
        IEnumerator Run()
        {
            yield return null;
            var body=FindFirstObjectByType<VRBodyController>();
            var rig=FindFirstObjectByType<JunkerEquipmentRig>();
            var pistol=FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None).Single(i=>i.itemId=="PKO");
            var hand=body.rightController.GetComponentInChildren<XRDirectInteractor>();
            Check(body&&rig&&pistol&&hand,"scene references resolve");
            foreach(var driver in body.xrOrigin.GetComponentsInChildren<TrackedPoseDriver>(true))driver.enabled=false;
            foreach(var gravity in body.xrOrigin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(true))gravity.enabled=false;
            pistol.currentSlot.ReleaseItem(pistol);
            // Hold the settled test object still until the grip press; OnReleased restores dynamics.
            pistol.Body.isKinematic=true;pistol.Body.useGravity=false;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name="Temporary pistol pickup floor";
            floor.transform.position=body.rightController.position+body.rightController.forward*.7f+Vector3.down*.45f;
            floor.transform.localScale=new Vector3(2f,.1f,2f);
            pistol.transform.position=floor.transform.position+Vector3.up*.2f;
            pistol.Body.linearVelocity=Vector3.zero;pistol.Body.angularVelocity=Vector3.zero;
            Physics.SyncTransforms();
            pistol.transform.position+=Vector3.up*(floor.GetComponent<Collider>().bounds.max.y-pistol.GetComponent<Collider>().bounds.min.y+.005f);
            Physics.SyncTransforms();
            body.rightController.position=pistol.GetComponent<Collider>().bounds.center+Vector3.up*.38f;
            hand.selectInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            hand.selectInput.manualPerformed=false;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.6f);
            // XR input can update the nested direct interactor after controller setup.
            // Place the floor relative to the actual query origin used by the rig.
            floor.transform.position=hand.transform.position+hand.transform.forward*.25f+Vector3.down*.4f;
            pistol.transform.position=floor.transform.position+Vector3.up*.2f;
            Physics.SyncTransforms();
            pistol.transform.position+=Vector3.up*(floor.GetComponent<Collider>().bounds.max.y-pistol.GetComponent<Collider>().bounds.min.y+.005f);
            Physics.SyncTransforms();
            pistol.Body.isKinematic=false;pistol.Body.useGravity=true;
            pistol.Body.linearVelocity=Vector3.zero;pistol.Body.angularVelocity=Vector3.zero;
            yield return new WaitForSeconds(.3f);
            var targets=new List<IXRInteractable>();hand.GetValidTargets(targets);
            Check(!targets.Contains(pistol.Grab),"pistol lies outside normal direct grip collider");
            Check(pistol.state==EquipmentState.World,"pistol rests as a world item");
            Check(Vector3.Distance(hand.transform.position,pistol.GetComponent<Collider>().ClosestPoint(hand.transform.position))<rig.floorPickupReach,"pistol is inside assisted pickup reach");
            hand.selectInput.manualPerformed=true;
            yield return null;yield return null;
            Check(pistol.state==EquipmentState.Held&&hand.isPerformingManualInteraction,"pressing grip retrieves pistol from floor");
            hand.selectInput.manualPerformed=false;
            yield return null;yield return null;
            Check(pistol.state==EquipmentState.World&&!hand.isPerformingManualInteraction,"releasing grip drops pistol normally");
            Destroy(floor);
        }
        void Finish()
        {
            File.WriteAllText("Temp/JunkerEquipment.floorpickupvalidation",report.ToString());
            EditorApplication.isPlaying=false;
        }
    }
    public sealed class JunkerEquipmentValidationRunner : MonoBehaviour
    {
        readonly StringBuilder report=new StringBuilder("Unity Play Mode equipment integration validation\n");
        bool finished;
        void Check(bool ok,string name) { if(!ok) throw new Exception(name); report.AppendLine("PASS "+name); }
        IEnumerator Start()
        {
            Application.logMessageReceived+=OnLog;
            var run=Run();
            while(true)
            {
                object current;
                try { if(!run.MoveNext()) break; current=run.Current; }
                catch(Exception e) { Finish(e); yield break; }
                yield return current;
            }
            Finish(null);
        }
        void OnLog(string text,string trace,LogType type)
        { if(type==LogType.Error || type==LogType.Exception) report.AppendLine("UNITY ERROR: "+text+"\n"+trace); }
        IEnumerator Run()
        {
            yield return null;
            var body=FindFirstObjectByType<VRBodyController>();
            foreach(var driver in body.xrOrigin.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled=false;
            body.leftOpticalTracking=null; body.rightOpticalTracking=null;
            body.hmd.position=body.xrOrigin.TransformPoint(new Vector3(0,body.standingHeight,0));
            body.leftController.SetPositionAndRotation(body.xrOrigin.TransformPoint(new Vector3(-.25f,1.25f,.35f)),body.xrOrigin.rotation);
            body.rightController.SetPositionAndRotation(body.xrOrigin.TransformPoint(new Vector3(.25f,1.25f,.35f)),body.xrOrigin.rotation);
            var rig=FindFirstObjectByType<JunkerEquipmentRig>();
            var items=FindObjectsByType<JunkerEquipmentItem>(FindObjectsSortMode.None);
            var left=body.leftController.GetComponentsInChildren<XRBaseInputInteractor>().First();
            var right=body.rightController.GetComponentsInChildren<XRBaseInputInteractor>().First();
            var gun=FindObjectsByType<JunkerPistol>(FindObjectsSortMode.None).Single(p=>p.GetComponent<JunkerEquipmentItem>().itemId=="PKO");var pistol=gun.GetComponent<JunkerEquipmentItem>();var slot=pistol.currentSlot;
            yield return new WaitForSeconds(.2f);
            Check(pistol.state==EquipmentState.Holstered && slot.Occupant==pistol,"pistol starts in right hip slot");
            Check(pistol.GetComponentsInChildren<Collider>().All(c=>c.enabled&&c.isTrigger) && pistol.Body.detectCollisions,"holstered colliders remain queryable trigger colliders");
            var oldPosition=pistol.transform.position; body.xrOrigin.position+=new Vector3(.1f,0,.1f);
            yield return null;yield return null;
            Check(Vector3.Distance(pistol.transform.position,slot.transform.position)<.002f && Vector3.Distance(oldPosition,pistol.transform.position)>.1f,"holster follows player movement");
            float before=slot.transform.position.y;body.hmd.position-=Vector3.up*.4f;
            yield return null;yield return null;
            Check(slot.transform.position.y<before-.25f,"holster follows physical crouch"); body.hmd.position+=Vector3.up*.4f;
            yield return null;yield return null;
            var near=body.rightController.GetComponentInChildren<XRDirectInteractor>();
            var sphere=near.GetComponent<SphereCollider>();
            var reachPosition=body.rightController.position;
            body.rightController.position+=pistol.GetComponent<Collider>().bounds.center-sphere.transform.TransformPoint(sphere.center);
            Physics.SyncTransforms();yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();yield return null;
            var targets=new List<IXRInteractable>();near.GetValidTargets(targets);
            Check(targets.Contains(pistol.Grab),"existing direct hand interactor detects holstered pistol by physical proximity");
            body.rightController.position=reachPosition;
            right.activateInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            right.activateInput.manualPerformed=false;
            right.StartManualInteraction((IXRSelectInteractable)pistol.Grab); yield return new WaitForSeconds(.2f);
            Check(pistol.state==EquipmentState.Held && !slot.Occupant && pistol.Grab.isSelected,"right hand draws pistol and frees slot");
            Check(pistol.Grab.selectMode==InteractableSelectMode.Single && pistol.Grab.interactorsSelecting.Count==1,"XRI single-owner selection (hand-to-hand transfer is allowed)");
            Check(!slot.TryMount(pistol),"slot cannot claim held pistol");
            Check(rig.playerColliders.All(c=>!c || Physics.GetIgnoreCollision(c,pistol.GetComponent<Collider>())),"item cannot push player capsule");
            var target=FindFirstObjectByType<JunkerTarget>(); target.transform.position=gun.muzzle.position+gun.muzzle.forward*2; target.transform.localScale=new Vector3(4,4,.2f); gun.hitMask=~0; Physics.SyncTransforms();
            int hits=target.Hits;
            right.activateInput.manualPerformed=true;yield return null;yield return null;
            if(gun.ShotsFired==0) gun.TryFire();
            if(!(gun.ShotsFired==1 && gun.LastShotHit && target.Hits==hits+1)) report.AppendLine($"PISTOL DIAGNOSTIC shots={gun.ShotsFired} hit={gun.LastShotHit} targetHits={target.Hits} before={hits} muzzle={gun.muzzle.position} forward={gun.muzzle.forward} target={target.transform.position}");
            Check(gun.ShotsFired==1 && gun.LastShotHit && target.Hits==hits+1,"trigger activation fires muzzle ray and damages target");
            Check(!gun.TryFire(),"pistol rate limit");
            right.activateInput.manualPerformed=false;
            right.EndManualInteraction(); yield return null;yield return null;
            Check(pistol.state==EquipmentState.World && !pistol.Body.isKinematic && pistol.Body.useGravity,"release away from slot drops dynamically");
            float y=pistol.transform.position.y;yield return new WaitForSeconds(.1f);Check(pistol.transform.position.y<y,"dropped pistol falls under gravity");
            left.StartManualInteraction((IXRSelectInteractable)pistol.Grab);yield return new WaitForSeconds(.15f);
            Check(pistol.state==EquipmentState.Held,"left hand can grab pistol");
            left.EndManualInteraction(); pistol.transform.position=slot.transform.position; yield return null;yield return null;
            Check(pistol.state==EquipmentState.Holstered && slot.Occupant==pistol,"release near compatible slot snaps back after XRI detach");
            var binoculars=FindFirstObjectByType<JunkerBinoculars>();var bi=binoculars.GetComponent<JunkerEquipmentItem>();right.StartManualInteraction((IXRSelectInteractable)bi.Grab);yield return null;
            bi.Grab.trackPosition=false;bi.Grab.trackRotation=false;
            bi.transform.rotation=body.hmd.rotation;bi.transform.position+=body.hmd.position+body.hmd.forward*.08f-binoculars.eyepiece.position;
            float fov=binoculars.hmd.fieldOfView;yield return null;
            Check(binoculars.viewing && binoculars.hmd.fieldOfView==fov,"binocular viewing mode without changing XR FOV");
            bi.transform.rotation*=Quaternion.Euler(0,90,0);yield return null;Check(!binoculars.viewing,"binocular alignment gate");right.EndManualInteraction();
            var tools=FindObjectsByType<JunkerScrewdriver>(FindObjectsSortMode.None);var motor=tools.Single(t=>t.powered);var manual=tools.Single(t=>!t.powered);var screw=motor.screws[0];
            var mi=motor.GetComponent<JunkerEquipmentItem>();right.StartManualInteraction((IXRSelectInteractable)mi.Grab);yield return null;mi.Grab.trackPosition=false;mi.Grab.trackRotation=false;
            motor.transform.rotation=Quaternion.LookRotation(-screw.transform.forward,screw.transform.up);motor.transform.position+=screw.Contact-motor.tip.position;yield return null;
            Check(motor.Engaged==screw,"powered bit engages aligned screw");float progress=screw.progress;
            right.activateInput.manualPerformed=true;yield return new WaitForSeconds(.15f);
            Check(screw.progress<progress,"powered trigger loosens screw");right.EndManualInteraction();yield return null;
            right.activateInput.manualPerformed=false;
            var man=manual.GetComponent<JunkerEquipmentItem>();left.StartManualInteraction((IXRSelectInteractable)man.Grab);yield return null;man.Grab.trackPosition=false;man.Grab.trackRotation=false;
            manual.transform.rotation=Quaternion.LookRotation(-screw.transform.forward,screw.transform.up);manual.transform.position+=screw.Contact-manual.tip.position;yield return null;
            Check(manual.Engaged==screw,"manual screwdriver engages the same screw");progress=screw.progress;manual.transform.RotateAround(manual.tip.position,manual.tip.forward,-50);yield return null;
            Check(screw.progress<progress,"manual wrist rotation loosens screw");left.EndManualInteraction();
            var panel=FindFirstObjectByType<JunkerRepairPanel>();foreach(var s in panel.screws) s.Turn(-100);yield return null;
            Check(panel.Unlocked && panel.cover.enabled,"repair cover becomes grabbable when all screws loosen");
            foreach(var glove in FindObjectsByType<JunkerGloveHand>(FindObjectsSortMode.None))
            {
                glove.previewPose=true;glove.grip=1;glove.trigger=1;yield return null;
                Check(glove.joints.All(j=>Quaternion.Angle(j.rest,j.bone.localRotation)>5),glove.name+" all fifteen finger joints curl");
                var skin=glove.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();skin.BakeMesh(baked);
                Check(baked.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),glove.name+" finite skinned vertices");Destroy(baked);
            }
        }
        void Finish(Exception error)
        {
            if(finished)return;finished=true;
            if(error!=null)report.AppendLine("FAIL "+error);
            else report.AppendLine("Synthetic checks complete. Headset comfort, final grip offsets and haptics still need hardware validation.");
            Application.logMessageReceived-=OnLog;
            File.WriteAllText("Temp/JunkerEquipment.playvalidation",report.ToString());
            EditorApplication.isPlaying=false;
        }
    }
}
