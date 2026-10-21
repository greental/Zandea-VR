using System;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [CreateAssetMenu(menuName = "Zandea/Friendly NPC/Voice profile")]
    public sealed class VoiceProfile : ScriptableObject
    {
        public string providerId = "kokoro";
        public string providerVoiceId;
        [TextArea] public string voiceDescription;
        [Range(.7f, 1.3f)] public float speechRate = 1f;
        public ExpressionType defaultExpression = ExpressionType.Neutral;
        [TextArea] public string licenseNotes;
    }

    public enum DialogueAction { None, BeginTrading, QuestHook, EndConversation }

    [Serializable]
    public sealed class DialogueResponse
    {
        public string text;
        public string nextNodeId;
        public DialogueAction action;
        public string conditionId;
    }

    [Serializable]
    public sealed class DialogueNode
    {
        public string id;
        public string speakerId;
        [TextArea(2, 5)] public string text;
        public VoiceProfile voiceOverride;
        public AudioClip audio;
        public ExpressionType expression;
        public string emotionalDeliveryId;
        public string gestureId;
        public string conditionId;
        public DialogueAction onEnter;
        public bool endOnComplete;
        public string nextNodeId;
        public List<DialogueResponse> responses = new();
        [HideInInspector] public string generatedFingerprint;
        // Editor-baked RMS envelope, sampled at 30 Hz. Empty when no clip exists.
        [HideInInspector] public float[] mouthEnvelope;
    }

    public interface IDialogueConditionSource
    {
        bool Evaluate(string conditionId);
    }

    [CreateAssetMenu(menuName = "Zandea/Friendly NPC/Dialogue")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        public string npcId;
        public VoiceProfile defaultVoice;
        public string entryNodeId;
        public List<DialogueNode> nodes = new();

        public DialogueNode Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var node in nodes) if (node != null && node.id == id) return node;
            return null;
        }
    }
}
