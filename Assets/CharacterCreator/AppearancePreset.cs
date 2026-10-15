using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    [CreateAssetMenu(menuName = "Kenshi VR/Appearance Preset")]
    public sealed class AppearancePreset : ScriptableObject
    {
        public CharacterAppearanceData appearance = new CharacterAppearanceData();
        public AppearanceItemDefinition headwear, torso, legs, feet, hands, outerArmor;
    }
}
