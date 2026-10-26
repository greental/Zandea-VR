using KenshiVR.CharacterCreator;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public static class DaranTheftVoiceAuthoring
    {
        const string DialoguePath = "Assets/Zandea/FriendlyNPC/Generated/DaranDialogue.asset";
        const string ProfilePath = "Assets/Zandea/FriendlyNPC/Generated/VoiceProfile_DaranTheftKokoro.asset";

        [MenuItem("Zandea/Friendly NPC/Prepare Daran theft dialogue")]
        public static void Prepare()
        {
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new System.InvalidOperationException("Less than 20 GiB free on D:. Dialogue authoring stopped.");
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(DialoguePath);
            if (!dialogue) throw new System.InvalidOperationException("Daran dialogue is missing: " + DialoguePath);
            var profile = AssetDatabase.LoadAssetAtPath<VoiceProfile>(ProfilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VoiceProfile>();
                profile.name = "VoiceProfile_DaranTheftKokoro";
                profile.providerId = "kokoro";
                profile.providerVoiceId = "am_michael";
                profile.voiceDescription = "Temporary restrained American male theft warning; compare with Daran's recorded voice before release.";
                profile.licenseNotes = "Kokoro-82M model: Apache-2.0. Check the selected voice and runtime dependencies before commercial release.";
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            AddOrBind(dialogue, profile, "stock_warning_001",
                "Don't walk off with my stock, traveler.");
            AddOrBind(dialogue, profile, "stock_stolen_001",
                "That's mine. Bring it back.");
            EditorUtility.SetDirty(dialogue);
            AssetDatabase.SaveAssets();
            Debug.Log("Daran's two theft lines are authored. Generate their audio from the Dialogue voice window when local Kokoro is available.");
        }

        static void AddOrBind(DialogueDefinition dialogue, VoiceProfile profile, string id, string text)
        {
            var node = dialogue.Find(id);
            if (node == null)
            {
                node = new DialogueNode { id = id, speakerId = dialogue.npcId,
                    text = text, expression = ExpressionType.Angry };
                dialogue.nodes.Add(node);
            }
            if (!node.voiceOverride) node.voiceOverride = profile;
        }
    }
}
