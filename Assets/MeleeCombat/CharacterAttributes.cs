using System;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public enum CharacterAttribute { Strength, Dexterity, MeleeAttack, MeleeDefense, Toughness, StaffProficiency, MartialArts }

    [Serializable]
    public struct CharacterAttributeValues
    {
        [Range(1, 100)] public int strength, dexterity, meleeAttack, meleeDefense, toughness, staffProficiency, martialArts;
        public static CharacterAttributeValues Average => new CharacterAttributeValues {
            strength = 50, dexterity = 50, meleeAttack = 50, meleeDefense = 50,
            toughness = 50, staffProficiency = 50, martialArts = 50
        };
        public int Get(CharacterAttribute attribute) => attribute switch {
            CharacterAttribute.Strength => strength,
            CharacterAttribute.Dexterity => dexterity,
            CharacterAttribute.MeleeAttack => meleeAttack,
            CharacterAttribute.MeleeDefense => meleeDefense,
            CharacterAttribute.Toughness => toughness,
            CharacterAttribute.MartialArts => martialArts > 0 ? martialArts : meleeAttack,
            _ => staffProficiency
        };
        public void Set(CharacterAttribute attribute, int value)
        {
            switch (attribute)
            {
                case CharacterAttribute.Strength: strength = value; break;
                case CharacterAttribute.Dexterity: dexterity = value; break;
                case CharacterAttribute.MeleeAttack: meleeAttack = value; break;
                case CharacterAttribute.MeleeDefense: meleeDefense = value; break;
                case CharacterAttribute.Toughness: toughness = value; break;
                case CharacterAttribute.MartialArts: martialArts = value; break;
                default: staffProficiency = value; break;
            }
        }
        public void Clamp(int maximum)
        {
            for (int i = 0; i < 7; i++)
                Set((CharacterAttribute)i, Mathf.Clamp(Get((CharacterAttribute)i), 1, maximum));
        }
    }

    // A profile supplies defaults. All combat reads this character's copied runtime values.
    // Future training, injury, and equipment systems can change runtime values without
    // writing to a shared asset.
    public sealed class CharacterAttributes : MonoBehaviour
    {
        public CharacterAttributeProfile profile;
        public CharacterAttributeValues startingValues = CharacterAttributeValues.Average;
        [Min(100)] public int runtimeMaximum = 200;
        [SerializeField] CharacterAttributeValues runtimeValues;
        bool initialized;
        public event Action<CharacterAttribute, int> Changed;
        public CharacterAttributeValues RuntimeValues { get { EnsureInitialized(); return runtimeValues; } }
        void Awake() => ResetToDefaults();
        void OnValidate() { startingValues.Clamp(100); runtimeMaximum = Mathf.Max(100, runtimeMaximum); }
        void EnsureInitialized() { if (!initialized) ResetToDefaults(); }
        public void ResetToDefaults()
        {
            runtimeValues = profile ? profile.values : startingValues;
            runtimeValues.Clamp(runtimeMaximum);
            initialized = true;
        }
        public void ApplyProfile(CharacterAttributeProfile value)
        {
            profile = value;
            ResetToDefaults();
            for (int i = 0; i < 7; i++)
                Changed?.Invoke((CharacterAttribute)i, runtimeValues.Get((CharacterAttribute)i));
        }
        public int Get(CharacterAttribute attribute) { EnsureInitialized(); return runtimeValues.Get(attribute); }
        public void SetRuntime(CharacterAttribute attribute, int value)
        {
            EnsureInitialized();
            value = Mathf.Clamp(value, 1, runtimeMaximum);
            if (runtimeValues.Get(attribute) == value) return;
            runtimeValues.Set(attribute, value);
            Changed?.Invoke(attribute, value);
        }
        public static CharacterAttributes Find(Transform root) => root ?
            root.GetComponent<CharacterAttributes>() ?? root.GetComponentInChildren<CharacterAttributes>(true) : null;
    }
}
