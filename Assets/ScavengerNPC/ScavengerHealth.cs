using UnityEngine;
using UnityEngine.Events;
using VRKoombar.WeaponsStaging.Contracts;
using KenshiVR.MeleeCombat;

namespace ScavengerNPC
{
    public sealed class ScavengerHealth : MonoBehaviour, IDamageable
    {
        [Min(1)] public float maxHealth = 100;
        public UnityEvent onDamaged = new UnityEvent();
        public UnityEvent onDeath = new UnityEvent();
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        void Awake() { Current = maxHealth; }
        public void ApplyDamage(in DamagePacket packet)
        {
            if (IsDead || packet.BaseDamage <= 0 || float.IsNaN(packet.BaseDamage)) return;
            float taken = packet.BaseDamage * CharacterCombatMath.DamageTaken(GetComponent<CharacterAttributes>());
            Current = Mathf.Max(0, Current - taken);
            var brain = GetComponent<ScavengerEnemy>();
            if (Current <= 0)
            {
                IsDead = true;
                if (brain) brain.Die();
                var ragdoll = GetComponent<ScavengerRagdoll>();
                if (ragdoll) ragdoll.Activate(packet.HitPointWorld, -packet.HitNormalWorld *
                    (4f * CharacterCombatMath.DamageTaken(GetComponent<CharacterAttributes>())));
                onDeath.Invoke();
            }
            else
            {
                var pose = GetComponent<ScavengerPose>();
                if (pose) pose.PlayHitReaction(packet.HitNormalWorld, taken);
                if (brain) brain.Alert(packet.Source ? packet.Source.transform : null);
                onDamaged.Invoke();
            }
        }
        public ShipCombatState GetCombatState() => IsDead ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;
    }
}
