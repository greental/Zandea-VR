using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MocapVariantValidation
{
    const string Key = "MocapVariantValidation.Running";
    static MocapStaffPreview[] previews;
    static Camera camera;
    static RenderTexture texture;
    static int shot, captureFrame;
    static readonly float[] Times = { 0f, .25f, .5f, .75f };
    static string DirectoryPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../GeneratedVariantEvidence"));
    static MocapVariantValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run only in the isolated batch project.");
        EditorSceneManager.OpenScene("Assets/MeleeCombat/MocapTest/GeneratedVariants/GeneratedWeaponGallery.unity");
        Directory.CreateDirectory(DirectoryPath);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    static void SetShot()
    {
        var index = shot / Times.Length;
        var preview = previews[index];
        for (int i = 0; i < previews.Length; i++) previews[i].transform.root.gameObject.SetActive(i == index);
        preview.animationIndex = 0;
        preview.playing = false;
        preview.normalizedTime = Times[shot % Times.Length];
        preview.Sample();
        var center = preview.transform.root.position;
        camera.transform.position = center + new Vector3(2.2f, 1.9f, 2.8f);
        camera.transform.LookAt(center + new Vector3(0, 1, .35f));
        camera.fieldOfView = 40;
        captureFrame = Time.frameCount + 3;
    }
    static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (previews == null)
            {
                previews = UnityEngine.Object.FindObjectsByType<MocapStaffPreview>(FindObjectsSortMode.None)
                    .OrderBy(x => x.transform.root.position.x).ToArray();
                if (previews.Length != 14) throw new InvalidOperationException("Expected fourteen weapon bandits, got " + previews.Length);
                camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
                texture = new RenderTexture(1536, 1024, 24);
                camera.targetTexture = texture;
                foreach (var preview in previews)
                {
                    foreach (var clip in preview.animations.Select((value, index) => (value, index)))
                    {
                        preview.animationIndex = clip.index;
                        preview.normalizedTime = .5f;
                        preview.Sample();
                        if (!clip.value || !preview.GetComponent<Animator>().enabled)
                            throw new InvalidOperationException("Animation failed on " + preview.transform.root.name + " clip " + clip.index);
                    }
                    Debug.Log($"VARIANT_CLIPS {preview.transform.root.name} count={preview.animations.Length}");
                }
                SetShot();
                return;
            }
            if (Time.frameCount < captureFrame) return;
            var current = previews[shot / Times.Length];
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            var name = $"{current.transform.root.name}_{current.normalizedTime:F2}";
            File.WriteAllBytes(Path.Combine(DirectoryPath, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = null;
            Debug.Log($"VARIANT_FRAME {name} animator={current.GetComponent<Animator>().enabled} left={current.LeftGripError:F4} right={current.RightGripError:F4} correctionL={current.LeftCorrection:F3} correctionR={current.RightCorrection:F3}");
            shot++;
            if (shot < previews.Length * Times.Length) { SetShot(); return; }
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
