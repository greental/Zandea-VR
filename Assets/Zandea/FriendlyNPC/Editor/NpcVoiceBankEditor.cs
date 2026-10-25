using UnityEditor;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public static class NpcVoiceBankEditor
    {
        [MenuItem("Zandea/Friendly NPC/Bake selected voice bank envelopes")]
        public static void BakeSelected()
        {
            var bank = Selection.activeObject as NpcVoiceBank;
            if (!bank) { Debug.LogError("Select a NpcVoiceBank asset first."); return; }
            int baked = 0;
            foreach (var voiceEvent in bank.events)
                foreach (var take in voiceEvent.takes)
                {
                    if (take == null || !take.clip) continue;
                    string path = AssetDatabase.GetAssetPath(take.clip);
                    take.mouthEnvelope = DialogueVoiceGenerator.BakeEnvelope(path, take.clip);
                    baked++;
                }
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            Debug.Log($"Baked speech envelopes for {baked} takes in {bank.name}.");
        }

        [MenuItem("Zandea/Friendly NPC/Bake selected voice bank envelopes", true)]
        static bool CanBake() => Selection.activeObject is NpcVoiceBank;
    }
}
