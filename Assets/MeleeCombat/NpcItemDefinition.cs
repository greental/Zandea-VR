using UnityEngine;
using KenshiVR.CharacterCreator;

namespace KenshiVR.MeleeCombat
{
    public enum NpcItemCategory { Weapon, Clothing, Consumable, Currency, Miscellaneous }
    public enum NpcEquipmentSlot { None, MainWeapon, Torso, Legs, Headwear }
    public enum GarmentFlattenAxis { None, X, Y, Z }

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
        [Header("Physical clothing loot")]
        public GarmentFlattenAxis flattenAxis = GarmentFlattenAxis.Z;
        [Range(.05f, 1f)] public float flattenFactor = .15f;
        [Min(.002f)] public float minimumThickness = .012f;
        public Vector3 lootEuler;
        public Vector3 gripOffset;
        public bool preferCachedLoot;
        [HideInInspector] public int lootBakeVersion;
    }
}
