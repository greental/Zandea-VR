using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.CharacterCreator;
using UnityEditor;
using UnityEngine;
using Zandea.Missions.Editor;

namespace Zandea.FriendlyNPC.Editor
{
    // Imports the owner's recorded MP3s into the existing dialogue and voice
    // bank. The text below is the owner's approved recording script, without
    // ElevenLabs performance tags.
    public static class DaranMissionVoiceImporter
    {
        const string SourceSubpath = "Downloads/Audio/DaranMerchant";
        const string Destination = "Assets/Audio/Dialogue/Daran/Missions/AnHonestDaysWork";
        const string DialoguePath = "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset";
        const string BankPath = "Assets/Zandea/FriendlyNPC/Generated/DaranVoiceBank.asset";
        const string Report = "Temp/DaranMissionIntegration/import-report.txt";
        const string Speaker = "merchant-daran";

        static readonly (string id, string text, ExpressionType expression)[] Lines =
        {
            ("daran_job_offer", "Looking for work, are you? Well, I've got three barrels that need hauling from the storage shed to my stall. Ten Zeks apiece. Easy money, if you've got a decent pair of arms.", ExpressionType.Neutral),
            ("daran_job_payment", "Ten Zeks for every barrel you bring back intact. Break one, and I'll charge you ten for the damage. Fair's fair.", ExpressionType.Neutral),
            ("daran_job_directions", "Storage shed's just across the settlement. Three barrels outside. Bring 'em here, and mind the corners.", ExpressionType.Neutral),
            ("daran_job_accept", "Good. Off you go, then. And try not to break anything.", ExpressionType.Happy),
            ("daran_job_decline", "Suit yourself. Barrels aren't going anywhere. Unfortunately.", ExpressionType.Neutral),
            ("daran_job_no_progress", "Those barrels won't move themselves, you know!", ExpressionType.Neutral),
            ("daran_job_one_delivered", "There's one! Two more to go.", ExpressionType.Neutral),
            ("daran_job_two_delivered", "That's two. Starting to think you might actually finish this.", ExpressionType.Happy),
            ("daran_job_barrel_broken", "Ah, bloody hell. I told you to be careful! That's ten Zeks for the damage.", ExpressionType.Angry),
            ("daran_job_encouragement", "Come on, put your back into it!", ExpressionType.Neutral),
            ("daran_job_careful", "Easy with that! Those barrels cost money!", ExpressionType.Angry),
            ("daran_job_turnin", "All finished, then? Right, let's see how you did.", ExpressionType.Neutral),
            ("daran_job_incomplete", "Still got barrels waiting for you. You haven't finished yet.", ExpressionType.Neutral),
            ("daran_job_perfect", "Well, look at that! All three intact. Thirty Zeks, as promised. Might have to hire you again.", ExpressionType.Happy),
            ("daran_job_two_intact", "Two delivered, one smashed. Twenty earned, ten for the damage. That leaves you ten Zeks. Could've been worse.", ExpressionType.Sad),
            ("daran_job_one_intact", "One delivered, two broken? That's ten earned and twenty in damages. You owe me ten Zeks.", ExpressionType.Sad),
            ("daran_job_all_broken", "Three barrels. Three! And you've smashed every last one! That's thirty Zeks you owe me. Some worker you turned out to be.", ExpressionType.Angry),
            ("daran_job_payout", "Here you go. Your hard-earned Zeks. Try not to spend them all at once.", ExpressionType.Neutral),
            ("daran_job_payment_complete", "There we are. All settled.", ExpressionType.Neutral),
            ("daran_job_debt_request", "You owe me money, friend. Put your purse on the counter and let's settle this.", ExpressionType.Angry),
            ("daran_job_insufficient_funds", "You haven't even got enough to cover the damage? Fine. But you still owe me.", ExpressionType.Angry),
            ("daran_job_debt_reminder", "You owe me money, friend. Put your purse on the counter and let's settle this.", ExpressionType.Angry),
            ("daran_job_debt_paid", "Right. Debt settled. Let's hope your next job goes a little better.", ExpressionType.Neutral),
            ("daran_job_abandon", "Giving up already? Fine. Just don't expect me to forget about any barrels you've broken.", ExpressionType.Sad),
            ("daran_job_completed_return", "Back again? Looking for more work, or have you finally come to buy something?", ExpressionType.Neutral),
            ("daran_job_goodbye", "Off you go, then. And remember, there's no shame in honest work. Just don't make a career out of breaking my things.", ExpressionType.Neutral)
        };

        static DialogueResponse Reply(string text, string next, DialogueAction action = DialogueAction.None) =>
            new() { text = text, nextNodeId = next, action = action };

        static DialogueNode Ensure(DialogueDefinition dialogue, string id, string text,
            ExpressionType expression, string next = null, bool end = false,
            params DialogueResponse[] responses)
        {
            var node = dialogue.Find(id);
            if (node == null)
            {
                node = new DialogueNode { id = id, speakerId = Speaker };
                dialogue.nodes.Add(node);
            }
            node.speakerId = Speaker;
            node.text = text;
            node.expression = expression;
            node.nextNodeId = next;
            node.endOnComplete = end;
            node.onEnter = DialogueAction.None;
            node.responses.Clear();
            node.responses.AddRange(responses);
            return node;
        }

        public static void EnsureDialogue(DialogueDefinition dialogue)
        {
            if (!dialogue) throw new InvalidOperationException("Daran dialogue asset is missing.");
            if (Lines.Length != 26 || Lines.Select(line => line.id).Distinct().Count() != 26)
                throw new InvalidOperationException("Mission line IDs must be 26 unique names.");
            foreach (var line in Lines)
                Ensure(dialogue, line.id, line.text, line.expression);

            var offer = dialogue.Find("daran_job_offer");
            offer.responses.AddRange(new[]
            {
                Reply("I'll do it.", "daran_job_accept", DialogueAction.AcceptContract),
                Reply("How much does it pay?", "daran_job_payment"),
                Reply("Where are the barrels?", "daran_job_directions"),
                Reply("Not interested.", "daran_job_decline")
            });
            foreach (var id in new[] { "daran_job_payment", "daran_job_directions" })
                dialogue.Find(id).responses.AddRange(new[]
                {
                    Reply("I'll do it.", "daran_job_accept", DialogueAction.AcceptContract),
                    Reply("Back to the job.", "daran_job_offer"),
                    Reply("No thanks.", "daran_job_decline")
                });
            dialogue.Find("daran_job_accept").responses.AddRange(new[]
            {
                Reply("I'll get started.", "daran_job_goodbye"),
                Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading)
            });
            dialogue.Find("daran_job_decline").endOnComplete = true;

            var progress = new[]
            {
                "daran_job_no_progress", "daran_job_one_delivered",
                "daran_job_two_delivered", "daran_job_barrel_broken"
            };
            foreach (var id in progress)
                dialogue.Find(id).responses.AddRange(new[]
                {
                    Reply("I'll get to it.", "daran_job_goodbye"),
                    Reply("I've finished.", "daran_job_incomplete"),
                    Reply("I quit.", "daran_job_abandon", DialogueAction.AbandonContract)
                });
            dialogue.Find("daran_job_incomplete").responses.AddRange(new[]
            {
                Reply("Back to work.", "daran_job_goodbye"),
                Reply("I quit.", "daran_job_abandon", DialogueAction.AbandonContract)
            });
            dialogue.Find("daran_job_turnin").responses.AddRange(new[]
            {
                Reply("Let's settle up.", "work_result"),
                Reply("Not yet.", "daran_job_goodbye")
            });
            foreach (var id in new[] { "daran_job_perfect", "daran_job_two_intact",
                         "daran_job_one_intact", "daran_job_all_broken" })
                dialogue.Find(id).nextNodeId = "work_settlement";

            dialogue.Find("daran_job_payout").responses.AddRange(new[]
            {
                Reply("I'll collect it.", "daran_job_goodbye"),
                Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading)
            });
            dialogue.Find("daran_job_debt_paid").nextNodeId = "daran_job_payment_complete";
            foreach (var id in new[] { "daran_job_debt_request", "daran_job_debt_reminder" })
                dialogue.Find(id).responses.AddRange(new[]
                {
                    Reply("I'll pay now.", "work_debt_result"),
                    Reply("I'll return with the money.", "daran_job_goodbye"),
                    Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading)
                });
            dialogue.Find("daran_job_insufficient_funds").responses.Add(
                Reply("I'll return with the money.", "daran_job_goodbye"));
            foreach (var id in new[] { "daran_job_payment_complete", "daran_job_completed_return",
                         "daran_job_abandon" })
                dialogue.Find(id).responses.AddRange(new[]
                {
                    Reply("Show me your wares.", "trade_offer_001", DialogueAction.BeginTrading),
                    Reply("Goodbye.", "daran_job_goodbye")
                });
            dialogue.Find("daran_job_goodbye").endOnComplete = true;
            Ensure(dialogue, "work_settlement", "", ExpressionType.Neutral);
            Ensure(dialogue, "work_debt_result", "", ExpressionType.Neutral);
            var generic = dialogue.Find("work_progress");
            if (generic != null)
            {
                generic.responses.Clear();
                generic.responses.AddRange(new[]
                {
                    Reply("I'll get to it.", "daran_job_goodbye"),
                    Reply("I've finished.", "daran_job_incomplete"),
                    Reply("I quit.", "daran_job_abandon", DialogueAction.AbandonContract)
                });
            }
            EditorUtility.SetDirty(dialogue);
        }

        [MenuItem("Zandea/Missions/Import Daran mission recordings")]
        public static void Import()
        {
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("D: has less than 20 GiB free. Audio import stopped.");
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                SourceSubpath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            var missing = Lines.Select(line => line.id + ".mp3")
                .Where(name => !File.Exists(Path.Combine(source, name))).ToArray();
            if (missing.Length > 0)
                throw new FileNotFoundException("Missing mission recordings: " + string.Join(", ", missing));
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(DialoguePath);
            var bank = AssetDatabase.LoadAssetAtPath<NpcVoiceBank>(BankPath);
            if (!dialogue || !bank) throw new InvalidOperationException("Existing Daran dialogue or voice bank is missing.");
            EnsureDialogue(dialogue);
            Directory.CreateDirectory(Destination);
            int copied = 0;
            foreach (var line in Lines)
            {
                string filename = line.id + ".mp3";
                string input = Path.Combine(source, filename);
                string path = Destination + "/" + filename;
                if (File.Exists(path))
                {
                    if (!SameBytes(input, path))
                        throw new IOException("Imported mission clip differs; existing asset preserved: " + path);
                }
                else { File.Copy(input, path); copied++; }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (!importer) throw new InvalidOperationException("No Unity AudioImporter for " + path);
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.CompressedInMemory ||
                    settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                    !settings.preloadAudioData)
                {
                    settings.loadType = AudioClipLoadType.CompressedInMemory;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = .8f;
                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) throw new InvalidOperationException("Unity could not import " + path);
                var envelope = DialogueVoiceGenerator.BakeEnvelope(path, clip);
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var node = dialogue.Find(line.id);
                node.audio = clip;
                node.mouthEnvelope = envelope;
                var voiceEvent = bank.Find(line.id);
                if (voiceEvent == null)
                {
                    voiceEvent = new NpcVoiceEvent { eventId = line.id };
                    bank.events.Add(voiceEvent);
                }
                voiceEvent.takes.Clear();
                voiceEvent.takes.Add(new NpcVoiceTake
                    { clip = clip, subtitle = node.text, mouthEnvelope = envelope });
            }
            EditorUtility.SetDirty(bank);
            EditorUtility.SetDirty(dialogue);
            AssetDatabase.SaveAssets();
            Validate(dialogue, bank);
            string reportPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), Report);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, "PASS 26 mission MP3 clips imported; " + copied +
                " newly copied; older Daran voice events preserved.\n");
            Debug.Log("[Daran mission voice] 26 recorded lines associated with dialogue and voice bank.");
        }

        public static void ImportBatch() => Import();

        public static void Validate(DialogueDefinition dialogue, NpcVoiceBank bank)
        {
            foreach (var line in Lines)
            {
                var node = dialogue.Find(line.id);
                var voiceEvent = bank.Find(line.id);
                if (node == null || !node.audio || voiceEvent == null || voiceEvent.takes.Count != 1 ||
                    voiceEvent.takes[0].clip != node.audio ||
                    voiceEvent.takes[0].subtitle != node.text || node.mouthEnvelope.Length == 0)
                    throw new InvalidOperationException("Mission voice mapping is incomplete: " + line.id);
            }
            foreach (var id in new[] { "greeting_001", "trade_offer_001", "trade_complete", "farewell_001" })
                if (bank.Find(id) == null || bank.Find(id).takes.Count == 0 || !bank.Find(id).takes[0].clip)
                    throw new InvalidOperationException("Existing merchant voice was lost: " + id);
        }

        static bool SameBytes(string left, string right)
        {
            var a = new FileInfo(left); var b = new FileInfo(right);
            if (a.Length != b.Length) return false;
            using var x = File.OpenRead(left);
            using var y = File.OpenRead(right);
            int l, r;
            do { l = x.ReadByte(); r = y.ReadByte(); if (l != r) return false; }
            while (l >= 0);
            return true;
        }
    }
}
