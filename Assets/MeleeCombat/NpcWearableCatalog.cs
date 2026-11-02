using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Authored NPC outfits need the same priced item assets as loose loot.
    public sealed class NpcWearableCatalog : ScriptableObject
    {
        public List<NpcItemDefinition> items = new();
        public List<NpcItemDefinition> weapons = new();

        public NpcItemDefinition Find(AppearanceItemDefinition appearance)
        {
            if (!appearance) return null;
            foreach (var item in items)
                if (item && item.equippedAppearance == appearance && item.worldPrefab)
                    return item;
            return null;
        }
        public NpcItemDefinition FindWeapon(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            string id = prefabName.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
            foreach (var item in weapons)
                if (item && item.itemId == id && item.worldPrefab) return item;
            return null;
        }
    }
}
