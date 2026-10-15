using System;
using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class ShieldSystem : MonoBehaviour, IShieldSystem
    {
        [Serializable]
        private class ArcState
        {
            public ArcShieldConfig config;
            public float currentShield;
            public float instability;
            public bool breached;
            public float breachEndTime;
            public float breachRecoveryEndTime;
            public float lastDamageTime;
        }

        [SerializeField] private ShieldZoneConfig shieldConfig;
        [SerializeField] private bool verboseLogs;
        [Tooltip("Extra no-regen lockout after taking damage. Helps combat end faster and prevents instant shield refills between hits.")]
        [SerializeField] private float additionalRegenDelayAfterDamage = 1.5f;

        private readonly Dictionary<ShieldArc, ArcState> _arcs = new Dictionary<ShieldArc, ArcState>();
        private LaserPressureTracker _laserPressureTracker;
        private float _temporaryRegenMultiplier = 1f;
        private float _temporaryRegenEndTime;

        private void Awake()
        {
            if (shieldConfig == null || shieldConfig.arcs == null || shieldConfig.arcs.Length == 0)
            {
                Debug.LogError("ShieldSystem missing ShieldZoneConfig.", this);
                enabled = false;
                return;
            }

            foreach (ArcShieldConfig cfg in shieldConfig.arcs)
            {
                _arcs[cfg.arc] = new ArcState
                {
                    config = cfg,
                    currentShield = cfg.capacity,
                    instability = 0f,
                    breached = false
                };
            }

            _laserPressureTracker = new LaserPressureTracker(
                shieldConfig.consecutiveHitWindowSeconds,
                shieldConfig.sameArcConsecutiveMultiplier);
        }

        private void Update()
        {
            float now = Time.time;
            if (_temporaryRegenMultiplier > 1f && now >= _temporaryRegenEndTime)
            {
                _temporaryRegenMultiplier = 1f;
            }

            foreach (ArcState state in _arcs.Values)
            {
                if (state.breached && now >= state.breachEndTime)
                {
                    state.breached = false;
                    state.breachRecoveryEndTime = now + state.config.breachRecoverySeconds;
                    DamageEventBus.RaiseShieldRecovered(state.config.arc);
                    if (verboseLogs) Debug.Log($"[Shield] Arc recovered: {state.config.arc}");
                }

                float instabilityDecay = state.config.instabilityDecayPerSecond * Time.deltaTime;
                state.instability = Mathf.Max(0f, state.instability - instabilityDecay);

                if (state.breached || now < state.breachRecoveryEndTime)
                {
                    continue;
                }

                if (now - state.lastDamageTime < state.config.rechargeDelay + Mathf.Max(0f, additionalRegenDelayAfterDamage))
                {
                    continue;
                }

                state.currentShield = Mathf.Min(
                    state.config.capacity,
                    state.currentShield + (state.config.rechargeRate * _temporaryRegenMultiplier * Time.deltaTime));
            }
        }

        public ShieldArc ResolveArc(Vector3 hitPointWorld)
        {
            return ShieldArcLocalizer.ResolveArc(transform, hitPointWorld);
        }

        public bool IsArcBreached(ShieldArc arc)
        {
            return TryGetArc(arc, out ArcState state) && state.breached;
        }

        public void ApplyShieldPressure(ShieldArc arc, in DamagePacket packet)
        {
            if (!TryGetArc(arc, out ArcState state))
            {
                return;
            }

            state.lastDamageTime = Time.time;

            if (packet.DamageType == DamageType.Laser)
            {
                float instabilityMult = _laserPressureTracker.GetInstabilityMultiplier(arc, Time.time);
                state.instability += state.config.instabilityGainPerLaserHit * instabilityMult;
            }

            state.currentShield = Mathf.Max(0f, state.currentShield - packet.BaseDamage);

            if (!state.breached && state.instability >= state.config.breachThreshold)
            {
                state.breached = true;
                state.breachEndTime = Time.time + state.config.breachDuration;
                DamageEventBus.RaiseShieldBreached(arc);
                if (verboseLogs) Debug.Log($"[Shield] Arc breached: {arc}");
            }
        }

        public float GetArcNormalized(ShieldArc arc)
        {
            if (!TryGetArc(arc, out ArcState state) || state.config.capacity <= 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01(state.currentShield / state.config.capacity);
        }

        public float GetArcInstability(ShieldArc arc)
        {
            if (!TryGetArc(arc, out ArcState state) || state.config.breachThreshold <= 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01(state.instability / state.config.breachThreshold);
        }

        public void RestoreFractionAllArcs(float fraction)
        {
            float clamped = Mathf.Clamp01(fraction);
            foreach (ArcState state in _arcs.Values)
            {
                float restore = state.config.capacity * clamped;
                state.currentShield = Mathf.Min(state.config.capacity, state.currentShield + restore);
            }
        }

        public void ActivateTemporaryRegenBonus(float multiplier, float durationSeconds)
        {
            _temporaryRegenMultiplier = Mathf.Max(1f, multiplier);
            _temporaryRegenEndTime = Time.time + Mathf.Max(0f, durationSeconds);
        }

        private bool TryGetArc(ShieldArc arc, out ArcState state)
        {
            return _arcs.TryGetValue(arc, out state);
        }
    }
}
