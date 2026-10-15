using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    [CreateAssetMenu(menuName = "Kenshi VR/Character attribute profile")]
    public sealed class CharacterAttributeProfile : ScriptableObject
    {
        public CharacterAttributeValues values = CharacterAttributeValues.Average;
        void OnValidate() => values.Clamp(100);
    }
}
