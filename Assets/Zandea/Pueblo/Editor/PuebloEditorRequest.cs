using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.Pueblo.Editor
{
    // Small local editor bridge for repeatable build/validation without entering VR.
    [InitializeOnLoad]
    public static class PuebloEditorRequest
    {
        const string Request = "Temp/ZandeaPueblo.request";
        const string Result = "Temp/ZandeaPueblo.request-result.txt";

        static PuebloEditorRequest() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            try
            {
                if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "build") PuebloSettlementBuilder.Build();
                else if (command == "validate") PuebloPlayValidation.Begin();
                else if (command == "validate-main") PuebloMainPlayValidation.Begin();
                else if (command == "capture") CaptureReview(false);
                else throw new InvalidOperationException("Unknown Pueblo editor request: " + command);
                File.WriteAllText(Result, "OK " + command);
            }
            catch (Exception error)
            {
                File.WriteAllText(Result, "FAIL " + command + "\n" + error);
                Debug.LogException(error);
            }
        }

        internal static void CaptureReview(bool runtime)
        {
            if (EditorApplication.isPlaying && !runtime)
                throw new InvalidOperationException("Exit Play Mode before capturing the Pueblo review.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Zandea/PuebloSettlementReview.unity")
                throw new InvalidOperationException("Open the Pueblo review scene before capture.");
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(c => c.name == "Pueblo review camera");
            if (!camera) throw new InvalidOperationException("Pueblo review camera is missing.");
            string destination = runtime ? "Tools/Pueblo/work/review_runtime.png" :
                "Tools/Pueblo/work/review.png";
            Directory.CreateDirectory("Tools/Pueblo/work");
            var target = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            try
            {
                if (runtime)
                {
                    camera.transform.position = new Vector3(62, 48, -70);
                    camera.transform.LookAt(Vector3.zero);
                }
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                image.Apply();
                File.WriteAllBytes(destination, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
