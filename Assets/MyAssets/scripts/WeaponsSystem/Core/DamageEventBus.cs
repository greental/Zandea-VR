using System;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public static class DamageEventBus
    {
        public static event Action<ShieldArc> ShieldBreached;
        public static event Action<ShieldArc> ShieldRecovered;
        public static event Action<SubsystemType> SubsystemDisabled;
        public static event Action<SubsystemType> SubsystemRecovered;
        public static event Action<HardpointState> HardpointStateChanged;
        public static event Action<CombatMode> CombatModeChanged;
        public static event Action<ShipCombatState> ShipCombatStateChanged;

        public static void RaiseShieldBreached(ShieldArc arc) => ShieldBreached?.Invoke(arc);
        public static void RaiseShieldRecovered(ShieldArc arc) => ShieldRecovered?.Invoke(arc);
        public static void RaiseSubsystemDisabled(SubsystemType subsystem) => SubsystemDisabled?.Invoke(subsystem);
        public static void RaiseSubsystemRecovered(SubsystemType subsystem) => SubsystemRecovered?.Invoke(subsystem);
        public static void RaiseHardpointStateChanged(HardpointState state) => HardpointStateChanged?.Invoke(state);
        public static void RaiseCombatModeChanged(CombatMode mode) => CombatModeChanged?.Invoke(mode);
        public static void RaiseShipCombatStateChanged(ShipCombatState state) => ShipCombatStateChanged?.Invoke(state);
    }
}
