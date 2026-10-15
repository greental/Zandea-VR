#if UNITY_EDITOR
using MyAssets.Planet;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetSystemRig))]
public sealed class PlanetSystemRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(iterator);
                continue;
            }

            if (iterator.name == "defaultPlanetRadiusMeters")
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(iterator, new GUIContent("Default Planet Radius Meters"));
                    if (GUILayout.Button("Set For All", GUILayout.Width(90f)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        var rig = (PlanetSystemRig)target;
                        Undo.RecordObject(target, "Set Planet Values From Radius");
                        if (rig.TerrainTransitionController != null)
                            Undo.RecordObject(rig.TerrainTransitionController, "Set Planet Values From Radius");
                        rig.SetAllValuesFromRadius();
                        EditorUtility.SetDirty(target);
                        if (rig.TerrainTransitionController != null)
                            EditorUtility.SetDirty(rig.TerrainTransitionController);
                        serializedObject.Update();
                    }
                }
            }
            else
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Planet Stack Terrain", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Bake Terrain Thumbnail From This Planet Stack"))
                PlanetProxyTerrainThumbnailBaker.BakeFromPlanetStack((PlanetSystemRig)target);
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Proxy terrain baking is disabled while entering or running Play Mode.", MessageType.Info);
    }
}
#endif
