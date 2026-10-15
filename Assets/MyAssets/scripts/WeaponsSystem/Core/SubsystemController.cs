using System;
using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public class SubsystemController : MonoBehaviour, ISubsystemController
    {
        [Serializable]
        private class SubsystemState
        {
            public SubsystemType subsystem;
            public float heat;
            public float integrity = 1f;
            public bool temporarilyDisabled;
            public bool permanentlyDisabled;
        }

        [Header("Tuning")]
        [SerializeField] private float heatDisableThreshold = 0.7f;
        [SerializeField] private float heatDecayPerSecond = 0.2f;
        [SerializeField] private float kineticDisableThreshold = 0.0f;
        [SerializeField] private bool verboseLogs;

        [SerializeField] private List<SubsystemState> initialSubsystems = new List<SubsystemState>
        {
            new SubsystemState { subsystem = SubsystemType.Engines },
            new SubsystemState { subsystem = SubsystemType.Weapons },
            new SubsystemState { subsystem = SubsystemType.Targeting },
            new SubsystemState { subsystem = SubsystemType.ShieldGenerator }
        };

        private readonly Dictionary<SubsystemType, SubsystemState> _states = new Dictionary<SubsystemType, SubsystemState>();

        private void Awake()
        {
            foreach (SubsystemState state in initialSubsystems)
            {
                _states[state.subsystem] = new SubsystemState
                {
                    subsystem = state.subsystem,
                    heat = state.heat,
                    integrity = state.integrity,
                    temporarilyDisabled = false,
                    permanentlyDisabled = state.integrity <= kineticDisableThreshold
                };
            }
        }

        private void Update()
        {
            foreach (SubsystemState state in _states.Values)
            {
                if (state.permanentlyDisabled)
                {
                    continue;
                }

                float previousHeat = state.heat;
                state.heat = Mathf.Max(0f, state.heat - heatDecayPerSecond * Time.deltaTime);

                bool shouldBeTempDisabled = state.heat >= heatDisableThreshold;
                if (shouldBeTempDisabled && !state.temporarilyDisabled)
                {
                    state.temporarilyDisabled = true;
                    DamageEventBus.RaiseSubsystemDisabled(state.subsystem);
                    if (verboseLogs) Debug.Log($"[Subsystem] Heat disabled: {state.subsystem}");
                }
                else if (!shouldBeTempDisabled && state.temporarilyDisabled)
                {
                    state.temporarilyDisabled = false;
                    DamageEventBus.RaiseSubsystemRecovered(state.subsystem);
                    if (verboseLogs && previousHeat > 0f) Debug.Log($"[Subsystem] Heat recovered: {state.subsystem}");
                }
            }
        }

        public void ApplyHeat(SubsystemType subsystem, float amount)
        {
            if (!TryGet(subsystem, out SubsystemState state) || state.permanentlyDisabled)
            {
                return;
            }

            state.heat = Mathf.Clamp01(state.heat + Mathf.Max(0f, amount));
        }

        public void ApplyKineticDamage(SubsystemType subsystem, float amount)
        {
            if (!TryGet(subsystem, out SubsystemState state) || state.permanentlyDisabled)
            {
                return;
            }

            state.integrity = Mathf.Max(0f, state.integrity - Mathf.Max(0f, amount));
            if (state.integrity <= kineticDisableThreshold)
            {
                state.permanentlyDisabled = true;
                state.temporarilyDisabled = false;
                DamageEventBus.RaiseSubsystemDisabled(subsystem);
                if (verboseLogs) Debug.Log($"[Subsystem] Kinetic disabled: {subsystem}");
            }
        }

        public bool IsSubsystemOperational(SubsystemType subsystem)
        {
            return TryGet(subsystem, out SubsystemState state) &&
                   !state.temporarilyDisabled &&
                   !state.permanentlyDisabled;
        }

        private bool TryGet(SubsystemType subsystem, out SubsystemState state)
        {
            return _states.TryGetValue(subsystem, out state);
        }
    }
}
