using System;
using System.IO;
using System.Linq;
using JunkerBody;
using MyAssets.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Zandea.POI;
using Zandea.Props;

namespace Zandea.Pueblo.Editor
{
    [InitializeOnLoad]
    public static class PuebloMainPlayValidation
    {
        const string Main = "Assets/Main.unity";
        const string Report = "Temp/ZandeaPueblo.main-validation.txt";
        const string Step = "ZandeaPuebloMainStep";
        const string Next = "ZandeaPuebloMainNext";
        const string Deadline = "ZandeaPuebloMainDeadline";
        const string NpcId = "ZandeaPuebloMainNpcId";
        const string PoiId = "zandea.pueblo.settlement.1";

        static PuebloMainPlayValidation() { EditorApplication.update += Tick; }

        [MenuItem("Zandea/POI/Pueblo/Validate Main Streaming")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before Main validation.");
            if (SceneManager.GetActiveScene().path != Main)
            {
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("Could not save open scenes before Main validation.");
                EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            }
            File.WriteAllText(Report, "RUNNING Pueblo Main treadmill validation\n");
            SessionState.SetInt(Step, 1);
            SessionState.SetFloat(Next, (float)(EditorApplication.timeSinceStartup + 4));
            SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            int step = SessionState.GetInt(Step, 0);
            if (step == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling ||
                EditorApplication.timeSinceStartup < SessionState.GetFloat(Next, 0)) return;
            SessionState.SetFloat(Next, (float)(EditorApplication.timeSinceStartup + 2));
            try
            {
                var poi = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).FirstOrDefault(p => p.poiId == PoiId);
                if (!poi) throw new InvalidOperationException("Main is missing the Pueblo POI.");
                var player = UnityEngine.Object.FindFirstObjectByType<VRBodyController>();
                if (!player || !player.xrOrigin)
                    throw new InvalidOperationException("Main has no active walking player's XR origin.");
                if (step == 1)
                {
                    var transition = UnityEngine.Object.FindObjectsByType<PlanetTerrainTransitionController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(t => t.gameObject.scene == poi.gameObject.scene);
                    if (transition) transition.ForceEnterSurfaceMode();
                    player.xrOrigin.position = poi.Center + poi.ApproachDirection * 45 + Vector3.up * 1.7f;
                    poi.treadmill.ConfigureTarget(player.xrOrigin);
                    File.AppendAllText(Report, "Approach=" + player.xrOrigin.position +
                        " site=" + poi.stableSite + " tile=" + poi.treadmill.CenterTile + "\n");
                    SessionState.SetInt(Step, 2);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 2)
                {
                    if (!poi.IsLoaded || poi.Members.Count != 6)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Pueblo did not load six bandits after approach: " +
                            "loaded=" + poi.IsLoaded + " members=" + poi.Members.Count +
                            " tile=" + poi.treadmill.CenterTile);
                    }
                    Check(poi.Members.All(n => n && n.agent && n.agent.isOnNavMesh),
                        "six Main bandits stand on navigation");
                    var terrain = Terrain.activeTerrains.FirstOrDefault(t => t && t.enabled && t.terrainData &&
                        t.GetComponent<TerrainCollider>()?.enabled == true &&
                        poi.Center.x >= t.transform.position.x &&
                        poi.Center.x <= t.transform.position.x + t.terrainData.size.x &&
                        poi.Center.z >= t.transform.position.z &&
                        poi.Center.z <= t.transform.position.z + t.terrainData.size.z);
                    Check(terrain && Mathf.Abs(terrain.SampleHeight(poi.Center) +
                        terrain.transform.position.y - poi.Center.y) < .5f,
                        "stamped Main terrain and collider support the Pueblo center");
                    var props = poi.GetComponent<OutpostPropPopulation>();
                    Check(props && props.Instances.Count >= 16,
                        "Main Pueblo populates pots, baskets, and storage props");
                    var path = new NavMeshPath();
                    Check(NavMesh.SamplePosition(poi.GatePosition + poi.ApproachDirection * 5,
                        out var start, 3, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(poi.Center, out var end, 3, NavMesh.AllAreas) &&
                        NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete,
                        "Main gate connects to the courtyard");
                    var dead = poi.Members[0];
                    Check(dead.health, "bandit health exists");
                    SessionState.SetInt(NpcId, dead.GetInstanceID());
                    dead.health.ForceDeath();
                    Check(dead.health.IsDead, "bandit death state is recorded");
                    player.xrOrigin.position = poi.Center + Vector3.right * 2800 + Vector3.up * 1.7f;
                    File.AppendAllText(Report, "Depart target tile=" + poi.treadmill.WorldToTile(player.xrOrigin.position) +
                        " center=" + poi.treadmill.CenterTile + " treadmillEnabled=" + poi.treadmill.enabled +
                        " active=" + poi.treadmill.gameObject.activeInHierarchy +
                        " tileSize=" + poi.treadmill.TileSizeMeters + "\n");
                    SessionState.SetInt(Step, 3);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 3)
                {
                    if (poi.IsLoaded)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Pueblo did not unload after leaving its terrain tile: " +
                            "observer=" + player.xrOrigin.position + " target=" + poi.treadmill.Target.position +
                            " centerTile=" + poi.treadmill.CenterTile + " targetTile=" +
                            poi.treadmill.WorldToTile(poi.treadmill.Target.position) +
                            " treadmillEnabled=" + poi.treadmill.enabled +
                            " active=" + poi.treadmill.gameObject.activeInHierarchy + " poi=" + poi.Center);
                    }
                    File.AppendAllText(Report, "PASS Pueblo structures unload with distant terrain\n");
                    player.xrOrigin.position = poi.Center + poi.ApproachDirection * 45 + Vector3.up * 1.7f;
                    SessionState.SetInt(Step, 4);
                    SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 65));
                    return;
                }
                if (step == 4)
                {
                    if (!poi.IsLoaded)
                    {
                        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                        throw new InvalidOperationException("Pueblo did not reload after returning to its site.");
                    }
                    Check(poi.Members.Count == 6 &&
                        poi.Members[0].GetInstanceID() == SessionState.GetInt(NpcId, 0) &&
                        poi.Members[0].health.IsDead,
                        "same dead bandit persists through terrain recycling");
                    File.AppendAllText(Report, "COMPLETE; headset traversal and visual feel remain manual checks.\n");
                    SessionState.SetInt(Step, 0);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception error)
            {
                File.AppendAllText(Report, "FAIL " + error + "\n");
                SessionState.SetInt(Step, 0);
                EditorApplication.isPlaying = false;
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            File.AppendAllText(Report, "PASS " + label + "\n");
        }
    }
}
