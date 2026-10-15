using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    public enum AppearanceSlot { Hair, Beard, Headwear, Torso, Legs, Feet, Hands, OuterArmor }

    [CreateAssetMenu(menuName = "Kenshi VR/Appearance Item")]
    public sealed class AppearanceItemDefinition : ScriptableObject
    {
        public AppearanceSlot slot;
        public GameObject prefab;
        [Tooltip("For rigid accessories; use the exact bone name in the character rig.")]
        public string attachBoneName;
        [Tooltip("Skinned assets must use the same bone names and rest pose as the character.")]
        public bool skinned;
        [Tooltip("Required body morph names on the clothing mesh. Leave empty only for a rigid item or normal-only fit.")]
        public string[] requiredMorphs;
        [Tooltip("Exact character renderer names to hide while equipped. The source body is split by material, not clothing region.")]
        public string[] hideRendererNames;
        [Tooltip("Items in the same family are preferred together by the clothing matcher.")]
        public string styleFamily;
        [Tooltip("A fitting note shown in the Inspector for preview-only pieces.")]
        public string fitNote;
        public bool hideHair;
        public bool coversOutfit;
        [Range(0f, 10f)] public float randomWeight = 1f;
    }

}
