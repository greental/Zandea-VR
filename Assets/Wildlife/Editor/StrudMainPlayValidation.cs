using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Zandea.Wildlife;
using VRKoombar.WeaponsStaging.Contracts;

// Runs in the already-open Main scene. All objects and movement exist only in Play mode.
[InitializeOnLoad]
public static class StrudMainPlayValidation
{
    const string Request = "Temp/StrudMainPlayValidation.request";
    const string Result = "Temp/StrudMainPlayValidation.result";
    const string Running = "StrudMainPlayValidation.Running";
    static readonly StringBuilder report = new StringBuilder();
    static QuadrupedMotor motor;
    static WildlifeBrain brain;
    static CreatureHealth health;
    static GameObject threat;
    static Vector3 start;
    static double began;
    static int stage;
    static StrudMainPlayValidation() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
            { File.WriteAllText(Result, "FAIL Active scene is not Main"); File.Delete(Request); return; }
            File.Delete(Request); SessionState.SetBool(Running, true); EditorApplication.isPlaying = true; return;
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (!motor)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Wildlife/Strud/Strud.prefab");
                if (!prefab) throw new Exception("Strud prefab missing");
                var camera = Camera.main;
                if (!camera) throw new Exception("Main scene has no active player camera");
                var terrain = Terrain.activeTerrain;
                if (!terrain || !terrain.GetComponent<TerrainCollider>()) throw new Exception("Main terrain collider is not active");
                Vector3 point = camera.transform.position + Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized * 20;
                Vector3 origin = point + Vector3.up * 100;
                if (!Physics.Raycast(origin, Vector3.down, out var ground, 300, ~0, QueryTriggerInteraction.Ignore) || !(ground.collider is TerrainCollider))
                    throw new Exception("No terrain tile near player for creature spawn");
                var animal = UnityEngine.Object.Instantiate(prefab, ground.point, Quaternion.LookRotation(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized));
                animal.name = "Strud play validation";
                motor = animal.GetComponent<QuadrupedMotor>(); brain = animal.GetComponent<WildlifeBrain>(); health = animal.GetComponent<CreatureHealth>();
                if (!motor || !brain || !health) throw new Exception("Prefab is missing runtime components");
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                brain.enabled = false; start = animal.transform.position; began = Time.time; stage = 0;
                motor.SetIntent(animal.transform.forward, motor.profile.walkSpeed); report.Clear(); report.AppendLine("Main terrain Play mode test");
            }
            double elapsed = Time.time - began;
            if (stage == 0 && elapsed > 6)
            {
                float moved = Vector3.Distance(start, motor.transform.position);
                if (moved < 2) throw new Exception("Strud did not walk on Main terrain: " + moved);
                report.AppendLine("PASS walk on active terrain: " + moved.ToString("F2") + "m");
                motor.SetIntent(motor.transform.forward, motor.profile.trotSpeed); stage++;
            }
            if (stage == 1 && elapsed > 10)
            {
                if (motor.Speed < 2 || !motor.animator.GetCurrentAnimatorStateInfo(0).IsName("Trot")) throw new Exception("Trot failed");
                report.AppendLine("PASS trot state and speed"); motor.SetIntent(Vector3.zero,0); stage++;
            }
            if (stage == 2 && elapsed > 14)
            {
                if (motor.Speed > .05f) throw new Exception("Stop failed");
                report.AppendLine("PASS stop"); threat = new GameObject("Strud threat"); threat.transform.position = motor.transform.position + Vector3.forward * 8;
                brain.SetThreat(threat.transform); brain.enabled = true; stage++;
            }
            if (stage == 3 && elapsed > 16)
            {
                if (brain.State != WildlifeBrain.Activity.Alert) throw new Exception("Alert failed: " + brain.State);
                report.AppendLine("PASS alert"); threat.transform.position = motor.transform.position + Vector3.forward * 2; stage++;
            }
            if (stage == 4 && elapsed > 18)
            {
                if (brain.State != WildlifeBrain.Activity.Flee) throw new Exception("Flee failed");
                report.AppendLine("PASS flee"); float current = health.Current;
                health.ApplyDamage(new DamagePacket { BaseDamage = 20, DamageType = DamageType.Kinetic, PhysicalImpact = PhysicalImpactKind.Blunt,
                    HitPointWorld = motor.transform.position + Vector3.up, HitNormalWorld = Vector3.back });
                if (health.Current >= current) throw new Exception("Damage failed");
                health.ApplyDamage(new DamagePacket { BaseDamage = 10000, DamageType = DamageType.Kinetic, PhysicalImpact = PhysicalImpactKind.Blunt,
                    HitPointWorld = motor.transform.position + Vector3.up, HitNormalWorld = Vector3.back });
                if (!motor.Dead || health.GetCombatState() != ShipCombatState.Destroyed) throw new Exception("Death failed");
                report.AppendLine("PASS damage, blood, death and AI stop"); stage++;
            }
            if (stage == 5 && elapsed > 21)
            {
                if (!motor.animator.GetCurrentAnimatorStateInfo(0).IsName("Death")) throw new Exception("Death clip failed");
                report.AppendLine("PASS death clip in Main terrain"); Finish(true, report.ToString());
            }
        }
        catch (Exception e) { Finish(false, report + "\n" + e); }
    }
    static void Finish(bool success, string message)
    {
        File.WriteAllText(Result, (success ? "PASS\n" : "FAIL\n") + message);
        SessionState.SetBool(Running, false);
        EditorApplication.isPlaying = false;
    }
}
