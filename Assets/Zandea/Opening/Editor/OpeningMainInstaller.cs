using System;
using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using KenshiVR.MeleeCombat;
using Zandea.Opening.Capture;

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
            var existingOpening = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Zandea opening encounter");
            if (existingOpening)
            {
                EnsureSurfaceAnchor(existingOpening);
                EditorSceneManager.SaveScene(scene, MainPath);
                Debug.Log("Opening encounter already installed in Main; surface navigation anchor verified.");
                return;
            }
            var candidates = scene.GetRootGameObjects()
                .Select(root => new { root, body = root.GetComponentInChildren<VRBodyController>(true) })
                .Where(item => item.body).ToArray();
            var players = candidates.Where(item => item.root.name == "VR player - Man Creator").ToArray();
            if (players.Length != 1)
                throw new InvalidOperationException("Main player root is ambiguous. XR roots: " +
                    string.Join(", ", candidates.Select(item => item.root.name + " [" +
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.root) + "]")));
            var player = players[0].root;
            var center = player.transform.position;
            OpeningHarnessBuilder.Populate(scene, player, center, false);
            var opening = scene.GetRootGameObjects().First(root => root.name == "Zandea opening encounter");
            EnsureSurfaceAnchor(opening);
            EditorSceneManager.SaveScene(scene, MainPath);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != MainPath).ToArray();
            var scenes = new EditorBuildSettingsScene[existing.Length + 1];
            scenes[0] = new EditorBuildSettingsScene(MainPath, true);
            Array.Copy(existing, 0, scenes, 1, existing.Length);
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("Opening installed in Main and Main set as first build scene.");
        }

        [MenuItem("Zandea/Opening/Repair Main surface navigation")]
        public static void RepairSurfaceNavigation()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
            var opening = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Zandea opening encounter");
            if (!opening) throw new InvalidOperationException("Opening encounter is not installed in Main.");
            EnsureSurfaceAnchor(opening);
            EditorSceneManager.SaveScene(scene, MainPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Opening surface navigation repair saved in Main.");
        }

        static void EnsureSurfaceAnchor(GameObject opening)
        {
            var director = opening.GetComponent<OpeningDirector>();
            var group = opening.GetComponent<BanditGroup>();
            var escort = opening.GetComponent<OpeningPrisonerEscort>();
            if (!director || !director.player || !group || !escort || !escort.destination ||
                director.slavers == null || director.slavers.Length != 3)
                throw new InvalidOperationException("Opening encounter references are incomplete for surface navigation.");
            var anchor = opening.GetComponent<OpeningSurfaceAnchor>();
            if (!anchor) anchor = opening.AddComponent<OpeningSurfaceAnchor>();
            anchor.player = director.player;
            anchor.group = group;
            anchor.slavers = director.slavers;
            anchor.escortDestination = escort.destination;
            EditorUtility.SetDirty(anchor);
        }
    }
}
