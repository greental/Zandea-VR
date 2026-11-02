#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Zandea.FriendlyNPC;

namespace Zandea.Missions.Editor
{
    public sealed class DaranMissionTests
    {
        [Test]
        public void AllRecordedMissionEventsHaveMatchingDialogueAndAudio()
        {
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(
                "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset");
            var bank = AssetDatabase.LoadAssetAtPath<NpcVoiceBank>(
                "Assets/Zandea/FriendlyNPC/Generated/DaranVoiceBank.asset");
            Assert.IsNotNull(dialogue);
            Assert.IsNotNull(bank);
            var missionFiles = AssetDatabase.FindAssets("t:AudioClip",
                    new[] { "Assets/Audio/Dialogue/Daran/Missions/AnHonestDaysWork" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.AreEqual(26, missionFiles.Length);
            foreach (var path in missionFiles)
            {
                string id = System.IO.Path.GetFileNameWithoutExtension(path);
                var node = dialogue.Find(id);
                var voice = bank.Find(id);
                Assert.IsNotNull(node, id);
                Assert.IsNotNull(voice, id);
                Assert.AreEqual(1, voice.takes.Count, id);
                Assert.AreSame(node.audio, voice.takes[0].clip, id);
                Assert.AreEqual(node.text, voice.takes[0].subtitle, id);
                Assert.IsNotEmpty(node.mouthEnvelope, id);
            }
            foreach (var id in new[] { "greeting_001", "trade_offer_001", "trade_complete", "farewell_001" })
                Assert.IsNotNull(bank.Find(id)?.takes.FirstOrDefault()?.clip,
                    "Older merchant voice lost: " + id);
        }

        [Test]
        public void MissionBranchSpeaksOutcomeAfterExplicitSettlementChoice()
        {
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(
                "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset");
            Assert.IsNotNull(dialogue);
            var greeting = dialogue.Find(dialogue.entryNodeId);
            Assert.IsTrue(greeting.responses.Any(response => response.nextNodeId == "work_001"));
            var offer = dialogue.Find("daran_job_offer");
            Assert.IsTrue(offer.responses.Any(response => response.nextNodeId == "daran_job_accept" &&
                response.action == DialogueAction.AcceptContract));
            Assert.IsTrue(offer.responses.Any(response => response.nextNodeId == "daran_job_payment"));
            Assert.IsTrue(offer.responses.Any(response => response.nextNodeId == "daran_job_directions"));
            var turnin = dialogue.Find("daran_job_turnin");
            Assert.IsTrue(turnin.responses.Any(response => response.nextNodeId == "work_result" &&
                response.action == DialogueAction.None));
            foreach (var id in new[] { "daran_job_perfect", "daran_job_two_intact",
                         "daran_job_one_intact", "daran_job_all_broken" })
                Assert.AreEqual("work_settlement", dialogue.Find(id).nextNodeId, id);
            Assert.AreEqual("You owe me money, friend. Put your purse on the counter and let's settle this.",
                dialogue.Find("daran_job_debt_reminder").text);
        }

        [Test]
        public void OutcomeVoiceAndNetUseAuthoritativeBarrelResults()
        {
            var definition = ScriptableObject.CreateInstance<TransportMissionDefinition>();
            definition.missionId = Guid.NewGuid().ToString("N");
            var root = new GameObject("Daran mission outcome test");
            root.SetActive(false);
            var contract = root.AddComponent<DaranContract>();
            contract.definition = definition;
            var session = MissionSessions.Get(definition.missionId);
            try
            {
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
                    Assert.IsTrue(session.Terminal);
                    Assert.AreEqual(result.net, session.Net(definition));
                    Assert.AreEqual(result.id, contract.ResolveNodeId("work_result"));
                    Assert.AreEqual("daran_job_turnin", contract.WorkNodeId);
                }
                session.barrels["broken-2"] = TransportState.AwaitingPickup;
                Assert.IsFalse(session.Terminal);
                Assert.AreEqual("daran_job_incomplete", contract.ResolveNodeId("work_result"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void AbandonmentKeepsAlreadyIncurredDamageDebt()
        {
            var definition = ScriptableObject.CreateInstance<TransportMissionDefinition>();
            definition.missionId = Guid.NewGuid().ToString("N");
            var root = new GameObject("Daran abandonment test");
            root.SetActive(false);
            var contract = root.AddComponent<DaranContract>();
            contract.definition = definition;
            var session = MissionSessions.Get(definition.missionId);
            session.state = MissionState.InProgress;
            session.barrels["barrel-1"] = TransportState.Destroyed;
            try
            {
                contract.Abandon();
                contract.Abandon();
                Assert.AreEqual(MissionState.Abandoned, session.state);
                Assert.AreEqual(10, session.debt);
                Assert.AreEqual("daran_job_debt_reminder", contract.WorkNodeId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }
    }
}
#endif
