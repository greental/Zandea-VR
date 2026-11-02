using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using KenshiVR.MeleeCombat;

namespace KenshiVR.CharacterCreator.Editor
{
    [InitializeOnLoad]
    public static class CreatorPlayerHandCapture
    {
        const string Request = "Library/CreatorPlayerHandCapture.request";
        const string FistOutput = "TestCaptures/ManPlayerFist-ThumbOutside.png";
        const string WeaponOutput = "TestCaptures/ManPlayerSwordGrip.png";
        const string SwordPath = "Assets/MyAssets/prefabs/weapons/Player practice sword - generated long blade.prefab";
        const string LastRequest = "CreatorPlayerHandCapture.LastRequest";
        const string LastCapture = "CreatorPlayerHandCapture.LastCapture";

        static CreatorPlayerHandCapture() => EditorApplication.update += Tick;

        [MenuItem("Zandea/Character Creator/Capture man player fist")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Main.unity")
                throw new InvalidOperationException("Open Assets/Main.unity before capturing the player hand.");
            File.WriteAllText(Request, Guid.NewGuid().ToString("N"));
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling) return;
            var token = File.ReadAllText(Request);
            if (token.StartsWith("completed:", StringComparison.Ordinal)) return;
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetString(LastRequest, "") == token) return;
                SessionState.SetString(LastRequest, token);
                EditorApplication.isPlaying = true;
                return;
            }
            if (SessionState.GetString("CreatorPlayerHandCapture.Probe", "") != token)
            {
                SessionState.SetString("CreatorPlayerHandCapture.Probe", token);
                var candidates = UnityEngine.Object.FindObjectsByType<CreatorPlayerHands>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                Debug.Log("Man hand capture Play probe: frame=" + Time.frameCount +
                    " candidates=" + candidates.Length + " active=" + candidates.Count(h => h && h.isActiveAndEnabled));
            }
            if (SessionState.GetString(LastCapture, "") == token) return;
            var hands = UnityEngine.Object.FindObjectsByType<CreatorPlayerHands>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).FirstOrDefault(h => h && h.isActiveAndEnabled);
            if (!hands || !hands.body || !hands.body.animator) return;
            SessionState.SetString(LastCapture, token);
            try { RenderPose(hands, false); RenderPose(hands, true); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally
            {
                File.WriteAllText(Request, "completed:" + token);
                EditorApplication.isPlaying = false;
            }
        }

        static void RenderPose(CreatorPlayerHands hands, bool held)
        {
            Debug.Log("Man hand capture joint bindings: left=" + hands.LeftJointCount +
                " right=" + hands.RightJointCount);
            hands.previewFist = !held;
            hands.previewHeldGrip = held;
            hands.SendMessage("LateUpdate");
            var wrist = hands.FindRigBone(false, "Hand", 0, HumanBodyBones.RightHand);
            var index = hands.FindRigBone(false, "Index", 1, HumanBodyBones.RightIndexProximal);
            var middle = hands.FindRigBone(false, "Mid", 1, HumanBodyBones.RightMiddleProximal);
            var little = hands.FindRigBone(false, "Pinky", 1, HumanBodyBones.RightLittleProximal);
            if (!wrist || !index || !middle || !little)
                throw new InvalidOperationException("The man player right hand has missing humanoid bones.");
            var outward = (middle.position - wrist.position).normalized;
            var palmNormal = Vector3.Cross(index.position - little.position, outward).normalized;
            var lateral = (index.position - little.position).normalized;
            var center = (wrist.position + middle.position) * .5f +
                (index.position - little.position).normalized * .02f;
            var cameraObject = new GameObject("Man player fist camera");
            var camera = cameraObject.AddComponent<Camera>();
            var light = cameraObject.AddComponent<Light>();
            GameObject weaponObject = null;
            var target = new RenderTexture(1024, 1024, 24);
            var previous = RenderTexture.active;
            var originals = new List<(SkinnedMeshRenderer renderer, Mesh mesh, bool enabled)>();
            var temporaryMeshes = new List<Mesh>();
            try
            {
                if (held)
                {
                    var swordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath);
                    if (!swordPrefab) throw new InvalidOperationException("Practice sword prefab is missing.");
                    weaponObject = UnityEngine.Object.Instantiate(swordPrefab);
                    var sword = weaponObject.GetComponent<PhysicalMeleeWeapon>();
                    if (!sword || !sword.rightGrip) throw new InvalidOperationException("Practice sword right grip is missing.");
                    sword.enabled = false;
                    var rigidbody = weaponObject.GetComponent<Rigidbody>();
                    if (rigidbody) { rigidbody.isKinematic = true; rigidbody.useGravity = false; }
                    foreach (var collider in weaponObject.GetComponentsInChildren<Collider>()) collider.enabled = false;
                    weaponObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, lateral);
                    var handleCenter = (wrist.position + middle.position) * .5f - palmNormal * .018f + outward * .02f;
                    weaponObject.transform.position += handleCenter - sword.rightGrip.position;
                }
                foreach (var skin in hands.body.avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    originals.Add((skin, skin.sharedMesh, skin.enabled));
                    var source = skin.sharedMesh;
                    var weights = source ? source.boneWeights : null;
                    var indices = skin.bones.Select((bone, index) => new { bone, index })
                        .Where(pair => pair.bone && (pair.bone == wrist || pair.bone.IsChildOf(wrist)))
                        .Select(pair => pair.index).ToHashSet();
                    if (!source || weights == null || weights.Length != source.vertexCount || indices.Count == 0)
                    { skin.enabled = false; continue; }
                    var handVertex = new bool[source.vertexCount];
                    for (int vertex = 0; vertex < weights.Length; vertex++)
                    {
                        var weight = weights[vertex];
                        var total = (indices.Contains(weight.boneIndex0) ? weight.weight0 : 0f) +
                            (indices.Contains(weight.boneIndex1) ? weight.weight1 : 0f) +
                            (indices.Contains(weight.boneIndex2) ? weight.weight2 : 0f) +
                            (indices.Contains(weight.boneIndex3) ? weight.weight3 : 0f);
                        handVertex[vertex] = total > .25f;
                    }
                    var isolated = UnityEngine.Object.Instantiate(source);
                    var kept = 0;
                    for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                    {
                        var triangles = source.GetTriangles(submesh);
                        var selected = new List<int>();
                        for (int t = 0; t < triangles.Length; t += 3)
                            if (handVertex[triangles[t]] && handVertex[triangles[t + 1]] && handVertex[triangles[t + 2]])
                            { selected.Add(triangles[t]); selected.Add(triangles[t + 1]); selected.Add(triangles[t + 2]); }
                        kept += selected.Count;
                        isolated.SetTriangles(selected, submesh, false);
                    }
                    temporaryMeshes.Add(isolated);
                    if (kept == 0) skin.enabled = false;
                    else skin.sharedMesh = isolated;
                }
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .14f, .17f);
                camera.orthographic = true;
                camera.orthographicSize = held ? .18f : .105f;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 1f;
                camera.transform.position = center - palmNormal * .28f + lateral * .18f + outward * .07f;
                camera.transform.LookAt(center, outward);
                light.type = LightType.Point;
                light.intensity = .45f;
                light.range = 2f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                image.Apply();
                Directory.CreateDirectory("TestCaptures");
                var output = held ? WeaponOutput : FistOutput;
                File.WriteAllBytes(output, image.EncodeToPNG());
                Debug.Log("Captured man player hand in Play Mode: " + output);
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                hands.previewFist = false;
                hands.previewHeldGrip = false;
                if (weaponObject) UnityEngine.Object.DestroyImmediate(weaponObject);
                foreach (var original in originals)
                    if (original.renderer)
                    { original.renderer.sharedMesh = original.mesh; original.renderer.enabled = original.enabled; }
                foreach (var mesh in temporaryMeshes) UnityEngine.Object.DestroyImmediate(mesh);
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
