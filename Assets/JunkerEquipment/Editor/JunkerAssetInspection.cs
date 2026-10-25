using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace JunkerEquipment.Editor
{
    public static class JunkerAssetInspection
    {
        [MenuItem("Zandea/Junker Equipment/Inspect source models")]
        public static void Inspect()
        {
            var s = new StringBuilder();
            for(int i=0;i<SceneManager.sceneCount;i++) { var scene=SceneManager.GetSceneAt(i); s.AppendLine($"SCENE {scene.path} dirty={scene.isDirty} playing={EditorApplication.isPlaying}"); }
            var paths = AssetDatabase.FindAssets("", new[]{"Assets/JunkerEquipment/Hands", "Assets/MyAssets/mesh/Armor/Gloves", "Assets/MyAssets/mesh/tools", "Assets/MyAssets/mesh/Pyxol"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".fbx") || p.EndsWith(".glb")).Concat(new[]{"Assets/MyAssets/prefabs/weapons/PKO.prefab", "Assets/Animated Hands/Prefabs/Left Hand Model.prefab", "Assets/Animated Hands/Prefabs/Right Hand Model.prefab"});
            foreach(var path in paths)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path); s.AppendLine("ASSET " + path); if(!prefab){s.AppendLine("NOT IMPORTED");continue;}
                foreach(var t in prefab.GetComponentsInChildren<Transform>(true)) s.AppendLine($"  {AnimationUtility.CalculateTransformPath(t,prefab.transform)} p={t.localPosition:F4} r={t.localEulerAngles:F1} scale={t.localScale:F4}");
                foreach(var r in prefab.GetComponentsInChildren<Renderer>(true)) { var sk=r as SkinnedMeshRenderer; var mf=r.GetComponent<MeshFilter>(); var mesh=sk?sk.sharedMesh:mf?mf.sharedMesh:null; s.AppendLine($" R {r.name} {r.GetType().Name} bounds={r.bounds} verts={(mesh?mesh.vertexCount:0)} bones={(sk?sk.bones.Length:0)} materials={string.Join(",",r.sharedMaterials.Select(m=>m?m.name+"/"+m.shader.name:"NULL"))}"); }
                foreach(var a in prefab.GetComponentsInChildren<Animator>(true)) s.AppendLine($" ANIMATOR {a.name} controller={a.runtimeAnimatorController}");
            }
            File.WriteAllText("Temp/JunkerEquipment.inspection",s.ToString());
        }
        [MenuItem("Zandea/Junker Equipment/Inspect scene interactions")]
        public static void Interactions()
        {
            var s=new StringBuilder();var body=UnityEngine.Object.FindFirstObjectByType<JunkerBody.VRBodyController>();
            s.AppendLine("LEFT "+AnimationUtility.CalculateTransformPath(body.leftController,body.xrOrigin));s.AppendLine("RIGHT "+AnimationUtility.CalculateTransformPath(body.rightController,body.xrOrigin));
            foreach(var c in body.xrOrigin.GetComponentsInChildren<Component>(true))
            {
                if(!c || !(c is Collider || c.GetType().Name.Contains("Interactor") || c.GetType().Name.Contains("Caster"))) continue;
                s.AppendLine(c.GetType().Name+" "+AnimationUtility.CalculateTransformPath(c.transform,body.xrOrigin)+" active="+c.gameObject.activeInHierarchy);
                var so=new SerializedObject(c);var p=so.GetIterator();while(p.NextVisible(true)) if(p.name.Contains("TriggerInteraction"))s.AppendLine("  "+p.name+"="+p.intValue);
            }
            File.WriteAllText("Temp/JunkerEquipment.interactions",s.ToString());
        }
    }
}
