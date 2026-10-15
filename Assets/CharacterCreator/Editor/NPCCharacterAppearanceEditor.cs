using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    [CustomEditor(typeof(NPCCharacterAppearance))]
    public sealed class NPCCharacterAppearanceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var npc = (NPCCharacterAppearance)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!npc.appearance))
            {
                if (GUILayout.Button("Apply preset") && npc.preset)
                { Apply(npc, () => npc.ApplyPreset(npc.preset)); }
                if (GUILayout.Button("Generate from archetype") && npc.archetype)
                { Apply(npc, () => npc.Generate(npc.seed)); }
                if (GUILayout.Button("Generate with next seed") && npc.archetype)
                { Undo.RecordObject(npc, "Change NPC seed"); npc.seed++; Apply(npc, () => npc.Generate(npc.seed)); }
                if (GUILayout.Button("Save current as preset"))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save NPC appearance", "NPC Appearance", "asset", "Choose a preset path");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var preset = ScriptableObject.CreateInstance<AppearancePreset>();
                        preset.appearance = npc.appearance.Data;
                        if (npc.cosmetics)
                        {
                            preset.headwear = npc.cosmetics.headwear; preset.torso = npc.cosmetics.torso;
                            preset.legs = npc.cosmetics.legs; preset.feet = npc.cosmetics.feet;
                            preset.hands = npc.cosmetics.hands; preset.outerArmor = npc.cosmetics.outerArmor;
                        }
                        AssetDatabase.CreateAsset(preset, path); AssetDatabase.SaveAssets();
                        Undo.RecordObject(npc, "Assign NPC appearance preset"); npc.preset = preset;
                        EditorUtility.SetDirty(npc);
                    }
                }
            }
        }
        static void Apply(NPCCharacterAppearance npc, System.Action action)
        {
            Undo.RecordObject(npc.appearance, "Change NPC appearance");
            if (npc.cosmetics) Undo.RecordObject(npc.cosmetics, "Change NPC clothing");
            action();
            EditorUtility.SetDirty(npc.appearance);
            PrefabUtility.RecordPrefabInstancePropertyModifications(npc.appearance);
            if (npc.cosmetics)
            {
                EditorUtility.SetDirty(npc.cosmetics);
                PrefabUtility.RecordPrefabInstancePropertyModifications(npc.cosmetics);
            }
            SceneView.RepaintAll();
        }
    }

    [CustomEditor(typeof(NPCFacialExpressionController))]
    public sealed class NPCFacialExpressionControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (NPCFacialExpressionController)target;
            if (controller.previewExpression != ExpressionType.Neutral && !controller.HasMorph(controller.previewExpression))
                EditorGUILayout.HelpBox("This expression needs the " + "Expr" + controller.previewExpression + " blend shape on the head mesh. Preview is intentionally neutral until that asset is supplied.", MessageType.Info);
        }
    }
}
