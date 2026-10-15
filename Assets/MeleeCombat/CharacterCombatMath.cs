using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public static class CharacterCombatMath
    {
        public static float Offset(CharacterAttributes stats, CharacterAttribute attribute) =>
            stats ? Mathf.Clamp((stats.Get(attribute) - 50f) / 50f, -1f, 2f) : 0f;

        public static float AttackSpeed(CharacterAttributes stats,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) => Mathf.Clamp(
            1f + .18f * Offset(stats, CharacterAttribute.Dexterity) +
            .07f * ProficiencyOffset(stats, weaponClass), .8f, 1.25f);

        public static float AttackInterval(CharacterAttributes stats,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) => Mathf.Clamp(
            1f - .25f * Offset(stats, CharacterAttribute.MeleeAttack) -
            .10f * ProficiencyOffset(stats, weaponClass), .7f, 1.3f);

        public static float DefensiveDecision(CharacterAttributes stats) => Mathf.Clamp(
            1f + .30f * Offset(stats, CharacterAttribute.MeleeDefense) +
            .08f * Offset(stats, CharacterAttribute.Dexterity), .7f, 1.4f);

        public static float DamageTaken(CharacterAttributes stats) => Mathf.Clamp(
            1f - .25f * Offset(stats, CharacterAttribute.Toughness), .65f, 1.25f);

        public static float DamageDealt(CharacterAttributes stats,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) => Mathf.Clamp(
            1f + .12f * Offset(stats, CharacterAttribute.Strength) +
            .08f * ProficiencyOffset(stats, weaponClass), .8f, 1.25f);

        public static float WeaponControl(CharacterAttributes stats, float mass,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) => Mathf.Clamp(
            1f + .25f * Offset(stats, CharacterAttribute.Strength) +
            .12f * ProficiencyOffset(stats, weaponClass) +
            .08f * Offset(stats, CharacterAttribute.Dexterity) -
            .10f * Mathf.Max(0f, mass - 2.1f) * Mathf.Max(0f, -Offset(stats, CharacterAttribute.Strength)),
            .55f, 1.45f);

        public static float DisarmChance(float baseline, CharacterAttributes attacker,
            CharacterAttributes defender, PhysicalMeleeWeapon attackingWeapon,
            PhysicalMeleeWeapon defendingWeapon, float relativeSpeed, float impulse,
            float minimum, float maximum)
        {
            float strength = Difference(attacker, defender, CharacterAttribute.Strength);
            float attackDefense = Value(attacker, CharacterAttribute.MeleeAttack) -
                Value(defender, CharacterAttribute.MeleeDefense);
            float proficiency = ProficiencyValue(attacker, attackingWeapon.proficiencyClass) -
                ProficiencyValue(defender, defendingWeapon.proficiencyClass);
            float massRatio = Mathf.Log(Mathf.Max(.1f, attackingWeapon.mass) /
                Mathf.Max(.1f, defendingWeapon.mass));
            float attackLength = Length(attackingWeapon), defenseLength = Length(defendingWeapon);
            float leverage = Mathf.Log(attackLength / defenseLength);
            float impact = .10f * Mathf.Clamp((impulse - 1.5f) / 4f, -1f, 1f) +
                .05f * Mathf.Clamp((relativeSpeed - 2.5f) / 4f, -1f, 1f);
            float logFactor = .40f * strength + .50f * attackDefense + .35f * proficiency +
                .12f * massRatio + .08f * leverage + impact;
            return Mathf.Clamp(baseline * Mathf.Exp(logFactor), minimum, maximum);
        }
        static float Value(CharacterAttributes stats, CharacterAttribute attribute) =>
            stats ? stats.Get(attribute) / 50f : 1f;
        static float ProficiencyValue(CharacterAttributes stats,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) =>
            weaponClass == PhysicalMeleeWeapon.WeaponProficiencyClass.Staff ?
                Value(stats, CharacterAttribute.StaffProficiency) : 1f;
        static float ProficiencyOffset(CharacterAttributes stats,
            PhysicalMeleeWeapon.WeaponProficiencyClass weaponClass) =>
            weaponClass == PhysicalMeleeWeapon.WeaponProficiencyClass.Staff ?
                Offset(stats, CharacterAttribute.StaffProficiency) : 0f;
        static float Difference(CharacterAttributes a, CharacterAttributes b, CharacterAttribute attribute) =>
            Value(a, attribute) - Value(b, attribute);
        static float Length(PhysicalMeleeWeapon weapon)
        {
            var capsule = weapon.GetComponent<CapsuleCollider>();
            return capsule ? Mathf.Max(.1f, capsule.height) : 1f;
        }
    }
}
