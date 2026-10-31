using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zandea.FriendlyNPC;

namespace Zandea.Missions.Editor
{
    // Batch-accessible validation for projects where tests in predefined Editor
    // assemblies are not discovered by the Unity Test Runner.
    public static class DaranMissionValidator
    {
        const string DialoguePath = "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset";
        const string BankPath = "Assets/Zandea/FriendlyNPC/Generated/DaranVoiceBank.asset";
        const string AudioFolder = "Assets/Audio/Dialogue/Daran/Missions/AnHonestDaysWork";

        [MenuItem("Zandea/Missions/Validate Daran mission in Main")]
        public static void ValidateOpenMain()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Main.unity")
                throw new InvalidOperationException("Open Assets/Main.unity to validate Daran.");
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(DialoguePath);
            var bank = AssetDatabase.LoadAssetAtPath<NpcVoiceBank>(BankPath);
            Require(dialogue && bank, "Daran dialogue or voice bank missing");
            Zandea.FriendlyNPC.Editor.DaranMissionVoiceImporter.Validate(dialogue, bank);

            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(clips.Length == 26, "Expected 26 mission clips, found " + clips.Length);
            foreach (var path in clips)
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(path);
                Require(dialogue.Find(id)?.audio == AssetDatabase.LoadAssetAtPath<AudioClip>(path),
                    "Wrong AudioClip assigned to " + id);
            }

            var greeting = dialogue.Find(dialogue.entryNodeId);
            Require(greeting.responses.Any(response => response.nextNodeId == "work_001"),
                "Greeting has no work branch");
            var offer = dialogue.Find("daran_job_offer");
            Require(offer.responses.Any(response => response.nextNodeId == "daran_job_accept" &&
                response.action == DialogueAction.AcceptContract), "Acceptance action missing");
            Require(offer.responses.Any(response => response.nextNodeId == "daran_job_payment"),
                "Payment question missing");
            Require(offer.responses.Any(response => response.nextNodeId == "daran_job_directions"),
                "Directions question missing");
            Require(dialogue.Find("daran_job_turnin").responses.Any(response =>
                response.nextNodeId == "work_result" && response.action == DialogueAction.None),
                "Outcome line would be skipped by settlement");

            var definition = ScriptableObject.CreateInstance<TransportMissionDefinition>();
            definition.missionId = Guid.NewGuid().ToString("N");
            var root = new GameObject("Daran mission validation");
            root.SetActive(false);
            try
            {
                var contract = root.AddComponent<DaranContract>();
                contract.definition = definition;
                var session = MissionSessions.Get(definition.missionId);
                var cases = new[]
                {
                    (delivered: 3, broken: 0, net: 30, id: "daran_job_perfect"),
                    (delivered: 2, broken: 1, net: 10, id: "daran_job_two_intact"),
                    (delivered: 1, broken: 2, net: -10, id: "daran_job_one_intact"),
                    (delivered: 0, broken: 3, net: -30, id: "daran_job_all_broken")
                };
                foreach (var result in cases)
                {
                    session.barrels.Clear();
                    for (int i = 0; i < result.delivered; i++)
                        session.barrels["delivered-" + i] = TransportState.DeliveredIntact;
                    for (int i = 0; i < result.broken; i++)
                        session.barrels["broken-" + i] = TransportState.Destroyed;
                    session.state = MissionState.ReadyToTurnIn;
                    Require(session.Terminal && session.Net(definition) == result.net &&
                        contract.ResolveNodeId("work_result") == result.id, "Wrong outcome " + result.id);
                    Require(dialogue.Find(result.id).nextNodeId == "work_settlement",
                        "Settlement sequence missing after " + result.id);
                }
                session.barrels["broken-2"] = TransportState.AwaitingPickup;
                Require(!session.Terminal && contract.ResolveNodeId("work_result") ==
                    "daran_job_incomplete", "Unfinished mission accepted as complete");
                session.state = MissionState.InProgress;
                session.barrels.Clear();
                session.barrels["broken"] = TransportState.Destroyed;
                contract.Abandon();
                Require(session.state == MissionState.Abandoned && session.debt == 10,
                    "Abandonment lost damage liability");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(definition);
            }

            HonestWorkAuthoring.ValidateMain();
            Debug.Log("PASS Daran mission: 26 clips, old merchant events, dialogue branches, " +
                "four outcomes, abandonment debt, and Main Pueblo wiring.");
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Main.unity", OpenSceneMode.Single);
            ValidateOpenMain();
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
