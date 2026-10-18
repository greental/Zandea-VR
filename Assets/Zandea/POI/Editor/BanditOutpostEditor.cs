using System;
using System.IO;
using System.Linq;
using System.Text;
using KenshiVR.MeleeCombat;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.POI.Editor
{
    [CustomEditor(typeof(BanditOutpost))]
    public sealed class BanditOutpostEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Required: 4–5 buildings, 6 canopies, 6 existing bandits. Cyan = paths; magenta = future props. Preview uses a private terrain copy in the test scene; Main is stamped at runtime.", MessageType.Info);
            if (Application.isPlaying) return;
            var poi = (BanditOutpost)target;
            if (GUILayout.Button("Regenerate preview with next seed"))
            {
                Undo.RecordObject(poi, "Regenerate outpost"); poi.seed++;
                BanditOutpostTools.Preview(poi);
            }
            if (GUILayout.Button("Validate 100 seeds")) BanditOutpostTools.Validate(poi.definition);
        }
    }
    [InitializeOnLoad]
    public static class BanditOutpostTools
    {
        const string Folder = "Assets/Zandea/POI";
        const string Prefabs = "Assets/MyAssets/prefabs/buildings/desertOutpost/";
        static BanditOutpostTools() { EditorApplication.update += Tick; }
        static void Tick()
        {
            const string request = "Temp/ZandeaPOI.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (command == "install") Install();
                else if (command == "validate") Validate(AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>(Folder + "/BanditOutpost.asset"));
                else if (command == "play") EditorApplication.isPlaying = true;
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "inspect") Inspect();
                else if (command == "test") OutpostPlayValidation.Begin();
                else if (command == "test-main") OutpostPlayValidation.BeginMain();
                else if (command == "review") RenderReview();
                else if (command == "refine")
                {
                    var d = AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>(Folder + "/BanditOutpost.asset");
                    d.mainBuilding = Asset("AdobeMain");
                    EditorUtility.SetDirty(d); AssetDatabase.SaveAssets(); Install();
                }
                else if (command == "refresh") AssetDatabase.Refresh();
                File.WriteAllText("Temp/ZandeaPOI.result", "OK " + command);
            }
            catch (Exception e) { File.WriteAllText("Temp/ZandeaPOI.result", e.ToString()); Debug.LogException(e); }
        }
        static OutpostAsset Asset(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
            if (!prefab) throw new Exception("Missing " + name);
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var renderers = instance.GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                return new OutpostAsset { prefab = prefab, size = bounds.size, center = bounds.center };
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        [MenuItem("Tools/Zandea/POI/Install Bandit Outpost Slice")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new Exception("Exit Play Mode before installation.");
            var d = AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>(Folder + "/BanditOutpost.asset");
            if (!d)
            {
                d = ScriptableObject.CreateInstance<BanditOutpostDefinition>();
                d.mainBuilding = Asset("AdobeMain");
                d.buildings = new[] { Asset("Adobe"), Asset("Adobe2"), Asset("AdobeRound"), Asset("AdobeSmallDome") };
                d.shades = new[] { Asset("Canopy"), Asset("canopyGray"), Asset("canopyRed"), Asset("CanopyYellow2") };
                d.wall = Asset("WallSingle"); d.gate = Asset("Gate"); d.tower = Asset("WatchTower");
                d.bandit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MeleeCombat/MocapTest/GeneratedVariants/StarvingBandit_ShortSaber.prefab").GetComponent<StarvingBandit>();
                AssetDatabase.CreateAsset(d, Folder + "/BanditOutpost.asset");
            }
            Validate(d);
            var main = SceneManager.GetSceneByPath("Assets/Main.unity");
            bool opened = !main.isLoaded;
            if (opened) main = EditorSceneManager.OpenScene("Assets/Main.unity", OpenSceneMode.Additive);
            var treadmill = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlanetTerrainTreadmill>(true)).First(t => t.gameObject.activeInHierarchy);
            var poi = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true)).FirstOrDefault();
            if (!poi)
            {
                var root = new GameObject("Zandea Bandit Outpost"); SceneManager.MoveGameObjectToScene(root, main);
                poi = root.AddComponent<BanditOutpost>();
            }
            poi.definition = d; poi.treadmill = treadmill;
            if (!poi.siteSelected)
            {
                var mainCamera = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"));
                var target = treadmill.Target ? treadmill.Target.position : mainCamera ? mainCamera.transform.position : Vector3.zero;
                var origin = treadmill.GetTileWorldPosition(new PlanetTileCoord(0, 0));
                if (!OutpostTerrain.FindSite(treadmill, d, new Vector2(target.x - origin.x, target.z - origin.z), out poi.stableSite, out poi.siteHeight))
                    throw new Exception("No acceptable near-start site; adjust slope/height limits after reviewing terrain.");
                poi.siteSelected = true;
            }
            EditorUtility.SetDirty(poi);
            EditorSceneManager.MarkSceneDirty(main); EditorSceneManager.SaveScene(main);
            CreateTestScene(poi);
            AssetDatabase.SaveAssets();
        }
        public static void Preview(BanditOutpost poi)
        {
            var old = poi.transform.Find("Layout Preview");
            if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            // Clear the nonserialized layout cache after Inspector changes.
            typeof(BanditOutpost).GetProperty("Layout").GetSetMethod(true).Invoke(poi, new object[] { null });
            poi.InstantiateStructures("Layout Preview");
            EditorSceneManager.MarkSceneDirty(poi.gameObject.scene);
            Selection.activeGameObject = poi.gameObject;
            SceneView.lastActiveSceneView?.LookAt(poi.Center, Quaternion.Euler(50, -25, 0), 85);
        }
        static void CreateTestScene(BanditOutpost source)
        {
            string path = Folder + "/BanditOutpostTest.unity";
            var existing = SceneManager.GetSceneByPath(path);
            if (existing.isLoaded) EditorSceneManager.CloseScene(existing, true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var groundData = new TerrainData { heightmapResolution = 129, size = new Vector3(256, 50, 256) };
            var heights = new float[129, 129];
            for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++) heights[z, x] = .1f;
            groundData.SetHeights(0, 0, heights);
            const string terrainPath = Folder + "/TestTerrain.asset";
            var oldData = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
            if (oldData) { EditorUtility.CopySerialized(groundData, oldData); UnityEngine.Object.DestroyImmediate(groundData); groundData = oldData; }
            else AssetDatabase.CreateAsset(groundData, terrainPath);
            var ground = Terrain.CreateTerrainGameObject(groundData); ground.name = "POI Test Ground"; ground.transform.position = new Vector3(-128, -5, -128);
            var root = new GameObject("Zandea Bandit Outpost Test");
            var treadmill = root.AddComponent<PlanetTerrainTreadmill>(); treadmill.enabled = false; treadmill.SetTileVerticalOffset(0);
            var camera = new GameObject("Review Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(75, 75, -100); camera.transform.LookAt(Vector3.zero); camera.farClipPlane = 500;
            treadmill.ConfigureTarget(camera.transform);
            var poi = root.AddComponent<BanditOutpost>(); poi.definition = source.definition; poi.treadmill = treadmill; poi.siteSelected = true; poi.stream = false; poi.seed = source.seed;
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            RenderSettings.ambientLight = new Color(.55f, .52f, .45f);
            Preview(poi);
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(source.gameObject.scene);
        }
        public static void Validate(BanditOutpostDefinition d)
        {
            var report = new StringBuilder();
            for (int seed = 0; seed < 100; seed++)
            {
                var a = OutpostLayout.Generate(d, seed); var b = OutpostLayout.Generate(d, seed);
                if (!a.Validate(d, out var error)) throw new Exception(error);
                if (a.structures.Count != b.structures.Count || a.reservations.Count != b.reservations.Count) throw new Exception("Nondeterministic count");
                for (int i = 0; i < a.structures.Count; i++)
                    if (a.structures[i].asset != b.structures[i].asset || a.structures[i].position != b.structures[i].position || a.structures[i].yaw != b.structures[i].yaw) throw new Exception("Nondeterministic layout");
            }
            report.AppendLine("PASS 100 seeds: required counts, footprint bounds, overlap/path rejection, deterministic structure placement.");
            var variant = UnityEngine.Object.Instantiate(d);
            try
            {
                for (int count = 4; count <= 5; count++)
                    for (int rotation = 0; rotation < 4; rotation++)
                    {
                        variant.buildingCount = count; variant.gateQuarterTurns = rotation;
                        for (int seed = 0; seed < 25; seed++) OutpostLayout.Generate(variant, seed);
                    }
                report.AppendLine("PASS 200 layouts across 4/5 buildings and all four gate orientations.");
            }
            finally { UnityEngine.Object.DestroyImmediate(variant); }
            ValidateTerrain(d);
            report.AppendLine("PASS stable terrain stamp across four tile corners, exact foundation height and repeatable regeneration.");
            report.AppendLine("Prefab dimensions (measured render bounds, meters):");
            foreach (var a in new[] { d.mainBuilding, d.wall, d.gate, d.tower }.Concat(d.buildings).Concat(d.shades)) report.AppendLine(a.prefab.name + " " + a.size);
            File.WriteAllText("Temp/ZandeaPOI.layout-validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        static void ValidateTerrain(BanditOutpostDefinition definition)
        {
            var owner = new GameObject("Temporary stamp validation");
            var t = owner.AddComponent<PlanetTerrainTreadmill>(); t.enabled = false;
            var tiles = new TerrainData[4];
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var data = tiles[i] = new TerrainData { heightmapResolution = 129, size = new Vector3(1024, 50, 1024) };
                    var heights = new float[129, 129];
                    for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++) heights[z, x] = .3f;
                    data.SetHeights(0, 0, heights);
                    OutpostTerrain.Stamp(t, definition, new Vector2(1024, 1024), 10, new PlanetTileCoord(i % 2, i / 2), data);
                }
                for (int i = 0; i < 129; i++)
                {
                    if (Mathf.Abs(tiles[0].GetHeight(128, i) - tiles[1].GetHeight(0, i)) > .002f ||
                        Mathf.Abs(tiles[0].GetHeight(i, 128) - tiles[2].GetHeight(i, 0)) > .002f ||
                        Mathf.Abs(tiles[3].GetHeight(0, i) - tiles[2].GetHeight(128, i)) > .002f)
                        throw new Exception("Terrain stamp seam mismatch");
                }
                if (Mathf.Abs(tiles[0].GetHeight(128, 128) - 10) > .002f) throw new Exception("Foundation not level");
                var before = tiles[0].GetHeights(0, 0, 129, 129);
                var fresh = new TerrainData { heightmapResolution = 129, size = tiles[0].size };
                try
                {
                    var heights = new float[129, 129];
                    for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++) heights[z, x] = .3f;
                    fresh.SetHeights(0, 0, heights);
                    OutpostTerrain.Stamp(t, definition, new Vector2(1024, 1024), 10, new PlanetTileCoord(0, 0), fresh);
                    var after = fresh.GetHeights(0, 0, 129, 129);
                    for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++)
                        if (before[z, x] != after[z, x]) throw new Exception("Nondeterministic stamp");
                }
                finally { UnityEngine.Object.DestroyImmediate(fresh); }
            }
            finally
            {
                foreach (var tile in tiles) if (tile) UnityEngine.Object.DestroyImmediate(tile);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
        static void Inspect()
        {
            var poi = UnityEngine.Object.FindFirstObjectByType<BanditOutpost>();
            if (!poi) throw new Exception("No outpost in current scene");
            File.WriteAllText("Temp/ZandeaPOI.inspect.txt", $"Playing={Application.isPlaying} Loaded={poi.IsLoaded} Members={poi.Members.Count} Center={poi.Center} Site={poi.stableSite}\n" +
                string.Join("\n", poi.Members.Select(n => n ? $"{n.name}: pos={n.transform.position} state={n.state} health={n.health.Current} consciousness={n.health.State} nav={n.agent.isOnNavMesh}" : "missing")));
        }
        static void RenderReview()
        {
            var scene = SceneManager.GetSceneByPath(Folder + "/BanditOutpostTest.unity");
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(Folder + "/BanditOutpostTest.unity", OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).First();
            var output = new RenderTexture(1440, 1000, 24);
            camera.targetTexture = output;
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = output;
            var pixels = new Texture2D(1440, 1000, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1440, 1000), 0, 0); pixels.Apply();
            File.WriteAllBytes("Temp/ZandeaPOI-review.png", pixels.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(output);
        }
    }
}
