using UnityEngine;
using KenshiVR.CharacterCreator;

namespace KenshiVR.MeleeCombat
{
    public enum NpcItemCategory { Weapon, Clothing, Consumable, Currency, Miscellaneous }
    public enum NpcEquipmentSlot { None, MainWeapon, Torso, Legs, Headwear }

    [CreateAssetMenu(menuName = "Kenshi VR/NPC Item")]
    public sealed class NpcItemDefinition : ScriptableObject
    {
        public string itemId;
        public string displayName;
        public NpcItemCategory category;
        [Min(0)] public int valueCats;
        [Min(0)] public float weight;
        public GameObject worldPrefab;
        public AppearanceItemDefinition equippedAppearance;
        public Sprite icon;
        public NpcEquipmentSlot defaultSlot;
    }
}
