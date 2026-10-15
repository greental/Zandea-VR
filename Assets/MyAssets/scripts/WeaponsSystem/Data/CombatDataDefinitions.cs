using System;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Data
{
    [CreateAssetMenu(menuName = "VRKoombar/Combat/Global Config")]
    public class CombatGlobalConfig : ScriptableObject
    {
        [Header("Routing")]
        public float kineticVsIntactShieldMultiplier = 0.15f;
        public float missileVsIntactShieldMultiplier = 0.2f;
        public float laserVsHullMultiplier = 0.1f;

        [Header("Debug")]
        public bool logDamageRouting = true;
        public bool logShieldBreaches = true;
        public bool logSubsystemFailures = true;
    }

    [Serializable]
    public struct ArcShieldConfig
    {
        public ShieldArc arc;
        public float capacity;
        public float rechargeDelay;
        public float rechargeRate;
        public float instabilityGainPerLaserHit;
        public float instabilityDecayPerSecond;
        public float breachThreshold;
        public float breachDuration;
        public float breachRecoverySeconds;
    }

    [CreateAssetMenu(menuName = "VRKoombar/Combat/Shield Zone Config")]
    public class ShieldZoneConfig : ScriptableObject
    {
        public ArcShieldConfig[] arcs;

        [Header("Consecutive Laser Rules")]
        public float consecutiveHitWindowSeconds = 1.0f;
        public float sameArcConsecutiveMultiplier = 1.4f;
    }

    [Serializable]
    public struct WeaponSubsystemEffect
    {
        public SubsystemType subsystem;
        public float heatEffect;
        public float kineticEffect;
    }

    [Serializable]
    public struct CombatModeProfile
    {
        public CombatMode mode;
        public float enginesMultiplier;
        public float shieldsMultiplier;
        public float weaponsMultiplier;
    }

    [CreateAssetMenu(menuName = "VRKoombar/Combat/Combat Mode Config")]
    public class ShipCombatModeConfig : ScriptableObject
    {
        public CombatModeProfile[] profiles;
    }

    [CreateAssetMenu(menuName = "VRKoombar/Combat/Undeploy Defense Config")]
    public class UndeployDefenseConfig : ScriptableObject
    {
        [Range(0f, 1f)]
        public float instantShieldRestoreFraction = 0.2f;
        public float temporaryShieldRegenBonus = 1.5f;
        public float bonusDurationSeconds = 6f;
        public float cooldownSeconds = 14f;
        public float minimumStowSeconds = 2.5f;
    }
}
