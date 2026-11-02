using System;
using System.Linq;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.Opening.Capture;
using Zandea.Pueblo;
using Zandea.POI;

namespace Zandea.Opening.Editor
{
    public static class OpeningMainInstaller
    {
        public const string MainPath = "Assets/Main.unity";

        [MenuItem("Zandea/Opening/Install encounter in Main")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
            var activePlayers = scene.GetRootGameObjects()
                .Select(root => new { root, body = root.GetComponentInChildren<VRBodyController>(true) })
                .Where(item => item.body && item.body.gameObject.activeInHierarchy &&
                    item.body.hmd && item.body.hmd.gameObject.activeInHierarchy).ToArray();
            if (activePlayers.Length != 1)
                throw new InvalidOperationException("Main needs exactly one active XR player; found " +
                    string.Join(", ", activePlayers.Select(item => item.root.name)));
            var player = activePlayers[0].root;
            var body = activePlayers[0].body;
            var opening = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Zandea opening encounter");
            if (opening)
            {
                var oldDirector = opening.GetComponent<OpeningDirector>();
                if (!oldDirector || oldDirector.player != body)
                {
                    RemoveOldOpening(opening);
                    opening = null;
                }
            }
            if (!opening)
            {
                OpeningHarnessBuilder.Populate(scene, player, body.xrOrigin.position, false);
                opening = scene.GetRootGameObjects().First(root => root.name == "Zandea opening encounter");
            }
            EnsureBindings(opening, body);
            // This Main-only Pueblo test helper teleports the active XR origin hundreds
            // of metres on Play. The opening uses the saved Inspector start instead.
            foreach (var testStart in UnityEngine.Object.FindObjectsByType<PuebloMissionTestStart>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (testStart.gameObject.scene == scene) { testStart.enableForTesting = false; EditorUtility.SetDirty(testStart); }
            var deferred = opening.GetComponent<OpeningDeferredWorldSystems>();
            if (!deferred) deferred = opening.AddComponent<OpeningDeferredWorldSystems>();
            var pois = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(poi => poi.gameObject.scene == scene).ToArray();
            var kareth = pois.SingleOrDefault(poi => poi.poiId.StartsWith("zandea.pueblo.", StringComparison.Ordinal));
            if (!kareth) throw new InvalidOperationException("Main is missing the authored Kareth settlement.");
            PrepareKareth(kareth, deferred);
            deferred.kareth = kareth;
            deferred.activateAfterOpening = pois.Where(poi => poi != kareth).Select(poi => poi.gameObject).Distinct().ToArray();
            foreach (var laterWorld in deferred.activateAfterOpening) laterWorld.SetActive(false);
            EditorUtility.SetDirty(deferred);
            EditorSceneManager.SaveScene(scene, MainPath);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != MainPath).ToArray();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainPath, true) }.Concat(existing).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Opening saved near the active XR player in Main. Inspector transforms remain authoritative.");
        }

        [MenuItem("Zandea/Opening/Repair Main surface navigation")]
        public static void RepairSurfaceNavigation() => Install();

        static void RemoveOldOpening(GameObject opening)
        {
            var director = opening.GetComponent<OpeningDirector>();
            var binder = opening.GetComponent<OpeningBindingAction>();
            if (director)
            {
                if (director.slavers != null)
                    foreach (var slaver in director.slavers) if (slaver) UnityEngine.Object.DestroyImmediate(slaver.gameObject);
                if (director.knockout) UnityEngine.Object.DestroyImmediate(director.knockout);
                if (director.surrender) UnityEngine.Object.DestroyImmediate(director.surrender);
                if (director.presentation && director.presentation.gameObject != opening)
                    UnityEngine.Object.DestroyImmediate(director.presentation);
            }
            if (binder && binder.restraints) UnityEngine.Object.DestroyImmediate(binder.restraints);
            UnityEngine.Object.DestroyImmediate(opening);
        }

        static void EnsureBindings(GameObject opening, VRBodyController body)
        {
            var director = opening.GetComponent<OpeningDirector>();
            var group = opening.GetComponent<BanditGroup>();
            if (!director || !group || director.slavers == null || director.slavers.Length != 3)
                throw new InvalidOperationException("Opening encounter references are incomplete.");
            var target = opening.GetComponent<OpeningTargetBinding>();
            var ultimatum = opening.GetComponent<SlaverUltimatum>();
            if (ultimatum)
            {
                ultimatum.responseSeconds = 4f;
                ultimatum.warningExtensionSeconds = 1.5f;
                EditorUtility.SetDirty(ultimatum);
            }
            if (!target) target = opening.AddComponent<OpeningTargetBinding>();
            target.targetPlayer = body;
            target.group = group;
            target.slavers = director.slavers;
            target.director = director;
            target.Apply();
            var navigation = opening.GetComponent<OpeningSurfaceAnchor>();
            if (!navigation) navigation = opening.AddComponent<OpeningSurfaceAnchor>();
            navigation.group = group;
            navigation.slavers = director.slavers;
            group.buildLocalNavMesh = false;
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(navigation);
            EditorUtility.SetDirty(group);
        }

        static void PrepareKareth(BanditOutpost kareth, OpeningDeferredWorldSystems deferred)
        {
            var preview = kareth.transform.Find("Layout Preview");
            if (!preview) throw new InvalidOperationException("Kareth has no authored Layout Preview.");
            Vector3 savedCityPosition = preview.position;
            bool onTerrain = Terrain.activeTerrains.Any(terrain => terrain && terrain.terrainData &&
                savedCityPosition.x >= terrain.transform.position.x &&
                savedCityPosition.x <= terrain.transform.position.x + terrain.terrainData.size.x &&
                savedCityPosition.z >= terrain.transform.position.z &&
                savedCityPosition.z <= terrain.transform.position.z + terrain.terrainData.size.z &&
                Mathf.Abs(terrain.SampleHeight(savedCityPosition) + terrain.transform.position.y - savedCityPosition.y) < 1f);
            if (!onTerrain) throw new InvalidOperationException("Kareth's saved city is not on the authored terrain.");
            kareth.name = "Kareth";
            kareth.transform.position = savedCityPosition;
            preview.position = savedCityPosition;
            var generated = kareth.transform.Find("Generated Layout Preview");
            if (generated) generated.gameObject.SetActive(false);
            preview.gameObject.SetActive(true);
            if (deferred.karethGroups == null || deferred.karethGroups.Length == 0)
            {
                deferred.karethGroups = preview.GetComponentsInChildren<BanditGroup>(true);
                deferred.karethGroupsWereEnabled = deferred.karethGroups.Select(group => group.enabled).ToArray();
            }
            if (deferred.karethActors == null || deferred.karethActors.Length == 0)
            {
                deferred.karethActors = preview.GetComponentsInChildren<StarvingBandit>(true)
                    .Select(member => member.gameObject).Distinct().ToArray();
                deferred.karethActorsWereActive = deferred.karethActors.Select(actor => actor.activeSelf).ToArray();
            }
            foreach (var group in deferred.karethGroups) if (group) group.enabled = false;
            foreach (var actor in deferred.karethActors) if (actor) actor.SetActive(false);
            kareth.holdAuthoredPreviewAtStart = true;
            kareth.gameObject.SetActive(true);
            EditorUtility.SetDirty(kareth);
        }
    }
}
