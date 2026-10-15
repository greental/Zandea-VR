using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public class AICombatPilot : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private WeaponGroupController weaponGroups;
        [SerializeField] private ShipCombatModeController modeController;
        [SerializeField] private ShieldSystem selfShields;
        [SerializeField] private float engagementRange = 700f;
        [SerializeField] private float lowShieldThreshold = 0.25f;

        private void Update()
        {
            if (target == null || weaponGroups == null || modeController == null || selfShields == null)
            {
                return;
            }

            float distance = Vector3.Distance(transform.position, target.position);
            if (distance > engagementRange)
            {
                return;
            }

            float frontShield = selfShields.GetArcNormalized(ShieldArc.Front);
            if (frontShield <= lowShieldThreshold)
            {
                modeController.SetMode(CombatMode.Runaway);
                return;
            }

            modeController.SetMode(CombatMode.Chase);
            weaponGroups.FirePrimary();
            weaponGroups.FireSecondary();
        }
    }
}
