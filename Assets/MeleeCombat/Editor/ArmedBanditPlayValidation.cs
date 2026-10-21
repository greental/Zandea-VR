using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class ArmedBanditPlayValidation
{
    const string Request = "Temp/ArmedBanditPlayValidation.request";
    const string Running = "ArmedBanditPlayValidation.Running";
    const string Output = "TestCaptures/ArmedBanditPass/play-result.txt";
    static List<StarvingBandit> actors;
    static float started, stageStarted;
    static int stage, lastFrame;
    static float[] maxGrip;
    static int[] worstStage;
    static float[] maxReach;
    static float[] maxPosition;
    static List<ScavengerHealth> probes;
    static Camera camera;
    static bool capturedStage;
    static readonly StringBuilder report = new StringBuilder();
    static ArmedBanditPlayValidation() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request); SessionState.SetBool(Running, true);
            EditorApplication.isPlaying = true; return;
        }
        if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (actors == null) { Prepare(); return; }
            float elapsed = Time.time - stageStarted;
            if (Time.time - started > 90f) throw new Exception("Live validation timed out");
            if (stage >= 9) { ContactChecks(elapsed); return; }
            if (!capturedStage && (stage == 0 && elapsed > 1.2f ||
                (stage == 1 || stage == 5) && elapsed > .55f))
            {
                CaptureStage(); capturedStage = true;
            }
            for (int i = 0; i < actors.Count; i++)
            {
                var brain = actors[i];
                var grip = brain.GetComponentInChildren<MocapStaffPreview>();
                if (stage == 0) brain.state = StarvingBandit.State.Approach;
                else if (stage <= 8 && brain.state != StarvingBandit.State.Retreat)
                    typeof(StarvingBandit).GetMethod("TickAttack", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(brain, null);
                if (elapsed > .35f)
                {
                    float error = grip.primaryHandIsLeft ? grip.LeftGripError : grip.RightGripError;
                    if (grip.secondaryHandGrips || grip.secondaryVisualWeapon) error = Mathf.Max(error, grip.LeftGripError, grip.RightGripError);
                    if (error > maxGrip[i]) { maxGrip[i] = error; worstStage[i] = stage; }
                    maxReach[i] = Mathf.Max(maxReach[i], grip.ReachCorrection);
                    maxPosition[i] = Mathf.Max(maxPosition[i], brain.staff.PositionError);
                    if (!brain.staff || brain.staff.State != PhysicalMeleeWeapon.HoldState.HeldByNpc)
                        throw new Exception(brain.name + " lost held weapon");
                    if (grip.secondaryPhysicalWeapon && grip.secondaryPhysicalWeapon.State != PhysicalMeleeWeapon.HoldState.HeldByNpc)
                        throw new Exception(brain.name + " saber did not equip");
                }
            }
            float duration = stage == 0 ? 2f : actors.Max(b => { var a=b.attacks[stage-1]; return a.windupSeconds+a.strikeSeconds+a.followSeconds+a.recoverySeconds; }) + .2f;
            if (elapsed < duration) return;
            if (stage == 8)
            {
                for (int i = 0; i < actors.Count; i++) report.AppendLine($"{(maxGrip[i] <= .025f ? "PASS" : "FAIL")} live {actors[i].name}: maxGrip={maxGrip[i]:F4}m stage={worstStage[i]} maxReach={maxReach[i]:F4}m physicalLag={maxPosition[i]:F4}m");
                PrepareContacts(); return;
            }
            stage++; stageStarted = Time.time; capturedStage = false;
            foreach (var brain in actors)
            {
                brain.state = StarvingBandit.State.Idle;
                brain.GetComponentInChildren<BanditMocapNpcDriver>().SendMessage("Update");
                typeof(StarvingBandit).GetMethod("StartAttack", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(brain, new object[] { stage - 1 });
            }
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); Finish(); }
    }
    static void Prepare()
    {
        // Only transient Play-mode objects are deactivated; exiting restores the user's scene.
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) root.SetActive(false);
        actors = new List<StarvingBandit>(); report.Clear();
        var paths = Directory.GetFiles("Assets/MeleeCombat/MocapTest/GeneratedVariants", "StarvingBandit_*.prefab").OrderBy(p=>p).ToArray();
        foreach (var path in paths)
        {
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), new Vector3(actors.Count * 5, 20, 3000), Quaternion.identity);
            var brain = root.GetComponent<StarvingBandit>();
            brain.enabled = false; brain.state = StarvingBandit.State.Approach;
            if (brain.agent) brain.agent.enabled = false;
            var martial = root.GetComponent<NpcMartialArts>(); if (martial) martial.enabled = false;
            var equip = root.GetComponent<NpcGeneratedWeapon>(); if (equip) equip.acquireNearbyWeapons = false;
            var animator = root.GetComponentInChildren<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            actors.Add(brain);
        }
        maxGrip = new float[actors.Count]; worstStage = new int[actors.Count];
        maxReach = new float[actors.Count]; maxPosition = new float[actors.Count];
        started = stageStarted = Time.time; stage = 0;
        capturedStage = false;
        var cameraObject = new GameObject("Armed validation camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.18f, .19f, .22f);
        var lighting = new GameObject("Armed validation light").AddComponent<Light>();
        lighting.type = LightType.Directional; lighting.intensity = 1.8f;
        lighting.transform.rotation = Quaternion.Euler(42, -32, 0);
    }
    static void Finish()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, report.ToString());
        SessionState.SetBool(Running, false);
        if (actors != null) foreach (var actor in actors) if (actor) UnityEngine.Object.Destroy(actor.gameObject);
        if (probes != null) foreach (var probe in probes) if (probe) UnityEngine.Object.Destroy(probe.gameObject);
        if (camera) UnityEngine.Object.Destroy(camera.gameObject);
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light && light.name == "Armed validation light") UnityEngine.Object.Destroy(light.gameObject);
        probes = null;
        actors = null; EditorApplication.isPlaying = false;
    }
    static void PrepareContacts()
    {
        probes = new List<ScavengerHealth>();
        foreach (var actor in actors)
        {
            actor.GetComponentInChildren<BanditMocapNpcDriver>().enabled = false;
            actor.GetComponentInChildren<Animator>().speed = 0;
            typeof(StarvingBandit).GetMethod("StartAttack", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(actor, new object[] { 0 });
            actor.state = StarvingBandit.State.Strike;
            var probe = new GameObject("Contact probe");
            probe.transform.position = actor.transform.position + Vector3.up - actor.transform.forward;
            var health = probe.AddComponent<ScavengerHealth>();
            health.knockoutTrauma = 1000; health.baseHeadKnockoutChance = 0;
            probe.AddComponent<SphereCollider>().radius = .06f;
            var body = probe.AddComponent<Rigidbody>(); body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            actor.target = probe.transform; probes.Add(health);
        }
        stage = 9; stageStarted = Time.time;
    }
    static void ContactChecks(float elapsed)
    {
        if (elapsed < 1.2f) return;
        if (stage == 9)
        {
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i]; var probe = probes[i];
                report.AppendLine($"{(probe.Current == probe.maxHealth ? "PASS" : "FAIL")} near miss {actor.name}: health={probe.Current}");
                var grip = actor.GetComponentInChildren<MocapStaffPreview>();
                var weapon = grip.secondaryPhysicalWeapon ? grip.secondaryPhysicalWeapon : actor.staff;
                var shapes = weapon.GetComponentsInChildren<MeshCollider>().Where(c=>c.enabled).ToArray();
                var bounds = shapes[0].bounds;
                foreach (var shape in shapes) bounds.Encapsulate(shape.bounds);
                // Pick an actual exposed collider surface, then drive a small target into it.
                var outside = bounds.center + Vector3.right * (bounds.extents.x + 1f);
                var surface = shapes.Select(c=>c.ClosestPoint(outside)).OrderBy(p=>(p-outside).sqrMagnitude).First();
                var body = probe.GetComponent<Rigidbody>();
                body.position = surface + Vector3.right * .35f;
                body.linearVelocity = Vector3.left * 3f;
            }
            stage = 10; stageStarted = Time.time; return;
        }
        for (int i = 0; i < actors.Count; i++)
        {
            var score = actors[i].GetComponent<BanditDuelStrikeDamage>();
            bool pass = probes[i].Current < probes[i].maxHealth && score.ContactHits == 1;
            report.AppendLine($"{(pass ? "PASS" : "FAIL")} real contact {actors[i].name}: health={probes[i].Current}, hits={score.ContactHits}, point={score.LastContactPoint}");
        }
        Finish();
    }
    static void CaptureStage()
    {
        var folder = Path.GetDirectoryName(Output);
        Directory.CreateDirectory(folder);
        var texture = RenderTexture.GetTemporary(640, 640, 24);
        var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            foreach (var actor in actors)
            {
                camera.transform.position = actor.transform.position + new Vector3(2.7f, 1.8f, 3.5f);
                camera.transform.LookAt(actor.transform.position + Vector3.up);
                camera.targetTexture = texture;
                camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); image.Apply();
                File.WriteAllBytes(folder + "/live_" + actor.name + "_" + stage + ".png", image.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.Destroy(image);
        }
    }
}
