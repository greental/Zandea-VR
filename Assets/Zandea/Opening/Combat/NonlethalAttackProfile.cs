using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Opening.Combat
{
    // Attach only to attackers intended to capture. Ordinary staff damage stays lethal.
    public sealed class NonlethalAttackProfile : MonoBehaviour
    {
        public bool requireBluntStaff = true;

        public bool AppliesTo(PhysicalMeleeWeapon weapon)
        {
            return enabled && weapon && (!requireBluntStaff ||
                weapon.strikeType == PhysicalMeleeWeapon.StrikeType.Blunt &&
                weapon.proficiencyClass == PhysicalMeleeWeapon.WeaponProficiencyClass.Staff);
        }
    }
}
