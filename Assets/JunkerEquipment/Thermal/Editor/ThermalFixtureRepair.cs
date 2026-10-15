using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalFixtureRepair
    {
        const string Request="Temp/JunkerThermal.repair-fixtures";
        const string Report="Temp/JunkerThermal.repair-result";
        static ThermalFixtureRepair() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(Request);
            try { Repair(); File.WriteAllText(Report,"PASS: board path rebased, pipe origins rebased, pipe starts as one grabbable assembly."); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/Junker Equipment/Thermal/Repair board and pipe fixtures")]
        public static void Repair()
        {
            foreach(var toolPath in new[]{"Assets/MyAssets/prefabs/Tools/PLS-T ScrapCutter.prefab","Assets/JunkerEquipment/Thermal/Generated/ScrapCutter.prefab"})
            {
                if(!AssetDatabase.LoadAssetAtPath<GameObject>(toolPath))continue;
                var tool=PrefabUtility.LoadPrefabContents(toolPath);
                try
                {
                    var fx=tool.GetComponent<ThermalFXController>();
                    if(fx)fx.sparkBackOffset=.1f;
                    PrefabUtility.SaveAsPrefabAsset(tool,toolPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(tool); }
            }
            var path="Assets/JunkerEquipment/Thermal/Generated/WeldablePipes.prefab";
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigurePipe(prefab.GetComponentInChildren<WeldConnection>(true));
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            var scene=SceneManager.GetSceneByPath(ThermalDemoBuilder.ScenePath);
            bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(ThermalDemoBuilder.ScenePath,OpenSceneMode.Additive);
            try
            {
                var demo=scene.GetRootGameObjects().FirstOrDefault(go=>go.name=="Thermal work demonstration");
                if(!demo)throw new InvalidOperationException("Thermal work demonstration root missing.");
                var board=demo.transform.Find("Cuttable metal board");
                var pipe=demo.transform.Find("Reversible pipe weld");
                if(!board || !pipe)throw new InvalidOperationException("Demo board or pipe fixture missing.");
                RebaseBoard(board);
                RebasePipe(pipe);
                ConfigurePipe(pipe.GetComponentInChildren<WeldConnection>(true));
                foreach(var label in demo.GetComponentsInChildren<TextMesh>(true))
                    if(label.text.Contains("Align pipe B with A"))
                        label.text="CUT seam to release pipe B\nAlign and WELD seam to rejoin";
                string backup="Temp/JunkerThermalDemo-before-fixture-repair.unity";
                if(!File.Exists(backup))File.Copy(ThermalDemoBuilder.ScenePath,backup);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
            AssetDatabase.SaveAssets();
        }
        static void RebaseBoard(Transform board)
        {
            var upper=board.Find("Upper frame");
            if(!upper)throw new InvalidOperationException("Board upper frame missing.");
            float offset=upper.localPosition.x;
            if(Mathf.Abs(offset)<1)return;
            board.localPosition+=Vector3.right*offset;
            foreach(string name in new[]{"Left frame","Right frame","Upper frame","Lower frame","Removable centre"})
            {
                var child=board.Find(name);
                if(!child)throw new InvalidOperationException("Board part missing: "+name);
                child.localPosition-=Vector3.right*offset;
            }
        }
        static void RebasePipe(Transform pipe)
        {
            var connection=pipe.GetComponentInChildren<WeldConnection>(true);
            if(!connection || !connection.bodyA || !connection.bodyB)throw new InvalidOperationException("Pipe connection missing.");
            var a=connection.bodyA.transform.Find("Authored pipe geometry");
            var b=connection.bodyB.transform.Find("Authored pipe geometry");
            if(!a || !b)throw new InvalidOperationException("Pipe cylinder geometry missing.");
            float offset=a.localPosition.x;
            if(Mathf.Abs(offset)<1)return;
            pipe.localPosition+=Vector3.right*offset;
            a.localPosition-=Vector3.right*offset;
            b.localPosition-=Vector3.right*offset;
        }
        static void ConfigurePipe(WeldConnection connection)
        {
            if(!connection || !connection.bodyA || !connection.bodyB)throw new InvalidOperationException("Pipe bodies missing.");
            connection.startWelded=true;
            connection.bodyA.isKinematic=false;
            connection.bodyA.useGravity=true;
            var grab=connection.bodyA.GetComponent<XRGrabInteractable>();
            if(!grab)grab=connection.bodyA.gameObject.AddComponent<XRGrabInteractable>();
            grab.retainTransformParent=false;
            grab.useDynamicAttach=true;
            grab.movementType=XRBaseInteractable.MovementType.VelocityTracking;
            connection.grabA=grab;
            EditorUtility.SetDirty(connection);
        }
    }
}
