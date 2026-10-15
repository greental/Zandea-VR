using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ScavengerNPC.Editor
{
    [InitializeOnLoad]
    public static class ScavengerVisualDiagnostics
    {
        const string Request = "Temp/Scavenger-diagnose.request";
        static ScavengerVisualDiagnostics() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            Diagnose();
        }

        [MenuItem("Tools/Scavenger NPC/Diagnose visual rig")]
        public static void Diagnose()
        {
            var report = new StringBuilder();
            foreach (var path in new[] { "Scavenger_JKR88", "Scavenger_PKO" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ScavengerNPC/Generated/" + path + ".prefab");
                if (prefab) Describe(prefab, report, "Prefab " + path);
            }
            foreach (var enemy in Object.FindObjectsByType<ScavengerEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (enemy.gameObject.scene.IsValid()) Describe(enemy.gameObject, report, "Scene " + enemy.gameObject.scene.path);
            File.WriteAllText("Library/Scavenger-visual.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        static void Describe(GameObject root, StringBuilder report, string label)
        {
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var pose = root.GetComponent<ScavengerPose>();
            report.AppendLine(label + " / " + root.name);
            if (!skin || !skin.sharedMesh || !pose) { report.AppendLine("Missing skin, mesh, or pose"); return; }
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            report.AppendLine($"Root scale={root.transform.lossyScale}, renderer scale={skin.transform.lossyScale}, mesh={skin.sharedMesh.name}, bindposes={skin.sharedMesh.bindposes.Length}, bones={skin.bones.Length}");
            report.AppendLine($"Baked local bounds={mesh.bounds}, scene bounds={skin.bounds}, hips={pose.hips.position}, head={pose.head.position}, right hand={pose.rightHand.position}");
            if (pose.gun) report.AppendLine($"Gun distance to hand={Vector3.Distance(pose.gun.position, pose.rightHand.position):F3}m, scale={pose.gun.lossyScale}");
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                var changes = PrefabUtility.GetPropertyModifications(root) ?? new PropertyModification[0];
                foreach (var change in changes.Where(c => c.propertyPath.Contains("m_LocalScale") || c.propertyPath.Contains("m_LocalRotation") || c.propertyPath.Contains("m_Bones") || c.propertyPath.Contains("m_Mesh")))
                    report.AppendLine($"Override {change.target?.name}.{change.propertyPath}={change.value}");
            }
            Object.DestroyImmediate(mesh);
        }
    }
}
