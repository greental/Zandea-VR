using System;
using System.Linq;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.Opening.Capture;
using Zandea.Opening.Combat;

namespace Zandea.Opening.Editor
{
    public static class OpeningIntegrationValidation
    {
        const string V1 = "Assets/Zandea/Opening/Prefabs/OpeningStaffBandit_V1.prefab";

        [MenuItem("Zandea/Opening/Validate authored scenes")]
        public static void Validate()
        {
            Require(EditorBuildSettings.scenes.Length > 0 &&
                EditorBuildSettings.scenes[0].enabled && EditorBuildSettings.scenes[0].path == OpeningMainInstaller.MainPath,
                "Main is the first enabled build scene");
            Require(AssetDatabase.AssetPathToGUID(V1) != AssetDatabase.AssetPathToGUID(OpeningHarnessBuilder.BanditPath),
                "V1 and V2 have distinct prefab GUIDs");
            ValidateScene(OpeningMainInstaller.MainPath);
            ValidateScene(OpeningHarnessBuilder.ScenePath);
            Debug.Log("OPENING_AUTHORED_SCENES_PASS");
        }

        static void ValidateScene(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var director = roots.Select(root => root.GetComponent<OpeningDirector>()).SingleOrDefault(value => value);
            Require(director, path + ": exactly one opening director");
            var rootObject = director.gameObject;
            var flow = rootObject.GetComponent<OpeningFlow>();
            var group = rootObject.GetComponent<BanditGroup>();
            var capture = rootObject.GetComponent<OpeningCaptureGroup>();
            var binder = rootObject.GetComponent<OpeningBindingAction>();
            var escort = rootObject.GetComponent<OpeningPrisonerEscort>();
            Require(flow && group && capture && binder && escort, path + ": opening components are present");
            Require(director.player && director.player.hmd && director.knockout && director.surrender &&
                director.ultimatum && director.presentation, path + ": player and presentation references");
            Require(director.slavers != null && director.slavers.Length == 3 &&
                director.slavers.All(member => member && member.GetComponent<NonlethalAttackProfile>() &&
                    member.GetComponent<NpcGeneratedWeapon>()), path + ": three nonlethal staff slavers");
            Require(director.slavers.All(member =>
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(member.gameObject) == OpeningHarnessBuilder.BanditPath),
                path + ": all slavers use V2");
            Require(group.authoredMembers != null && group.authoredMembers.SequenceEqual(director.slavers) &&
                capture.members != null && capture.members.SequenceEqual(director.slavers),
                path + ": group/capture member references");
            Require(flow.director == director && flow.capture == capture && flow.binder == binder &&
                flow.escort == escort && escort.destination && binder.restraints,
                path + ": capture flow and destination references");
            Require(director.routes != null && director.routes.Length == Enum.GetValues(typeof(OpeningOutcome)).Length &&
                director.routes.Select(route => route.outcome).Distinct().Count() == director.routes.Length,
                path + ": each outcome has one route");
            Debug.Log("OPENING_SCENE_WIRED " + path);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Opening validation failed: " + message);
        }
    }
}
