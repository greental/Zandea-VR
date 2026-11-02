using ScavengerNPC;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    // NPC damage is committed only by an actual weapon collision, once per attack.
    public sealed class BanditDuelStrikeDamage : MonoBehaviour
    {
        public StarvingBandit brain;
        [Min(0)] public float damage = 9f;
        // Retained for serialized scenes/builders; proximity and chance no longer award hits.
        [HideInInspector] public float maximumRootDistance = 1.85f;
        [HideInInspector] public float cleanHitChance = .8f;
        int scoredAttack = -1;
        readonly RaycastHit[] sweepHits = new RaycastHit[24];
        Vector3 previousPositiveTip, previousNegativeTip;
        PhysicalMeleeWeapon previousWeapon;
        bool tipHistoryValid;
        public Vector3 LastContactPoint { get; private set; }
        public int ContactHits { get; private set; }

        void Awake() { if (!brain) brain = GetComponent<StarvingBandit>(); }

        void LateUpdate()
        {
            if (!brain || !brain.staff)
            {
                tipHistoryValid = false;
                return;
            }
            var grip = brain.GetComponentInChildren<MocapStaffPreview>(true);
            var weapon = grip && grip.secondaryPhysicalWeapon &&
                grip.secondaryPhysicalWeapon.State == PhysicalMeleeWeapon.HoldState.HeldByNpc
                ? grip.secondaryPhysicalWeapon : brain.staff;
            Vector3 positiveLocal, negativeLocal;
            if (weapon != brain.staff)
            {
                if (!StaffAxisCalibration.TryMeasure(weapon.transform, out positiveLocal, out negativeLocal))
                {
                    tipHistoryValid = false;
                    return;
                }
            }
            else brain.GetTipLocals(out positiveLocal, out negativeLocal);
            Vector3 positive = weapon.transform.TransformPoint(positiveLocal);
            Vector3 negative = weapon.transform.TransformPoint(negativeLocal);
            if (tipHistoryValid && previousWeapon == weapon &&
                brain.state == StarvingBandit.State.Strike && scoredAttack != brain.AttackSequence)
            {
                // Kinematic animated weapons can cross a moving target between two
                // physics contacts. Sweep the actual authored tip paths for this
                // rendered strike instead of awarding a range- or chance-based hit.
                SweepTip(weapon, previousPositiveTip, positive);
                if (scoredAttack != brain.AttackSequence)
                    SweepTip(weapon, previousNegativeTip, negative);
            }
            previousPositiveTip = positive;
            previousNegativeTip = negative;
            previousWeapon = weapon;
            tipHistoryValid = true;
        }

        void SweepTip(PhysicalMeleeWeapon weapon, Vector3 from, Vector3 to)
        {
            Vector3 travel = to - from;
            float distance = travel.magnitude;
            if (distance < .001f) return;
            int count = Physics.SphereCastNonAlloc(from, .055f, travel / distance, sweepHits, distance,
                ~0, QueryTriggerInteraction.Collide);
            float speed = distance / Mathf.Max(Time.deltaTime, .001f);
            for (int i = 0; i < count; i++)
            {
                var hit = sweepHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(brain.transform)) continue;
                if (TryScoreContact(weapon, hit.collider, hit.point, hit.normal, speed)) return;
            }
        }

        public bool TryScoreContact(PhysicalMeleeWeapon weapon, Collider other, Vector3 point, Vector3 normal, float speed)
        {
            if (!enabled || !brain || !brain.staff || !weapon || weapon.ownerRoot != brain.transform ||
                brain.state != StarvingBandit.State.Strike || scoredAttack == brain.AttackSequence ||
                !brain.target || brain.health && brain.health.State != NpcConsciousness.Conscious ||
                speed < weapon.minimumImpactSpeed) return false;
            var grip = brain.GetComponentInChildren<MocapStaffPreview>(true);
            if (grip && grip.secondaryPhysicalWeapon && weapon == brain.staff) return false;
            var enemy = other.GetComponentInParent<ScavengerHealth>();
            var player = other.GetComponentInParent<ScavengerPlayerHealth>();
            if ((!enemy && !player) || enemy == brain.health || enemy && enemy.IsDead || player && player.IsDead) return false;
            // Keep patrol allies safe: only the committed target can receive this strike.
            if (enemy != brain.target.GetComponentInParent<ScavengerHealth>() ||
                player != brain.target.GetComponentInParent<ScavengerPlayerHealth>()) return false;
            var defender = enemy ? enemy.GetComponent<StarvingBandit>() : null;
            if (defender && (defender.state == StarvingBandit.State.Dodge || defender.DeflectsDamageFrom(weapon))) return false;
            if (player && PracticeSwordGuard.BlocksRecentStrike(weapon)) return false;
            scoredAttack = brain.AttackSequence;
            LastContactPoint = point;
            ContactHits++;
            var packet = new DamagePacket {
                BaseDamage = damage * CharacterCombatMath.DamageDealt(GetComponent<CharacterAttributes>(), weapon.proficiencyClass),
                DamageType = DamageType.Kinetic,
                PhysicalImpact = weapon.strikeType == PhysicalMeleeWeapon.StrikeType.Blunt ? PhysicalImpactKind.Blunt : PhysicalImpactKind.Sharp,
                HitPointWorld = point, HitNormalWorld = normal,
                Source = gameObject, WeaponId = weapon.name, Timestamp = Time.time
            };
            if (enemy) enemy.ApplyDamage(packet); else player.ApplyDamage(packet);
            if (weapon.strikeType != PhysicalMeleeWeapon.StrikeType.Blunt &&
                (enemy == null || !enemy.LastDamageBlocked))
            {
                var motion = weapon.GetComponent<Rigidbody>();
                var velocity = motion ? motion.GetPointVelocity(point) : Vector3.zero;
                var blood = other.GetComponentInParent<GoreImpactSystem>();
                if (blood) blood.RegisterImpact(packet, velocity, enemy ? enemy.LastDamageTaken : damage);
                var staining = weapon.GetComponent<WeaponBloodSurface>();
                if (!staining) staining = weapon.gameObject.AddComponent<WeaponBloodSurface>();
                staining.AddBlood(enemy ? enemy.LastDamageTaken : damage, point);
                var anatomy = other.GetComponentInParent<DismemberableCharacter>();
                if (anatomy && weapon.strikeType == PhysicalMeleeWeapon.StrikeType.Cutting)
                    anatomy.TrySever(point, velocity, weapon.transform.TransformDirection(weapon.bladeEdgeLocalAxis),
                        enemy ? enemy.LastDamageTaken : damage, true, Vector3.zero);
            }
            return true;
        }
    }
}
