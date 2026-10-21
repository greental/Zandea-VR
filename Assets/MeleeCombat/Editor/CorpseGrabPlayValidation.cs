using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[InitializeOnLoad]
public static class CorpseGrabPlayValidation
{
    const string Request = "Temp/CorpseGrabPlayValidation.request";
    const string Result = "Temp/CorpseGrabPlayValidation.result";
    const string Running = "CorpseGrabPlayValidation.Running";
    const string StartedPlay = "CorpseGrabPlayValidation.StartedPlay";
    const string Prefab = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
    static GameObject actor;
    static GameObject handObject, secondHandObject, managerObject;
    static XRDirectInteractor hand, secondHand;
    static ScavengerRagdoll ragdoll;
    static DismemberableCharacter anatomy;
    static int phase, phaseFrame;
    static int initialColliders;

    static CorpseGrabPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Tools/Melee Combat/Validate grabbable and severable corpse")]
    public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            SessionState.SetBool(Running, true);
            SessionState.SetBool(StartedPlay, !EditorApplication.isPlaying);
            phase = 0;
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (phase == 0)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                if (!prefab) throw new Exception("Missing corpse validation prefab");
                actor = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 2, 0), Quaternion.identity);
                ragdoll = actor.GetComponent<ScavengerRagdoll>();
                anatomy = actor.GetComponent<DismemberableCharacter>();
                var health = actor.GetComponent<ScavengerHealth>();
                if (!ragdoll || !anatomy || !health) throw new Exception("NPC lacks corpse systems");
                anatomy.minimumEdgeAlignment = 0f;
                health.ForceDeath();
                if (!health.IsDead || !ragdoll.Active || !ragdoll.CorpseGrab ||
                    !ragdoll.CorpseGrab.isActiveAndEnabled ||
                    !ragdoll.CorpseGrab.GetComponent<Rigidbody>() ||
                    ragdoll.CorpseGrab.GetComponent<Rigidbody>().isKinematic)
                    throw new Exception("Dead ragdoll has no dynamic grab anchor");
                initialColliders = ragdoll.CorpseGrab.colliders.Count;
                if (initialColliders < 4) throw new Exception("Corpse grab lacks body colliders");
                if (ragdoll.CorpseGrab.selectMode != InteractableSelectMode.Multiple)
                    throw new Exception("Corpse grab does not accept both hands");
                phase = 1; phaseFrame = Time.frameCount;
                return;
            }
            if (phase == 1)
            {
                if (Time.frameCount < phaseFrame + 120) return;
                var manager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();
                if (!manager) { managerObject = new GameObject("Corpse validation interaction manager");
                    manager = managerObject.AddComponent<XRInteractionManager>(); }
                ragdoll.CorpseGrab.interactionManager = manager;
                handObject = new GameObject("Corpse validation hand");
                handObject.transform.position = ragdoll.CorpseGrab.transform.position;
                var shape = handObject.AddComponent<SphereCollider>();
                shape.isTrigger = true; shape.radius = .06f;
                hand = handObject.AddComponent<XRDirectInteractor>();
                hand.interactionManager = manager;
                hand.StartManualInteraction((IXRSelectInteractable)ragdoll.CorpseGrab);
                secondHandObject = new GameObject("Corpse validation second hand");
                var headBone = anatomy.GetJointBone(SeverPoint.Head);
                if (!headBone) throw new Exception("Corpse has no head grab location");
                secondHandObject.transform.position = headBone.position;
                var secondShape = secondHandObject.AddComponent<SphereCollider>();
                secondShape.isTrigger = true; secondShape.radius = .06f;
                secondHand = secondHandObject.AddComponent<XRDirectInteractor>();
                secondHand.interactionManager = manager;
                secondHand.StartManualInteraction((IXRSelectInteractable)ragdoll.CorpseGrab);
                phase = 2; phaseFrame = Time.frameCount;
                return;
            }
            if (phase == 2)
            {
                if (Time.frameCount < phaseFrame + 3) return;
                if (!ragdoll.CorpseGrab.isSelected || !hand.hasSelection || !secondHand.hasSelection ||
                    ragdoll.CorpseGrab.interactorsSelecting.Count != 2)
                    throw new Exception("Both XR hands could not select separate locations on the fallen body");
                hand.EndManualInteraction();
                if (!ragdoll.CorpseGrab.isSelected || !secondHand.hasSelection)
                    throw new Exception("Releasing the first hand dropped the second corpse grip");
                secondHand.EndManualInteraction();
                var bone = anatomy.GetJointBone(SeverPoint.Head);
                if (!bone || !anatomy.TrySever(bone.position, Vector3.right * 8f,
                    Vector3.right, 7f, true, Vector3.zero))
                    throw new Exception("Head could not be severed from a fallen corpse");
                var head = UnityEngine.Object.FindObjectsByType<SeveredBodyPartIdentity>(FindObjectsSortMode.None)
                    .FirstOrDefault(part => part.npcId == anatomy.npcId && part.bodyPart == SeverPoint.Head);
                if (!head || !head.GetComponentsInChildren<MeshFilter>()
                    .Any(filter => filter.sharedMesh && filter.sharedMesh.triangles.Length > 300))
                    throw new Exception("Postmortem head has no rendered flesh");
                if (!ragdoll.CorpseGrab || !ragdoll.CorpseGrab.isActiveAndEnabled ||
                    ragdoll.CorpseGrab.colliders.Count >= initialColliders)
                    throw new Exception("Corpse grab was lost or still includes severed head colliders");
                phase = 3; phaseFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount < phaseFrame + 30) return;
            var shoulder = anatomy.GetJointBone(SeverPoint.LeftShoulder);
            if (!shoulder || !anatomy.TrySever(shoulder.position, Vector3.right * 8f,
                Vector3.right, 7f, true, Vector3.zero))
                throw new Exception("Arm could not be severed after the corpse's head");
            if (ragdoll.CorpseGrab.colliders.Any(shape => !shape || !shape.enabled))
                throw new Exception("Corpse grab retained disabled severed limb colliders");
            File.WriteAllText(Result, "PASS dead ragdoll has two simultaneous dynamic body grips; " +
                "either hand can release; head and arm sever after floor fall; grab colliders update after cuts");
            Finish();
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL corpse grab and postmortem sever\n" + error);
            Debug.LogException(error);
            Finish();
        }
    }

    static void Finish()
    {
        if (actor) UnityEngine.Object.Destroy(actor);
        if (handObject) UnityEngine.Object.Destroy(handObject);
        if (secondHandObject) UnityEngine.Object.Destroy(secondHandObject);
        if (managerObject) UnityEngine.Object.Destroy(managerObject);
        actor = null; handObject = secondHandObject = managerObject = null;
        hand = secondHand = null; ragdoll = null; anatomy = null;
        SessionState.SetBool(Running, false);
        bool startedPlay = SessionState.GetBool(StartedPlay, false);
        SessionState.SetBool(StartedPlay, false);
        if (startedPlay) EditorApplication.isPlaying = false;
    }
}
