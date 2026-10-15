using System;
using UnityEngine;

namespace VRKoombar.WeaponsStaging.Contracts
{
    public enum DamageType
    {
        Laser,
        Kinetic,
        Missile
    }

    public enum ShieldArc
    {
        Front,
        Left,
        Right,
        Rear
    }

    public enum SubsystemType
    {
        Engines,
        Weapons,
        Targeting,
        ShieldGenerator
    }

    public enum HardpointState
    {
        Stowed,
        Deploying,
        Deployed,
        Undeploying
    }

    public enum CombatMode
    {
        Chase,
        Tanky,
        Runaway
    }

    public enum ShipCombatState
    {
        CombatReady,
        CombatDisabled,
        Destroyed
    }

    [Serializable]
    public struct DamagePacket
    {
        public DamageType DamageType;
        public float BaseDamage;
        public float HeatAmount;
        public Vector3 HitPointWorld;
        public Vector3 HitNormalWorld;
        public GameObject Source;
        public string WeaponId;
        public float Timestamp;
    }

    public interface IDamageable
    {
        void ApplyDamage(in DamagePacket packet);
        ShipCombatState GetCombatState();
    }

    public interface IShieldSystem
    {
        ShieldArc ResolveArc(Vector3 hitPointWorld);
        bool IsArcBreached(ShieldArc arc);
        void ApplyShieldPressure(ShieldArc arc, in DamagePacket packet);
        float GetArcNormalized(ShieldArc arc);
    }

    public interface ISubsystemController
    {
        void ApplyHeat(SubsystemType subsystem, float amount);
        void ApplyKineticDamage(SubsystemType subsystem, float amount);
        bool IsSubsystemOperational(SubsystemType subsystem);
    }

    public interface IWeaponHardpoint
    {
        HardpointState State { get; }
        bool CanFire { get; }
        bool TrySetDeployed(bool deployed);
    }

    public interface IWeaponController
    {
        bool CanFire { get; }
        void Tick(float deltaTime);
        bool TryFire();

        /// <summary>
        /// XR trigger state for gatling-style weapons: loop while held, tail when released.
        /// Called every frame by <see cref="VRKoombar.WeaponsStaging.Core.ShipWeaponsOrchestrator"/>.
        /// </summary>
        void SetGatlingTriggerHeld(bool held);
    }

    public interface IShipCombatModeController
    {
        CombatMode CurrentMode { get; }
        void SetMode(CombatMode mode);
        float GetEngineMultiplier();
        float GetShieldMultiplier();
        float GetWeaponMultiplier();
    }
}
