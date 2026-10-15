using System;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    // The Inspector and NPC tools can share the same restrained color palettes.
    public static class CharacterAppearanceRandomizer
    {
        static readonly Color[] SkinTones = {
            new Color(.94f, .78f, .67f), new Color(.86f, .66f, .53f),
            new Color(.76f, .54f, .41f), new Color(.64f, .43f, .32f),
            new Color(.51f, .33f, .25f), new Color(.38f, .24f, .18f)
        };
        static readonly Color[] EyeTones = {
            new Color(.24f, .16f, .09f), new Color(.43f, .28f, .15f),
            new Color(.38f, .37f, .19f), new Color(.23f, .38f, .28f),
            new Color(.22f, .35f, .49f), new Color(.42f, .47f, .48f)
        };
        static readonly Color[] HairTones = {
            new Color(.07f, .055f, .045f), new Color(.17f, .105f, .065f),
            new Color(.32f, .19f, .10f), new Color(.47f, .25f, .12f),
            new Color(.58f, .46f, .27f), new Color(.54f, .52f, .49f)
        };

        static float Between(System.Random random, float min, float max) =>
            Mathf.Lerp(min, max, (float)random.NextDouble());
        static Color PickNeighborBlend(System.Random random, Color[] palette)
        {
            int first = random.Next(palette.Length);
            int second = Mathf.Clamp(first + (random.Next(2) == 0 ? -1 : 1), 0, palette.Length - 1);
            return Color.Lerp(palette[first], palette[second], Between(random, 0f, .45f));
        }
        static float FaceValue(System.Random random, CharacterAppearanceController controller, AppearanceProperty property) =>
            controller.IsSupported(property) ? Between(random, .2f, .8f) : .5f;

        public static void Body(CharacterAppearanceData data, System.Random random)
        {
            data.height = Between(random, .85f, 1.15f);
            data.weight = Between(random, .1f, .95f);
            data.muscularity = Between(random, .1f, .95f);
        }
        public static void Face(CharacterAppearanceData data, CharacterAppearanceController controller, System.Random random)
        {
            data.noseWidth = FaceValue(random, controller, AppearanceProperty.NoseWidth);
            data.noseTip = FaceValue(random, controller, AppearanceProperty.NoseTip);
            data.jawWidth = FaceValue(random, controller, AppearanceProperty.JawWidth);
            data.earSize = FaceValue(random, controller, AppearanceProperty.EarSize);
            data.foreheadShape = FaceValue(random, controller, AppearanceProperty.ForeheadShape);
            data.eyeSize = FaceValue(random, controller, AppearanceProperty.EyeSize);
            data.eyeSpacing = FaceValue(random, controller, AppearanceProperty.EyeSpacing);
            data.eyebrowHeight = FaceValue(random, controller, AppearanceProperty.EyebrowHeight);
            data.eyebrowThickness = FaceValue(random, controller, AppearanceProperty.EyebrowThickness);
            data.eyebrowAngle = FaceValue(random, controller, AppearanceProperty.EyebrowAngle);
            data.eyebrowSpacing = FaceValue(random, controller, AppearanceProperty.EyebrowSpacing);
            data.eyeShape = FaceValue(random, controller, AppearanceProperty.EyeShape);
            data.eyeTilt = FaceValue(random, controller, AppearanceProperty.EyeTilt);
            data.mouthWidth = FaceValue(random, controller, AppearanceProperty.MouthWidth);
            data.lipFullness = FaceValue(random, controller, AppearanceProperty.LipFullness);
        }
        public static void Colors(CharacterAppearanceData data, System.Random random)
        {
            Skin(data, random);
            Eyes(data, random);
        }
        public static void Skin(CharacterAppearanceData data, System.Random random) =>
            data.skinColor = PickNeighborBlend(random, SkinTones);
        public static void Eyes(CharacterAppearanceData data, System.Random random) =>
            data.eyeColor = PickNeighborBlend(random, EyeTones);
        public static void Hair(CharacterAppearanceData data, CosmeticCatalog catalog, System.Random random)
        {
            var available = new System.Collections.Generic.List<HairStyle> { HairStyle.Bald };
            if (catalog)
                foreach (HairStyle style in Enum.GetValues(typeof(HairStyle)))
                    if (style != HairStyle.Bald && catalog.ForHair(style) && catalog.ForHair(style).prefab)
                        available.Add(style);
            data.hairStyle = available[random.Next(available.Count)];
            data.hairVariant = catalog && catalog.HairVariantCount(data.hairStyle) > 0 ? random.Next(catalog.HairVariantCount(data.hairStyle)) : 0;
            data.hairColor = PickNeighborBlend(random, HairTones);
            if (data.linkBeardColor) data.beardColor = data.hairColor;
        }
        public static void Beard(CharacterAppearanceData data, CosmeticCatalog catalog, System.Random random)
        {
            var available = new System.Collections.Generic.List<BeardStyle> { BeardStyle.CleanShaven };
            if (catalog)
                foreach (BeardStyle style in Enum.GetValues(typeof(BeardStyle)))
                    if (style != BeardStyle.CleanShaven && catalog.ForBeard(style) && catalog.ForBeard(style).prefab)
                        available.Add(style);
            data.beardStyle = available[random.Next(available.Count)];
            data.beardVariant = catalog && catalog.BeardVariantCount(data.beardStyle) > 0 ? random.Next(catalog.BeardVariantCount(data.beardStyle)) : 0;
            data.linkBeardColor = random.NextDouble() < .8;
            data.beardColor = data.linkBeardColor ? data.hairColor : PickNeighborBlend(random, HairTones);
        }
    }
}
