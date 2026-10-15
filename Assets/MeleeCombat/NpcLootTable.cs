using System;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    [CreateAssetMenu(menuName = "Kenshi VR/NPC Loot Table")]
    public sealed class NpcLootTable : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public NpcItemDefinition item;
            [Range(0, 1)] public float probability = 1;
            [Min(0)] public int minimum = 1, maximum = 1;
            [Min(0)] public float weight = 1;
            public NpcEquipmentSlot slot;
            public NpcEquipmentSlot pocketOf;
        }
        public Entry[] guaranteed = Array.Empty<Entry>();
        public Entry[] rolls = Array.Empty<Entry>();
        [Min(0)] public int weightedPicks;
        public Entry[] weighted = Array.Empty<Entry>();
    }
}
