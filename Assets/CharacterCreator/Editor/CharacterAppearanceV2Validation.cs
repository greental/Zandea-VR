using System;
using System.IO;
using System.Text;
using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class CharacterAppearanceV2Validation
    {
        [InitializeOnLoadMethod]
        static void Queue() => EditorApplication.delayCall += Run;

        [MenuItem("Tools/Character Creator/Validate V2 appearance")]
        public static void Run()
        {
            const string modelPath = "Assets/CharacterCreator/Models/male_creator.glb";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (!asset) return;
            var report = new StringBuilder();
            GameObject root = null;
            try
            {
                root = new GameObject("Temporary V2 appearance validation");
                root.hideFlags = HideFlags.HideAndDontSave;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                model.transform.SetParent(root.transform, false);
                var appearance = root.AddComponent<CharacterAppearanceController>();
                appearance.visualRoot = model.transform; appearance.Initialize();
                Check(appearance.IsSupported(AppearanceProperty.Weight), "Weight morph", report);
                Check(appearance.IsSupported(AppearanceProperty.Muscularity), "Muscularity morph", report);
                Check(appearance.IsSupported(AppearanceProperty.NoseTip), "Nose tip morph", report);
                Check(appearance.IsSupported(AppearanceProperty.EyebrowHeight), "Brow morph", report);
                Check(appearance.IsSupported(AppearanceProperty.EyeShape), "Eye shape morph", report);
                Check(appearance.IsSupported(AppearanceProperty.MouthWidth), "Mouth morph", report);
                Check(appearance.IsSupported(AppearanceProperty.SkinColor), "Skin material", report);
                Check(appearance.IsSupported(AppearanceProperty.EyeColor), "Eye material", report);
                var data = appearance.Data; data.weight = 1f; data.muscularity = 1f; data.eyeColor = Color.green;
                appearance.Apply(data);
                var block = new MaterialPropertyBlock();
                bool bodyWeight = false, eyeTexture = false, corneaTexture = false, expressions = false;
                int validEyeMaterials = 0;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.sharedMesh) continue;
                    int heavy = renderer.sharedMesh.GetBlendShapeIndex("WeightHeavy");
                    if (heavy >= 0 && renderer.sharedMesh.GetBlendShapeFrameWeight(heavy, 0) == 1f &&
                        Mathf.Approximately(renderer.GetBlendShapeWeight(heavy), 1f)) bodyWeight = true;
                    expressions |= renderer.sharedMesh.GetBlendShapeIndex("ExprHappy") >= 0;
                    if (renderer.sharedMaterials.Length == 0 || !renderer.sharedMaterials[0]) continue;
                    string materialName = renderer.sharedMaterials[0].name;
                    renderer.GetPropertyBlock(block, 0);
                    if (materialName.StartsWith("Std_Eye_")) eyeTexture |= block.GetTexture("diffuseTexture") != null;
                    if (materialName.StartsWith("Std_Cornea_")) corneaTexture |= block.GetTexture("diffuseTexture") != null;
                    if ((materialName.StartsWith("Std_Eye_") || materialName.StartsWith("Std_Cornea_")) &&
                        renderer.sharedMaterial.shader && renderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader" &&
                        !materialName.Contains("iris preview")) validEyeMaterials++;
                    if (materialName.StartsWith("Std_Eye_R"))
                        report.AppendLine("INFO: right eye bounds " + renderer.bounds.center + " / " + renderer.bounds.extents);
                }
                Check(bodyWeight, "Morph frame uses 0–1 weights", report);
                Check(eyeTexture, "Iris texture override assigned", report);
                Check(corneaTexture, "Visible cornea iris texture override assigned", report);
                Check(validEyeMaterials == 4, "Imported eye and cornea materials retained", report);
                Check(expressions, "Expression morph targets", report);
                var serializedAppearance = new SerializedObject(appearance);
                serializedAppearance.Update();
                var chosenSkin = new Color(.72f, .5f, .38f);
                serializedAppearance.FindProperty("appearance").FindPropertyRelative("skinColor").colorValue = chosenSkin;
                serializedAppearance.ApplyModifiedProperties();
                appearance.Preview();
                var skinRenderer = Array.Find(model.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                    renderer => renderer.sharedMaterial && renderer.sharedMaterial.name.StartsWith("Std_Skin_Head"));
                bool manualSkinApplied = false;
                if (skinRenderer)
                {
                    skinRenderer.GetPropertyBlock(block, 0);
                    manualSkinApplied = Vector4.Distance(block.GetColor("diffuseFactor"), chosenSkin) < .01f;
                }
                Check(manualSkinApplied, "Manual Inspector skin color reaches head material", report);
                data = appearance.Data; data.jawWidth = .1f; data.weight = .1f; data.muscularity = .1f;
                appearance.Apply(data);
                int fittedTeeth = 0;
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.sharedMesh) continue;
                    int index = renderer.sharedMesh.GetBlendShapeIndex("TeethNarrow");
                    if (index >= 0 && renderer.GetBlendShapeWeight(index) > .5f) fittedTeeth++;
                }
                Check(fittedTeeth == 2, "Upper and lower teeth narrow with small jaw", report);
                var randomSkin = new CharacterAppearanceData();
                CharacterAppearanceRandomizer.Skin(randomSkin, new System.Random(5));
                Check(randomSkin.skinColor.r > randomSkin.skinColor.g &&
                    randomSkin.skinColor.g > randomSkin.skinColor.b,
                    "Random skin stays in the skin tone palette", report);
                var facial = root.AddComponent<NPCFacialExpressionController>();
                facial.visualRoot = model.transform;
                var jaw = Array.Find(model.GetComponentsInChildren<Transform>(true),
                    bone => bone.name == "CC_Base_JawRoot_040");
                var closedJaw = jaw ? jaw.localRotation : Quaternion.identity;
                facial.SetExpression(ExpressionType.Surprised, 1f, 0f);
                Check(jaw && Quaternion.Angle(closedJaw, jaw.localRotation) > 10f,
                    "Surprised expression opens jaw bone", report);
                facial.ResetExpression(0f);
                var archetype = AssetDatabase.LoadAssetAtPath<AppearanceArchetype>("Assets/CharacterCreator/Archetypes/Starving Bandit.asset");
                if (archetype)
                {
                    var a = archetype.Generate(new System.Random(314), appearance);
                    var b = archetype.Generate(new System.Random(314), appearance);
                    var c = archetype.Generate(new System.Random(315), appearance);
                    Check(JsonUtility.ToJson(a.appearance) == JsonUtility.ToJson(b.appearance), "Same seed reproduces appearance", report);
                    Check(JsonUtility.ToJson(a.appearance) != JsonUtility.ToJson(c.appearance), "Different seed varies appearance", report);
                }
                else report.AppendLine("PENDING: archetype assets have not imported yet");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); }
            finally { if (root) UnityEngine.Object.DestroyImmediate(root); }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/CharacterCreatorV2Validation.txt", report.ToString());
            Debug.Log("Character appearance V2 validation:\n" + report);
        }
        static void Check(bool condition, string label, StringBuilder report) =>
            report.AppendLine((condition ? "PASS: " : "FAIL: ") + label);
    }
}
