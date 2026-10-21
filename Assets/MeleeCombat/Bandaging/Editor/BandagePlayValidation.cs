#if UNITY_EDITOR
using System;
using System.IO;
using ScavengerNPC;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.Bandaging.Editor
{
    [InitializeOnLoad]
    public static class BandagePlayValidation
    {
        const string Request = "Temp/BandagePlayValidation.request";
        const string Result = "Temp/BandagePlayValidation.result";
        const string Running = "BandagePlayValidation.Running";
        const string StartedPlay = "BandagePlayValidation.StartedPlay";
        static GameObject actor, rollObject, handObject, managerObject;
        static PlayerWounds wounds;
        static ScavengerPlayerHealth health;
        static WoundWrapZone zone;
        static BandageRoll roll;
        static XRGrabInteractable grab;
        static XRDirectInteractor hand;
        static int phase, frame, steps;
        static float healthBeforeWrap;
        static float recoveryCheckAt;
        static BandagePlayValidation() => EditorApplication.update += Tick;

        [MenuItem("Kenshi VR/Bandaging/Validate damage grab trigger and treatment in Play Mode")]
        public static void Run() { File.WriteAllText(Request, "run"); Tick(); }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
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
                    actor = new GameObject("Bandage play validation actor");
                    actor.SetActive(false);
                    actor.transform.position = new Vector3(500f, 20f, 500f);
                    health = actor.AddComponent<ScavengerPlayerHealth>();
                    wounds = actor.AddComponent<PlayerWounds>();
                    wounds.spawnEditorTestWound = false;
                    wounds.recoveryDelayAfterHit = 1000f;
                    var bone = new GameObject("Imported-scale forearm").transform;
                    bone.SetParent(actor.transform, false);
                    bone.localScale = Vector3.one * .01f;
                    zone = bone.gameObject.AddComponent<WoundWrapZone>();
                    zone.bone = bone; zone.radius = .052f; zone.length = .25f;
                    wounds.zones.Add(zone);
                    actor.SetActive(true);
                    // Includes both physical slash contact and the coarse contact used by staff combat.
                    health.ApplyDamage(new DamagePacket { BaseDamage = 12f, DamageType = DamageType.Kinetic,
                        PhysicalImpact = PhysicalImpactKind.Sharp, HitPointWorld = zone.ToWorld(Vector3.right * .052f) });
                    health.ApplyDamage(new DamagePacket { BaseDamage = 9f, DamageType = DamageType.Kinetic,
                        PhysicalImpact = PhysicalImpactKind.Blunt, HitPointWorld = zone.ToWorld(Vector3.up * .5f) });
                    Require(wounds.Wounds.Count == 2, "sharp and root-capsule blunt hits produce treatable injuries");
                    Require(wounds.Wounds[0].bleedingPerSecond > 0f && wounds.Wounds[1].bleedingPerSecond == 0f,
                        "bruises do not create cutting blood loss");
                    float size = wounds.Wounds[0].visual.GetComponent<Renderer>().bounds.size.x;
                    Require(size > .025f && size < .05f, "visible world-sized wound on 0.01-scale avatar bone");
                    managerObject = new GameObject("Bandage test interaction manager");
                    var manager = managerObject.AddComponent<XRInteractionManager>();
                    handObject = new GameObject("Bandage test hand");
                    handObject.transform.SetParent(actor.transform, false);
                    handObject.AddComponent<SphereCollider>().isTrigger = true;
                    hand = handObject.AddComponent<XRDirectInteractor>();
                    hand.interactionManager = manager;
                    rollObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MyAssets/prefabs/medical/BandageRoll.prefab"));
                    roll = rollObject.GetComponent<BandageRoll>();
                    grab = rollObject.GetComponent<XRGrabInteractable>();
                    grab.interactionManager = manager;
                    Move(0f);
                    phase = 1; frame = Time.frameCount;
                    return;
                }
                if (Time.frameCount <= frame + 2) return;
                if (phase == 1)
                {
                    hand.StartManualInteraction((IXRSelectInteractable)grab);
                    grab.trackPosition = grab.trackRotation = false;
                    phase = 10; frame = Time.frameCount;
                    return;
                }
                if (phase == 10)
                {
                    Require(grab.isSelected, "supplied prefab can be grabbed");
                    Require(roll.appliedWrapLifetime > 0f && roll.fittedRadiusScale < 1f,
                        "completed wraps have a finite lifetime and fitted visual radius");
                    Move(0f);
                    // Deliberately activate immediately, before the first candidate-query LateUpdate.
                    grab.activated.Invoke(new ActivateEventArgs { interactorObject = hand, interactableObject = grab });
                    healthBeforeWrap = health.Current;
                    phase = 2; frame = Time.frameCount;
                    return;
                }
                if (phase == 2)
                {
                    Require(roll.State == BandageRoll.WrapState.Wrapping,
                        $"same-frame grip + trigger anchors cloth (state={roll.State}, selected={grab.isSelected}, snap={roll.SnapEligible}, tipDistance={Vector3.Distance(roll.looseTip.position, wounds.Wounds[0].WorldPosition):F3})");
                    Require(health.Current <= healthBeforeWrap, "attachment does not instantly heal");
                    hand.EndManualInteraction();
                    phase = 3; frame = Time.frameCount;
                    return;
                }
                if (phase == 3)
                {
                    Require(roll && roll.State == BandageRoll.WrapState.Idle && !wounds.Wounds[0].treated,
                        "dropping cancels without consuming or treating");
                    Move(0f);
                    hand.StartManualInteraction((IXRSelectInteractable)grab);
                    grab.trackPosition = grab.trackRotation = false;
                    phase = 30; frame = Time.frameCount;
                    return;
                }
                if (phase == 30)
                {
                    Require(grab.isSelected, "roll can be grabbed again after cancellation");
                    Move(0f);
                    grab.activated.Invoke(new ActivateEventArgs { interactorObject = hand, interactableObject = grab });
                    phase = 4; frame = Time.frameCount; steps = 0;
                    return;
                }
                if (phase == 4)
                {
                    if (++steps <= 20)
                    {
                        Move(steps % 2 == 0 ? 0f : 15f);
                        frame = Time.frameCount - 2;
                        return;
                    }
                    Require(roll && roll.CompletedRotations < .1f, "oscillation cannot complete wraps");
                    phase = 5; steps = 0; frame = Time.frameCount;
                    return;
                }
                if (phase == 5)
                {
                    if (++steps <= 54)
                    {
                        Move(steps * 20f);
                        frame = Time.frameCount - 2;
                        return;
                    }
                    phase = 6; frame = Time.frameCount;
                    return;
                }
                if (phase == 6)
                {
                    Require(wounds.Wounds[0].treated && wounds.Wounds[0].bandage, "three real spatial turns treat the wound");
                    Require(wounds.Wounds[0].bleedingPerSecond == 0f, "bandage stops bleeding at the treated wound");
                    Require(wounds.Wounds[0].bandage.parent == zone.bone, "completed wrap remains on the bone");
                    var wrapMesh = wounds.Wounds[0].bandage.GetComponent<MeshFilter>().sharedMesh;
                    Require(wrapMesh && wrapMesh.vertexCount > 100, "completed wrap keeps its fabric mesh");
                    foreach (var vertex in wrapMesh.vertices)
                    {
                        var point = zone.ToFrame(wounds.Wounds[0].bandage.TransformPoint(vertex));
                        float radius = new Vector2(point.x, point.z).magnitude;
                        Require(radius < zone.radius * .9f + zone.surfaceOffset + .003f,
                            "applied fabric fits inside the old oversize wrap radius");
                    }
                    Require(!rollObject, "roll consumed once after completion");
                    Require(!hand.hasSelection && !hand.isPerformingManualInteraction, "consumption clears the manual selection latch");
                    rollObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MyAssets/prefabs/medical/BandageRoll.prefab"));
                    var nextGrab = rollObject.GetComponent<XRGrabInteractable>();
                    nextGrab.interactionManager = hand.interactionManager;
                    hand.StartManualInteraction((IXRSelectInteractable)nextGrab);
                    Require(nextGrab.isSelected, "same hand can grab another roll immediately after treatment");
                    hand.EndManualInteraction();
                    Require(health.Current <= healthBeforeWrap, "no immediate health refill");
                    wounds.recoveryDelayAfterHit = 0f;
                    healthBeforeWrap = health.Current;
                    recoveryCheckAt = Time.time + 1f;
                    phase = 7; frame = Time.frameCount;
                    return;
                }
                if (Time.time < recoveryCheckAt) return;
                Require(health.Current > healthBeforeWrap && health.Current < health.maxHealth, "bandaged damage recovers gradually");
                File.WriteAllText(Result, "PASS: sharp/blunt damage, visible wound at imported bone scale, prefab grab, same-frame trigger attachment, drop/retry, oscillation rejection, three-turn treatment, fitted temporary wrap, bone attachment, and consumption.");
                Finish();
            }
            catch (Exception error) { File.WriteAllText(Result, "FAIL\n" + error); Debug.LogException(error); Finish(); }
        }
        static void Move(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float radius = phase == 5 ? .25f : .10f;
            Vector3 point = zone.ToWorld(new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius));
            handObject.transform.position = point;
            rollObject.transform.position += point - roll.looseTip.position;
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Finish()
        {
            if (rollObject) UnityEngine.Object.Destroy(rollObject);
            if (actor) UnityEngine.Object.Destroy(actor);
            if (managerObject) UnityEngine.Object.Destroy(managerObject);
            SessionState.SetBool(Running, false);
            if (SessionState.GetBool(StartedPlay, false)) EditorApplication.isPlaying = false;
            SessionState.SetBool(StartedPlay, false);
        }
    }
}
#endif
