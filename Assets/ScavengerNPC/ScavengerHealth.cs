using System;
using UnityEngine;
using UnityEngine.Events;
using VRKoombar.WeaponsStaging.Contracts;
using KenshiVR.MeleeCombat;

namespace ScavengerNPC
{
    public enum NpcConsciousness { Conscious, Unconscious, Dead }

    public sealed class ScavengerHealth : MonoBehaviour, IDamageable
    {
        [Min(1f)] public float maxHealth = 100f;
        public UnityEvent onDamaged = new UnityEvent();
        public UnityEvent onDeath = new UnityEvent();
        public UnityEvent onKnockout = new UnityEvent();
        public UnityEvent onWakeUp = new UnityEvent();
        public event Action<NpcConsciousness, NpcConsciousness> StateChanged;

        [Header("Accumulated trauma")]
        [Min(1f)] public float knockoutTrauma = 60f;
        [Min(0f)] public float minimumKnockoutHit = 3f;
        [Range(0f, 1f)] public float severeHealthFraction = .13f;
        [Min(0f)] public float unarmedTraumaMultiplier = 2f;
        [Min(0f)] public float bluntTraumaMultiplier = .85f;
        [Min(0f)] public float sharpTraumaMultiplier = .16f;
        [Min(.1f)] public float knockoutDuration = 12f;
        [Range(0f, 1f)] public float recoveryHealthFraction = .12f;
        [Min(0f)] public float additionalDamageDelay = .4f;

        [Header("Short head knockout")]
        [Min(0f)] public float minimumHeadHitDamage = 7f;
        [Min(.05f)] public float headHitRadius = .3f;
        [Range(0f, 1f)] public float baseHeadKnockoutChance = .04f;
        [Range(0f, 1f)] public float maximumHeadKnockoutChance = .25f;
        [Range(0f, 1f)] public float headProtection;
        [Min(.1f)] public float shortKnockoutDuration = 2.5f;

        [Header("Recovery behavior")]
        public bool fleeWhenUnarmed = true;
        [Range(0f, 1f)] public float fleeBelowHealthFraction = .35f;
        [Min(0f)] public float fleeSeconds = 7f;
        public bool logStateChanges;

        public float Current { get; private set; }
        public float Trauma { get; private set; }
        public NpcConsciousness State { get; private set; } = NpcConsciousness.Conscious;
        public bool IsDead => State == NpcConsciousness.Dead;
        public bool LastDamageBlocked { get; private set; }
        public float LastDamageTaken { get; private set; }
        public bool IsUnconscious => State == NpcConsciousness.Unconscious;
        public bool ShouldFlee => State == NpcConsciousness.Conscious && (permanentFlee ||
            Time.time < fleeUntil && (Current / maxHealth < fleeBelowHealthFraction ||
            fleeWhenUnarmed && weaponLostAtKnockout));

        float wakeTime, fleeUntil;
        bool permanentCollapse;
        bool permanentFlee;
        bool weaponLostAtKnockout;
        ScavengerRagdoll ragdoll;
        StarvingBandit bandit;
        ScavengerEnemy enemy;
        NpcGeneratedWeapon generatedWeapon;
        CharacterAttributes attributes;
        NpcMartialArts martial;
        ScavengerPose pose;
        Transform head;

        void Awake()
        {
            Current = maxHealth;
            ragdoll = GetComponent<ScavengerRagdoll>();
            bandit = GetComponent<StarvingBandit>();
            if (bandit)
            {
                if (!GetComponent<GoreImpactSystem>()) gameObject.AddComponent<GoreImpactSystem>();
                if (!GetComponent<DismemberableCharacter>()) gameObject.AddComponent<DismemberableCharacter>();
            }
            enemy = GetComponent<ScavengerEnemy>();
            generatedWeapon = GetComponent<NpcGeneratedWeapon>();
            attributes = GetComponent<CharacterAttributes>();
            martial = GetComponent<NpcMartialArts>();
            pose = GetComponent<ScavengerPose>();
            if (ragdoll && ragdoll.animator && ragdoll.animator.isHuman)
                head = ragdoll.animator.GetBoneTransform(HumanBodyBones.Head);
        }

        void Update()
        {
            if (!permanentCollapse && State == NpcConsciousness.Unconscious && Time.time >= wakeTime &&
                Current >= maxHealth * recoveryHealthFraction) WakeUp();
        }

        public void ReactToAmputation(Vector3 hitPoint, Vector3 direction)
        {
            if (IsDead) return;
            fleeUntil = Mathf.Max(fleeUntil, Time.time + fleeSeconds * 2f);
            weaponLostAtKnockout = true;
            permanentFlee = true;
            if (martial) martial.InterruptCombat();
            if (bandit) bandit.InterruptCombatForKnockout();
            if (pose) pose.PlayHitReaction(-direction, severeHitReactionDamage);
        }

        const float severeHitReactionDamage = 30f;

        public void KnockOutForAmputation(Vector3 hitPoint, Vector3 direction)
        {
            if (IsDead) return;
            permanentCollapse = true;
            KnockOut(hitPoint, direction);
        }

        public void ApplyDamage(in DamagePacket packet)
        {
            LastDamageBlocked = false;
            LastDamageTaken = 0f;
            if (IsDead || packet.BaseDamage <= 0f || float.IsNaN(packet.BaseDamage) || float.IsInfinity(packet.BaseDamage)) return;
            float taken = packet.BaseDamage * CharacterCombatMath.DamageTaken(attributes);
            bool blocked = State == NpcConsciousness.Conscious && martial && martial.profile &&
                packet.DamageType == DamageType.Kinetic && packet.Source &&
                martial.Blocks(packet.HitPointWorld, packet.Source.transform.position);
            if (blocked)
            {
                taken *= martial.profile.guardedDamageMultiplier;
                martial.SuccessfulBlock();
            }
            LastDamageBlocked = blocked;
            LastDamageTaken = taken;
            Current = Mathf.Max(0f, Current - taken);
            if (Current <= 0f) { Die(packet.HitPointWorld, ImpactDirection(packet) * 4f); return; }
            if (enemy) enemy.Alert(packet.Source ? packet.Source.transform : null);
            if (IsUnconscious)
            {
                wakeTime = Mathf.Max(wakeTime, Time.time + additionalDamageDelay + knockoutDuration * taken / maxHealth);
                if (ragdoll) ragdoll.ApplyImpact(packet.HitPointWorld, ImpactDirection(packet) * Mathf.Min(taken * .15f, 4f));
            }
            else
            {
                if (!blocked && taken >= minimumKnockoutHit)
                {
                    float multiplier = packet.PhysicalImpact == PhysicalImpactKind.Unarmed ? unarmedTraumaMultiplier :
                        packet.PhysicalImpact == PhysicalImpactKind.Blunt ? bluntTraumaMultiplier : sharpTraumaMultiplier;
                    Trauma += taken * multiplier;
                }
                if (ragdoll && (Trauma >= knockoutTrauma || Current <= maxHealth * severeHealthFraction))
                    KnockOut(packet.HitPointWorld, ImpactDirection(packet));
                else
                {
                    float chance = blocked ? 0f : HeadKnockoutChance(packet, taken);
                    if (ragdoll && chance > 0f && UnityEngine.Random.value < chance)
                        KnockOut(packet.HitPointWorld, ImpactDirection(packet), shortKnockoutDuration);
                    else if (pose) pose.PlayHitReaction(packet.HitNormalWorld, taken);
                }
            }
            onDamaged.Invoke();
        }

        Vector3 ImpactDirection(in DamagePacket packet)
        {
            Vector3 direction = packet.Source ? transform.position - packet.Source.transform.position : -packet.HitNormalWorld;
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            return direction.sqrMagnitude > .001f ? direction : transform.forward;
        }

        public float HeadKnockoutChance(in DamagePacket packet, float taken)
        {
            if (packet.DamageType != DamageType.Kinetic ||
                packet.PhysicalImpact != PhysicalImpactKind.Unarmed && packet.PhysicalImpact != PhysicalImpactKind.Blunt ||
                taken < minimumHeadHitDamage || !head ||
                (packet.HitPointWorld - head.position).sqrMagnitude > headHitRadius * headHitRadius) return 0f;
            var attacker = packet.Source ? CharacterAttributes.Find(packet.Source.transform) : null;
            float strength = CharacterCombatMath.Offset(attacker, CharacterAttribute.Strength);
            float skill = CharacterCombatMath.Offset(attacker,
                packet.PhysicalImpact == PhysicalImpactKind.Unarmed ? CharacterAttribute.MartialArts : CharacterAttribute.StaffProficiency);
            float toughness = CharacterCombatMath.Offset(attributes, CharacterAttribute.Toughness);
            float chance = baseHeadKnockoutChance + .018f * (taken - minimumHeadHitDamage) +
                .07f * strength + .10f * skill - .16f * toughness;
            return Mathf.Clamp(chance * (1f - headProtection), 0f, maximumHeadKnockoutChance);
        }

        public void KnockOut(Vector3 hitPoint, Vector3 direction, float duration = -1f)
        {
            if (State != NpcConsciousness.Conscious || !ragdoll) return;
            Transition(NpcConsciousness.Unconscious);
            wakeTime = Time.time + (duration > 0f ? duration : knockoutDuration * (1f + 1f - Current / maxHealth));
            if (martial) martial.InterruptCombat();
            if (bandit) bandit.InterruptCombatForKnockout();
            weaponLostAtKnockout = generatedWeapon && generatedWeapon.Instance || bandit && bandit.staff;
            if (generatedWeapon) generatedWeapon.DropForKnockout();
            else if (bandit && bandit.staff)
            {
                bandit.staff.DropFromNpc();
                bandit.SetEquippedStaff(null, bandit.weaponGoal);
            }
            if (enemy) enemy.OnKnockout();
            ragdoll.Activate(hitPoint, (direction.normalized + Vector3.up * .25f).normalized * ragdoll.knockdownImpulse);
            onKnockout.Invoke();
        }

        public void WakeUp()
        {
            if (permanentCollapse || !IsUnconscious || !ragdoll || !ragdoll.Recover()) return;
            Trauma = knockoutTrauma * .25f;
            fleeUntil = Time.time + fleeSeconds;
            Transition(NpcConsciousness.Conscious);
            if (bandit) bandit.OnRecoveredFromKnockout();
            onWakeUp.Invoke();
        }

        public void Die(Vector3 hitPoint, Vector3 impulse)
        {
            if (IsDead) return;
            Transition(NpcConsciousness.Dead);
            if (martial) martial.InterruptCombat();
            if (bandit) bandit.InterruptCombatForKnockout();
            if (generatedWeapon) generatedWeapon.DropForKnockout();
            else if (bandit && bandit.staff)
            {
                bandit.staff.DropFromNpc();
                bandit.SetEquippedStaff(null, bandit.weaponGoal);
            }
            if (enemy) enemy.Die();
            if (ragdoll) ragdoll.Activate(hitPoint, impulse);
            onDeath.Invoke();
        }

        public void ApplyBleeding(float damage)
        {
            if (IsDead || damage <= 0f) return;
            Current = Mathf.Max(0f, Current - damage);
            if (Current <= 0f) Die(transform.position + Vector3.up, Vector3.zero);
        }

        void Transition(NpcConsciousness next)
        {
            var old = State;
            State = next;
            if (logStateChanges) Debug.Log($"{name}: {old} -> {next} (health {Current:F1}, trauma {Trauma:F1})", this);
            StateChanged?.Invoke(old, next);
        }

        [ContextMenu("Combat Debug/Force Knockout")]
        public void ForceKnockout() => KnockOut(transform.position + Vector3.up, transform.forward);
        [ContextMenu("Combat Debug/Force Wake Up")]
        public void ForceWakeUp()
        {
            if (!IsUnconscious) return;
            wakeTime = 0f;
            Current = Mathf.Max(Current, maxHealth * recoveryHealthFraction);
            WakeUp();
        }
        [ContextMenu("Combat Debug/Force Death")]
        public void ForceDeath() { Current = 0f; Die(transform.position + Vector3.up, Vector3.zero); }

        public ShipCombatState GetCombatState() => IsDead ? ShipCombatState.Destroyed :
            IsUnconscious ? ShipCombatState.CombatDisabled : ShipCombatState.CombatReady;
    }
}
