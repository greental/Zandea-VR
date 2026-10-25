using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public sealed class DialogueVoiceGenerator : EditorWindow
    {
        DialogueDefinition dialogue;
        string selectedNodeId;
        string pythonExecutable = "python";
        bool replaceExisting;

        [MenuItem("Zandea/Friendly NPC/Generate dialogue voice")]
        static void Open() => GetWindow<DialogueVoiceGenerator>("Dialogue voice");

        void OnGUI()
        {
            dialogue = (DialogueDefinition)EditorGUILayout.ObjectField("Dialogue", dialogue,
                typeof(DialogueDefinition), false);
            pythonExecutable = EditorGUILayout.TextField("Python executable", pythonExecutable);
            replaceExisting = EditorGUILayout.Toggle("Regenerate selected clip", replaceExisting);
            if (!dialogue) return;
            EditorGUILayout.HelpBox("Generation is explicit. Existing clips are preserved as hashed files. " +
                "Kokoro needs a local Python package and licensed model; ElevenLabs needs ELEVENLABS_API_KEY.",
                MessageType.Info);
            if (GUILayout.Button("Generate missing or changed lines for this NPC")) GenerateAll(false);
            foreach (var node in dialogue.nodes)
            {
                if (node == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(node.id + "  " + (node.audio ? "✓" : "missing"));
                    if (GUILayout.Button("Generate", GUILayout.Width(85)))
                    { selectedNodeId = node.id; GenerateAll(replaceExisting); }
                }
            }
        }

        static string SafeName(string value)
        {
            var builder = new StringBuilder();
            foreach (char c in value ?? "") builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return builder.Length > 0 ? builder.ToString() : "unnamed";
        }
        static string Hash(string source)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
            return BitConverter.ToString(bytes, 0, 6).Replace("-", "").ToLowerInvariant();
        }
        static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        void GenerateAll(bool force)
        {
            if (!dialogue) return;
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
            { UnityEngine.Debug.LogError("Less than 20 GiB free on D:. Dialogue voice import stopped."); return; }
            string target = selectedNodeId;
            selectedNodeId = null;
            int generated = 0, skipped = 0, failed = 0;
            foreach (var node in dialogue.nodes)
            {
                if (node == null || !string.IsNullOrEmpty(target) && node.id != target) continue;
                var profile = node.voiceOverride ? node.voiceOverride : dialogue.defaultVoice;
                if (!profile || string.IsNullOrWhiteSpace(profile.providerVoiceId) || string.IsNullOrWhiteSpace(node.text))
                { UnityEngine.Debug.LogError("Voice missing profile, voice ID, or text for " + node?.id); failed++; continue; }
                string fingerprint = Hash(profile.providerId + "|" + profile.providerVoiceId + "|" +
                    profile.speechRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + node.text);
                if (!force && node.audio && string.IsNullOrEmpty(node.generatedFingerprint)) { skipped++; continue; }
                if (!force && node.audio && node.generatedFingerprint == fingerprint) { skipped++; continue; }
                string folder = "Assets/Audio/Dialogue/" + SafeName(dialogue.npcId);
                Directory.CreateDirectory(folder);
                string extension = profile.providerId == "elevenlabs" ? ".mp3" : ".wav";
                string path = folder + "/" + SafeName(node.id) + "_" + fingerprint + extension;
                try
                {
                    if (!File.Exists(path))
                    {
                        if (profile.providerId == "kokoro") GenerateKokoro(node, profile, path);
                        else if (profile.providerId == "elevenlabs") GenerateElevenLabs(node, profile, path);
                        else throw new InvalidOperationException("Unknown voice provider: " + profile.providerId);
                    }
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (!clip) throw new InvalidOperationException("Unity could not import " + path);
                    node.audio = clip;
                    node.generatedFingerprint = fingerprint;
                    node.mouthEnvelope = BakeEnvelope(path, clip);
                    generated++;
                }
                catch (Exception error)
                { UnityEngine.Debug.LogError("Voice generation failed for " + node.id + ": " + error.Message); failed++; }
            }
            EditorUtility.SetDirty(dialogue);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"Dialogue voice: {generated} associated, {skipped} unchanged, {failed} failed.");
        }

        void GenerateKokoro(DialogueNode node, VoiceProfile profile, string output)
        {
            string script = Path.GetFullPath("Tools/Voice/kokoro_generate.py");
            var process = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = Quote(script) + " --text " + Quote(node.text) + " --voice " +
                    Quote(profile.providerVoiceId) + " --speed " +
                    profile.speechRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " --output " + Quote(Path.GetFullPath(output)),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using var child = Process.Start(process);
            if (child == null) throw new InvalidOperationException("Could not start Python");
            var errorText = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(300000))
            { child.Kill(); throw new TimeoutException("Kokoro did not finish within five minutes"); }
            string stderr = errorText.GetAwaiter().GetResult();
            if (child.ExitCode != 0) throw new InvalidOperationException(stderr);
        }

        [Serializable] sealed class ElevenRequest
        {
            public string text;
            public string model_id = "eleven_multilingual_v2";
        }
        void GenerateElevenLabs(DialogueNode node, VoiceProfile profile, string output)
        {
            string key = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("ELEVENLABS_API_KEY is not set");
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("xi-api-key", key);
            using var payload = new StringContent(JsonUtility.ToJson(new ElevenRequest { text = node.text }),
                Encoding.UTF8, "application/json");
            var uri = "https://api.elevenlabs.io/v1/text-to-speech/" + Uri.EscapeDataString(profile.providerVoiceId);
            using var response = client.PostAsync(uri, payload).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("ElevenLabs " + (int)response.StatusCode + ": " +
                    response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            File.WriteAllBytes(output, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        }

        internal static float[] BakeEnvelope(string path, AudioClip clip)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer) return Array.Empty<float>();
            var original = importer.defaultSampleSettings;
            try
            {
                var temporary = original;
                temporary.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = temporary;
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                float[] samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) throw new InvalidOperationException("Cannot read imported audio for mouth envelope");
                int frames = Mathf.CeilToInt(clip.length * 30f);
                var envelope = new float[frames];
                int frameSamples = Mathf.Max(1, clip.frequency / 30);
                for (int f = 0; f < frames; f++)
                {
                    int from = f * frameSamples * clip.channels;
                    int end = Mathf.Min(samples.Length, (f + 1) * frameSamples * clip.channels);
                    float energy = 0f;
                    for (int s = from; s < end; s++) energy += samples[s] * samples[s];
                    envelope[f] = Mathf.Clamp01(Mathf.Sqrt(energy / Mathf.Max(1, end - from)) * 3.2f);
                }
                return envelope;
            }
            finally
            {
                var compressed = original;
                compressed.compressionFormat = AudioCompressionFormat.Vorbis;
                compressed.loadType = AudioClipLoadType.CompressedInMemory;
                importer.defaultSampleSettings = compressed;
                importer.SaveAndReimport();
            }
        }
    }
}
