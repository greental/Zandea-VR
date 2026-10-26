using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public static class DaranVoicePackImporter
    {
        const string Source = @"C:\Users\green\Downloads\Audio\DaranMerchant";
        const string Destination = "Assets/Audio/Dialogue/Daran";
        const string BankPath = "Assets/Zandea/FriendlyNPC/Generated/DaranVoiceBank.asset";
        const string PrefabPath = "Assets/Zandea/FriendlyNPC/Generated/DaranMerchant.prefab";
        static readonly Dictionary<string, string> TakeSubtitles = new()
        {
            ["greeting_01_2"] = "Hm. Looking to trade, traveler? Or are you looking for work?",
            ["greeting_02_2"] = "Ah, a customer! Come closer! Let's see what you need.",
            ["goodbye_01"] = "Safe travels! Try not to die before you come back.",
            ["goodbye_01_2"] = "Safe travels! And try not to die before you come back.",
            ["market_call_01_2"] = "Weapons! Supplies! Finest steel in the wasteland! Well, finest I've got anyway."
        };

        static readonly (string eventId, string subtitle, string[] files)[] Lines =
        {
            ("greeting_001", "Looking to trade, traveler? Or are you looking for work?",
                new[] { "greeting_01", "greeting_01_2" }),
            ("greeting_002", "Ah, a customer! Come closer. Let's see what you need.",
                new[] { "greeting_02", "greeting_02_2" }),
            ("trade_offer_001", "Go on, have a look. Anything on the counter is for sale. Well, almost anything.",
                new[] { "browse_01" }),
            ("sell_ack_001", "Let's see what you've brought me. Hmm, interesting.", new[] { "sell_01" }),
            ("insufficient_funds_001", "You're short on cats, friend. Afraid good intentions don't pay the bills. Put something back.",
                new[] { "insufficient_funds", "insufficient_funds_2" }),
            ("transaction_ready_001", "Fair enough. Come on, let's shake on it.",
                new[] { "handshake_offer", "handshake_offer_2" }),
            ("trade_complete", "Pleasure doing business with you. A fair trade, more or less.",
                new[] { "trade_complete", "trade_complete_2" }),
            ("farewell_001", "Safe travels! And try not to die before you come back.",
                new[] { "goodbye_01", "goodbye_01_2" }),
            ("market_call_01", "Weapons! Supplies! Finest steel in the wasteland! Well, finest I've got anyway.",
                new[] { "market_call_01", "market_call_01_2" }),
            ("market_call_02", "Come closer, come closer! You there, yes, you with the miserable-looking boots. I've got just the thing.",
                new[] { "market_call_02" }),
            ("market_call_03", "Bandages, food, weapons! Everything you need to survive another bloody day.",
                new[] { "market_call_03" })
        };

        static readonly (string eventId, string subtitle, string file)[] OptionalTheftLines =
        {
            ("stock_warning_001", "Don't walk off with my stock, traveler.", "stock_warning_001.wav"),
            ("stock_stolen_001", "That's mine. Bring it back.", "stock_stolen_001.wav")
        };

        [MenuItem("Zandea/Friendly NPC/Prepare Daran dialogue audio")]
        public static void PreparePlayback()
        {
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("Less than 20 GiB free on D:. Audio import stopped.");
            foreach (var line in Lines)
                foreach (string name in line.files)
                    SetLowLatencyImport(Destination + "/" + name + ".mp3");
            foreach (var line in OptionalTheftLines)
                if (File.Exists(Destination + "/" + line.file))
                    SetLowLatencyImport(Destination + "/" + line.file);
            AssetDatabase.SaveAssets();
        }

        static void SetLowLatencyImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer) return;
            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.DecompressOnLoad &&
                settings.preloadAudioData) return;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        [MenuItem("Zandea/Friendly NPC/Import Daran voice pack")]
        public static void Import()
        {
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("Less than 20 GiB free on D:. Voice import stopped.");
            if (!Directory.Exists(Source))
                throw new DirectoryNotFoundException("Daran voice folder not found: " + Source);
            Directory.CreateDirectory(Destination);
            foreach (var line in Lines)
                foreach (string name in line.files)
                {
                    string source = Path.Combine(Source, name + ".wav.mp3");
                    if (!File.Exists(source)) throw new FileNotFoundException("Missing Daran voice clip", source);
                    // The downloads carry MP3 bitstreams despite their double extension.
                    string target = Destination + "/" + name + ".mp3";
                    if (File.Exists(target))
                    {
                        if (!SameBytes(source, target))
                            throw new IOException("Imported clip differs; preserve or deliberately replace " + target);
                    }
                    else File.Copy(source, target);
                    AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                }
            var bank = AssetDatabase.LoadAssetAtPath<NpcVoiceBank>(BankPath);
            if (!bank)
            {
                bank = ScriptableObject.CreateInstance<NpcVoiceBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }
            bank.events.Clear();
            foreach (var line in Lines)
            {
                var voiceEvent = new NpcVoiceEvent { eventId = line.eventId,
                    cooldownSeconds = line.eventId == "sell_ack_001" ? 12f : 0f };
                foreach (string name in line.files)
                {
                    string path = Destination + "/" + name + ".mp3";
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (!clip) throw new InvalidOperationException("Unity could not import " + path);
                    var envelope = DialogueVoiceGenerator.BakeEnvelope(path, clip);
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    voiceEvent.takes.Add(new NpcVoiceTake { clip = clip,
                        subtitle = TakeSubtitles.TryGetValue(name, out string subtitle) ? subtitle : line.subtitle,
                        mouthEnvelope = envelope });
                }
                bank.events.Add(voiceEvent);
            }
            foreach (var line in OptionalTheftLines)
            {
                string path = Destination + "/" + line.file;
                if (!File.Exists(path)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) throw new InvalidOperationException("Unity could not import " + path);
                var envelope = DialogueVoiceGenerator.BakeEnvelope(path, clip);
                bank.events.Add(new NpcVoiceEvent { eventId = line.eventId,
                    takes = new List<NpcVoiceTake> { new() { clip = clip,
                        subtitle = line.subtitle, mouthEnvelope = envelope } } });
            }
            PreparePlayback();
            bank.ambientEventIds.Clear();
            bank.ambientEventIds.AddRange(new[] { "market_call_01", "market_call_02", "market_call_03" });
            EditorUtility.SetDirty(bank);

            var profile = AssetDatabase.LoadAssetAtPath<VoiceProfile>(
                "Assets/Zandea/FriendlyNPC/Generated/VoiceProfile_MerchantMale01.asset");
            if (profile)
            {
                profile.providerId = "elevenlabs";
                profile.providerVoiceId = ""; // The original provider voice ID was not supplied.
                profile.voiceDescription = "Recorded weathered frontier merchant; restrained delivery.";
                profile.licenseNotes = "ElevenLabs prerecorded pack supplied by project owner. Confirm voice rights and commercial terms before release.";
                EditorUtility.SetDirty(profile);
            }

            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(
                "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset");
            if (dialogue)
            {
                foreach (var line in Lines)
                {
                    var node = dialogue.Find(line.eventId);
                    if (node != null && !string.IsNullOrEmpty(line.subtitle))
                    { node.text = line.subtitle; EditorUtility.SetDirty(dialogue); }
                }
                var original = dialogue.Find("greeting_001");
                if (original != null && dialogue.Find("greeting_002") == null)
                {
                    var alternate = new DialogueNode {
                        id = "greeting_002", speakerId = original.speakerId,
                        text = "Ah, a customer! Come closer. Let's see what you need.",
                        expression = original.expression, gestureId = original.gestureId,
                        responses = new List<DialogueResponse>(original.responses)
                    };
                    dialogue.nodes.Add(alternate);
                    EditorUtility.SetDirty(dialogue);
                }
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var npc = root.GetComponent<FriendlyNpcInteraction>();
                if (!npc) throw new InvalidOperationException("Daran prefab has no FriendlyNpcInteraction");
                npc.voiceBank = bank;
                if (npc.mouthAudio) { npc.mouthAudio.volume = .85f; npc.mouthAudio.priority = 90; }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Imported Daran recordings, baked 30 Hz mouth envelopes, and prepared low-latency playback. " +
                "Theft lines are included only when their WAV files exist in " + Destination);
        }

        // Batch validation entry point; the live editor keeps its authored scene layout.
        public static void ImportAndValidate()
        {
            Import();
            DaranMerchantBuilder.BuildAndValidate();
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
