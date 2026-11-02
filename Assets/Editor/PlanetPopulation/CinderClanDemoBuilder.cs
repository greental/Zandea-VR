using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CinderClanDemoBuilder
{
    const string Request = "Temp/CinderClanDemo.request";
    const string Source = "Assets/MyAssets/prefabs/buildings/cinderClan/";
    const string ScenePath = "Assets/Scenes/Zandea/CinderClanModularDemo.unity";
    const string CaptureDirectory = "TestCaptures/CinderClanModularDemo";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        if (File.Exists(Request)) EditorApplication.delayCall += RunRequested;
    }

    static void RunRequested()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RunRequested; return; }
        File.Delete(Request);
        Build();
    }

    [MenuItem("Zandea/POI/Modular Buildings/Build Cinder Clan Demo Scene")]
    public static void Build()
    {
        var door = Load("wall_door");
        var solid = Load("wall_b");
        var window = Load("wall_windows_b");
        var framed = Load("wall_frame_b");
        var framedWindow = Load("wall_frame_windows_c");
        var roof = Load("קטע_גג_גמלוני_עם_רעפים_כהים");
        if (!door || !solid || !window || !framed || !framedWindow || !roof)
        { Debug.LogError("Cinder Clan demo is missing one or more required prefabs."); return; }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Demo ground"; ground.transform.localScale = new Vector3(12, 1, 5);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(.42f, .36f, .28f); ground.GetComponent<Renderer>().sharedMaterial = material;
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(45, -40, 0);
        Create(door, solid, window, framed, framedWindow, roof, new Vector3(-28, 0, 0), 8, 8, false, "01 - 8 x 8 single story");
        Create(door, solid, window, framed, framedWindow, roof, new Vector3(0, 0, 0), 8, 12, true, "02 - 8 x 12 two story");
        Create(door, solid, window, framed, framedWindow, roof, new Vector3(32, 0, 0), 12, 12, false, "03 - 12 x 12 single story");
        var camera = new GameObject("Demo capture camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox; camera.fieldOfView = 42;
        camera.transform.position = new Vector3(0, 27, -90); camera.transform.LookAt(new Vector3(0, 3, 0));
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        Directory.CreateDirectory(CaptureDirectory);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Capture(camera, new Vector3(0, 27, -90), new Vector3(0, 3, 0), "overview.png");
        Capture(camera, new Vector3(-39, 13, -20), new Vector3(-28, 2, 0), "single_8x8.png");
        Capture(camera, new Vector3(-11, 17, -22), new Vector3(0, 4, 0), "two_story_8x12.png");
        Capture(camera, new Vector3(18, 18, -24), new Vector3(32, 3, 0), "single_12x12.png");
        AssetDatabase.Refresh();
        Debug.Log("[Cinder Clan Demo] Saved " + ScenePath + " and four images in " + CaptureDirectory);
    }

    static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Source + name + ".prefab");

    static void Create(GameObject door, GameObject solid, GameObject window, GameObject framed,
        GameObject framedWindow, GameObject roof, Vector3 center, float width, float depth, bool twoStory, string name)
    {
        var root = new GameObject(name); root.transform.position = center;
        for (int floor = 0; floor < (twoStory ? 2 : 1); floor++)
        {
            float y = floor * 2.7f;
            Place(floor == 0 ? door : framedWindow, root.transform, new Vector3(0, y, -depth / 2), Quaternion.identity, width);
            Place(floor == 0 ? window : framed, root.transform, new Vector3(0, y, depth / 2), Quaternion.Euler(0, 180, 0), width);
            for (int side = -1; side <= 1; side += 2)
            {
                int count = Mathf.CeilToInt(depth / 6f);
                for (int i = 0; i < count; i++)
                {
                    float length = depth / count;
                    var prefab = floor == 0 ? (i == 0 ? window : solid) : (i == 0 ? framedWindow : framed);
                    Place(prefab, root.transform, new Vector3(side * width / 2, y, -depth / 2 + (i + .5f) * length),
                        Quaternion.Euler(0, side * 90, 0), length);
                }
            }
        }
        var roofInstance = PrefabUtility.InstantiatePrefab(roof, root.transform) as GameObject;
        roofInstance.name = "Existing roof candidate - visual demonstration";
        roofInstance.transform.localPosition = new Vector3(0, (twoStory ? 5.4f : 2.7f), 0);
        roofInstance.transform.localScale = new Vector3(width / 7.33f, 1, depth / 10f);
    }

    static void Place(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, float targetLength)
    {
        var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        instance.transform.localPosition = position;
        instance.transform.localRotation = rotation;
        float measured = prefab.name.StartsWith("wall_door", StringComparison.Ordinal) ? 8f : 6f;
        instance.transform.localScale = new Vector3(targetLength / measured, 1, 1);
    }

    static void Capture(Camera camera, Vector3 position, Vector3 target, string filename)
    {
        camera.transform.position = position; camera.transform.LookAt(target);
        var render = new RenderTexture(1400, 900, 24);
        var texture = new Texture2D(1400, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = render; camera.Render();
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(CaptureDirectory, filename), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(render);
        }
    }
}
