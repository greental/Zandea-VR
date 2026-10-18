using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;

namespace Zandea.POI.Editor
{
    [InitializeOnLoad]
    public static class OutpostPlayValidation
    {
        static double next;
        static OutpostPlayValidation() { EditorApplication.update += Tick; }
        public static void Begin()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop before starting validation.");
            EditorSceneManager.OpenScene("Assets/Zandea/POI/BanditOutpostTest.unity");
            SessionState.SetInt("ZandeaPOITest", 1);
            File.WriteAllText("Temp/ZandeaPOI.play-validation.txt", "Starting focused Play Mode validation\n");
            EditorApplication.isPlaying = true;
        }
        public static void BeginMain()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop before starting validation.");
            EditorSceneManager.OpenScene("Assets/Main.unity");
            SessionState.SetInt("ZandeaPOITest", 10);
            File.WriteAllText("Temp/ZandeaPOI.play-validation.txt", "Starting Main treadmill Play Mode validation\n");
            EditorApplication.isPlaying = true;
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "PASS " + message + "\n");
        }
        static void Tick()
        {
            int step = SessionState.GetInt("ZandeaPOITest", 0);
            if (step == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            var p = UnityEngine.Object.FindFirstObjectByType<BanditOutpost>();
            if (!p || Time.time < 3) return;
            try
            {
                if (step == 10)
                {
                    var transitions = UnityEngine.Object.FindObjectsByType<PlanetTerrainTransitionController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "Transitions=" + string.Join(", ", transitions.Select(t => t.name + ":" + t.TravelMode)) + "\n");
                    var transition = transitions.FirstOrDefault(t => t.gameObject.scene == p.gameObject.scene);
                    if (transition) transition.ForceEnterSurfaceMode();
                    p.treadmill.enabled = true;
                    var probe = new GameObject("POI validation terrain observer").transform;
                    probe.position = p.Center + p.ApproachDirection * 55 + Vector3.up * 1.7f;
                    p.treadmill.ConfigureTarget(probe);
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", $"Surface transition={(transition ? transition.TravelMode.ToString() : "missing")}, terrainActive={p.treadmill.gameObject.activeInHierarchy}, treadmillEnabled={p.treadmill.enabled}, tile={p.treadmill.CenterTile}, terrains={p.treadmill.GetComponentsInChildren<Terrain>(true).Length}, center={p.Center}, probe={probe.position}\n");
                    SessionState.SetInt("ZandeaPOITest", 11);
                    next = EditorApplication.timeSinceStartup + 8;
                }
                else if (step == 11)
                {
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", $"Arrival terrainActive={p.treadmill.gameObject.activeInHierarchy}, treadmillEnabled={p.treadmill.enabled}, tile={p.treadmill.CenterTile}, terrains={p.treadmill.GetComponentsInChildren<Terrain>(true).Length}, loaded={p.IsLoaded}, members={p.Members.Count}\n");
                    Check(p.IsLoaded && p.Members.Count == 6, "Main outpost loads with six bandits near the site");
                    Check(p.Members.All(n => n.agent.isOnNavMesh), "Main bandits stand on terrain navigation");
                    bool ground = Physics.Raycast(p.Center + Vector3.up * 3, Vector3.down, out var hit, 6) && Mathf.Abs(hit.point.y - p.Center.y) < .15f;
                    Check(ground, "Main stamped terrain collider matches foundation height");
                    Capture(p);
                    p.Members[0].health.ForceDeath();
                    SessionState.SetInt("ZandeaPOINpcID", p.Members[0].GetInstanceID());
                    p.treadmill.Target.position = p.Center + Vector3.right * (p.treadmill.TileSizeMeters * 12);
                    SessionState.SetInt("ZandeaPOITest", 12);
                    next = EditorApplication.timeSinceStartup + 8;
                }
                else if (step == 12)
                {
                    Check(!p.IsLoaded, "Outpost unloads while treadmill recycles beyond the site");
                    p.treadmill.Target.position = p.Center + p.ApproachDirection * 55 + Vector3.up * 1.7f;
                    SessionState.SetInt("ZandeaPOITest", 13);
                    next = EditorApplication.timeSinceStartup + 8;
                }
                else if (step == 13)
                {
                    Check(p.IsLoaded && p.Members.Count == 6, "Main outpost reloads after tile recycling");
                    Check(p.Members[0].GetInstanceID() == SessionState.GetInt("ZandeaPOINpcID", 0) && p.Members[0].health.IsDead, "Main death survives tile recycling");
                    Check(p.Members.Skip(1).All(n => n.agent.isOnNavMesh), "Surviving Main bandits recover navigation");
                    SessionState.SetInt("ZandeaPOITest", 0); EditorApplication.isPlaying = false;
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "COMPLETE Main streaming test\n");
                }
                else if (step == 1)
                {
                    Check(p.IsLoaded, "Structures loaded");
                    Check(p.Members.Count == 6, "Exactly six NPCs");
                    Check(p.Members.All(n => n.health && !n.health.IsDead && !n.health.IsUnconscious && n.agent.isOnNavMesh), "Six conscious bandits on navigation");
                    Camera.main.transform.position = p.Members[0].transform.position + Vector3.up * 1.6f + p.ApproachDirection * 2;
                    SessionState.SetInt("ZandeaPOITest", 4);
                    next = EditorApplication.timeSinceStartup + 2;
                }
                else if (step == 4)
                {
                    Check(p.Members[0].groupEngaged, "Entrance guard detects and engages nearby player camera");
                    var path = new NavMeshPath();
                    Check(NavMesh.CalculatePath(p.GatePosition + p.ApproachDirection * 8, p.Center, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "Gate approach reaches courtyard");
                    Check(p.IsReserved(p.stableSite.x, p.stableSite.y), "Stable courtyard exclusion query");
                    p.Members[0].health.ForceDeath(); p.Members[1].health.KnockOut(p.Members[1].transform.position, Vector3.forward, 120);
                    SessionState.SetInt("ZandeaPOINpcID", p.Members[0].GetInstanceID());
                    Check(p.Members[0].health.IsDead && p.Members[1].health.IsUnconscious, "Existing death and knockout transitions");
                    typeof(BanditOutpost).GetMethod("Unload", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(p, null);
                    SessionState.SetInt("ZandeaPOITest", 2);
                }
                else if (step == 2)
                {
                    Check(p.IsLoaded && p.Members.Count == 6, "Outpost reload after unload");
                    Check(p.Members[0].GetInstanceID() == SessionState.GetInt("ZandeaPOINpcID", 0) && p.Members[0].health.IsDead && p.Members[1].health.IsUnconscious,
                        "Encounter identity, death and unconsciousness preserved");
                    var origin = FloatingOriginManager.EnsureFor(p.treadmill.Target);
                    origin.ShiftOrigin(new Vector3(1000, 0, 1000));
                    SessionState.SetInt("ZandeaPOITest", 3);
                }
                else if (step == 3)
                {
                    Check(p.Members.Skip(2).All(n => n.agent.isOnNavMesh), "Living agents rebound after floating-origin shift");
                    Check(Vector3.Distance(p.transform.Find("Bandit Outpost Structures").position, p.Center) < .01f, "Structures retain stable site after origin shift");
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "COMPLETE; melee hits and VR locomotion still require hands-on verification.\n");
                    SessionState.SetInt("ZandeaPOITest", 0);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception e)
            {
                File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "FAIL " + e + "\n");
                SessionState.SetInt("ZandeaPOITest", 0); EditorApplication.isPlaying = false;
            }
        }
        static void Capture(BanditOutpost poi)
        {
            var camera = new GameObject("Temporary outpost review camera").AddComponent<Camera>();
            var output = new RenderTexture(1440, 1000, 24);
            var pixels = new Texture2D(1440, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.transform.position = poi.Center + new Vector3(75, 75, -100);
                camera.transform.LookAt(poi.Center); camera.farClipPlane = 3000;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = output; camera.Render();
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, 1440, 1000), 0, 0); pixels.Apply();
                File.WriteAllBytes("Temp/ZandeaPOI-main-review.png", pixels.EncodeToPNG());
                camera.transform.position = poi.GatePosition + poi.ApproachDirection * 14 + Vector3.up * 2.5f;
                camera.transform.LookAt(poi.Center + Vector3.up * 1.5f);
                camera.Render();
                pixels.ReadPixels(new Rect(0, 0, 1440, 1000), 0, 0); pixels.Apply();
                File.WriteAllBytes("Temp/ZandeaPOI-entrance-review.png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(output);
            }
        }
    }
}
