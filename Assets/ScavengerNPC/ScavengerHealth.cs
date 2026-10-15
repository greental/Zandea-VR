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
        [Min(0f)] public float headKnockdownDamage = 7.5f;
        [Min(.05f)] public float headHitRadius = .3f;
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        void Awake() { Current = maxHealth; }
        public void ApplyDamage(in DamagePacket packet)
        {
            if (IsDead || packet.BaseDamage <= 0 || float.IsNaN(packet.BaseDamage)) return;
            float taken = packet.BaseDamage * CharacterCombatMath.DamageTaken(GetComponent<CharacterAttributes>());
            var martial = GetComponent<NpcMartialArts>();
            bool blocked = martial && packet.DamageType == DamageType.Kinetic && packet.Source &&
                martial.Blocks(packet.HitPointWorld, packet.Source.transform.position);
            if (blocked)
            {
                taken *= martial.profile.guardedDamageMultiplier;
                martial.SuccessfulBlock();
            }
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
                var ragdoll = GetComponent<ScavengerRagdoll>();
                Transform head = ragdoll && ragdoll.animator && ragdoll.animator.isHuman ?
                    ragdoll.animator.GetBoneTransform(HumanBodyBones.Head) : null;
                bool strongHeadHit = martial && martial.IsUnarmed && !blocked &&
                    packet.DamageType == DamageType.Kinetic && taken >= headKnockdownDamage &&
                    head && Vector3.Distance(packet.HitPointWorld, head.position) <= headHitRadius;
                if (strongHeadHit && !ragdoll.Active)
                {
                    Vector3 direction = packet.Source ? transform.position - packet.Source.transform.position :
                        packet.HitNormalWorld;
                    ragdoll.KnockDown(packet.HitPointWorld, direction);
                }
                var pose = GetComponent<ScavengerPose>();
                if (pose && !strongHeadHit) pose.PlayHitReaction(packet.HitNormalWorld, taken);
                if (brain) brain.Alert(packet.Source ? packet.Source.transform : null);
                onDamaged.Invoke();
            }
        }
        public ShipCombatState GetCombatState() => IsDead ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;
    }
}
