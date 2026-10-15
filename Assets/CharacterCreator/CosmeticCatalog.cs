using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    [CreateAssetMenu(menuName = "Kenshi VR/Cosmetic Catalog")]
    public sealed class CosmeticCatalog : ScriptableObject
    {
        public AppearanceItemDefinition shortHair, mediumHair, longTiedBackHair;
        public AppearanceItemDefinition stubble, shortBeard, fullBeard;
        public AppearanceItemDefinition[] shortHairVariants, mediumHairVariants, longHairVariants;
        public AppearanceItemDefinition[] stubbleVariants, shortBeardVariants, fullBeardVariants;
        public AppearanceItemDefinition[] wardrobeItems;
        public AppearanceItemDefinition ForHair(HairStyle style, int variant = 0) => Pick(HairVariants(style), variant, HairFallback(style));
        public AppearanceItemDefinition ForBeard(BeardStyle style, int variant = 0) => Pick(BeardVariants(style), variant, BeardFallback(style));
        public int HairVariantCount(HairStyle style) => Count(HairVariants(style), HairFallback(style));
        public int BeardVariantCount(BeardStyle style) => Count(BeardVariants(style), BeardFallback(style));
        AppearanceItemDefinition[] HairVariants(HairStyle style) => style switch
        {
            HairStyle.Short => shortHairVariants, HairStyle.Medium => mediumHairVariants,
            HairStyle.LongTiedBack => longHairVariants, _ => null
        };
        AppearanceItemDefinition[] BeardVariants(BeardStyle style) => style switch
        {
            BeardStyle.Stubble => stubbleVariants, BeardStyle.ShortBeard => shortBeardVariants,
            BeardStyle.FullBeard => fullBeardVariants, _ => null
        };
        static int Count(AppearanceItemDefinition[] variants, AppearanceItemDefinition fallback) =>
            variants != null && variants.Length > 0 ? variants.Length : fallback ? 1 : 0;
        static AppearanceItemDefinition Pick(AppearanceItemDefinition[] variants, int variant, AppearanceItemDefinition fallback) =>
            variants != null && variants.Length > 0 ? variants[Mathf.Abs(variant) % variants.Length] : fallback;
        AppearanceItemDefinition HairFallback(HairStyle style) => style switch
        {
            HairStyle.Short => shortHair,
            HairStyle.Medium => mediumHair,
            HairStyle.LongTiedBack => longTiedBackHair,
            _ => null
        };
        AppearanceItemDefinition BeardFallback(BeardStyle style) => style switch
        {
            BeardStyle.Stubble => stubble,
            BeardStyle.ShortBeard => shortBeard,
            BeardStyle.FullBeard => fullBeard,
            _ => null
        };
    }
}
