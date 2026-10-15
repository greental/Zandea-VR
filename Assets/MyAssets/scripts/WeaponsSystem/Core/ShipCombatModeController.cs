using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class ShipCombatModeController : MonoBehaviour, IShipCombatModeController
    {
        [SerializeField] private ShipCombatModeConfig modeConfig;
        [SerializeField] private CombatMode defaultMode = CombatMode.Tanky;

        public CombatMode CurrentMode { get; private set; }

        private void Awake()
        {
            CurrentMode = defaultMode;
            DamageEventBus.RaiseCombatModeChanged(CurrentMode);
        }

        public void SetMode(CombatMode mode)
        {
            if (CurrentMode == mode)
            {
                return;
            }

            CurrentMode = mode;
            DamageEventBus.RaiseCombatModeChanged(CurrentMode);
        }

        public float GetEngineMultiplier() => GetProfile(CurrentMode).enginesMultiplier;
        public float GetShieldMultiplier() => GetProfile(CurrentMode).shieldsMultiplier;
        public float GetWeaponMultiplier() => GetProfile(CurrentMode).weaponsMultiplier;

        private CombatModeProfile GetProfile(CombatMode mode)
        {
            if (modeConfig == null || modeConfig.profiles == null)
            {
                return DefaultProfile(mode);
            }

            foreach (CombatModeProfile profile in modeConfig.profiles)
            {
                if (profile.mode == mode)
                {
                    return profile;
                }
            }

            return DefaultProfile(mode);
        }

        private static CombatModeProfile DefaultProfile(CombatMode mode)
        {
            return new CombatModeProfile
            {
                mode = mode,
                enginesMultiplier = 1f,
                shieldsMultiplier = 1f,
                weaponsMultiplier = 1f
            };
        }
    }
}
