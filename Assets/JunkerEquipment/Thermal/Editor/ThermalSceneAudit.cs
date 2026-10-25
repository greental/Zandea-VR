using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalSceneAudit
    {
        const string Request="Temp/JunkerThermal.audit";
        static ThermalSceneAudit() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(Request); File.WriteAllText("Temp/JunkerThermal.audit-result",Audit());
        }
        [MenuItem("Zandea/Junker Equipment/Thermal/Audit scene fixtures")]
        public static string Audit()
        {
            var scene=SceneManager.GetSceneByPath(ThermalDemoBuilder.ScenePath);
            var b=new StringBuilder();
            b.AppendLine("scene loaded="+scene.isLoaded+" dirty="+scene.isDirty);
            if(!scene.isLoaded)return b.ToString();
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var board in root.GetComponentsInChildren<CuttableSection>(true))
                {
                    b.AppendLine($"BOARD {board.name} active={board.gameObject.activeInHierarchy} position={board.transform.position} scale={board.transform.lossyScale} released={board.Released} cutout={board.cutout?.name}");
                    foreach(var p in board.paths) if(p && p.Valid)
                    {
                        b.AppendLine($" PATH {p.name} length={p.Length} first={p.PointAtDistance(0)} last={p.PointAtDistance(p.Length)} tolerance={p.tolerance}");
                        foreach(var c in board.GetComponentsInChildren<Collider>(true).Where(c=>c && c.enabled))
                            b.AppendLine($" COLLIDER {c.name} type={c.GetType().Name} bounds={c.bounds} firstDist={Vector3.Distance(c.ClosestPoint(p.PointAtDistance(0)),p.PointAtDistance(0))}");
                    }
                }
                foreach(var w in root.GetComponentsInChildren<WeldConnection>(true))
                {
                    b.AppendLine($"PIPE {w.name} state={w.State} attached={w.Attached} aligned={w.Aligned} A={w.bodyA?.name} B={w.bodyB?.name} gap={(w.anchorA && w.anchorB ? Vector3.Distance(w.anchorA.position,w.anchorB.position):0)}");
                    foreach(var body in new[]{w.bodyA,w.bodyB})if(body)
                    {
                        b.AppendLine($" BODY {body.name} kinematic={body.isKinematic} gravity={body.useGravity} parent={body.transform.parent?.name} pos={body.position} colliders={body.GetComponentsInChildren<Collider>(true).Length}");
                        foreach(var c in body.GetComponentsInChildren<Collider>(true))b.AppendLine($"  SHAPE {c.name} {c.GetType().Name} enabled={c.enabled} convex={(c is MeshCollider m?m.convex:false)} bounds={c.bounds}");
                    }
                }
            }
            return b.ToString();
        }
    }
}
