using System;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.Opening.Capture;

namespace Zandea.Opening.Editor
{
    public static class OpeningHarnessBuilder
    {
        public const string ScenePath = "Assets/Scenes/Zandea/OpeningCaptureTest.unity";
        const string ArenaPath = "Assets/MeleeCombat/MocapTest/GeneratedVariants/ImportedCombatVRArena.unity";
        const string PlayerPath = "Assets/MeleeCombat/VRPlayerMainCombat.prefab";
        public const string BanditPath = "Assets/Zandea/Opening/Prefabs/OpeningStaffBandit_V2.prefab";

        [MenuItem("Zandea/Opening/Build test scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPath);
            if (!playerPrefab || !banditPrefab) throw new InvalidOperationException("Opening harness prefabs are missing.");
            var scene = EditorSceneManager.OpenScene(ArenaPath, OpenSceneMode.Single);
            foreach (var old in UnityEngine.Object.FindObjectsByType<StarvingBandit>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<VRBodyController>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.transform.root.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<PhysicalMeleeWeapon>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var old in UnityEngine.Object.FindObjectsByType<BanditDuelMonitor>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            playerObject.name = "Opening VR player";
            playerObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            playerObject.SetActive(true);
            Populate(scene, playerObject, Vector3.zero, true);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Opening harness ready: " + ScenePath);
        }

        public static void Populate(Scene scene, GameObject playerObject, Vector3 center, bool includeDiagnostics)
        {
            var banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPath);
            if (!banditPrefab) throw new InvalidOperationException("Opening staff V2 prefab is missing.");
            var body = playerObject.GetComponentInChildren<VRBodyController>(true);
            var health = playerObject.GetComponentInChildren<ScavengerNPC.ScavengerPlayerHealth>(true);
            if (!body || !body.hmd || !health) throw new InvalidOperationException("Opening player is missing XR body, HMD or health.");
            var knockout = playerObject.AddComponent<PlayerKnockout>();
            knockout.health = health;
            var surrender = playerObject.AddComponent<PlayerSurrenderGesture>();
            surrender.body = body;
            surrender.knockout = knockout;
            var restraints = playerObject.AddComponent<PlayerWristRestraints>();
            restraints.body = body;
            var presentation = playerObject.AddComponent<KnockoutPresentation>();
            presentation.knockout = knockout;
            presentation.viewCamera = body.hmd.GetComponent<Camera>();
            var members = new StarvingBandit[3];
            for (int i = 0; i < members.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / members.Length;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(banditPrefab, scene);
                instance.name = "Opening staff slaver " + (i + 1);
                instance.transform.SetPositionAndRotation(center + radial * 3.5f, Quaternion.LookRotation(-radial));
                var bandit = instance.GetComponent<StarvingBandit>();
                if (!bandit) throw new InvalidOperationException("Staff bandit prefab has no StarvingBandit.");
                instance.AddComponent<Zandea.Opening.Combat.NonlethalAttackProfile>();
                bandit.target = body.hmd;
                bandit.initialAttackDelay = 2f + i * .4f;
                members[i] = bandit;
            }
            var root = new GameObject(includeDiagnostics ? "Opening harness" : "Zandea opening encounter");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = center;
            var group = root.AddComponent<BanditGroup>();
            group.player = body.hmd;
            group.banditPrefab = members[0];
            group.authoredMembers = members;
            group.groupSize = members.Length;
            group.buildLocalNavMesh = false;
            group.sightRange = 20f;
            group.fieldOfView = 360f;
            var capture = root.AddComponent<OpeningCaptureGroup>();
            capture.group = group;
            capture.members = members;
            capture.prisoner = body;
            var binder = root.AddComponent<OpeningBindingAction>();
            binder.capture = capture;
            binder.restraints = restraints;
            binder.prisoner = body;
            var escortDestination = new GameObject("Opening escort destination").transform;
            escortDestination.SetParent(root.transform);
            escortDestination.position = center + playerObject.transform.forward * 8f;
            var escort = root.AddComponent<OpeningPrisonerEscort>();
            escort.capture = capture;
            escort.restraints = restraints;
            escort.destination = escortDestination;
            var ultimatum = root.AddComponent<SlaverUltimatum>();
            ultimatum.leader = members[0];
            ultimatum.guards = new[] { members[1], members[2] };
            ultimatum.player = body;
            ultimatum.surrender = surrender;
            var title = root.AddComponent<OpeningTitlePresentation>();
            title.viewCamera = body.hmd.GetComponent<Camera>();
            var director = root.AddComponent<OpeningDirector>();
            director.player = body;
            director.playerHealth = health;
            director.knockout = knockout;
            director.surrender = surrender;
            director.ultimatum = ultimatum;
            director.slavers = members;
            director.presentation = title;
            director.encounterCenter = root.transform;
            director.routes = new[] {
                new OpeningRoute { outcome = OpeningOutcome.CapturedUnconscious, showTitle = true },
                new OpeningRoute { outcome = OpeningOutcome.CapturedCompliant, showTitle = true },
                new OpeningRoute { outcome = OpeningOutcome.SlaversDefeated, showTitle = true },
                new OpeningRoute { outcome = OpeningOutcome.Escaped, showTitle = true },
                new OpeningRoute { outcome = OpeningOutcome.Aborted, showTitle = false }
            };
            var flow = root.AddComponent<OpeningFlow>();
            flow.surrender = surrender;
            flow.ultimatum = ultimatum;
            flow.capture = capture;
            flow.binder = binder;
            flow.escort = escort;
            flow.director = director;
            if (includeDiagnostics)
            {
                var diagnostics = root.AddComponent<OpeningHarnessDiagnostics>();
                diagnostics.player = body;
                diagnostics.playerHealth = health;
                diagnostics.group = group;
                diagnostics.bandits = members;
                diagnostics.knockout = knockout;
                diagnostics.surrender = surrender;
                diagnostics.restraints = restraints;
                diagnostics.capture = capture;
                diagnostics.escort = escort;
                diagnostics.director = director;
            }
        }

        [MenuItem("Zandea/Opening/Build and play test scene")]
        public static void BuildAndPlay()
        {
            Build();
            if (SceneManager.GetActiveScene().path == ScenePath) EditorApplication.isPlaying = true;
        }
    }
}
