using System;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public sealed class NPCCharacterAppearance : MonoBehaviour
    {
        public CharacterAppearanceController appearance;
        public CharacterCosmeticsController cosmetics;
        public AppearancePreset preset;
        public AppearanceArchetype archetype;
        public int seed = 12345;

        void Awake()
        {
            if (!appearance) appearance = GetComponent<CharacterAppearanceController>();
            if (!cosmetics) cosmetics = GetComponent<CharacterCosmeticsController>();
        }
        public void Generate(int deterministicSeed)
        {
            if (!appearance || !archetype) return;
            appearance.Initialize();
            var generated = archetype.Generate(new System.Random(deterministicSeed), appearance);
            ApplyGenerated(generated);
        }
        public void ApplyPreset(AppearancePreset source)
        {
            if (!source) return;
            ApplyGenerated(new GeneratedAppearance { appearance = source.appearance, headwear = source.headwear,
                torso = source.torso, legs = source.legs, feet = source.feet, hands = source.hands, outerArmor = source.outerArmor });
        }
        public void ApplyGenerated(GeneratedAppearance source)
        {
            if (source == null || !appearance) return;
            appearance.Initialize();
            appearance.Apply(source.appearance);
            if (!cosmetics) return;
            cosmetics.headwear = source.headwear; cosmetics.torso = source.torso;
            cosmetics.legs = source.legs; cosmetics.feet = source.feet;
            cosmetics.hands = source.hands; cosmetics.outerArmor = source.outerArmor;
            cosmetics.Refresh();
        }
    }
}
