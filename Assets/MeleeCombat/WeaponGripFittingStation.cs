using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Editor-only authoring marker. Its custom Inspector supplies the weapon dropdown and preview.
    public sealed class WeaponGripFittingStation : MonoBehaviour
    {
        public PhysicalMeleeWeapon weaponPrefab;
        [HideInInspector] public GameObject characterPreview;
        [HideInInspector] public PhysicalMeleeWeapon weaponPreview;
    }
}
