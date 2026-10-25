using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class FittingsSceneReport
    {
        const string Request = "Temp/FittingsSceneReport.request";
        const string Output = "Temp/FittingsSceneReport.txt";

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Request)) return;
                File.Delete(Request);
                Export();
            };
        }

        [MenuItem("Zandea/Character Creator/Export Fittings Scene Report")]
        public static void Export()
        {
            var scene = EditorSceneManager.GetSceneByPath("Assets/Scenes/Fittings.unity");
            bool openedForReport = !scene.isLoaded;
            if (openedForReport) scene = EditorSceneManager.OpenScene("Assets/Scenes/Fittings.unity", OpenSceneMode.Additive);
            try
            {
            var text = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith("CombinedStarvingBandit")) continue;
                text.AppendLine("CHARACTER " + root.name);
                var cosmetics = root.GetComponent<CharacterCosmeticsController>();
                if (cosmetics)
                    text.AppendLine("SLOTS " + Name(cosmetics.headwear) + " | " + Name(cosmetics.torso) + " | " +
                        Name(cosmetics.legs) + " | " + Name(cosmetics.feet) + " | " + Name(cosmetics.hands) +
                        " | " + Name(cosmetics.outerArmor));
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child == root.transform) continue;
                    if (!child.name.StartsWith("Appearance ") && !child.name.StartsWith("wrapped_boot")) continue;
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    var marker = child.GetComponent<AppearanceSpawnedItem>();
                    text.AppendLine("ITEM " + child.name + " | source=" + AssetDatabase.GetAssetPath(source) +
                        " | parent=" + child.parent.name + " | marker=" +
                        (marker ? marker.slot + ":" + Name(marker.definition) : "none"));
                    AppendTransform(text, child);
                    foreach (var nested in child.GetComponentsInChildren<Transform>(true))
                    {
                        if (nested == child) continue;
                        if (nested.parent == child) AppendTransform(text, nested);
                    }
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                    {
                        text.Append("  MATERIAL " + renderer.name);
                        foreach (var material in renderer.sharedMaterials)
                            text.Append(" | " + AssetDatabase.GetAssetPath(material));
                        text.AppendLine();
                    }
                }
            }
            File.WriteAllText(Output, text.ToString());
            Debug.Log("Fittings scene report written to " + Output);
            }
            finally { if (openedForReport) EditorSceneManager.CloseScene(scene, true); }
        }

        static string Name(Object obj) => obj ? obj.name : "none";
        static void AppendTransform(StringBuilder text, Transform transform)
        {
            text.AppendLine("  TRANSFORM " + transform.name + " p=" + transform.localPosition.ToString("F6") +
                " r=" + transform.localRotation.ToString("F6") + " s=" + transform.localScale.ToString("F6"));
        }
    }
}
 
