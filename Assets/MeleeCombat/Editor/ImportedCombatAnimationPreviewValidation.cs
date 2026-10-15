using System;
using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ImportedCombatAnimationPreviewValidation
{
    const string Key = "ImportedCombatAnimationPreviewValidation.Running";
    static readonly (string prefab, string clip)[] Shots = {
        ("Axe", "standing melee attack downward"),
        ("Axe", "standing melee combo attack ver. 2"),
        ("HookSword", "standing melee attack horizontal"),
        ("LongBlade", "Great Sword Slash")
    };
    static readonly float[] Times = { .2f, .5f, .8f };
    static Camera camera;
    static RenderTexture texture;
    static GameObject subject;
    static MocapStaffPreview preview;
    static int shot, waitUntilFrame;
    static bool pass = true;
    static ImportedCombatAnimationPreviewValidation() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated Unity batch copy.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = Vector3.one * 2;
        camera = new GameObject("Preview camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(3, 2, 4);
        camera.transform.LookAt(new Vector3(0, 1, 0));
        camera.fieldOfView = 40;
        camera.backgroundColor = new Color(.25f, .25f, .25f);
        var light = new GameObject("Preview light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        EditorSceneManager.SaveScene(scene, "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatValidation.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (!camera) camera = Object.FindFirstObjectByType<Camera>();
            if (!texture)
            {
                texture = new RenderTexture(960, 720, 24);
                camera.targetTexture = texture;
                Prepare();
                return;
            }
            if (Time.frameCount < waitUntilFrame) return;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            var entry = Shots[shot / Times.Length];
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../ImportedCombatEvidence"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"{entry.prefab}_{shot / Times.Length}_{shot % Times.Length}.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.active = null;
            float error = preview.primaryHandIsLeft ? preview.LeftGripError : preview.RightGripError;
            if (preview.secondaryHandGrips) error = Mathf.Max(error, preview.LeftGripError, preview.RightGripError);
            Debug.Log($"IMPORTED_COMBAT_GRIP {entry.prefab} {entry.clip} t={Times[shot % Times.Length]:F1} primaryError={error:F3} reach={preview.ReachCorrection:F3}");
            pass &= error < .12f;
            shot++;
            if (shot < Shots.Length * Times.Length) { Prepare(); return; }
            Debug.Log(pass ? "IMPORTED_COMBAT_PREVIEW_PASS" : "IMPORTED_COMBAT_PREVIEW_FAIL");
            SessionState.SetBool(Key, false);
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            EditorApplication.Exit(pass ? 0 : 1);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(1);
        }
    }

    static void Prepare()
    {
        var entry = Shots[shot / Times.Length];
        if (!subject || subject.name != entry.prefab)
        {
            if (subject) Object.Destroy(subject);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_" + entry.prefab + ".prefab");
            subject = Object.Instantiate(asset);
            subject.name = entry.prefab;
            var brain = subject.GetComponent<StarvingBandit>();
            brain.enabled = false;
            var agent = subject.GetComponent<NavMeshAgent>();
            if (agent) agent.enabled = false;
            preview = subject.GetComponentInChildren<MocapStaffPreview>(true);
            var driver = preview.GetComponent<BanditMocapNpcDriver>();
            if (driver) driver.enabled = false;
            preview.staff.enabled = false;
            preview.staff.GetComponent<Collider>().enabled = false;
            preview.staff.GetComponent<Rigidbody>().isKinematic = true;
            preview.driveVisualStaff = true;
            preview.enabled = true;
            preview.playing = false;
        }
        preview.animationIndex = Array.FindIndex(preview.animations, c => c && c.name == entry.clip);
        if (preview.animationIndex < 0) throw new InvalidOperationException("Missing preview clip " + entry.clip);
        preview.normalizedTime = Times[shot % Times.Length];
        preview.Sample();
        waitUntilFrame = Time.frameCount + 3;
    }
}
