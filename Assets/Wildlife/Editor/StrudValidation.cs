using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MyAssets.Planet;
using Zandea.Wildlife;
using VRKoombar.WeaponsStaging.Contracts;

[InitializeOnLoad]
public static class StrudValidation
{
    const string Running = "StrudValidation.Running";
    const string Result = "Temp/StrudValidation.result";
    const string AssetRequest = "Temp/StrudAssetValidation.request";
    const string PlayRequest = "Temp/StrudTestPlayValidation.request";
    static readonly StringBuilder report = new StringBuilder();
    static QuadrupedMotor motor;
    static WildlifeBrain brain;
    static GameObject threat;
    static Vector3 start;
    static double startTime;
    static double startRealTime;
    static int stage;
    static float maximumPlantDrift;
    static Vector3[] lastFeet;
    static bool[] lastPlanted;
    static StrudValidation() { EditorApplication.update += Tick; }
    public static void RunBatch()
    {
        try
        {
            StrudBuilder.Build();
            ValidateAssets();
            EditorSceneManager.OpenScene(StrudBuilder.Folder + "/StrudTest.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    [MenuItem("Tools/Zandea/Wildlife/Validate Strud Assets")]
    public static void ValidateAssets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StrudBuilder.Folder + "/Strud.prefab");
        Require(prefab, "Prefab missing");
        var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
        Require(skin && skin.bones.Length >= 27 && skin.sharedMesh.boneWeights.Length == skin.sharedMesh.vertexCount, "Skin/bone weights missing");
        var anim = prefab.GetComponentInChildren<Animator>();
        Require(anim && anim.avatar && anim.avatar.isValid && !anim.avatar.isHuman, "Generic avatar invalid");
        Require(anim.runtimeAnimatorController, "Prefab Animator has no controller: " + anim.name);
        var instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var mesh = new Mesh();
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var animator = instance.GetComponentInChildren<Animator>(true);
            Require(animator && animator.runtimeAnimatorController,
                "Instantiated Animator has no controller: " + string.Join(",", instance.GetComponentsInChildren<Animator>(true)
                    .Select(a => a.name + "=" + (a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "null"))));
            var available = animator.runtimeAnimatorController.animationClips.Where(c => c).ToArray();
            var clip = available.FirstOrDefault(c => c.name == "Walk");
            Require(clip, "Walk clip missing after prefab instantiation; available: " + string.Join(",", available.Select(c => c.name)));
            clip.SampleAnimation(animator.gameObject, 0); renderer.BakeMesh(mesh); var first = mesh.vertices;
            clip.SampleAnimation(animator.gameObject, .4f); renderer.BakeMesh(mesh); var second = mesh.vertices;
            int changed = 0;
            for (int i = 0; i < first.Length; i++) if ((first[i] - second[i]).sqrMagnitude > .0025f) changed++;
            Require(changed > first.Length / 5, "Walk does not visibly deform enough vertices: " + changed);
            UnityEngine.Object.DestroyImmediate(mesh);
            foreach (string n in new[] { "SeatAnchor", "MountLeftPoint", "MountRightPoint", "DismountLeftPoint", "DismountRightPoint", "ReinsAnchor", "CargoAnchor_Left", "CargoAnchor_Right", "CargoAnchor_Back" })
                Require(instance.GetComponentsInChildren<Transform>().Any(t => t.name == n), "Missing " + n);
            File.WriteAllText("Temp/StrudAssetValidation.result", "PASS Generic avatar, 27 bones, weighted skin; " + changed + "/" + first.Length + " vertices move >5cm in walk; all 9 mount anchors");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }
    static void Tick()
    {
        if (File.Exists(PlayRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(PlayRequest);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "StrudTest")
                File.WriteAllText(Result, "FAIL Active scene is not StrudTest");
            else { SessionState.SetBool(Running, true); EditorApplication.isPlaying = true; }
            return;
        }
        if (File.Exists(AssetRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(AssetRequest);
            try { ValidateAssets(); }
            catch (Exception e) { File.WriteAllText("Temp/StrudAssetValidation.result", "FAIL " + e); Debug.LogException(e); }
        }
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!motor)
            {
                motor = UnityEngine.Object.FindFirstObjectByType<QuadrupedMotor>();
                if (!motor) return;
                brain = motor.GetComponent<WildlifeBrain>(); brain.enabled = false;
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                start = motor.transform.position; startTime = Time.time; startRealTime = EditorApplication.timeSinceStartup; stage = 0;
                motor.SetIntent(Vector3.forward, motor.profile.walkSpeed);
                lastFeet = new Vector3[4]; lastPlanted = new bool[4];
                report.Clear(); report.AppendLine("PLAY validation started");
            }
            double elapsed = Time.time - startTime;
            if (elapsed < .5 && EditorApplication.timeSinceStartup - startRealTime > 30)
                throw new Exception("Unity Play mode is not advancing game time while the editor is unfocused");
            var grounding = motor.GetComponent<QuadrupedGrounding>();
            if (stage == 0 && elapsed > 2)
                for (int i = 0; i < 4; i++)
                {
                    var leg = grounding.legs[i];
                    if (leg.planted && lastPlanted[i]) maximumPlantDrift = Mathf.Max(maximumPlantDrift, Vector3.Distance(lastFeet[i], leg.foot.position));
                    lastFeet[i] = leg.foot.position; lastPlanted[i] = leg.planted;
                }
            if (stage == 0 && elapsed > 6)
            {
                float moved = Vector3.Distance(start, motor.transform.position);
                Require(moved > 2, "Walk failed to move: " + moved + " speed=" + motor.Speed +
                    " requested=" + motor.RequestedSpeed + " block=" + motor.LastBlock +
                    " position=" + motor.transform.position);
                report.AppendLine("PASS walk displacement " + moved.ToString("F2") + "m; maximum observed per-editor-tick planted foot drift " + maximumPlantDrift.ToString("F3") + "m");
                Capture("Walk");
                motor.SetIntent(Vector3.forward, motor.profile.trotSpeed); stage++;
            }
            if (stage == 1 && elapsed > 10)
            {
                Require(motor.Speed > 2, "Trot failed to accelerate");
                Require(motor.animator.GetCurrentAnimatorStateInfo(0).IsName("Trot"), "Animator did not enter Trot");
                report.AppendLine("PASS trot speed " + motor.Speed.ToString("F2")); Capture("Trot");
                var origin = new GameObject("Validation origin").AddComponent<FloatingOriginManager>();
                Vector3 position = motor.transform.position; Vector3 plant = grounding.legs[0].plant;
                origin.ShiftOrigin(new Vector3(1000, 0, -1000));
                Vector3 expected = position - new Vector3(1000,0,-1000);
                Require(Vector3.Distance(motor.transform.position, expected) < .001f,
                    "Root origin shift failed: before=" + position + " expected=" + expected +
                    " after=" + motor.transform.position + " body=" + motor.GetComponent<Rigidbody>().position +
                    " shifts=" + origin.ShiftCount);
                Require(Vector3.Distance(grounding.legs[0].plant, plant - new Vector3(1000,0,-1000)) < .001f, "Foot origin shift failed");
                report.AppendLine("PASS real FloatingOriginManager shift preserves root and planted feet");
                motor.SetIntent(Vector3.zero, 0); stage++;
            }
            if (stage == 2 && elapsed > 14)
            {
                Require(motor.Speed < .01f, "Failed to stop");
                report.AppendLine("PASS controlled stop");
                brain.enabled = true;
                threat = new GameObject("Validation threat"); threat.transform.position = motor.transform.position + Vector3.forward * 8;
                brain.SetThreat(threat.transform); stage++;
            }
            if (stage == 3 && elapsed > 16)
            {
                Require(brain.State == WildlifeBrain.Activity.Alert, "AI did not become alert: " + brain.State);
                report.AppendLine("PASS threat alert"); threat.transform.position = motor.transform.position + Vector3.forward * 2; stage++;
            }
            if (stage == 4 && elapsed > 18)
            {
                Require(brain.State == WildlifeBrain.Activity.Flee, "AI did not flee"); report.AppendLine("PASS threat flee");
                var health = motor.GetComponent<CreatureHealth>(); float before = health.Current;
                health.ApplyDamage(new DamagePacket { BaseDamage = 10, PhysicalImpact = PhysicalImpactKind.Blunt, HitPointWorld = motor.transform.position + Vector3.up, HitNormalWorld = Vector3.back, Source = threat });
                Require(Mathf.Abs(health.Current - (before - 10)) < .01f, "Damage routing failed");
                health.ApplyDamage(new DamagePacket { BaseDamage = 1000, PhysicalImpact = PhysicalImpactKind.Blunt, HitPointWorld = motor.transform.position + Vector3.up, HitNormalWorld = Vector3.back });
                Require(motor.Dead && health.GetCombatState() == ShipCombatState.Destroyed && !brain.enabled, "Death failed");
                report.AppendLine("PASS IDamageable hit, health depletion, death, AI disabled"); stage++;
            }
            if (stage == 5 && elapsed > 22)
            {
                Require(motor.animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), "Death animation not active");
                Capture("Death"); report.AppendLine("PASS stable corpse and death animation"); Finish(true, report.ToString());
            }
        }
        catch (Exception e) { Finish(false, report + "\n" + e); }
    }
    static void Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var camera = Camera.main; if (!camera) return;
        camera.transform.position = motor.transform.position + new Vector3(7,4,9); camera.transform.LookAt(motor.transform.position + Vector3.up * 1.8f);
        var target = new RenderTexture(1200,900,24); var image = new Texture2D(1200,900,TextureFormat.RGB24,false);
        var previous = RenderTexture.active; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        image.ReadPixels(new Rect(0,0,1200,900),0,0); image.Apply(); File.WriteAllBytes("Temp/Strud_" + name + ".png", image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Finish(bool success, string message)
    {
        SessionState.SetBool(Running, false); File.WriteAllText(Result, (success ? "PASS\n" : "FAIL\n") + message);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1); else EditorApplication.isPlaying = false;
    }
}
