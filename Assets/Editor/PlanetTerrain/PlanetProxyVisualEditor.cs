#if UNITY_EDITOR
using MyAssets.Planet;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetProxyVisual))]
public sealed class PlanetProxyVisualEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Planet Proxy Terrain", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Bake Terrain Thumbnail From This Planet"))
                PlanetProxyTerrainThumbnailBaker.BakeFromProxy((PlanetProxyVisual)target);
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Proxy terrain baking is disabled while entering or running Play Mode.", MessageType.Info);
    }
}
#endif