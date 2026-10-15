using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Text;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class CharacterCreatorSceneBuilder
    {
        const string ModelPath = "Assets/CharacterCreator/Models/male_creator.glb";
        const string ScenePath = "Assets/CharacterCreator/CharacterCreatorMorphPOC.unity";

        [InitializeOnLoadMethod]
        static void CreateMissingSceneAfterReload()
        {
            EditorApplication.delayCall += () =>
            {
                ReportImport();
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists(ScenePath)) Build();
            };
        }

        static void ReportImport()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) return;
            var report = new StringBuilder();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                report.Append(renderer.name).Append(" | mesh: ").Append(renderer.sharedMesh ? renderer.sharedMesh.name : "none");
                foreach (var material in renderer.sharedMaterials)
                    report.Append(" | material: ").Append(material ? material.name : "none")
                          .Append(" | shader: ").Append(material ? material.shader.name : "none")
                          .Append(" | diffuseTexture: ").Append(material && material.HasProperty("diffuseTexture"))
                          .Append(" | diffuseFactor: ").Append(material && material.HasProperty("diffuseFactor"));
                if (renderer.sharedMesh)
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                        report.Append(" | morph: ").Append(renderer.sharedMesh.GetBlendShapeName(i));
                report.AppendLine();
            }
            File.WriteAllText("Temp/CharacterCreatorImportReport.txt", report.ToString());
        }

        [MenuItem("Tools/Character Creator/Build POC Scene")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) { Debug.LogError("Could not import " + ModelPath); return; }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var character = new GameObject("Character Turntable");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Male GLB instance";
            visual.transform.SetParent(character.transform, false);
            visual.transform.localPosition = Vector3.zero;
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Debug.Log("Creator mesh: " + renderer.name + " / " + (renderer.sharedMesh ? renderer.sharedMesh.name : "none") +
                          " / shapes " + (renderer.sharedMesh ? renderer.sharedMesh.blendShapeCount : 0) +
                          " / materials " + string.Join(", ", System.Array.ConvertAll(renderer.sharedMaterials, m => m ? m.name : "null")));
            var appearance = character.AddComponent<CharacterAppearanceController>();
            appearance.visualRoot = visual.transform;
            var cosmetics = character.AddComponent<CharacterCosmeticsController>();
            cosmetics.appearance = appearance;
            cosmetics.skeletonRoot = visual.transform;
            cosmetics.catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>("Assets/CharacterCreator/CosmeticCatalog.asset");
            var creator = new GameObject("Character Creator").AddComponent<CharacterCreatorPOC>();
            creator.character = appearance; creator.turntable = character.transform;

            var camera = new GameObject("Preview Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.45f, 3.8f);
            camera.transform.LookAt(new Vector3(.45f, 1.1f, 0));
            var light = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(38, -25, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview Floor"; floor.transform.localScale = Vector3.one * .5f;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            Debug.Log("Created " + ScenePath + ". Press Play for the world-space creator UI.");
        }
    }
}
