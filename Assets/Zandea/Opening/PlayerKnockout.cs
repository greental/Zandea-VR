using System;
using System.Collections.Generic;
using JunkerBody;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Zandea.Opening
{
    public enum PlayerConsciousness { Conscious, KnockoutStarting, Unconscious, Recovering }

    // Reusable player action gate. Tracked XR transforms, camera and body IK remain active.
    public sealed class PlayerKnockout : MonoBehaviour
    {
        [Min(0f)] public float knockoutStartSeconds = .35f;
        [Min(0f)] public float defaultRecoverySeconds = 12f;
        [Min(0f)] public float recoverySeconds = .5f;
        public ScavengerPlayerHealth health;
        public PlayerConsciousness State { get; private set; } = PlayerConsciousness.Conscious;
        public event Action<PlayerConsciousness, PlayerConsciousness> StateChanged;
        public event Action<string> KnockoutStarted;
        public event Action Unconscious, RecoveryStarted, Recovered;

        readonly Dictionary<Behaviour, bool> prior = new Dictionary<Behaviour, bool>();
        float phaseEnd, wakeAt;
        bool indefinite;

        void Awake()
        {
            if (!health) health = GetComponentInParent<ScavengerPlayerHealth>();
            if (!health) health = GetComponentInChildren<ScavengerPlayerHealth>(true);
        }

        public bool RequestKnockout(string cause = "unspecified", bool remainUnconscious = false, float delay = -1f)
        {
            if (State != PlayerConsciousness.Conscious || health && health.IsDead) return false;
            indefinite = remainUnconscious;
            wakeAt = remainUnconscious ? float.PositiveInfinity : Time.time + Mathf.Max(0f, delay < 0f ? defaultRecoverySeconds : delay);
            GateActions();
            Transition(PlayerConsciousness.KnockoutStarting);
            KnockoutStarted?.Invoke(cause);
            phaseEnd = Time.time + knockoutStartSeconds;
            return true;
        }

        public bool Recover()
        {
            if (State != PlayerConsciousness.Unconscious && State != PlayerConsciousness.KnockoutStarting) return false;
            if (health && health.IsDead) return false;
            indefinite = false;
            Transition(PlayerConsciousness.Recovering);
            RecoveryStarted?.Invoke();
            phaseEnd = Time.time + recoverySeconds;
            return true;
        }

        void Update()
        {
            if (health && health.IsDead) return;
            if (State == PlayerConsciousness.KnockoutStarting && Time.time >= phaseEnd)
            {
                Transition(PlayerConsciousness.Unconscious);
                Unconscious?.Invoke();
            }
            if (State == PlayerConsciousness.Unconscious && !indefinite && Time.time >= wakeAt) Recover();
            if (State == PlayerConsciousness.Recovering && Time.time >= phaseEnd)
            {
                ReleaseActions();
                Transition(PlayerConsciousness.Conscious);
                Recovered?.Invoke();
            }
        }

        void GateActions()
        {
            prior.Clear();
            foreach (var component in GetComponentsInChildren<ContinuousMoveProvider>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<ContinuousTurnProvider>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<JumpProvider>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<JunkerSprint>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<JunkerEquipmentRig>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<PlayerMartialHand>(true)) Gate(component);
            foreach (var component in GetComponentsInChildren<XRDirectInteractor>(true)) Gate(component);
        }

        void Gate(Behaviour component)
        {
            if (!component || prior.ContainsKey(component)) return;
            prior.Add(component, component.enabled);
            if (component.enabled) component.enabled = false;
        }

        void ReleaseActions()
        {
            foreach (var entry in prior)
                if (entry.Key && entry.Value && !(health && health.IsDead)) entry.Key.enabled = true;
            prior.Clear();
        }

        void Transition(PlayerConsciousness next)
        {
            if (State == next) return;
            var previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        void OnDisable()
        {
            ReleaseActions();
            State = PlayerConsciousness.Conscious;
            indefinite = false;
        }
    }
}
