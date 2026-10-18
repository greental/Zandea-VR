using UnityEngine;
using UnityEngine.Events;
using VRKoombar.WeaponsStaging.Contracts;
using KenshiVR.MeleeCombat;
namespace Zandea.Wildlife
{
    [RequireComponent(typeof(QuadrupedMotor), typeof(GoreImpactSystem))]
    public sealed class CreatureHealth : MonoBehaviour, IDamageable
    {
        public UnityEvent onDamaged = new UnityEvent(), onDeath = new UnityEvent();
        public float Current { get; private set; }
        QuadrupedMotor motor;
        WildlifeBrain brain;
        GoreImpactSystem blood;
        CapsuleCollider bodyCollider;
        float deathTime = -1;
        void Awake() { motor = GetComponent<QuadrupedMotor>(); brain = GetComponent<WildlifeBrain>(); blood = GetComponent<GoreImpactSystem>(); bodyCollider = GetComponent<CapsuleCollider>(); }
        void OnEnable()
        {
            Current = motor.profile.maxHealth; deathTime = -1;
            if (bodyCollider) { bodyCollider.center = new Vector3(0, 1.7f, .15f); bodyCollider.radius = .68f; }
            if (motor.animator) motor.animator.SetBool("Dead", false);
            if (brain) brain.enabled = true;
        }
        public void ApplyDamage(in DamagePacket packet)
        {
            if (Current <= 0 || !float.IsFinite(packet.BaseDamage) || packet.BaseDamage <= 0) return;
            Current = Mathf.Max(0, Current - packet.BaseDamage);
            motor.animator.SetTrigger("Hit");
            if (brain) brain.Frighten(packet.Source ? packet.Source.transform : null);
            // Existing sharp melee already emits GoreImpactSystem blood. Emit here for other damage sources.
            if (blood && packet.PhysicalImpact != PhysicalImpactKind.Sharp)
                blood.RegisterImpact(packet, -packet.HitNormalWorld, packet.BaseDamage);
            onDamaged.Invoke();
            if (Current <= 0)
            {
                motor.Kill(); deathTime = Time.time;
                if (brain) brain.enabled = false;
                if (blood) blood.RegisterCreatureDeath();
                onDeath.Invoke();
            }
        }
        void Update()
        {
            if (deathTime < 0 || Time.time - deathTime < 2.5f || !bodyCollider) return;
            // Stable collidable corpse: visual collapse is authored, no untested quadruped ragdoll.
            bodyCollider.center = new Vector3(-.35f, .65f, .15f); bodyCollider.radius = .6f;
            deathTime = -1;
        }
        public ShipCombatState GetCombatState() => Current > 0 ? ShipCombatState.CombatReady : ShipCombatState.Destroyed;
    }
}
