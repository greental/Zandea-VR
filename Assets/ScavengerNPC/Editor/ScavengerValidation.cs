using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;

namespace ScavengerNPC.Editor
{
    [InitializeOnLoad]
    public static class ScavengerValidation
    {
        // Local, one-shot request allows setup after Unity finishes importing the new scripts.
        const string Request = "Temp/Scavenger-build.request";
        static ScavengerValidation() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            try { ScavengerSetup.Build(); Validate(); }
            catch (Exception e) { File.WriteAllText("Library/Scavenger-build.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/Scavenger NPC/Validate generated prefabs")]
        public static void Validate()
        {
            var restartSign = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ScavengerNPC/Generated/ScavengerRestartSign.prefab");
            Require(restartSign && restartSign.GetComponent<ScavengerRestartSign>() &&
                restartSign.GetComponentInChildren<Collider>(), "Missing shootable restart sign");
            foreach (string name in new[] { "Scavenger_JKR88", "Scavenger_PKO", "Scavenger_JKR88_Ammo" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ScavengerNPC/Generated/" + name + ".prefab");
                if (!prefab && name == "Scavenger_JKR88_Ammo" && !AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab")) continue;
                Require(prefab, "Missing prefab " + name);
                Require(prefab.GetComponent<ScavengerHealth>(), "Missing health");
                Require(prefab.GetComponent<NavMeshAgent>(), "Missing navigation");
                var brain = prefab.GetComponent<ScavengerEnemy>();
                Require(brain && brain.weapon && brain.weapon.muzzle && brain.eyes, "Missing combat references");
                var ragdoll = prefab.GetComponent<ScavengerRagdoll>();
                Require(ragdoll && ragdoll.bones.Length == 13 && ragdoll.bones.All(b => b && b.isKinematic && b.interpolation == RigidbodyInterpolation.None), "Ragdoll configuration");
                Require(brain.weapon.transform.IsChildOf(prefab.GetComponent<ScavengerPose>().rightHand), "Weapon must follow right hand hierarchy");
                if (name == "Scavenger_JKR88_Ammo")
                {
                    var pistol = brain.weapon.pistol;
                    Require(pistol && pistol.muzzle && pistol.batterySocket && pistol.batterySocket.startingCell,
                        "Ammo pistol must retain its muzzle, socket, and starting cell");
                }
                Require(prefab.GetComponentsInChildren<CharacterJoint>().Length == 12, "Joint configuration");
                Require(brain.animator.runtimeAnimatorController.animationClips.Length >= 3, "Locomotion clips");
                var model = brain.animator.transform;
                foreach (var clip in brain.animator.runtimeAnimatorController.animationClips)
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        Require(string.IsNullOrEmpty(binding.path) || model.Find(binding.path), "Unresolved animation path " + binding.path);
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script");
            }
            File.AppendAllText("Library/Scavenger-build.txt", "PASS: prefab references, 13 bodies/12 joints per NPC, animation paths, no missing scripts.\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
