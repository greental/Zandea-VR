using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public static class CharacterClothingMatcher
    {
        static readonly AppearanceSlot[] Slots =
        {
            AppearanceSlot.Headwear, AppearanceSlot.Torso, AppearanceSlot.Legs,
            AppearanceSlot.Feet, AppearanceSlot.Hands, AppearanceSlot.OuterArmor
        };

        public static void Match(CharacterCosmeticsController cosmetics, System.Random random = null, string requestedFamily = null)
        {
            if (!cosmetics || !cosmetics.catalog || cosmetics.catalog.wardrobeItems == null) return;
            random = random ?? new System.Random();
            var items = cosmetics.catalog.wardrobeItems;
            var families = new List<string>();
            foreach (var item in items)
            {
                if (!item || !item.prefab || string.IsNullOrEmpty(item.styleFamily)) continue;
                if (item.slot != AppearanceSlot.Torso && item.slot != AppearanceSlot.Legs) continue;
                if (!families.Contains(item.styleFamily)) families.Add(item.styleFamily);
            }
            if (families.Count == 0) return;
            string family = !string.IsNullOrEmpty(requestedFamily) && families.Contains(requestedFamily)
                ? requestedFamily : families[random.Next(families.Count)];
            foreach (var slot in Slots)
            {
                if (slot == AppearanceSlot.OuterArmor && cosmetics.torso)
                {
                    cosmetics.outerArmor = null;
                    continue;
                }
                var candidates = new List<AppearanceItemDefinition>();
                foreach (var item in items)
                    if (item && item.prefab && item.slot == slot && item.styleFamily == family && item.randomWeight > 0)
                        candidates.Add(item);
                // Complete the outfit when a style family has no item for this part.
                if (candidates.Count == 0)
                    foreach (var item in items)
                        if (item && item.prefab && item.slot == slot && item.randomWeight > 0)
                            candidates.Add(item);
                var selected = Pick(candidates, random);
                switch (slot)
                {
                    case AppearanceSlot.Headwear: cosmetics.headwear = selected; break;
                    case AppearanceSlot.Torso: cosmetics.torso = selected; break;
                    case AppearanceSlot.Legs: cosmetics.legs = selected; break;
                    case AppearanceSlot.Feet: cosmetics.feet = selected; break;
                    case AppearanceSlot.Hands: cosmetics.hands = selected; break;
                    case AppearanceSlot.OuterArmor: cosmetics.outerArmor = selected; break;
                }
            }
            if (cosmetics.outerArmor && cosmetics.outerArmor.coversOutfit)
                cosmetics.torso = cosmetics.legs = null;
            cosmetics.Refresh();
        }

        static AppearanceItemDefinition Pick(List<AppearanceItemDefinition> items, System.Random random)
        {
            float total = 0f;
            foreach (var item in items) total += Mathf.Max(0f, item.randomWeight);
            if (total <= 0f) return null;
            float draw = (float)random.NextDouble() * total;
            foreach (var item in items)
            {
                draw -= Mathf.Max(0f, item.randomWeight);
                if (draw <= 0f) return item;
            }
            return items[items.Count - 1];
        }
    }
}

