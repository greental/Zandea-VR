using System;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    [Serializable] public struct FloatRange
    {
        [Range(0f, 1.2f)] public float min, max;
        public float Pick(System.Random random) => Mathf.Lerp(min, max, (float)random.NextDouble());
    }
    [Serializable] public struct WeightedHair { public HairStyle style; [Min(0)] public float weight; }
    [Serializable] public struct WeightedBeard { public BeardStyle style; [Min(0)] public float weight; }
    [Serializable] public struct WeightedItem { public AppearanceItemDefinition item; [Min(0)] public float weight; }
    [Serializable] public sealed class GeneratedAppearance
    {
        public CharacterAppearanceData appearance;
        public AppearanceItemDefinition headwear, torso, legs, feet, hands, outerArmor;
    }

    [CreateAssetMenu(menuName = "Kenshi VR/Appearance Archetype")]
    public sealed class AppearanceArchetype : ScriptableObject
    {
        public FloatRange height = new FloatRange { min = .9f, max = 1.1f };
        public FloatRange weight = new FloatRange { min = .25f, max = .75f };
        public FloatRange muscularity = new FloatRange { min = .25f, max = .75f };
        public FloatRange faceVariation = new FloatRange { min = .25f, max = .75f };
        public Color skinLow = new Color(.35f, .23f, .18f);
        public Color skinHigh = new Color(.9f, .72f, .58f);
        public Color eyeLow = new Color(.2f, .28f, .25f);
        public Color eyeHigh = new Color(.5f, .63f, .75f);
        public WeightedHair[] hair;
        public WeightedBeard[] beard;
        public WeightedItem[] headwear, torso, legs, feet, hands, outerArmor;

        public GeneratedAppearance Generate(System.Random random, CharacterAppearanceController controller = null)
        {
            var result = new GeneratedAppearance();
            var data = new CharacterAppearanceData
            {
                height = Mathf.Clamp(height.Pick(random), .8f, 1.2f),
                weight = weight.Pick(random), muscularity = muscularity.Pick(random),
                skinColor = Color.Lerp(skinLow, skinHigh, (float)random.NextDouble()),
                eyeColor = Color.Lerp(eyeLow, eyeHigh, (float)random.NextDouble()),
                hairStyle = PickHair(random), beardStyle = PickBeard(random)
            };
            data.noseWidth = Vary(random, controller, AppearanceProperty.NoseWidth);
            data.noseTip = Vary(random, controller, AppearanceProperty.NoseTip);
            data.jawWidth = Vary(random, controller, AppearanceProperty.JawWidth);
            data.earSize = Vary(random, controller, AppearanceProperty.EarSize);
            data.foreheadShape = Vary(random, controller, AppearanceProperty.ForeheadShape);
            data.eyeSize = Vary(random, controller, AppearanceProperty.EyeSize);
            data.eyeSpacing = Vary(random, controller, AppearanceProperty.EyeSpacing);
            data.eyebrowHeight = Vary(random, controller, AppearanceProperty.EyebrowHeight);
            data.eyebrowThickness = Vary(random, controller, AppearanceProperty.EyebrowThickness);
            data.eyebrowAngle = Vary(random, controller, AppearanceProperty.EyebrowAngle);
            data.eyebrowSpacing = Vary(random, controller, AppearanceProperty.EyebrowSpacing);
            data.eyeShape = Vary(random, controller, AppearanceProperty.EyeShape);
            data.eyeTilt = Vary(random, controller, AppearanceProperty.EyeTilt);
            data.mouthWidth = Vary(random, controller, AppearanceProperty.MouthWidth);
            data.lipFullness = Vary(random, controller, AppearanceProperty.LipFullness);
            data.hairColor = Color.Lerp(new Color(.08f, .06f, .04f), new Color(.55f, .38f, .24f), (float)random.NextDouble());
            data.beardColor = data.hairColor;
            result.appearance = data;
            result.headwear = PickItem(headwear, random); result.torso = PickItem(torso, random);
            result.legs = PickItem(legs, random); result.feet = PickItem(feet, random);
            result.hands = PickItem(hands, random); result.outerArmor = PickItem(outerArmor, random);
            return result;
        }
        float Vary(System.Random random, CharacterAppearanceController controller, AppearanceProperty property) =>
            controller && !controller.IsSupported(property) ? .5f : faceVariation.Pick(random);
        HairStyle PickHair(System.Random random)
        {
            float total = 0; if (hair != null) foreach (var entry in hair) total += Mathf.Max(0, entry.weight);
            if (total <= 0) return HairStyle.Bald;
            double draw = random.NextDouble() * total;
            if (hair != null) foreach (var entry in hair) { draw -= Mathf.Max(0, entry.weight); if (draw <= 0) return entry.style; }
            return HairStyle.Bald;
        }
        BeardStyle PickBeard(System.Random random)
        {
            float total = 0; if (beard != null) foreach (var entry in beard) total += Mathf.Max(0, entry.weight);
            if (total <= 0) return BeardStyle.CleanShaven;
            double draw = random.NextDouble() * total;
            if (beard != null) foreach (var entry in beard) { draw -= Mathf.Max(0, entry.weight); if (draw <= 0) return entry.style; }
            return BeardStyle.CleanShaven;
        }
        static AppearanceItemDefinition PickItem(WeightedItem[] options, System.Random random)
        {
            float total = 0; if (options != null) foreach (var option in options) total += Mathf.Max(0, option.weight);
            if (total <= 0) return null;
            double draw = random.NextDouble() * total;
            foreach (var option in options) { draw -= Mathf.Max(0, option.weight); if (draw <= 0) return option.item; }
            return null;
        }
    }
}
