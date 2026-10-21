using System;
using System.IO;
using System.Linq;
using System.Text;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ArmedBanditValidation
{
    const string Request = "Temp/ArmedBanditValidation.request";
    const string Output = "TestCaptures/ArmedBanditPass";
    static ArmedBanditValidation() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Run(); } catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/result.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/Melee Combat/Validate armed bandit poses and colliders")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        int failures = 0, samples = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var cameraObject = new GameObject("Armed pass camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.cameraType = CameraType.Preview;
            camera.transform.position = new Vector3(2.6f, 1.9f, 3.5f);
            camera.transform.LookAt(new Vector3(0, 1f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.17f, .18f, .2f);
            camera.fieldOfView = 35;
            var lightObject = new GameObject("Armed pass light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(40, -35, 0);
            foreach (var path in Directory.GetFiles("Assets/MeleeCombat/MocapTest/GeneratedVariants", "StarvingBandit_*.prefab").OrderBy(p => p))
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                try
                {
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var grip = root.GetComponentInChildren<MocapStaffPreview>(true);
                    var driver = root.GetComponentInChildren<BanditMocapNpcDriver>(true);
                    var brain = root.GetComponent<StarvingBandit>();
                    var animator = grip.GetComponent<Animator>();
                    grip.enabled = false; driver.enabled = false; brain.enabled = false;
                    if (!grip.staff)
                    {
                        var equipment = root.GetComponent<NpcGeneratedWeapon>();
                        var weapon = UnityEngine.Object.Instantiate(equipment.weaponPrefab, root.transform);
                        grip.staff = weapon; brain.staff = weapon;
                    }
                    grip.driveVisualStaff = true;
                    animator.enabled = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    var states = controller.layers[0].stateMachine.states.Select(s => s.state.name)
                        .Where(n => n == "Locomotion" || n.StartsWith("Attack")).ToArray();
                    float worstGrip = 0, minForward = float.MaxValue;
                    foreach (var state in states)
                    {
                        var directions = state == "Locomotion" ? new[] { Vector2.zero, Vector2.up, Vector2.down, Vector2.left, Vector2.right,
                            new Vector2(.7f,.7f), new Vector2(-.7f,.7f), new Vector2(.7f,-.7f), new Vector2(-.7f,-.7f) } : new[] { Vector2.zero };
                        foreach (var direction in directions)
                        for (int frame = 0; frame <= 10; frame++)
                        {
                            animator.SetFloat("MoveX", direction.x); animator.SetFloat("MoveY", direction.y);
                            animator.Play(state, 0, frame / 10f); animator.Update(0);
                            brain.state = state == "Locomotion" ? StarvingBandit.State.Approach : StarvingBandit.State.Strike;
                            driver.ApplyArmedGuard(1f);
                            grip.runtimeWeaponOffsetWeight = 1f;
                            grip.runtimeWeaponOffsetWorld = state == "Locomotion" && grip.secondaryHandGrips ?
                                root.transform.TransformDirection(new Vector3(0, .08f, .22f)) : Vector3.zero;
                            grip.ApplyGripFromCurrentPose();
                            float error = grip.primaryHandIsLeft ? grip.LeftGripError : grip.RightGripError;
                            if (grip.secondaryHandGrips || grip.secondaryVisualWeapon) error = Mathf.Max(grip.LeftGripError, grip.RightGripError);
                            worstGrip = Mathf.Max(worstGrip, error); samples++;
                            if (state == "Locomotion")
                            {
                                var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                                foreach (var hand in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                                {
                                    if (!grip.secondaryHandGrips && !grip.secondaryVisualWeapon && (hand == HumanBodyBones.LeftHand) != grip.primaryHandIsLeft) continue;
                                    var wrist = animator.GetBoneTransform(hand);
                                    minForward = Mathf.Min(minForward, Vector3.Dot(wrist.position - chest.position, root.transform.forward));
                                }
                            }
                            if (frame == 5 && direction == Vector2.zero &&
                                (state == "Locomotion" || state == "AttackDirection0" || state == "AttackDirection4" ||
                                 root.name == "StarvingBandit_LongBlade" &&
                                 (state == "AttackDirection1" || state == "AttackDirection5")))
                                Capture(camera, root.name + "_" + state);
                        }
                    }
                    float maxMeshGap = 0;
                    foreach (var filter in grip.staff.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!filter.sharedMesh) continue;
                        var shapes = filter.GetComponents<MeshCollider>().Where(c => c.enabled).ToArray();
                        if (shapes.Length == 0) { maxMeshGap = float.PositiveInfinity; break; }
                        var vertices = filter.sharedMesh.vertices;
                        for (int vertex = 0; vertex < vertices.Length; vertex += Mathf.Max(1, vertices.Length / 1500))
                        {
                            var point = filter.transform.TransformPoint(vertices[vertex]);
                            maxMeshGap = Mathf.Max(maxMeshGap, shapes.Min(c => Vector3.Distance(point, c.ClosestPoint(point))));
                        }
                    }
                    bool pass = worstGrip <= .025f && minForward >= .10f && maxMeshGap <= .035f && grip.staff.useMeshContactShape;
                    if (!pass) failures++;
                    report.AppendLine($"{(pass ? "PASS" : "FAIL")} {root.name}: maxGrip={worstGrip:F4}m minWristAheadOfChest={minForward:F4}m meshGap={maxMeshGap:F4}m fittedContact={grip.staff.useMeshContactShape}");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        report.Insert(0, $"Pose samples={samples}, failures={failures}\n");
        File.WriteAllText(Output + "/result.txt", report.ToString());
        Debug.Log("ARMED_VALIDATION\n" + report);
    }
    static void Capture(Camera camera, string name)
    {
        var baked = new System.Collections.Generic.List<GameObject>();
        var skins = camera.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(s => s.enabled).ToArray();
        foreach (var skin in skins)
        {
            var copy = new GameObject("Baked evidence");
            SceneManager.MoveGameObjectToScene(copy, camera.scene);
            copy.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            copy.transform.localScale = Vector3.one;
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            File.AppendAllText(Output + "/bake-diagnostics.txt", $"{name} {skin.name} scale={skin.transform.lossyScale} baked={mesh.bounds}\n");
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false; baked.Add(copy);
        }
        var texture = RenderTexture.GetTemporary(800, 800, 24);
        var previous = RenderTexture.active;
        var image = new Texture2D(800, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 800, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        }
        finally {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(texture); UnityEngine.Object.DestroyImmediate(image);
            foreach (var copy in baked) { UnityEngine.Object.DestroyImmediate(copy.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(copy); }
            foreach (var skin in skins) skin.enabled = true;
        }
    }
}
