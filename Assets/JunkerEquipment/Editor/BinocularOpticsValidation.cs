using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JunkerEquipment.Editor
{
    /// <summary>Renders an asymmetric test image; never opens/saves the user's scene.</summary>
    [InitializeOnLoad]
    public static class BinocularOpticsValidation
    {
        const string Request = "Temp/JunkerOptics.validate.request";
        const string Report = "Temp/JunkerOptics.validation.txt";
        static string OutputFolder => Application.isBatchMode ? "Validation" : "Temp";
        static double nextPoll;
        static BinocularOpticsValidation() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            Validate();
        }

        [MenuItem("Zandea/Junker Equipment/Validate binocular optics rendering")]
        public static void Validate()
        {
            var report = new StringBuilder();
            try { Run(report); report.AppendLine("RESULT: PASS (headset confirmation of stereo output still required)"); }
            catch (Exception error) { report.AppendLine("RESULT: FAIL\n" + error); Debug.LogException(error); }
            File.WriteAllText(Report, report.ToString());
            Debug.Log(report.ToString());
        }

        static void Invoke(OpticalDevice device, string method)
        {
            typeof(OpticalDevice).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(device, null);
        }

        // Batch entry point for a minimal copy of the optics assets. This allows
        // GPU render checks while the main Unity project is running in Play Mode.
        public static void RunIsolated()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RunIsolated is only for the validation batch project.");
            AssetDatabase.ImportAsset("Assets/OpticalEyepiece.shader", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var pipeline = UniversalRenderPipelineAsset.Create();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            var report = new StringBuilder();
            int result = 0;
            Directory.CreateDirectory(OutputFolder);
            try { Run(report, false); report.AppendLine("RESULT: PASS (headset stereo confirmation remains manual)"); }
            catch (Exception error) { result = 1; report.AppendLine("RESULT: FAIL\n" + error); Debug.LogException(error); }
            File.WriteAllText(Path.Combine(OutputFolder, "JunkerOptics.validation.txt"), report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(result);
        }

        static void Run(StringBuilder report, bool checkPrefabs = true)
        {
            void Check(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException(description);
                report.AppendLine("PASS: " + description);
            }
            var shader = Shader.Find("JunkerEquipment/OpticalEyepiece");
            Check(shader && shader.isSupported, "Optical shader imported and supported");
            var variants = new ShaderVariantCollection();
            foreach (var keywords in new[] { Array.Empty<string>(), new[] { "STEREO_INSTANCING_ON" }, new[] { "STEREO_MULTIVIEW_ON" } })
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipelineDefaultUnlit, keywords));
            variants.WarmUp();
            Object.DestroyImmediate(variants);
            Check(!ShaderUtil.ShaderHasError(shader), "Mono, stereo instancing and multiview shader variants compile");
            if (checkPrefabs) foreach (var path in new[] { "Assets/MyAssets/prefabs/Player/Junker.prefab", "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var optics = prefab ? prefab.GetComponentInChildren<OpticalDevice>(true) : null;
                Check(optics && optics.eye && optics.eyepiece && optics.objective && optics.opticsCamera && optics.requiredGrab &&
                    optics.immersiveViewing && optics.viewingShader == shader && optics.opticalDisplayRenderer,
                    path + " has binocular refs, stereo viewing mode and shader asset");
                Check(optics.GetComponent("BinocularScannerHUD"), path + " has saved binocular scanner/HUD settings");
            }

            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Optical rendering validation");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = Vector3.zero;
            GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform, false); return go; }
            OpticalDevice device = null;
            Texture2D pattern = null;
            Material targetMaterial = null;
            try
            {
                var head = Child("Synthetic head").AddComponent<Camera>();
                head.scene = scene;
                head.enabled = false;
                head.clearFlags = CameraClearFlags.SolidColor;
                head.backgroundColor = Color.white;
                head.nearClipPlane = .1f;
                head.fieldOfView = 60;
                head.aspect = 4f / 3f;
                head.GetUniversalAdditionalCameraData().allowXRRendering = false;
                var item = Child("Synthetic optic");
                item.transform.localPosition = Vector3.forward * .08f;
                device = item.AddComponent<OpticalDevice>();
                var eyepiece = new GameObject("Eyepiece").transform; eyepiece.SetParent(item.transform, false);
                var objective = new GameObject("Objective").transform; objective.SetParent(item.transform, false); objective.localPosition = Vector3.forward * .15f;
                var cameraGo = new GameObject("Optics camera"); cameraGo.transform.SetParent(item.transform, false);
                var source = cameraGo.AddComponent<Camera>(); source.scene = scene;
                device.eye = head.transform; device.eyepiece = eyepiece; device.objective = objective;
                device.opticsCamera = source; device.requireHeld = false; device.viewingShader = shader;
                // Pixel probes below use a 512x512 fixture; production optics use a larger texture.
                device.textureResolution = 512;
                float hmdFov = head.fieldOfView;
                Invoke(device, "Awake"); Invoke(device, "OnEnable"); Invoke(device, "Update"); Invoke(device, "LateUpdate");
                Check(device.IsViewing && source.enabled && device.OutputTexture, "Aligned close optic activates camera and texture");
                Check(device.OutputTexture.width == device.OutputTexture.height && Mathf.Approximately(source.aspect, 1), "Square optical projection matches its texture");
                Check(!source.GetUniversalAdditionalCameraData().allowXRRendering, "Source camera renders a mono image for sharing between eyes");
                objective.rotation = Quaternion.Euler(0, 15, 0); Invoke(device, "LateUpdate");
                Check(source.transform.forward.x > .2f && Vector3.Angle(source.transform.forward, objective.forward) < .01f,
                    "Turning objective right turns source camera right");
                objective.rotation = Quaternion.identity; Invoke(device, "LateUpdate");
                var output = device.OutputTexture;
                device.CycleZoom();
                Check(device.CurrentZoom == 4 && device.OutputTexture == output, "Zoom cycles without recreating texture");
                Check(Mathf.Approximately(head.fieldOfView, hmdFov), "HMD field of view stays unchanged");
                pattern = MakePattern();
                // Render real world-space markers through the source camera, so
                // the check includes Unity's camera-to-texture orientation.
                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "Asymmetric optical target";
                panel.transform.SetParent(root.transform, false);
                panel.transform.position = objective.position + objective.forward * 2;
                panel.transform.rotation = objective.rotation;
                float side = 4 * Mathf.Tan(source.fieldOfView * Mathf.Deg2Rad * .5f);
                panel.transform.localScale = new Vector3(side, side, 1);
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                targetMaterial = new Material(shader);
                targetMaterial.SetFloat("_ImmersiveView", 0);
                targetMaterial.SetFloat("_FlipHorizontal", 0);
                targetMaterial.SetTexture("_BaseMap", pattern);
                panel.GetComponent<Renderer>().sharedMaterial = targetMaterial;
                source.clearFlags = CameraClearFlags.SolidColor; source.backgroundColor = Color.black;
                source.Render();
                RenderAndCheck(head, device, 512, 384, "left", report);
                RenderAndCheck(head, device, 512, 288, "right", report);
                RenderAndCheckFusion(head, device, report);
                // The synthetic world target uses the optical shader for its test pattern;
                // put it in the world queue so it cannot cover camera-space HUD graphics.
                targetMaterial.renderQueue = (int)RenderQueue.Geometry;
                CheckCameraHud(source, device, report);
                CheckStabilisation(device, report);
                eyepiece.localPosition = Vector3.right; Invoke(device, "Update");
                Check(!device.IsViewing && !source.enabled && !head.transform.GetChild(0).GetComponent<Renderer>().enabled,
                    "Moving away disables source camera and removes black surround");
                eyepiece.localPosition = Vector3.zero; objective.rotation = Quaternion.Euler(0, 90, 0); Invoke(device, "Update");
                Check(!device.IsViewing, "Sideways optic does not activate");
                objective.rotation = Quaternion.identity; device.requireHeld = true; Invoke(device, "Update");
                Check(!device.IsViewing, "Ungrabbed optic does not activate");
            }
            finally
            {
                if (device) { Invoke(device, "OnDisable"); Invoke(device, "OnDestroy"); }
                if (pattern) Object.DestroyImmediate(pattern);
                if (targetMaterial) Object.DestroyImmediate(targetMaterial);
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void CheckStabilisation(OpticalDevice device, StringBuilder report)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var filter = typeof(OpticalDevice).GetMethod("FilterPose", flags);
            var rotation = typeof(OpticalDevice).GetField("stableRotation", flags);
            var position = typeof(OpticalDevice).GetField("stablePosition", flags);
            var savedRotation = rotation.GetValue(device);
            var savedPosition = position.GetValue(device);
            try
            {
                rotation.SetValue(device, Quaternion.identity);
                position.SetValue(device, Vector3.zero);
                float maxAngle = 0, maxPosition = 0;
                for (int frame = 0; frame < 300; frame++)
                {
                    float shake = Mathf.Sin(frame / 90f * 17f * Mathf.PI * 2);
                    filter.Invoke(device, new object[] { Vector3.right * (.003f * shake), Quaternion.Euler(0, .35f * shake, 0), 1f / 90 });
                    if (frame > 90)
                    {
                        maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(Quaternion.identity, (Quaternion)rotation.GetValue(device)));
                        maxPosition = Mathf.Max(maxPosition, ((Vector3)position.GetValue(device)).magnitude);
                    }
                }
                if (maxAngle > .1f || maxPosition > .001f) throw new Exception("Optical stabilisation does not sufficiently suppress hand tremors");
                var turn = Quaternion.Euler(0, 20, 0);
                for (int frame = 0; frame < 180; frame++)
                    filter.Invoke(device, new object[] { Vector3.one, turn, 1f / 90 });
                if (Quaternion.Angle(turn, (Quaternion)rotation.GetValue(device)) > .25f ||
                    Vector3.Distance(Vector3.one, (Vector3)position.GetValue(device)) > .001f)
                    throw new Exception("Stabilised view does not settle after a deliberate turn");
                report.AppendLine("PASS: stabilisation suppresses 17Hz angular/position hand tremors and settles after deliberate turns");
            }
            finally { rotation.SetValue(device, savedRotation); position.SetValue(device, savedPosition); }
        }

        static void CheckCameraHud(Camera source, OpticalDevice device, StringBuilder report)
        {
            var hud = new GameObject("Optical HUD validation", typeof(RectTransform), typeof(UnityEngine.Canvas), typeof(UnityEngine.UI.CanvasScaler));
            hud.transform.SetParent(source.transform, false);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = source;
            canvas.planeDistance = .1f;
            var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(512, 512);
            scaler.matchWidthOrHeight = .5f;
            var marker = new GameObject("Top-left range marker", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            marker.transform.SetParent(hud.transform, false);
            var rect = marker.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(-70, 65);
            rect.sizeDelta = new Vector2(40, 20);
            marker.GetComponent<UnityEngine.UI.Image>().color = Color.cyan;
            var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            float previousFov = source.fieldOfView;
            var previous = RenderTexture.active;
            try
            {
                foreach (float fov in new[] { 30f, 8f })
                {
                    source.fieldOfView = fov;
                    scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                    Canvas.ForceUpdateCanvases();
                    source.Render();
                    RenderTexture.active = device.OutputTexture;
                    pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                    var color = pixels.GetPixel(186, 321);
                    File.WriteAllBytes(Path.Combine(OutputFolder, "JunkerOptics.hud.png"), pixels.EncodeToPNG());
                    if (color.r > .15f || color.g < .8f || color.b < .8f)
                        throw new Exception("Top-left camera HUD is missing or moves when zoom changes; FOV " + fov + ", color " + color +
                            ", canvas " + ((RectTransform)hud.transform).rect + ", scale " + canvas.scaleFactor + ", marker viewport " + source.WorldToViewportPoint(rect.position));
                }
                report.AppendLine("PASS: top-left HUD renders into shared optical texture and stays fixed across zoom");
            }
            finally
            {
                source.fieldOfView = previousFov;
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(hud);
            }
        }

        static void RenderAndCheckFusion(Camera head, OpticalDevice device, StringBuilder report)
        {
            var reference = new Color[5];
            for (int eye = 0; eye < 2; eye++)
            {
                var target = new RenderTexture(512, 384, 24);
                var image = new Texture2D(512, 384, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                try
                {
                    target.Create(); head.targetTexture = target;
                    var pose = Matrix4x4.TRS(head.transform.position, head.transform.rotation, Vector3.one);
                    var eyePose = pose * Matrix4x4.TRS(new Vector3(eye == 0 ? -.032f : .032f, eye == 0 ? .002f : -.002f, 0),
                        Quaternion.Euler(0, eye == 0 ? -.6f : .6f, 0), Vector3.one);
                    head.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * eyePose.inverse;
                    var projection = Matrix4x4.Perspective(60, 4f / 3f, .1f, 100);
                    projection.m00 *= eye == 0 ? 1.08f : .94f;
                    projection.m11 *= eye == 0 ? .97f : 1.04f;
                    projection.m02 = eye == 0 ? .16f : -.12f;
                    projection.m12 = eye == 0 ? .035f : -.025f;
                    head.projectionMatrix = projection;
                    Invoke(device, "LateUpdate"); head.Render();
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 512, 384), 0, 0); image.Apply();
                    string name = eye == 0 ? "stereo-left" : "stereo-right";
                    File.WriteAllBytes(Path.Combine(OutputFolder, "JunkerOptics." + name + ".png"), image.EncodeToPNG());

                    int count = 0; Vector2 centroid = Vector2.zero;
                    for (int y = 0; y < 384; y++) for (int x = 0; x < 512; x++)
                    {
                        var color = image.GetPixel(x, y);
                        if (color.r > .8f && color.g > .8f && color.b < .2f) { centroid += new Vector2(x + .5f, y + .5f); count++; }
                    }
                    if (count == 0) throw new Exception(name + ": shared image missing");
                    centroid /= count;
                    var expected = head.WorldToViewportPoint(pose.MultiplyPoint3x4(Vector3.forward * device.viewingFocusDistance));
                    Vector2 expectedPixel = new Vector2(expected.x * 512, expected.y * 384);
                    if (Vector2.Distance(centroid, expectedPixel) > 2f)
                        throw new Exception(name + ": image center is not on the shared focus plane; got " + centroid + " expected " + expectedPixel);

                    float planeRadius = device.viewingFocusDistance * 2 * device.viewingRadius * Mathf.Tan(30 * Mathf.Deg2Rad);
                    var points = new[] { Vector2.zero, new Vector2(-.6f, 0), new Vector2(.6f, 0), new Vector2(0, .6f), new Vector2(0, -.6f) };
                    for (int i = 0; i < points.Length; i++)
                    {
                        var point = new Vector3(points[i].x * planeRadius, points[i].y * planeRadius, device.viewingFocusDistance);
                        var pixel = head.WorldToViewportPoint(pose.MultiplyPoint3x4(point));
                        var color = image.GetPixel(Mathf.RoundToInt(pixel.x * 512), Mathf.RoundToInt(pixel.y * 384));
                        if (eye == 0) reference[i] = color;
                        else if (Vector3.Distance(new Vector3(color.r, color.g, color.b), new Vector3(reference[i].r, reference[i].g, reference[i].b)) > .08f)
                            throw new Exception("Stereo eyes disagree at shared-plane image feature " + i);
                    }
                    report.AppendLine($"PASS: {name}: asymmetric projection, unequal FOV and 64mm IPD converge at {device.viewingFocusDistance:0.#}m; center error {Vector2.Distance(centroid, expectedPixel):0.00}px");
                }
                finally
                {
                    head.targetTexture = null; head.ResetWorldToCameraMatrix(); head.ResetProjectionMatrix();
                    RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
                }
            }
            report.AppendLine("PASS: both eyes sample identical features on the shared focus plane");
        }

        static Texture2D MakePattern()
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float u = (x + .5f) / 128f, v = (y + .5f) / 128f;
                Color color = u < .35f ? Color.red : u > .65f ? Color.green : Color.gray;
                if (v > .72f) color = Color.blue;
                if (v < .28f) color = Color.magenta;
                if (new Vector2(u - .5f, v - .5f).sqrMagnitude < .18f * .18f) color = Color.yellow;
                pixels[y * 128 + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        static void RenderAndCheck(Camera head, OpticalDevice device, int width, int height, string eye, StringBuilder report)
        {
            var target = new RenderTexture(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); head.targetTexture = target; head.aspect = (float)width / height;
                Invoke(device, "LateUpdate"); head.Render();
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(OutputFolder, "JunkerOptics." + eye + ".png"), image.EncodeToPNG());
                int cx = width / 2, cy = height / 2;
                int radius = Mathf.RoundToInt(Mathf.Min(width, height) * device.viewingRadius);
                var left = image.GetPixel(cx - Mathf.RoundToInt(radius * .6f), cy);
                var right = image.GetPixel(cx + Mathf.RoundToInt(radius * .6f), cy);
                if (!(left.r > .8f && left.g < .2f && right.g > .8f && right.r < .2f)) throw new Exception(eye + ": horizontal view is mirrored or missing");
                var top = image.GetPixel(cx, cy + Mathf.RoundToInt(radius * .6f));
                var bottom = image.GetPixel(cx, cy - Mathf.RoundToInt(radius * .6f));
                if (!(top.b > .8f && top.r < .2f && bottom.b > .8f && bottom.r > .8f)) throw new Exception(eye + ": vertical view inverted or missing");
                foreach (var corner in new[] { image.GetPixel(4, 4), image.GetPixel(width - 5, 4), image.GetPixel(4, height - 5), image.GetPixel(width - 5, height - 5) })
                    if (corner.r > .02f || corner.g > .02f || corner.b > .02f) throw new Exception(eye + ": surround is not black");
                bool Yellow(Color color) => color.r > .8f && color.g > .8f && color.b < .2f;
                int horizontal = 0, vertical = 0;
                for (int x = 0; x < width; x++) if (Yellow(image.GetPixel(x, cy))) horizontal++;
                for (int y = 0; y < height; y++) if (Yellow(image.GetPixel(cx, y))) vertical++;
                if (horizontal < 20 || Mathf.Abs(horizontal - vertical) > 4) throw new Exception(eye + ": image circle compressed: " + horizontal + "x" + vertical);
                report.AppendLine($"PASS: {eye} synthetic viewport {width}x{height}: correct X/Y orientation, black corners, circular image {horizontal}x{vertical}");
            }
            finally
            {
                head.targetTexture = null; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
    }
}
