using System;
using UnityEngine;
using VRKoombar.WeaponsStaging.Core;

// POC scene opt-in: configure each Razara gun independently without changing the original scene.
public sealed class PlayerWeaponHeatSettingsV3 : MonoBehaviour
{
    [SerializeField] ShipHUDManager playerHud;
    [Serializable]
    public struct HeatSetting
    {
        public WeaponController weapon;
        [Range(.05f, 1f)] public float overheatThreshold;
        [Range(0f, 1f)] public float resumeThreshold;
        [Min(0f)] public float coolingPerSecond;
    }

    [SerializeField] HeatSetting[] weapons;
    [SerializeField, Range(.05f, 1f)] float laserOverheat = .75f;
    [SerializeField, Range(0f, 1f)] float laserResume = .3f;
    [SerializeField, Min(0f)] float laserCooling = .32f;
    [SerializeField, Range(.05f, 1f)] float kineticOverheat = .9f;
    [SerializeField, Range(0f, 1f)] float kineticResume = .35f;
    [SerializeField, Min(0f)] float kineticCooling = .4f;

    void Awake()
    {
        if (playerHud == null) playerHud = FindFirstObjectByType<ShipHUDManager>();
        CombatDamageable hull = playerHud != null ? playerHud.GetComponentInParent<CombatDamageable>() : null;
        if (hull == null) return;
        foreach (WeaponController weapon in hull.GetComponentsInChildren<WeaponController>(true))
        {
            bool laser = weapon.WeaponDamageType == VRKoombar.WeaponsStaging.Contracts.DamageType.Laser;
            weapon.ConfigureHeat(true, laser ? laserOverheat : kineticOverheat,
                laser ? laserResume : kineticResume, laser ? laserCooling : kineticCooling);
        }
        foreach (HeatSetting setting in weapons)
            if (setting.weapon != null)
                setting.weapon.ConfigureHeat(true, setting.overheatThreshold, setting.resumeThreshold, setting.coolingPerSecond);
    }
}
