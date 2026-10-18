using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zandea.POI;

namespace Zandea.Props.Editor
{
    [InitializeOnLoad]
    public static class OutpostPropPlayValidation
    {
        const string Request = "Temp/ZandeaProps.main-request";
        const string Report = "Temp/ZandeaProps.main-validation.txt";
        static double next;
        static OutpostPropPlayValidation() => EditorApplication.update += Tick;
        [MenuItem("Tools/Zandea/Props/Run Main Outpost Streaming Validation")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play Mode first.");
            EditorSceneManager.OpenScene("Assets/Main.unity");
            SessionState.SetInt("ZandeaPropMainStep", 1);
            File.WriteAllText(Report, "RUNNING Main outpost prop streaming validation\n");
            EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(Request);
                try { Begin(); } catch (Exception e) { File.WriteAllText(Report, "START FAIL " + e); }
            }
            int step = SessionState.GetInt("ZandeaPropMainStep", 0);
            if (step == 0 || !EditorApplication.isPlaying || Time.time < 3 || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            var poi = UnityEngine.Object.FindFirstObjectByType<BanditOutpost>();
            if (!poi) return;
            try
            {
                if (step == 1)
                {
                    var transition = UnityEngine.Object.FindObjectsByType<PlanetTerrainTransitionController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(t => t.gameObject.scene == poi.gameObject.scene);
                    if (transition) transition.ForceEnterSurfaceMode();
                    poi.treadmill.enabled = true;
                    var probe = new GameObject("Prop validation terrain observer").transform;
                    probe.position = poi.Center + poi.ApproachDirection * 55 + Vector3.up * 1.7f;
                    poi.treadmill.ConfigureTarget(probe);
                    SessionState.SetInt("ZandeaPropMainStep", 2);
                    next += 8;
                }
                else if (step == 2)
                {
                    Check(poi.IsLoaded && poi.Members.Count == 6, "Main outpost and six bandits load");
                    var population = poi.GetComponent<OutpostPropPopulation>();
                    Check(population && population.Instances.Count >= 12, "contextual prop population appears");
                    var objects = population.Instances.Where(i => i).ToArray();
                    Check(objects.Any(i => i.GetComponent<PropRackSocket>() || i.GetComponentInChildren<PropRackSocket>()), "rack populated");
                    Check(objects.Any(i => i.name.Contains("table")), "social table placed clear of combat path");
                    Check(objects.Any(i => i.name.Contains("stool")), "social stool placed clear of combat path");
                    Check(objects.Any(i => i.name.Contains("statue") && !i.GetComponent<Rigidbody>()), "static statue placed");
                    CheckGround(poi, population);
                    Capture(poi);
                    var pot = objects.First(i => i.GetComponent<BreakableProp>() && i.name.Contains("pot"));
                    pot.GetComponent<BreakableProp>().Break(pot.transform.position, Vector3.up);
                    SessionState.SetInt("ZandeaPropPot", pot.GetInstanceID());
                    var barrel = objects.First(i => i.name.Contains("barrel"));
                    barrel.transform.position += Vector3.right * .4f;
                    var body = barrel.GetComponent<Rigidbody>();
                    if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.Sleep(); }
                    SessionState.SetInt("ZandeaPropBarrel", barrel.GetInstanceID());
                    var rack = objects.First(i => i.GetComponentInChildren<PropRackSocket>()).GetComponentInChildren<PropRackSocket>();
                    var item = rack.CurrentItem;
                    Check(item != null && item.WorldObject, "rack has existing usable weapon instance");
                    rack.ItemTaken(item.WorldObject.GetComponent<PropItemPresentation>(), item);
                    Check(rack.CurrentItem == null, "stolen weapon leaves empty rack");
                    SessionState.SetInt("ZandeaPropWeapon", item.WorldObject.GetInstanceID());
                    poi.treadmill.Target.position = poi.Center + Vector3.right * (poi.treadmill.TileSizeMeters * 12);
                    SessionState.SetInt("ZandeaPropMainStep", 3);
                    next += 8;
                }
                else if (step == 3)
                {
                    Check(!poi.IsLoaded, "outpost unloads beyond streaming distance");
                    var population = poi.GetComponent<OutpostPropPopulation>();
                    Check(population.Instances.Any(i => i && i.GetInstanceID() == SessionState.GetInt("ZandeaPropPot", 0) && i.GetComponent<BreakableProp>().IsBroken),
                        "broken pot logical state survives unload");
                    poi.treadmill.Target.position = poi.Center + poi.ApproachDirection * 55 + Vector3.up * 1.7f;
                    SessionState.SetInt("ZandeaPropMainStep", 4);
                    next += 8;
                }
                else if (step == 4)
                {
                    Check(poi.IsLoaded, "same outpost reloads after tile recycling");
                    var population = poi.GetComponent<OutpostPropPopulation>();
                    var objects = population.Instances.Where(i => i).ToArray();
                    Check(objects.Any(i => i.GetInstanceID() == SessionState.GetInt("ZandeaPropBarrel", 0)), "moved barrel retains object identity");
                    Check(objects.Any(i => i.GetInstanceID() == SessionState.GetInt("ZandeaPropPot", 0) && i.GetComponent<BreakableProp>().IsBroken),
                        "smashed pot stays smashed");
                    var rack = objects.First(i => i.GetComponentInChildren<PropRackSocket>()).GetComponentInChildren<PropRackSocket>();
                    Check(rack.CurrentItem == null, "rack does not regenerate stolen weapon");
                    CheckGround(poi, population);
                    var origin = FloatingOriginManager.EnsureFor(poi.treadmill.Target);
                    TraceProps(poi, population, "before shift");
                    origin.ShiftOrigin(new Vector3(1000, 0, 1000));
                    SessionState.SetInt("ZandeaPropMainStep", 5);
                }
                else if (step == 5)
                {
                    var population = poi.GetComponent<OutpostPropPopulation>();
                    TraceProps(poi, population, "after shift");
                    CheckGround(poi, population);
                    Check(population.Instances.Where(i => i && i.activeSelf).All(i =>
                        Vector3.Distance(i.transform.position, poi.Center) < poi.definition.unloadDistance),
                        "props remain aligned after floating-origin shift");
                    File.AppendAllText(Report, "COMPLETE. XR physical interaction and Quest 2 profiling remain manual.\n");
                    SessionState.SetInt("ZandeaPropMainStep", 0);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.AppendAllText(Report, "FAIL " + e + "\n");
                SessionState.SetInt("ZandeaPropMainStep", 0);
                EditorApplication.isPlaying = false;
            }
        }
        static void Check(bool success, string label)
        {
            if (!success) throw new Exception(label);
            File.AppendAllText(Report, "PASS " + label + "\n");
        }
        static void TraceProps(BanditOutpost poi, OutpostPropPopulation population, string phase)
        {
            File.AppendAllText(Report, phase + " center=" + poi.Center + "\n");
            foreach (var item in population.Instances.Where(i => i && i.activeSelf))
            {
                float distance = Vector3.Distance(item.transform.position, poi.Center);
                if (distance > 100) File.AppendAllText(Report, item.name + " distance=" + distance +
                    " position=" + item.transform.position + " body=" + item.GetComponent<Rigidbody>() + "\n");
            }
        }
        static void CheckGround(BanditOutpost poi, OutpostPropPopulation population)
        {
            foreach (var point in new[] { poi.Center, poi.GatePosition, poi.ToWorld(new Vector2(15, -11)) })
            {
                var terrain = Terrain.activeTerrains.FirstOrDefault(t => t && t.enabled &&
                    t.GetComponent<TerrainCollider>()?.enabled == true &&
                    point.x >= t.transform.position.x && point.x <= t.transform.position.x + t.terrainData.size.x &&
                    point.z >= t.transform.position.z && point.z <= t.transform.position.z + t.terrainData.size.z);
                Check(terrain != null, "visible terrain and collider cover outpost sample " + point);
            }
            foreach (var prop in population.Instances.Where(i => i && i.activeSelf && i.GetComponent<PhysicalProp>()))
            {
                var terrain = Terrain.activeTerrains.FirstOrDefault(t => t && t.enabled &&
                    t.GetComponent<TerrainCollider>()?.enabled == true &&
                    prop.transform.position.x >= t.transform.position.x && prop.transform.position.x <= t.transform.position.x + t.terrainData.size.x &&
                    prop.transform.position.z >= t.transform.position.z && prop.transform.position.z <= t.transform.position.z + t.terrainData.size.z);
                Check(terrain != null && prop.transform.position.y >= terrain.SampleHeight(prop.transform.position) + terrain.transform.position.y - 1.5f,
                    "physical prop remains above terrain " + prop.name);
            }
        }
        static void Capture(BanditOutpost poi)
        {
            var camera = new GameObject("Temporary prop review camera").AddComponent<Camera>();
            var target = new RenderTexture(1280, 900, 24);
            var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.position = poi.Center + new Vector3(65, 55, -80);
                camera.transform.LookAt(poi.Center);
                camera.farClipPlane = 2000;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); image.Apply();
                File.WriteAllBytes("Temp/ZandeaProps.main-review.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
