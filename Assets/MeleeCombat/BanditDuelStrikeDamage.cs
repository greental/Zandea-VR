using ScavengerNPC;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    // The authored staff can glance off another moving NPC's staff without a body
    // collision. Award one body hit near the impact portion of a committed strike.
    public sealed class BanditDuelStrikeDamage : MonoBehaviour
    {
        public StarvingBandit brain;
        [Min(0)] public float damage = 9f;
        [Min(.5f)] public float maximumRootDistance = 1.85f;
        [Range(0f, 1f)] public float cleanHitChance = .8f;
        bool scoredThisStrike;

        void Awake() { if (!brain) brain = GetComponent<StarvingBandit>(); }

        void Update()
        {
            if (!brain) return;
            if (brain.state != StarvingBandit.State.Strike)
            {
                scoredThisStrike = false;
                return;
            }
            if (scoredThisStrike || brain.AttackProgress < .65f || !brain.target || brain.health && brain.health.IsDead) return;
            scoredThisStrike = true;
            if (Random.value > cleanHitChance) return;
            var enemy = brain.target.GetComponentInParent<ScavengerHealth>();
            var player = brain.target.GetComponentInParent<ScavengerPlayerHealth>();
            if ((!enemy && !player) || enemy == brain.health || enemy && enemy.IsDead || player && player.IsDead) return;
            var victim = enemy ? enemy.transform : player.transform;
            var defender = enemy ? enemy.GetComponent<StarvingBandit>() : null;
            if (defender && (defender.state == StarvingBandit.State.Dodge ||
                defender.DeflectsDamageFrom(brain.staff))) return;
            if (player && PracticeSwordGuard.BlocksRecentStrike(brain.staff)) return;
            var separation = Vector3.ProjectOnPlane(victim.position - transform.position, Vector3.up);
            if (separation.magnitude > maximumRootDistance) return;
            var hitPoint = victim.position + Vector3.up * 1.1f;
            var capsule = victim.GetComponent<CapsuleCollider>();
            if (capsule) hitPoint = capsule.ClosestPoint(brain.ActualTipPosition);
            var normal = (hitPoint - brain.ActualTipPosition).normalized;
            if (normal.sqrMagnitude < .01f) normal = transform.forward;
            var packet = new DamagePacket {
                BaseDamage = damage, DamageType = DamageType.Kinetic,
                HitPointWorld = hitPoint, HitNormalWorld = normal,
                Source = gameObject, WeaponId = brain.staff ? brain.staff.name : "Bandit staff",
                Timestamp = Time.time
            };
            if (enemy) enemy.ApplyDamage(packet);
            else player.ApplyDamage(packet);
        }
    }
}
