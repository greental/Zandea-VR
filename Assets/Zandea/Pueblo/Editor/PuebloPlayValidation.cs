using System;
using System.IO;
using System.Linq;
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
    public static class PuebloPlayValidation
    {
        const string ReviewPath = "Assets/Scenes/Zandea/PuebloSettlementReview.unity";
        const string Report = "Temp/ZandeaPueblo.play-validation.txt";
        const string Step = "ZandeaPuebloValidationStep";
        const string Earliest = "ZandeaPuebloValidationEarliest";
        const string Deadline = "ZandeaPuebloValidationDeadline";

        static PuebloPlayValidation() => EditorApplication.update += Tick;

        [MenuItem("Zandea/POI/Pueblo/Validate Review Scene")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode first.");
            if (SceneManager.GetActiveScene().path != ReviewPath)
            {
                // Preserve any open authored scene before switching to the focused review.
                if (!EditorSceneManager.SaveOpenScenes())
                    throw new InvalidOperationException("Could not save the currently open scenes.");
                EditorSceneManager.OpenScene(ReviewPath, OpenSceneMode.Single);
            }
            File.WriteAllText(Report, "RUNNING Pueblo review validation\n");
            SessionState.SetInt(Step, 1);
            SessionState.SetFloat(Earliest, (float)(EditorApplication.timeSinceStartup + 3));
            SessionState.SetFloat(Deadline, (float)(EditorApplication.timeSinceStartup + 45));
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (SessionState.GetInt(Step, 0) == 0 || !EditorApplication.isPlaying ||
                EditorApplication.timeSinceStartup < SessionState.GetFloat(Earliest, 0)) return;
            var poi = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).FirstOrDefault(p => p.poiId == "zandea.pueblo.settlement.1.review");
            if (!poi || !poi.IsLoaded)
            {
                if (EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0)) return;
                Fail("Pueblo review POI did not load within 45 seconds.");
                return;
            }
            try
            {
                Check(poi.Members.Count == 6, "six hostile bandits inhabit the settlement");
                var population = poi.GetComponent<OutpostPropPopulation>();
                Check(population && population.Instances.Count >= 16, "contextual props populate settlement");
                var props = population.Instances.Where(p => p).ToArray();
                Check(props.Count(p => p.name.Contains("pot")) >= 6, "pots appear across ramadas");
                Check(props.Count(p => p.name.Contains("basket")) >= 3, "wicker baskets appear in settlement");
                Check(props.Any(p => p.name.Contains("barrel")) && props.Any(p => p.name.Contains("chest")) &&
                      props.Any(p => p.name.Contains("table")), "storage and social props appear");
                var outside = poi.GatePosition + poi.ApproachDirection * 5;
                Check(NavMesh.SamplePosition(outside, out var start, 3, NavMesh.AllAreas), "approach has navigation");
                Check(NavMesh.SamplePosition(poi.Center, out var end, 3, NavMesh.AllAreas), "courtyard has navigation");
                var path = new NavMeshPath();
                Check(NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                      path.status == NavMeshPathStatus.PathComplete, "gate connects to courtyard");
                foreach (var sample in new[] { poi.Center, poi.GatePosition })
                    Check(Terrain.activeTerrains.Any(t => t && t.enabled && t.GetComponent<TerrainCollider>()?.enabled == true &&
                        sample.x >= t.transform.position.x && sample.x <= t.transform.position.x + t.terrainData.size.x &&
                        sample.z >= t.transform.position.z && sample.z <= t.transform.position.z + t.terrainData.size.z),
                        "terrain and collider cover " + sample);
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    PuebloEditorRequest.CaptureReview(true);
                File.AppendAllText(Report, "COMPLETE; Main streaming and headset feel still require checks.\n");
                SessionState.SetInt(Step, 0);
                EditorApplication.isPlaying = false;
            }
            catch (Exception error) { Fail(error.ToString()); }
        }

        static void Check(bool success, string label)
        {
            if (!success) throw new InvalidOperationException(label);
            File.AppendAllText(Report, "PASS " + label + "\n");
        }

        static void Fail(string reason)
        {
            File.AppendAllText(Report, "FAIL " + reason + "\n");
            SessionState.SetInt(Step, 0);
            EditorApplication.isPlaying = false;
        }
    }
}
