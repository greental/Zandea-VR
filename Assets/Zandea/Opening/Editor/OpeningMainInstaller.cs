using System;
using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Zandea.Opening.Editor
{
    // One-time authoring action. Saved Main contains ordinary serialized scene objects.
    public static class OpeningMainInstaller
    {
        public const string MainPath = "Assets/Main.unity";

        [MenuItem("Zandea/Opening/Install encounter in Main")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().Any(root => root.name == "Zandea opening encounter"))
            {
                Debug.Log("Opening encounter already installed in Main. Existing authoring preserved.");
                return;
            }
            var players = scene.GetRootGameObjects()
                .Select(root => new { root, body = root.GetComponentInChildren<VRBodyController>(true) })
                .Where(item => item.body).ToArray();
            if (players.Length != 1)
                throw new InvalidOperationException("Main must contain exactly one player VRBodyController root; found " + players.Length);
            var player = players[0].root;
            var center = player.transform.position;
            OpeningHarnessBuilder.Populate(scene, player, center, false);
            EditorSceneManager.SaveScene(scene, MainPath);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != MainPath).ToArray();
            var scenes = new EditorBuildSettingsScene[existing.Length + 1];
            scenes[0] = new EditorBuildSettingsScene(MainPath, true);
            Array.Copy(existing, 0, scenes, 1, existing.Length);
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("Opening installed in Main and Main set as first build scene.");
        }
    }
}
