using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public class CombatSandboxDebugController : MonoBehaviour
    {
        [SerializeField] private ShieldSystem targetShields;
        [SerializeField] private WeaponController[] weaponControllers;
        [SerializeField] private bool forceShieldBreach;
        [SerializeField] private ShieldArc forcedArc = ShieldArc.Front;
        [SerializeField] private bool infiniteAmmo;
        [SerializeField] private bool logEvents = true;

        private void OnEnable()
        {
            if (!logEvents)
            {
                return;
            }

            DamageEventBus.ShieldBreached += OnShieldBreached;
            DamageEventBus.SubsystemDisabled += OnSubsystemDisabled;
            DamageEventBus.HardpointStateChanged += OnHardpointChanged;
            DamageEventBus.CombatModeChanged += OnModeChanged;
        }

        private void OnDisable()
        {
            if (!logEvents)
            {
                return;
            }

            DamageEventBus.ShieldBreached -= OnShieldBreached;
            DamageEventBus.SubsystemDisabled -= OnSubsystemDisabled;
            DamageEventBus.HardpointStateChanged -= OnHardpointChanged;
            DamageEventBus.CombatModeChanged -= OnModeChanged;
        }

        private void Update()
        {
            if (forceShieldBreach && targetShields != null)
            {
                DamagePacket p = new DamagePacket
                {
                    DamageType = DamageType.Laser,
                    BaseDamage = 0f,
                    HeatAmount = 0f,
                    HitPointWorld = targetShields.transform.position + targetShields.transform.forward,
                    Timestamp = Time.time
                };

                for (int i = 0; i < 8; i++)
                {
                    targetShields.ApplyShieldPressure(forcedArc, p);
                }

                forceShieldBreach = false;
            }

            if (weaponControllers != null)
            {
                foreach (WeaponController weapon in weaponControllers)
                {
                    if (weapon != null)
                    {
                        weapon.SendMessage("SetInfiniteAmmoForDebug", infiniteAmmo, SendMessageOptions.DontRequireReceiver);
                    }
                }
            }
        }

        private void OnShieldBreached(ShieldArc arc) => Debug.Log($"[CombatDebug] Shield breached: {arc}");
        private void OnSubsystemDisabled(SubsystemType subsystem) => Debug.Log($"[CombatDebug] Subsystem disabled: {subsystem}");
        private void OnHardpointChanged(HardpointState state) => Debug.Log($"[CombatDebug] Hardpoint state: {state}");
        private void OnModeChanged(CombatMode mode) => Debug.Log($"[CombatDebug] Combat mode: {mode}");
    }
}
