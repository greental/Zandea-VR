#if UNITY_EDITOR
using MyAssets.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlanetStackEditorTools
{
    [MenuItem("Zandea/Koombar/Planet Terrain/Create Planet Stack")]
    public static void CreatePlanetStack()
    {
        GameObject planetObject = new GameObject(GetUniqueRootName("PlanetStack"));
        Undo.RegisterCreatedObjectUndo(planetObject, "Create Planet Stack");

        PlanetSystemRig rig = planetObject.AddComponent<PlanetSystemRig>();
        planetObject.AddComponent<PlanetDefinition>();
        planetObject.AddComponent<PlanetTransitionState>();
        planetObject.AddComponent<PlanetProxyVisual>();

        rig.BuildMissingPlanetChildren();

        Selection.activeGameObject = planetObject;
        EditorSceneManager.MarkSceneDirty(planetObject.scene);
        Debug.Log("[PlanetStackEditorTools] Created a planet stack with PlanetDefinition, PlanetProxyVisual, PlanetSystemRig, and generated PlanetTerrainTreadmill child. Select the treadmill to configure/generate tiles, then select the proxy to bake its terrain thumbnail.", planetObject);
    }

    static string GetUniqueRootName(string baseName)
    {
        if (GameObject.Find(baseName) == null)
            return baseName;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = baseName + " " + i;
            if (GameObject.Find(candidate) == null)
                return candidate;
        }

        return baseName + " " + System.Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
#endif