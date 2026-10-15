#if UNITY_EDITOR
using MyAssets.Planet;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTerrainTransitionController))]
public sealed class PlanetTerrainTransitionControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var controller = (PlanetTerrainTransitionController)target;
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Treadmill Reveal Live Diagnostics", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Reveal Start Y at Altitude 0 (m)", controller.TreadmillRevealStartY);
            EditorGUILayout.FloatField("Highest Treadmill Y (m)", controller.TreadmillRevealEndY);
            EditorGUILayout.FloatField("Travel per 1 m Altitude Descent (m)", controller.TreadmillRevealMetersPerAltitudeMeter);
            EditorGUILayout.FloatField("Current Treadmill Y (m)", controller.TreadmillCurrentY);
            EditorGUILayout.FloatField("Y Change Since Previous Frame (m)", controller.TreadmillYChange);
        }

        if (EditorApplication.isPlaying)
            Repaint();
    }
}
#endif
