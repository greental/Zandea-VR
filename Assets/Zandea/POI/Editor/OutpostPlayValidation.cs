using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using JunkerBody;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;

namespace Zandea.POI.Editor
{
    [InitializeOnLoad]
    public static class OutpostPlayValidation
    {
        static double next;
        static OutpostPlayValidation() { EditorApplication.update += Tick; }
        public static void AuditMain()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Main.unity");
            var outpost = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BanditOutpost>(true)).First();
            var preview = outpost.transform.Find("Layout Preview");
            var lines = new System.Collections.Generic.List<string>
            {
                $"Preview={preview.position}, rotation={preview.rotation.eulerAngles}, definition={outpost.definition.dimensions}, children={preview.childCount}"
            };
            foreach (Transform child in preview)
            {
                var renderers = child.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(child.position, Vector3.zero);
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var delta = Quaternion.Inverse(preview.rotation) * (child.position - preview.position);
                lines.Add($"CHILD {child.name} active={child.gameObject.activeSelf} local={delta} bounds={bounds.size} bottom={bounds.min.y - preview.position.y:F2} renderers={renderers.Length}");
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == outpost.gameObject || root.GetComponentInChildren<Terrain>(true)) continue;
                var delta = root.transform.position - preview.position;
                if (new Vector2(delta.x, delta.z).magnitude > 150 || Mathf.Abs(delta.y) > 60) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.zero);
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                lines.Add($"ROOT {root.name} delta={delta} bounds={bounds.size} bottom={bounds.min.y - preview.position.y:F2} renderers={renderers.Length}");
            }
            Directory.CreateDirectory("Logs/BugPass_20261019");
            File.WriteAllLines("Logs/BugPass_20261019/outpost-geometry-audit.txt", lines);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
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
            const string request = "Temp/ZandeaPOI.play-request";
            if (File.Exists(request) && !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                File.Delete(request);
                try
                {
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Main.unity")
                        throw new Exception("Open Main before validating its live POI; the current scene was preserved.");
                    SessionState.SetInt("ZandeaPOITest", 10);
                    File.WriteAllText("Temp/ZandeaPOI.play-validation.txt", "Starting Main treadmill Play Mode validation\n");
                    EditorApplication.isPlaying = true;
                }
                catch (Exception e) { File.WriteAllText("Temp/ZandeaPOI.play-validation.txt", "FAIL " + e); }
            }
            int step = SessionState.GetInt("ZandeaPOITest", 0);
            if (step == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 2;
            var p = UnityEngine.Object.FindFirstObjectByType<BanditOutpost>();
            if (!p || Time.time < 3) return;
            try
            {
                if (step == 10)
                {
                    Check(p.enabled && p.treadmill && p.treadmill.enabled,
                        "Main POI and terrain streaming start enabled without Inspector changes");
                    var player = UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
                    var playerPoint = player && player.xrOrigin ? player.xrOrigin.position : Vector3.zero;
                    var playerTerrain = Terrain.activeTerrains.FirstOrDefault(t => t.terrainData &&
                        playerPoint.x >= t.transform.position.x && playerPoint.x <= t.transform.position.x + t.terrainData.size.x &&
                        playerPoint.z >= t.transform.position.z && playerPoint.z <= t.transform.position.z + t.terrainData.size.z);
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt",
                        $"Player={playerPoint}, target={(p.treadmill.Target ? p.treadmill.Target.position.ToString() : "missing")}, " +
                        $"terrainAtPlayer={(playerTerrain ? (playerTerrain.SampleHeight(playerPoint) + playerTerrain.transform.position.y).ToString("F2") : "missing")}, " +
                        $"tileOffset={p.treadmill.TileVerticalOffsetMeters:F2}, preview={p.transform.Find("Layout Preview")?.position}\n");
                    Check(playerTerrain && Mathf.Abs(playerTerrain.SampleHeight(playerPoint) +
                        playerTerrain.transform.position.y - playerPoint.y) < 3f,
                        "Standalone Main terrain starts at the player's ground height");
                    Check(p.IsLoaded && p.Members.Count == 4,
                        "Outpost is already spawned when its terrain tile is spawned");
                    var transitions = UnityEngine.Object.FindObjectsByType<PlanetTerrainTransitionController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt", "Transitions=" + string.Join(", ", transitions.Select(t => t.name + ":" + t.TravelMode)) + "\n");
                    var transition = transitions.FirstOrDefault(t => t.gameObject.scene == p.gameObject.scene);
                    if (transition) transition.ForceEnterSurfaceMode();
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
                    Check(p.IsLoaded && p.Members.Count == 4, "Main outpost loads its four authored bandits near the site");
                    Check(p.Members.All(n => n.agent.isOnNavMesh), "Main bandits stand on terrain navigation");
                    bool ground = Physics.Raycast(p.Center + Vector3.up * 8, Vector3.down, out var hit, 16) && Mathf.Abs(hit.point.y - p.Center.y) < 2f;
                    Check(ground, "Main terrain collider supports the authored center");
                    var authored = p.transform.Find("Layout Preview");
                    Check(authored && authored.gameObject.activeInHierarchy && Vector3.Distance(authored.position, p.Center) < .01f,
                        "Main uses the actual authored settlement at its resolved terrain location");
                    Check(authored.Find("AdobeRound") && authored.Find("AdobeStairs") && authored.Find("Adobe2") &&
                        authored.Find("GateSide") && authored.Find("GateSide (1)") && authored.Find("StarvingBanditGroup (3)"),
                        "All separately authored structures and the bandit group stream with the settlement");
                    var stairs = authored.GetComponentsInChildren<OutpostStairRamp>();
                    Check(stairs.Length > 0 && stairs.All(stair => stair.GetComponent<MeshCollider>() &&
                        stair.GetComponent<MeshCollider>().sharedMesh &&
                        stair.GetComponentsInChildren<BoxCollider>().Where(box => box.name.StartsWith("Step ")).All(box => !box.enabled)),
                        "authored stairs have a continuous walkable ramp instead of tall step blockers");
                    var ramp = stairs[0].GetComponent<MeshCollider>();
                    var vertices = ramp.sharedMesh.vertices;
                    var foot = stairs[0].transform.TransformPoint((vertices[8] + vertices[9]) * .5f);
                    var landing = stairs[0].transform.TransformPoint((vertices[2] + vertices[3]) * .5f);
                    var route = new NavMeshPath();
                    bool footOnMesh = NavMesh.SamplePosition(foot, out var stairStart, 2f, NavMesh.AllAreas);
                    bool landingOnMesh = NavMesh.SamplePosition(landing, out var stairEnd, 2f, NavMesh.AllAreas);
                    bool hasRoute = footOnMesh && landingOnMesh &&
                        NavMesh.CalculatePath(stairStart.position, stairEnd.position, NavMesh.AllAreas, route);
                    var midpoint = stairs[0].transform.TransformPoint((vertices[0] + vertices[2]) * .5f);
                    bool midOnMesh = NavMesh.SamplePosition(midpoint, out var stairMid, 2f, NavMesh.AllAreas);
                    var firstHalf = new NavMeshPath(); var secondHalf = new NavMeshPath();
                    if (footOnMesh && midOnMesh) NavMesh.CalculatePath(stairStart.position, stairMid.position, NavMesh.AllAreas, firstHalf);
                    if (midOnMesh && landingOnMesh) NavMesh.CalculatePath(stairMid.position, stairEnd.position, NavMesh.AllAreas, secondHalf);
                    File.AppendAllText("Temp/ZandeaPOI.play-validation.txt",
                        $"Stairs foot={foot} nav={stairStart.position} found={footOnMesh}, " +
                        $"mid={midpoint} nav={stairMid.position} found={midOnMesh} first={firstHalf.status} second={secondHalf.status}, " +
                        $"landing={landing} nav={stairEnd.position} found={landingOnMesh}, " +
                        $"route={hasRoute} status={route.status} corners={route.corners.Length}\n");
                    Check(footOnMesh && landingOnMesh &&
                        stairEnd.position.y - stairStart.position.y > 3f && hasRoute &&
                        route.status == NavMeshPathStatus.PathComplete,
                        "NPC navigation crosses the stairs from the lower floor to the landing");
                    var left = p.ToWorld(new Vector2(-43, 25));
                    var right = p.ToWorld(new Vector2(43, 25));
                    var leftTerrain = Terrain.activeTerrains.FirstOrDefault(t => t.terrainData && t.GetComponent<TerrainCollider>() &&
                        left.x >= t.transform.position.x && left.x <= t.transform.position.x + t.terrainData.size.x &&
                        left.z >= t.transform.position.z && left.z <= t.transform.position.z + t.terrainData.size.z);
                    var rightTerrain = Terrain.activeTerrains.FirstOrDefault(t => t.terrainData && t.GetComponent<TerrainCollider>() &&
                        right.x >= t.transform.position.x && right.x <= t.transform.position.x + t.terrainData.size.x &&
                        right.z >= t.transform.position.z && right.z <= t.transform.position.z + t.terrainData.size.z);
                    Check(leftTerrain && rightTerrain &&
                        Mathf.Abs((leftTerrain.SampleHeight(left) + leftTerrain.transform.position.y) -
                                  (rightTerrain.SampleHeight(right) + rightTerrain.transform.position.y)) > 2f,
                        "The authored terrain slope remains beneath the walls");
                    var group = authored.GetComponentsInChildren<KenshiVR.MeleeCombat.BanditGroup>(true)
                        .FirstOrDefault(candidate => candidate.authoredMembers != null &&
                            candidate.authoredMembers.Contains(p.Members[1]));
                    Check(group, "Authored bandit group is present for weapon selection");
                    int memberIndex = Array.IndexOf(group.authoredMembers, p.Members[1]);
                    int triedWeapons = 0;
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab",
                        new[] { "Assets/MeleeCombat/MocapTest/GeneratedVariants" }))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                        var profile = prefab ? prefab.GetComponent<KenshiVR.MeleeCombat.StarvingBandit>() : null;
                        if (!profile) continue;
                        var generated = prefab.GetComponent<KenshiVR.MeleeCombat.NpcGeneratedWeapon>();
                        var selected = generated && generated.weaponPrefab ? generated.weaponPrefab : profile.staff;
                        var sourceDriver = profile.GetComponentInChildren<KenshiVR.MeleeCombat.BanditMocapNpcDriver>(true);
                        var liveDriver = p.Members[1].GetComponentInChildren<KenshiVR.MeleeCombat.BanditMocapNpcDriver>(true);
                        Check(selected && group.EquipAuthoredMember(memberIndex, selected, profile) &&
                            p.Members[1].staff && p.Members[1].staff.name == selected.name &&
                            p.Members[1].staff.State == KenshiVR.MeleeCombat.PhysicalMeleeWeapon.HoldState.HeldByNpc &&
                            (!sourceDriver || liveDriver &&
                                liveDriver.GetComponent<Animator>().runtimeAnimatorController ==
                                    sourceDriver.GetComponent<Animator>().runtimeAnimatorController &&
                                liveDriver.grip.secondaryHandGrips == sourceDriver.grip.secondaryHandGrips &&
                                p.Members[1].attacks.Length == profile.attacks.Length),
                            "Authored bandit equips and adopts the combat profile for " + prefab.name);
                        triedWeapons++;
                    }
                    Check(triedWeapons == 15, "All generated weapon choices are available to an authored bandit");
                    SessionState.SetInt("ZandeaPOILayoutID", authored.GetInstanceID());
                    SessionState.SetInt("ZandeaPOILayoutChildren", authored.childCount);
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) Capture(p);
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
                    Check(p.IsLoaded && p.Members.Count == 4, "Main authored outpost reloads after tile recycling");
                    Check(p.Members[0].GetInstanceID() == SessionState.GetInt("ZandeaPOINpcID", 0) && p.Members[0].health.IsDead, "Main death survives tile recycling");
                    Check(p.Members.Skip(1).All(n => n.agent.isOnNavMesh), "Surviving Main bandits recover navigation");
                    var authored = p.transform.Find("Layout Preview");
                    Check(authored && authored.GetInstanceID() == SessionState.GetInt("ZandeaPOILayoutID", 0) &&
                        authored.childCount == SessionState.GetInt("ZandeaPOILayoutChildren", 0),
                        "Authored settlement and prop hierarchy survive unload and reload without regeneration");
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
                    var structures = p.transform.Find("Layout Preview") ?? p.transform.Find("Bandit Outpost Structures");
                    Check(structures && Vector3.Distance(structures.position, p.Center) < .01f, "Structures retain stable site after origin shift");
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
