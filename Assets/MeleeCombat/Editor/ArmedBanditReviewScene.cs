using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ArmedBanditReviewScene
{
    const string Request = "Temp/ArmedBanditReviewScene.request";
    const string Result = "Temp/ArmedBanditReviewScene.result";
    const string ScenePath = "Assets/MeleeCombat/MocapTest/ArmedBanditReview.unity";
    static ArmedBanditReviewScene() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { File.Delete(Request); }
        catch (IOException) { return; }
        try { Create(); File.WriteAllText(Result, "CREATED " + ScenePath); }
        catch (Exception e) { File.WriteAllText(Result, e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Melee Combat/Create armed bandit review scene")]
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var paths = Directory.GetFiles("Assets/MeleeCombat/MocapTest/GeneratedVariants", "StarvingBandit_*.prefab")
                .OrderBy(p => p).ToArray();
            if (paths.Length != 15) throw new Exception("Expected 15 armed bandit variants, found " + paths.Length);
            var actors = new StarvingBandit[paths.Length];
            var labels = new Transform[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i].Replace('\\', '/'));
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.name = Path.GetFileNameWithoutExtension(paths[i]);
                root.transform.SetPositionAndRotation(new Vector3((i % 5) * 4.2f, 0f, (i / 5) * 4.5f), Quaternion.identity);
                actors[i] = root.GetComponent<StarvingBandit>();
                if (!actors[i]) throw new Exception("Missing bandit brain: " + paths[i]);

                var label = new GameObject("Label " + root.name);
                SceneManager.MoveGameObjectToScene(label, scene);
                label.transform.position = root.transform.position + Vector3.up * 2.5f;
                var text = label.AddComponent<TextMesh>();
                text.text = (i + 1) + "  " + root.name.Replace("StarvingBandit_", "");
                text.fontSize = 42;
                text.characterSize = .055f;
                text.anchor = TextAnchor.MiddleCenter;
                text.color = new Color(.95f, .95f, .78f);
                labels[i] = label.transform;
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Review floor";
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(8.4f, -.16f, 4.5f);
            floor.transform.localScale = new Vector3(23f, .3f, 16f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader)
            {
                const string materialPath = "Assets/MeleeCombat/MocapTest/ArmedBanditReviewFloor.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = new Material(shader) { color = new Color(.29f, .31f, .32f), name = "Review floor grey" };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                floor.GetComponent<Renderer>().sharedMaterial = material;
            }

            var cameraObject = new GameObject("Review Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.13f, .15f, .17f);
            camera.nearClipPlane = .04f;
            var lightObject = new GameObject("Review Sun");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.7f;
            lightObject.transform.rotation = Quaternion.Euler(45, -35, 0);

            var controller = new GameObject("Review Controls");
            SceneManager.MoveGameObjectToScene(controller, scene);
            var review = controller.AddComponent<ArmedBanditReview>();
            review.bandits = actors;
            review.labels = labels;
            review.reviewCamera = camera;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("ARMED_BANDIT_REVIEW_SCENE_CREATED " + ScenePath);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}
