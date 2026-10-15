using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;

namespace ScavengerNPC.Editor
{
    [InitializeOnLoad]
    public static class ScavengerPlayValidation
    {
        const string Key = "Scavenger.BatchValidation";
        static ScavengerPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                {
                    SessionState.SetBool(Key, false);
                    new GameObject("Scavenger validation").AddComponent<ScavengerValidationRunner>();
                }
            };
        }
        // For an isolated batch project only: exits the editor when complete.
        public static void RunBatch()
        {
            ScavengerValidation.Validate();
            EditorSceneManager.OpenScene("Assets/ScavengerNPC/Generated/ScavengerDemo.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
    }
    public sealed class ScavengerValidationRunner : MonoBehaviour
    {
        string report = "";
        void Check(bool condition, string label)
        {
            report += (condition ? "PASS " : "FAIL ") + label + "\n";
            File.WriteAllText("Library/Scavenger-play.txt", report);
            Debug.Log((condition ? "PASS " : "FAIL ") + label);
            if (!condition) { Debug.LogError(label); EditorApplication.Exit(1); throw new InvalidOperationException(label); }
        }
        IEnumerator Start()
        {
            var enemy = FindFirstObjectByType<ScavengerEnemy>();
            var player = FindFirstObjectByType<ScavengerPlayerHealth>();
            var health = enemy.GetComponent<ScavengerHealth>();
            var cover = FindObjectsByType<ScavengerCover>(FindObjectsSortMode.None).First(c => c.name.Contains("protected side"));
            var pose = enemy.GetComponent<ScavengerPose>();
            yield return null;
            Check(enemy.weapon.transform.IsChildOf(pose.rightHand), "weapon follows the animated hand hierarchy");
            Check(enemy.GetComponent<ScavengerRagdoll>().bones.All(b => b.interpolation == RigidbodyInterpolation.None), "physics interpolation leaves animated bones alone");
            Check(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, "spawn on baked NavMesh");
            Check(health.Current == 100 && !health.IsDead, "health initialized");
            Check(!enemy.ClearLine(cover.transform.position + Vector3.up * 1.4f, player.transform.position), "wall blocks sight from cover");
            Check(enemy.ClearLine(cover.peek.position + Vector3.up * 1.4f, player.transform.position), "peek point sees target");
            bool sought = false, hid = false, peeked = false, crouched = false;
            var sides = new HashSet<int>();
            float end = Time.time + 25;
            while (Time.time < end && (enemy.weapon.ShotsFired == 0 || sides.Count < 2))
            {
                sought |= enemy.CurrentAction == ScavengerEnemy.Action.SeekingCover;
                hid |= enemy.CurrentAction == ScavengerEnemy.Action.Hiding;
                peeked |= enemy.CurrentAction == ScavengerEnemy.Action.Peeking;
                crouched |= enemy.CurrentAction == ScavengerEnemy.Action.Hiding && pose.CrouchAmount > .5f;
                if (enemy.CurrentAction == ScavengerEnemy.Action.Peeking) sides.Add(enemy.CurrentPeekIndex);
                yield return null;
            }
            Check(sought && hid && peeked, "sees player, runs to cover, hides, peeks");
            Check(crouched, "crouches while hiding");
            Check(sides.Contains(0) && sides.Contains(1), "alternates between the two side peeks");
            Check(enemy.weapon.ShotsFired > 0, "aimed burst fires from peek");
            var skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh(); skin.BakeMesh(baked);
            Check(baked.bounds.size.x < 2f && baked.bounds.size.y < 2.5f && baked.bounds.size.z < 2f, "animated body stays connected while aiming");
            Destroy(baked);
            yield return new WaitForSeconds(2);
            Check(player.Current < player.maxHealth, "NPC shots damage player collider");
            var halfCover = FindObjectsByType<ScavengerCover>(FindObjectsSortMode.None).First(c => c.name.Contains("Half cover"));
            var overNpc = Instantiate(prefabForOver(), new Vector3(-8, 0, 5), Quaternion.Euler(0, 180, 0)).GetComponent<ScavengerEnemy>();
            overNpc.target = player.transform; overNpc.targetRoot = player.transform;
            yield return null;
            overNpc.Alert(player.transform);
            bool overCrouched = false, overPeeked = false;
            float overEnd = Time.time + 15;
            while (Time.time < overEnd && overNpc.weapon.ShotsFired == 0)
            {
                overCrouched |= overNpc.CurrentAction == ScavengerEnemy.Action.Hiding && overNpc.GetComponent<ScavengerPose>().CrouchAmount > .5f;
                overPeeked |= overNpc.CurrentAction == ScavengerEnemy.Action.Peeking &&
                    Vector3.Distance(overNpc.transform.position, halfCover.transform.position) < .6f;
                yield return null;
            }
            Check(overCrouched && overPeeked && overNpc.weapon.ShotsFired > 0, "half cover crouch rises and shoots in place");
            enemy.Alert(player.transform);
            var packet = new DamagePacket { BaseDamage = 25, Source = player.gameObject, HitPointWorld = enemy.eyes.position, HitNormalWorld = Vector3.back };
            health.ApplyDamage(packet);
            Check(health.Current == 75 && !health.IsDead, "nonlethal hit deducts health");
            yield return new WaitForSeconds(.1f);
            Check(pose.HitReactionWeight > .05f, "nonlethal hit animates upper-body flinch");
            yield return new WaitForSeconds(pose.hitDuration + .05f);
            Check(pose.HitReactionWeight < .01f, "hit flinch settles back into locomotion");
            packet.BaseDamage = 100; health.ApplyDamage(packet);
            var ragdoll = enemy.GetComponent<ScavengerRagdoll>();
            Check(health.IsDead && enemy.CurrentAction == ScavengerEnemy.Action.Dead, "lethal hit transitions to dead");
            Check(!enemy.GetComponent<NavMeshAgent>().enabled && !enemy.animator.enabled && ragdoll.bones.All(b => !b.isKinematic), "death enables physics and disables animation/navigation");
            Check(!cover.Occupant, "death releases cover");
            int shots = enemy.weapon.ShotsFired;
            health.ApplyDamage(packet);
            yield return new WaitForSeconds(1);
            Check(enemy.weapon.ShotsFired == shots && health.Current == 0, "dead NPC cannot fire or die twice");
            Check(ragdoll.bones.All(b => !float.IsNaN(b.position.x) && b.position.y > -2), "ragdoll remains finite and above floor");
            Check(ragdoll.GetComponentsInChildren<CharacterJoint>().All(j =>
                Vector3.Distance(j.transform.TransformPoint(j.anchor), j.connectedBody.transform.TransformPoint(j.connectedAnchor)) < .35f),
                "ragdoll joints stay connected after impact");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ScavengerNPC/Generated/Scavenger_PKO.prefab");
            var idleNpc = Instantiate(prefab, new Vector3(8, 0, 6), Quaternion.identity).GetComponent<ScavengerEnemy>();
            var farTarget = new GameObject("Out of sight target").transform; farTarget.position = new Vector3(1000, 1, 1000); idleNpc.target = farTarget;
            var waypoint = new GameObject("Patrol waypoint").transform; waypoint.position = new Vector3(8, 0, 2);
            idleNpc.patrolPoints = new[] { waypoint }; idleNpc.idleAction = ScavengerEnemy.Action.Patrol;
            yield return new WaitForSeconds(3);
            Check(idleNpc.CurrentAction == ScavengerEnemy.Action.Patrol && idleNpc.transform.position.z < 4, "unalerted patrol follows waypoint");
            idleNpc.idleAction = ScavengerEnemy.Action.Standing;
            yield return new WaitForSeconds(.3f);
            Check(idleNpc.GetComponent<NavMeshAgent>().isStopped && !idleNpc.IsCombat, "standing stops navigation without distant-target aggro");
            idleNpc.idleAction = ScavengerEnemy.Action.Sitting;
            yield return new WaitForSeconds(.3f);
            var idlePose = idleNpc.GetComponent<ScavengerPose>();
            Check(idlePose.hips.position.y - idleNpc.transform.position.y < .8f, "sitting lowers pelvis");
            idleNpc.idleAction = ScavengerEnemy.Action.Casual;
            yield return new WaitForSeconds(7);
            Check(idleNpc.CurrentAction == ScavengerEnemy.Action.Casual && !idleNpc.IsCombat, "casual idle remains unalerted");
            EditorApplication.Exit(0);
        }
        static GameObject prefabForOver() => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ScavengerNPC/Generated/Scavenger_PKO.prefab");
    }
}
