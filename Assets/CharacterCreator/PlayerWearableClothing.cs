using System.Collections.Generic;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    // The worn visual is supplied by the creator wardrobe; the physical loot object
    // stays alive so its NpcItemInstance and backpack identity are never replaced.
    public sealed class PlayerWearableClothing : MonoBehaviour
    {
        public CharacterCosmeticsController cosmetics;
        public VRBodyController body;
        [Min(.15f)] public float dressingReach = .7f;
        readonly Dictionary<AppearanceSlot, WearableLoot> worn = new();
        readonly Dictionary<AppearanceSlot, AppearanceItemDefinition> previous = new();

        void Awake()
        {
            if (!cosmetics) cosmetics = GetComponent<CharacterCosmeticsController>();
            if (!body) body = GetComponent<VRBodyController>();
        }

        public Transform Anchor(AppearanceSlot slot)
        {
            if (!body) return null;
            return slot switch
            {
                AppearanceSlot.Headwear => body.head,
                AppearanceSlot.Legs => body.hips,
                AppearanceSlot.Torso or AppearanceSlot.OuterArmor => body.chest,
                _ => null
            };
        }

        public bool TryWear(WearableLoot loot)
        {
            if (!isActiveAndEnabled || !cosmetics || !loot || loot.IsWorn ||
                !loot.CanWear || loot.InventoryItem.IsHeld || loot.InventoryItem.CurrentContainer)
                return false;
            var slot = loot.Appearance.slot;
            var anchor = Anchor(slot);
            if (!anchor || worn.ContainsKey(slot)) return false;
            // Long skirts are naturally released below the pelvis. Measure to the
            // closest point on their physical collider rather than their import pivot.
            var shape = loot.GetComponent<Collider>();
            Vector3 nearest = shape && shape.enabled ? shape.ClosestPoint(anchor.position) : loot.transform.position;
            if (Vector3.Distance(nearest, anchor.position) > dressingReach)
                return false;
            var old = Get(slot);
            Set(slot, loot.Appearance);
            cosmetics.Refresh();
            bool fitted = false;
            foreach (var visual in transform.root.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                if (visual.slot == slot && visual.definition == loot.Appearance)
                { fitted = true; break; }
            if (!fitted)
            {
                Set(slot, old);
                cosmetics.Refresh();
                return false;
            }
            previous[slot] = old;
            worn[slot] = loot;
            loot.SetWorn(this, anchor);
            return true;
        }

        public bool Remove(WearableLoot loot)
        {
            if (!loot) return false;
            AppearanceSlot slot = default;
            bool found = false;
            foreach (var entry in worn)
                if (entry.Value == loot) { slot = entry.Key; found = true; break; }
            if (!found) return false;
            worn.Remove(slot);
            previous.TryGetValue(slot, out var old);
            previous.Remove(slot);
            if (cosmetics) { Set(slot, old); cosmetics.Refresh(); }
            loot.SetWorn(null, null);
            return true;
        }

        AppearanceItemDefinition Get(AppearanceSlot slot) => slot switch
        {
            AppearanceSlot.Headwear => cosmetics.headwear,
            AppearanceSlot.Torso => cosmetics.torso,
            AppearanceSlot.Legs => cosmetics.legs,
            AppearanceSlot.OuterArmor => cosmetics.outerArmor,
            _ => null
        };

        void Set(AppearanceSlot slot, AppearanceItemDefinition value)
        {
            switch (slot)
            {
                case AppearanceSlot.Headwear: cosmetics.headwear = value; break;
                case AppearanceSlot.Torso: cosmetics.torso = value; break;
                case AppearanceSlot.Legs: cosmetics.legs = value; break;
                case AppearanceSlot.OuterArmor: cosmetics.outerArmor = value; break;
            }
        }
    }
}
