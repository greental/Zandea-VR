using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalFixturePlayCheck
    {
        const string Request="Temp/JunkerThermal.fixture-play-check";
        const string Report="Temp/JunkerThermal.fixture-play-result";
        const string Flag="JunkerThermal.FixturePlayCheck";
        static ThermalFixturePlayCheck() { EditorApplication.update+=Poll; EditorApplication.playModeStateChanged+=Changed; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(Request); SessionState.SetBool(Flag,true); EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Flag,false))return;
            SessionState.SetBool(Flag,false);
            new GameObject("Thermal fixture play check").AddComponent<Runner>();
        }
        sealed class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                yield return null; yield return new WaitForFixedUpdate();
                ThermalPathHeatView heat=null;
                bool ready=false;
                try
                {
                    var scene=SceneManager.GetSceneByPath(ThermalDemoBuilder.ScenePath);
                    var demo=scene.GetRootGameObjects().First(g=>g.name=="Thermal work demonstration");
                    var pipe=demo.GetComponentInChildren<WeldConnection>(true);
                    Check(pipe && pipe.Attached && pipe.State==WeldState.Welded,"pipe starts welded");
                    Check(pipe.bodyB.transform.parent==pipe.bodyA.transform && pipe.bodyB.isKinematic && !pipe.bodyB.detectCollisions,"pipe B is one non-simulated child");
                    Check(pipe.bodyB.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled) && pipe.grabA && pipe.grabA.enabled && pipe.grabB && !pipe.grabB.enabled,"only whole assembly is grabbable");
                    var board=demo.GetComponentsInChildren<CuttableSection>(true).First();
                    var path=board.Current;
                    Check(path && path.Valid && board.Accepts(ThermalMode.Cut),"board accepts CUT");
                    var first=path.PointAtDistance(0);
                    float gap=board.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled).Min(c=>Vector3.Distance(first,c.ClosestPoint(first)));
                    Check(gap<.02f,"board start point reaches metal collider (gap "+gap+")");
                    Check(board.Work(ThermalMode.Cut,first,1,"",.05f,1),"board cut progress advances");
                    var fx=demo.GetComponentInChildren<ThermalFXController>(true);
                    Check(fx && fx.PathHeatMaterial,"heated seam material available");
                    heat=path.gameObject.AddComponent<ThermalPathHeatView>();
                    heat.Initialize(fx.PathHeatMaterial); heat.Record(path.Distance,Vector3.back,ThermalMode.Cut);
                    ready=true;
                }
                catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
                if(ready)
                {
                    yield return null;
                    try
                    {
                        Check(heat.GetComponent<MeshRenderer>().enabled && heat.GetComponent<MeshFilter>().sharedMesh.vertexCount>2,"heated seam ribbon renders");
                        File.WriteAllText(Report,"PASS: pipe initial attachment, one-body pickup, board cutting, and heated path ribbon.");
                    }
                    catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
                }
                EditorApplication.isPlaying=false;
            }
            static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
        }
    }
}
