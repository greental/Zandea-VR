using UnityEngine;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class DefensiveDisengageController : MonoBehaviour
    {
        [SerializeField] private UndeployDefenseConfig config;
        [SerializeField] private WeaponHardpoint[] hardpoints;
        [SerializeField] private CombatDamageable damageable;
        [SerializeField] private bool verboseLogs;

        private float _bonusEndTime;
        private bool _bonusActive;

        public bool BonusActive => _bonusActive && Time.time < _bonusEndTime;

        private void Update()
        {
            if (_bonusActive && Time.time >= _bonusEndTime)
            {
                _bonusActive = false;
                if (verboseLogs) Debug.Log("[Disengage] Defensive bonus ended.", this);
            }
        }

        public bool TryActivateByUndeploy()
        {
            if (config == null || hardpoints == null || hardpoints.Length == 0 || damageable == null)
            {
                return false;
            }

            bool anyConsumed = false;
            foreach (WeaponHardpoint hardpoint in hardpoints)
            {
                if (hardpoint != null && hardpoint.TryConsumeUndeployDefense())
                {
                    anyConsumed = true;
                }
            }

            if (!anyConsumed)
            {
                return false;
            }

            damageable.ApplyUndeployDefenseBonus(
                config.instantShieldRestoreFraction,
                config.temporaryShieldRegenBonus,
                config.bonusDurationSeconds);
            _bonusActive = true;
            _bonusEndTime = Time.time + config.bonusDurationSeconds;
            if (verboseLogs) Debug.Log("[Disengage] Defensive shield bonus activated.", this);
            return true;
        }
    }
}
