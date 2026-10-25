using System;
using System.IO;
using System.Linq;
using System.Text;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SeveredLimbPlayValidation
{
    const string Request = "Temp/SeveredLimbPlayValidation.v5.request";
    const string Result = "Temp/SeveredLimbPlayValidation.result";
    const string Running = "SeveredLimbPlayValidation.Running";
    const string StartedPlay = "SeveredLimbPlayValidation.StartedPlay";
    const string Prefab = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
    static bool executed;
    static GameObject source;
    static SeveredBodyPartIdentity detachedHead;
    static readonly StringBuilder report = new StringBuilder();

    static SeveredLimbPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Zandea/Melee Combat/Validate severed limbs in Play mode")]
    public static void Run()
    {
        File.WriteAllText(Request, "run");
        Tick();
    }

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
            executed = false;
            report.Clear();
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (!executed)
            {
                executed = true;
                Validate();
                return;
            }
            if (!detachedHead || !detachedHead.protectedFromCleanup ||
                !HasHeadFlesh(detachedHead.gameObject, out var bounds, out var triangles))
                throw new Exception("Detached head flesh disappeared after its source NPC was destroyed");
            report.AppendLine($"PASS head persists after source destruction: {triangles} triangles, bounds {bounds.size}");
            File.WriteAllText(Result, "PASS severed limb Play validation\n" + report);
            Finish();
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL severed limb Play validation\n" + report + error);
            Debug.LogException(error);
            Finish();
        }
    }

    static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (!prefab) throw new Exception("Missing bandit prefab: " + Prefab);
        source = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 2, 0), Quaternion.identity);
        source.name = "Severed limb validation NPC";
        var anatomy = source.GetComponent<DismemberableCharacter>();
        if (!anatomy) throw new Exception("Bandit has no DismemberableCharacter");
        anatomy.forceSeverOnValidJointStrike = true;
        anatomy.minimumEdgeAlignment = 0;
        var head = anatomy.GetJointBone(SeverPoint.Head);
        if (!head || !anatomy.TrySever(head.position, Vector3.right * 12, Vector3.right, 30, true, Vector3.zero))
            throw new Exception("Head strike did not produce a severed part");
        detachedHead = UnityEngine.Object.FindObjectsByType<SeveredBodyPartIdentity>(FindObjectsSortMode.None)
            .FirstOrDefault(part => part.npcId == anatomy.npcId && part.bodyPart == SeverPoint.Head);
        if (!detachedHead || !HasHeadFlesh(detachedHead.gameObject, out var bounds, out var triangles))
            throw new Exception("Detached head has no visible flesh geometry. " +
                (detachedHead ? DescribeMeshes(detachedHead.gameObject) : "No detached head identity"));
        report.AppendLine($"PASS head flesh: {triangles} triangles, bounds {bounds.size}");
        var headFlesh = detachedHead.GetComponentsInChildren<MeshFilter>(true)
            .First(filter => filter.name == "Object_9");
        CaptureFlesh(detachedHead, headFlesh, bounds, "SeveredHeadValidation.png", "head");
        UnityEngine.Object.Destroy(source);
        ValidateLimb(prefab, SeverPoint.LeftShoulder, "Object_11");
        ValidateLimb(prefab, SeverPoint.RightShoulder, "Object_11");
        ValidateLimb(prefab, SeverPoint.LeftElbow, "Object_11");
        ValidateLimb(prefab, SeverPoint.RightElbow, "Object_11");
        ValidateLimb(prefab, SeverPoint.LeftWrist, "Object_11");
        ValidateLimb(prefab, SeverPoint.RightWrist, "Object_11");
        ValidateLimb(prefab, SeverPoint.LeftHip, "Object_12");
        ValidateLimb(prefab, SeverPoint.RightHip, "Object_12");
        ValidateLimb(prefab, SeverPoint.LeftKnee, "Object_12");
        ValidateLimb(prefab, SeverPoint.RightKnee, "Object_12");
    }

    static void ValidateLimb(GameObject prefab, SeverPoint point, string fleshName)
    {
        var actor = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 2, 0), Quaternion.identity);
        try
        {
            var anatomy = actor.GetComponent<DismemberableCharacter>();
            anatomy.forceSeverOnValidJointStrike = true;
            anatomy.minimumEdgeAlignment = 0;
            var bone = anatomy.GetJointBone(point);
            if (!bone || !anatomy.TrySever(bone.position, Vector3.right * 12, Vector3.right, 30, true, Vector3.zero))
                throw new Exception(point + " strike did not produce a severed part");
            var part = UnityEngine.Object.FindObjectsByType<SeveredBodyPartIdentity>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.npcId == anatomy.npcId && candidate.bodyPart == point);
            var flesh = part ? part.GetComponentsInChildren<MeshFilter>(true)
                .FirstOrDefault(filter => filter.name == fleshName) : null;
            var renderer = flesh ? flesh.GetComponent<MeshRenderer>() : null;
            int count = flesh && flesh.sharedMesh ? flesh.sharedMesh.triangles.Length / 3 : 0;
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                count < 100 || renderer.bounds.size.magnitude < .08f)
                throw new Exception($"{point} has no visible flesh: " + (part ? DescribeMeshes(part.gameObject) : "no part"));
            report.AppendLine($"PASS {point} flesh: {count} triangles, bounds {renderer.bounds.size}");
            if (point == SeverPoint.LeftShoulder || point == SeverPoint.LeftHip)
                CaptureFlesh(part, flesh, renderer.bounds, "Severed" + point + "Validation.png", point.ToString());
            UnityEngine.Object.Destroy(part.gameObject);
        }
        finally { UnityEngine.Object.Destroy(actor); }
    }

    static bool HasHeadFlesh(GameObject part, out Bounds bounds, out int triangles)
    {
        bounds = default;
        triangles = 0;
        foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh || filter.name != "Object_9") continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            triangles += filter.sharedMesh.triangles.Length / 3;
            bounds = renderer.bounds;
        }
        return triangles > 1000 && bounds.size.x > .08f && bounds.size.y > .1f &&
            bounds.size.x < .6f && bounds.size.y < .6f;
    }

    static string DescribeMeshes(GameObject part)
    {
        var rows = part.GetComponentsInChildren<MeshFilter>(true).Select(filter =>
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            return $"{filter.name}/{(filter.sharedMesh ? filter.sharedMesh.name : "null")}: " +
                $"triangles={(filter.sharedMesh ? filter.sharedMesh.triangles.Length / 3 : 0)} " +
                $"bounds={(renderer ? renderer.bounds.size.ToString() : "no renderer")}";
        });
        return string.Join("; ", rows);
    }

    static void CaptureFlesh(SeveredBodyPartIdentity part, MeshFilter flesh, Bounds bounds,
        string fileName, string label)
    {
        const int layer = 30;
        foreach (var child in part.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 0;
        if (!flesh) throw new Exception("Missing " + label + " flesh renderer for image capture");
        flesh.gameObject.layer = layer;
        var cameraObject = new GameObject("Severed head validation camera");
        var lightObject = new GameObject("Severed head validation light");
        var camera = cameraObject.AddComponent<Camera>();
        var light = lightObject.AddComponent<Light>();
        var target = new RenderTexture(512, 512, 24);
        var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
        try
        {
            camera.transform.position = bounds.center + Vector3.forward * (bounds.size.z + .5f);
            camera.transform.LookAt(bounds.center);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(.2f, Mathf.Max(bounds.size.x, bounds.size.y) * .7f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.green;
            camera.cullingMask = 1 << layer;
            camera.targetTexture = target;
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.cullingMask = 1 << layer;
            light.transform.rotation = Quaternion.Euler(25, 25, 0);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            File.WriteAllBytes("Temp/" + fileName, image.EncodeToPNG());
            int drawnPixels = image.GetPixels32().Count(pixel => pixel.r > 20 || pixel.b > 20 || pixel.g < 230);
            if (drawnPixels < 1000)
                throw new Exception($"{label} flesh did not render into the camera ({drawnPixels} drawn pixels)");
            report.AppendLine($"PASS {label} flesh rendered with its game material: {drawnPixels} pixels");
        }
        finally
        {
            if (flesh) flesh.gameObject.layer = 0;
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(cameraObject);
            UnityEngine.Object.Destroy(lightObject);
        }
    }

    static void Finish()
    {
        SessionState.SetBool(Running, false);
        bool startedPlay = SessionState.GetBool(StartedPlay, false);
        SessionState.SetBool(StartedPlay, false);
        if (detachedHead) UnityEngine.Object.Destroy(detachedHead.gameObject);
        if (startedPlay) EditorApplication.isPlaying = false;
    }
}
