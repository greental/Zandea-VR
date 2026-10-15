using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class CombatDamageable : MonoBehaviour, IDamageable
    {
        [SerializeField] private CombatGlobalConfig globalConfig;
        [SerializeField] private ShieldSystem shieldSystem;
        [SerializeField] private SubsystemController subsystemController;
        [SerializeField] private float maxHull = 100f;
        [SerializeField] private float combatDisabledHullThreshold = 20f;
        [SerializeField] private bool verboseLogs;

        [Header("Destroyed behavior")]
        [SerializeField] private bool explodableOnDestroyed = true;
        [SerializeField] private GameObject[] destroyedExplosionPrefabs;
        [SerializeField] private bool spawnAllDestroyedExplosionPrefabs;
        [SerializeField] [Min(0.01f)] private float destroyedExplosionScale = 1f;
        [SerializeField] private bool destroyGameObjectOnDestroyed = true;
        [SerializeField] [Min(0f)] private float destroyDelaySeconds;

        private float _currentHull;
        private ShipCombatState _state = ShipCombatState.CombatReady;
        private bool _destroyHandled;
        public void PreserveDestroyedObject() => destroyGameObjectOnDestroyed = false;
        private Vector3 _lastHitPoint;
        private Vector3 _lastHitNormal = Vector3.up;

        private void Awake()
        {
            _currentHull = maxHull;
        }

        public void ApplyDamage(in DamagePacket packet)
        {
            _lastHitPoint = packet.HitPointWorld;
            _lastHitNormal = packet.HitNormalWorld.sqrMagnitude > 1e-8f ? packet.HitNormalWorld.normalized : Vector3.up;

            ShieldArc arc = shieldSystem != null
                ? shieldSystem.ResolveArc(packet.HitPointWorld)
                : ShieldArc.Front;

            float arcShieldNormalized = shieldSystem != null ? shieldSystem.GetArcNormalized(arc) : 0f;
            bool shieldStillUp = shieldSystem != null && arcShieldNormalized > 0.001f;
            bool arcBreached = shieldSystem != null && shieldSystem.IsArcBreached(arc);
            bool sourceDebug = ShouldLogSourceDebug(packet);

            if (!arcBreached && shieldStillUp && shieldSystem != null)
            {
                DamagePacket shieldPacket = packet;
                shieldPacket.BaseDamage = GetShieldAdjustedDamage(packet);
                shieldSystem.ApplyShieldPressure(arc, shieldPacket);

                if (verboseLogs || (globalConfig != null && globalConfig.logDamageRouting))
                {
                    Debug.Log($"[Damageable] SHIELD hit '{name}' arc={arc} type={packet.DamageType} raw={packet.BaseDamage:F2} applied={shieldPacket.BaseDamage:F2} shieldN={arcShieldNormalized:F2}", this);
                }

                if (sourceDebug)
                {
                    Debug.Log($"[Damageable][SourceDebug] SHIELD damage applied target='{name}' arc={arc} type={packet.DamageType} amount={shieldPacket.BaseDamage:F2} hullBlockedByShield=true", this);
                }
            }
            else
            {
                float hullDamage = GetHullAdjustedDamage(packet);
                _currentHull = Mathf.Max(0f, _currentHull - hullDamage);
                ApplySubsystemEffects(packet);

                if (verboseLogs || (globalConfig != null && globalConfig.logDamageRouting))
                {
                    Debug.Log($"[Damageable] HULL hit '{name}' arc={arc} type={packet.DamageType} raw={packet.BaseDamage:F2} hullApplied={hullDamage:F2} shieldN={arcShieldNormalized:F2} breached={arcBreached}", this);
                }

                if (sourceDebug)
                {
                    string reason;
                    if (hullDamage <= 0f)
                        reason = "Hull damage <= 0 after routing";
                    else if (shieldStillUp && !arcBreached)
                        reason = "Blocked by shield (unexpected routing path)";
                    else
                        reason = "Shield bypassed/down";

                    Debug.Log($"[Damageable][SourceDebug] HULL damage applied target='{name}' arc={arc} type={packet.DamageType} hullApplied={hullDamage:F2} reason='{reason}'", this);
                }
            }

            UpdateShipState();
        }

        public ShipCombatState GetCombatState()
        {
            return _state;
        }

        /// <summary>Current hull / max hull, clamped 0–1.</summary>
        public float HullFraction => maxHull > 1e-5f ? Mathf.Clamp01(_currentHull / maxHull) : 0f;

        public bool IsDestroyed => _state == ShipCombatState.Destroyed;

        public void ApplyUndeployDefenseBonus(float restoreFraction, float regenMultiplier, float durationSeconds)
        {
            if (shieldSystem == null)
            {
                return;
            }

            shieldSystem.RestoreFractionAllArcs(restoreFraction);
            shieldSystem.ActivateTemporaryRegenBonus(regenMultiplier, durationSeconds);
            if (verboseLogs) Debug.Log($"[Damageable] Undeploy defense bonus applied: {restoreFraction:P0}");
        }

        private void ApplySubsystemEffects(in DamagePacket packet)
        {
            if (subsystemController == null)
            {
                return;
            }

            switch (packet.DamageType)
            {
                case DamageType.Laser:
                    subsystemController.ApplyHeat(SubsystemType.Engines, packet.HeatAmount * 0.6f);
                    subsystemController.ApplyHeat(SubsystemType.Weapons, packet.HeatAmount * 0.6f);
                    subsystemController.ApplyHeat(SubsystemType.Targeting, packet.HeatAmount * 0.8f);
                    subsystemController.ApplyHeat(SubsystemType.ShieldGenerator, packet.HeatAmount);
                    break;
                case DamageType.Kinetic:
                case DamageType.Missile:
                    subsystemController.ApplyKineticDamage(SubsystemType.Engines, packet.BaseDamage * 0.02f);
                    subsystemController.ApplyKineticDamage(SubsystemType.Weapons, packet.BaseDamage * 0.03f);
                    subsystemController.ApplyKineticDamage(SubsystemType.Targeting, packet.BaseDamage * 0.015f);
                    subsystemController.ApplyKineticDamage(SubsystemType.ShieldGenerator, packet.BaseDamage * 0.02f);
                    break;
            }
        }

        private float GetShieldAdjustedDamage(in DamagePacket packet)
        {
            // Lasers: WeaponProjectile sets BaseDamage = hull DPS, HeatAmount = shield DPS; shields must use HeatAmount.
            float rawShield = packet.HeatAmount > 0f ? packet.HeatAmount : packet.BaseDamage;

            if (globalConfig == null)
            {
                return rawShield;
            }

            return packet.DamageType switch
            {
                DamageType.Kinetic => packet.BaseDamage * globalConfig.kineticVsIntactShieldMultiplier,
                DamageType.Missile => packet.BaseDamage * globalConfig.missileVsIntactShieldMultiplier,
                DamageType.Laser => rawShield,
                _ => rawShield
            };
        }

        private float GetHullAdjustedDamage(in DamagePacket packet)
        {
            if (globalConfig == null)
            {
                return packet.BaseDamage;
            }

            return packet.DamageType switch
            {
                DamageType.Laser => packet.BaseDamage * globalConfig.laserVsHullMultiplier,
                _ => packet.BaseDamage
            };
        }

        private static bool ShouldLogSourceDebug(in DamagePacket packet)
        {
            if (packet.Source == null)
            {
                return false;
            }

            NPCShipAI npc = packet.Source.GetComponentInParent<NPCShipAI>();
            return npc != null && npc.ShowDebugLogs;
        }

        private void UpdateShipState()
        {
            ShipCombatState previous = _state;

            if (_currentHull <= 0f)
            {
                _state = ShipCombatState.Destroyed;
            }
            else if (_currentHull <= combatDisabledHullThreshold)
            {
                _state = ShipCombatState.CombatDisabled;
            }
            else
            {
                _state = ShipCombatState.CombatReady;
            }

            if (_state != previous)
            {
                DamageEventBus.RaiseShipCombatStateChanged(_state);
                if (verboseLogs) Debug.Log($"[Damageable] Ship state => {_state}", this);

                if (_state == ShipCombatState.Destroyed && !_destroyHandled)
                {
                    _destroyHandled = true;
                    HandleDestroyed();
                }
            }
        }

        private void HandleDestroyed()
        {
            if (explodableOnDestroyed)
            {
                SpawnDestroyedExplosionPrefabs();
            }

            if (destroyGameObjectOnDestroyed)
            {
                Destroy(gameObject, destroyDelaySeconds);
            }
        }

        private void SpawnDestroyedExplosionPrefabs()
        {
            if (destroyedExplosionPrefabs == null || destroyedExplosionPrefabs.Length == 0)
            {
                return;
            }

            Vector3 p = _lastHitPoint.sqrMagnitude > 1e-8f ? _lastHitPoint : transform.position;
            Vector3 n = _lastHitNormal.sqrMagnitude > 1e-8f ? _lastHitNormal : transform.up;

            if (spawnAllDestroyedExplosionPrefabs)
            {
                for (int i = 0; i < destroyedExplosionPrefabs.Length; i++)
                {
                    GameObject prefab = destroyedExplosionPrefabs[i];
                    if (prefab == null) continue;
                    WeaponImpactVfx.Spawn(prefab, p, n, destroyedExplosionScale);
                }

                return;
            }

            int idx = Random.Range(0, destroyedExplosionPrefabs.Length);
            GameObject selected = destroyedExplosionPrefabs[idx];
            if (selected != null)
            {
                WeaponImpactVfx.Spawn(selected, p, n, destroyedExplosionScale);
            }
        }
    }
}
