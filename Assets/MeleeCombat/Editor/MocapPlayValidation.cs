using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KenshiVR.MeleeCombat;

// Explicit command-line entry point only. Never operates the interactive editor.
[InitializeOnLoad]
public static class MocapPlayValidation
{
    const string Key = "MocapPlayValidation.Running";
    static MocapStaffPreview preview;
    static Camera camera;
    static RenderTexture texture;
    static int shot, captureFrame;
    static readonly float[] Times = { 0f, .2f, .4f, .6f, .8f, .999f };
    static string DirectoryPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../MocapGripEvidence"));
    static MocapPlayValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use this renderer only in the isolated batch project.");
        EditorSceneManager.OpenScene("Assets/MeleeCombat/MocapTest/MocapCombatTest.unity");
        Directory.CreateDirectory(DirectoryPath);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void SetShot()
    {
        int view = (shot / Times.Length) % 3;
        preview.attachment = shot < Times.Length * 3 ? MocapStaffPreview.Attachment.RawAnimation : MocapStaffPreview.Attachment.BothHands;
        preview.normalizedTime = Times[shot % Times.Length];
        var positions = new[] { new Vector3(2.8f, 2.1f, 3.8f), new Vector3(3.8f, 1.8f, -.2f), new Vector3(-2.8f, 2.1f, -3.5f) };
        camera.transform.position = positions[view];
        camera.transform.LookAt(new Vector3(0, 1, .4f));
        camera.fieldOfView = 40;
        preview.Sample();
        captureFrame = Time.frameCount + 3;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (!preview)
            {
                preview = UnityEngine.Object.FindFirstObjectByType<MocapStaffPreview>();
                if (!preview) return;
                preview.playing = false;
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
                texture = new RenderTexture(1536, 1024, 24);
                camera.targetTexture = texture;
                SetShot();
                return;
            }
            if (Time.frameCount < captureFrame) return;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            var name = $"{preview.attachment}_view{(shot / Times.Length) % 3}_{preview.normalizedTime:F3}";
            File.WriteAllBytes(Path.Combine(DirectoryPath, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = null;
            Debug.Log($"PLAY_GRIP {name} left={preview.LeftGripError:F6} right={preview.RightGripError:F6} correctionL={preview.LeftCorrection:F4} correctionR={preview.RightCorrection:F4}");
            shot++;
            if (shot < Times.Length * 6) { SetShot(); return; }
            SessionState.SetBool(Key, false);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            SessionState.SetBool(Key, false);
            EditorApplication.Exit(1);
        }
    }
}
