using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [Serializable]
    public sealed class NpcVoiceTake
    {
        public AudioClip clip;
        [TextArea(1, 4)] public string subtitle;
        [HideInInspector] public float[] mouthEnvelope;
    }

    [Serializable]
    public sealed class NpcVoiceEvent
    {
        public string eventId;
        [Min(0f)] public float cooldownSeconds;
        public List<NpcVoiceTake> takes = new();
    }

    // Audio authoring stays separate from dialogue nodes and NPC presentation.
    [CreateAssetMenu(menuName = "Zandea/Friendly NPC/Voice Bank")]
    public sealed class NpcVoiceBank : ScriptableObject
    {
        public List<NpcVoiceEvent> events = new();
        public List<string> ambientEventIds = new();

        public NpcVoiceEvent Find(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            foreach (var voiceEvent in events)
                if (voiceEvent != null && voiceEvent.eventId == eventId) return voiceEvent;
            return null;
        }

        public static int SelectNextIndex(int count, int last, int randomOffset)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (last < 0 || last >= count) return (randomOffset & int.MaxValue) % count;
            return (last + 1 + (randomOffset & int.MaxValue) % (count - 1)) % count;
        }
    }
}
