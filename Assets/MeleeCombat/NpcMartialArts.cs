using System;
using System.Collections.Generic;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    [Serializable]
    public sealed class MartialStrike
    {
        public string stateName;
        public HumanBodyBones limb = HumanBodyBones.RightHand;
        [Min(0)] public float damage = 7f, range = 1.35f, staminaCost;
        [Min(0)] public float minimumRange, committedForwardTravel;
        [Range(0, 1)] public float activeFrom = .25f, activeUntil = .65f;
        [Min(.01f)] public float cooldown = .8f, staggerStrength = 1f, weight = 1f;
        [Range(.02f, .3f)] public float contactRadius = .13f;
    }

    [CreateAssetMenu(menuName = "Kenshi VR/Combat/Martial Arts Profile")]
    public sealed class MartialArtsProfile : ScriptableObject
    {
        public RuntimeAnimatorController controller;
        public MartialStrike[] strikes;
        [Min(.5f)] public float preferredRange = 1.15f;
        [Min(.1f)] public float approachSpeed = 2.2f, retreatSpeed = 1.3f;
        [Range(0, 1)] public float blockChance = .2f, counterChance = .1f;
        [Range(0, 1)] public float guardedDamageMultiplier = .25f;
        [Min(.1f)] public float blockSeconds = .45f, recoveryMultiplier = 1f;
    }

    // Shares the bandit's target, NavMeshAgent, health, and equipment lifecycle.
    [DefaultExecutionOrder(260), RequireComponent(typeof(StarvingBandit))]
    public sealed class NpcMartialArts : MonoBehaviour
    {
        public MartialArtsProfile profile;
        public StarvingBandit brain;
        public Animator animator;
        public bool IsUnarmed => brain && !brain.staff && (!brain.health || !brain.health.IsDead);
        public bool IsGuarding => IsUnarmed && Time.time < guardUntil;
        public bool IsStriking => IsUnarmed && activeStrike != null && Time.time < attackUntil;
        readonly Collider[] contacts = new Collider[24];
        readonly HashSet<IDamageable> scored = new HashSet<IDamageable>();
        RuntimeAnimatorController armedController;
        bool armedRootMotion;
        Transform limb;
        Transform hitSpine;
        MartialStrike activeStrike;
        Vector3 previousLimb;
        float attackStart, attackUntil, nextAttack, guardUntil, nextDefense, nextRetreat;
        Vector3 strikeOrigin, strikeForward, landingFrom, landingTo;
        float landingStart, landingUntil;
        float staggerUntil, staggerStart;
        int lastStrike = -1;
        bool wasUnarmed;
        bool hasMoveX, hasMoveY;
        int visualState;
        ScavengerRagdoll ragdoll;
        MocapStaffPreview fingerPose;
        Vector3 staggerDirection;
        float staggerDistanceLeft;

        void Awake()
        {
            if (!brain) brain = GetComponent<StarvingBandit>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            ragdoll = GetComponent<ScavengerRagdoll>();
            fingerPose = GetComponentInChildren<MocapStaffPreview>(true);
            if (animator)
            {
                armedController = animator.runtimeAnimatorController;
                armedRootMotion = animator.applyRootMotion;
            }
            if (brain && brain.health) brain.health.onDamaged.AddListener(OnDamaged);
        }

        void OnDestroy()
        {
            if (brain && brain.health) brain.health.onDamaged.RemoveListener(OnDamaged);
        }

        void OnDamaged()
        {
            if (!IsUnarmed) return;
            activeStrike = null;
            scored.Clear();
            staggerUntil = Time.time + .35f;
            staggerStart = Time.time;
            staggerDirection = brain.target ?
                Vector3.ProjectOnPlane(transform.position - brain.target.position, Vector3.up).normalized : -transform.forward;
            staggerDistanceLeft = .45f;
            if (!hitSpine && animator && animator.isHuman)
                hitSpine = animator.GetBoneTransform(HumanBodyBones.Spine);
            nextAttack = Mathf.Max(nextAttack, staggerUntil);
            brain.state = StarvingBandit.State.Stagger;
            Play("FightGuard", 3);
        }

        public void ReceiveStrikeStagger(float strength)
        {
            if (!IsUnarmed) return;
            OnDamaged();
            staggerUntil = Mathf.Max(staggerUntil, Time.time + .2f * strength);
            staggerDistanceLeft = Mathf.Max(staggerDistanceLeft, .45f * strength);
        }

        void Update()
        {
            if (IsUnarmed == wasUnarmed) return;
            wasUnarmed = IsUnarmed;
            activeStrike = null;
            scored.Clear();
            landingUntil = 0f;
            if (!animator) return;
            animator.speed = 1f;
            animator.applyRootMotion = wasUnarmed ? false : armedRootMotion;
            animator.runtimeAnimatorController = wasUnarmed && profile && profile.controller ? profile.controller : armedController;
            hasMoveX = hasMoveY = false;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name == "MoveX") hasMoveX = true;
                if (parameter.name == "MoveY") hasMoveY = true;
            }
            visualState = 0;
            if (wasUnarmed) Play("FightIdle", 1);
            else if (armedController) animator.CrossFadeInFixedTime("Locomotion", .15f);
        }

        // Called from StarvingBandit.Update, after its target/death checks.
        public void TickCombat()
        {
            if (!profile || !animator || !brain.target || ragdoll && ragdoll.Active) return;
            var agent = brain.agent;
            if (landingUntil > 0f)
            {
                Stop(agent);
                brain.state = StarvingBandit.State.Recover;
                return;
            }
            Vector3 toward = Vector3.ProjectOnPlane(brain.target.position - transform.position, Vector3.up);
            float distance = toward.magnitude;
            Vector3 localVelocity = agent && agent.enabled && agent.isOnNavMesh ?
                transform.InverseTransformDirection(agent.velocity) / Mathf.Max(.1f, profile.approachSpeed) : Vector3.zero;
            if (hasMoveX) animator.SetFloat("MoveX", Mathf.Clamp(localVelocity.x, -1f, 1f), .1f, Time.deltaTime);
            if (hasMoveY) animator.SetFloat("MoveY", Mathf.Clamp(localVelocity.z, -1f, 1f), .1f, Time.deltaTime);
            if (activeStrike == null && toward.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toward), 260f * Time.deltaTime);
            if (distance > brain.detectRange && !brain.groupControlled) { brain.state = StarvingBandit.State.Idle; Stop(agent); return; }
            if (Time.time < staggerUntil)
            {
                brain.state = StarvingBandit.State.Stagger;
                Stop(agent);
                if (agent && agent.enabled && agent.isOnNavMesh && staggerDistanceLeft > 0f)
                {
                    float step = Mathf.Min(staggerDistanceLeft, 1.7f * Time.deltaTime);
                    agent.Move(staggerDirection * step);
                    staggerDistanceLeft -= step;
                }
                return;
            }
            if (activeStrike != null)
            {
                Stop(agent);
                brain.state = Time.time < attackUntil ? StarvingBandit.State.Strike : StarvingBandit.State.Recover;
                if (Time.time >= attackUntil)
                {
                    if (activeStrike.committedForwardTravel > 0f) BeginLanding(agent);
                    activeStrike = null;
                    scored.Clear();
                }
                return;
            }
            if (Time.time < guardUntil) { brain.state = StarvingBandit.State.Block; Stop(agent); return; }
            if (Time.time >= nextDefense && distance < profile.preferredRange + .7f && IncomingThreat())
            {
                nextDefense = Time.time + .8f;
                if (UnityEngine.Random.value < profile.blockChance)
                {
                    guardUntil = Time.time + profile.blockSeconds;
                    brain.state = StarvingBandit.State.Block;
                    Stop(agent);
                    Play("FightGuard", 3);
                    return;
                }
            }
            bool canAttackAtRange = false;
            if (Time.time >= nextAttack && profile.strikes != null)
                foreach (var strike in profile.strikes)
                    if (CanReach(strike, distance) && strike.weight > 0f) { canAttackAtRange = true; break; }
            if (distance > profile.preferredRange + .12f && !canAttackAtRange)
            {
                brain.state = StarvingBandit.State.Approach;
                Move(agent, brain.target.position, profile.approachSpeed);
                Play("FightMove", 2);
                return;
            }
            if (distance < profile.preferredRange * .65f && Time.time >= nextRetreat)
            {
                brain.state = StarvingBandit.State.Retreat;
                Move(agent, transform.position - toward.normalized, profile.retreatSpeed);
                nextRetreat = Time.time + .45f;
                return;
            }
            Stop(agent);
            brain.state = StarvingBandit.State.Circle;
            Play("FightIdle", 1);
            if (Time.time < nextAttack || profile.strikes == null || profile.strikes.Length == 0) return;
            int index = ChooseStrike(distance);
            if (index < 0) return;
            lastStrike = index;
            activeStrike = profile.strikes[index];
            limb = animator.GetBoneTransform(activeStrike.limb);
            if (!limb) { activeStrike = null; return; }
            previousLimb = limb.position;
            scored.Clear();
            attackStart = Time.time;
            strikeOrigin = transform.position;
            strikeForward = transform.forward;
            float duration = Mathf.Max(.1f, animator.runtimeAnimatorController ? FindDuration(activeStrike.stateName) : .8f);
            attackUntil = attackStart + duration;
            nextAttack = attackUntil + activeStrike.cooldown * profile.recoveryMultiplier;
            brain.state = StarvingBandit.State.Anticipate;
            Play(activeStrike.stateName, 4);
        }

        void LateUpdate()
        {
            if (landingUntil > 0f)
            {
                float progress = Mathf.InverseLerp(landingStart, landingUntil, Time.time);
                if (brain.agent && brain.agent.enabled && brain.agent.isOnNavMesh)
                    brain.agent.Warp(Vector3.Lerp(landingFrom, landingTo, progress));
                if (progress >= 1f) landingUntil = 0f;
            }
            if (IsStriking && activeStrike.committedForwardTravel <= 0f && animator &&
                brain.agent && brain.agent.enabled && brain.agent.isOnNavMesh)
                brain.agent.Move(Vector3.ProjectOnPlane(animator.deltaPosition, Vector3.up));
            if (IsUnarmed && hitSpine && Time.time < staggerUntil)
            {
                float phase = Mathf.InverseLerp(staggerStart, staggerUntil, Time.time);
                hitSpine.localRotation *= Quaternion.Euler(-20f * Mathf.Sin(phase * Mathf.PI), 0f, 0f);
            }
            if (IsUnarmed && fingerPose && fingerPose.fingers != null)
                foreach (var finger in fingerPose.fingers)
                    if (finger.bone) finger.bone.localRotation = finger.rotation;
            if (!IsStriking || !limb || !profile) return;
            Vector3 current = limb.position;
            float progress = Mathf.InverseLerp(attackStart, attackUntil, Time.time);
            if (progress >= activeStrike.activeFrom && progress <= activeStrike.activeUntil)
                ResolveContact(previousLimb, current);
            previousLimb = current;
        }

        int ChooseStrike(float distance)
        {
            float total = 0;
            for (int i = 0; i < profile.strikes.Length; i++)
                if (CanReach(profile.strikes[i], distance))
                    total += profile.strikes[i].weight * (i == lastStrike ? .35f : 1f);
            if (total <= 0) return -1;
            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < profile.strikes.Length; i++)
            {
                var strike = profile.strikes[i];
                if (!CanReach(strike, distance)) continue;
                roll -= strike.weight * (i == lastStrike ? .35f : 1f);
                if (roll <= 0) return i;
            }
            return -1;
        }

        static bool CanReach(MartialStrike strike, float distance) =>
            strike != null && strike.range >= distance && distance >= strike.minimumRange &&
            (strike.stateName != "knee strike" || distance <= .85f);

        void BeginLanding(NavMeshAgent agent)
        {
            Vector3 desired = strikeOrigin + strikeForward * activeStrike.committedForwardTravel;
            if (agent && agent.enabled && agent.isOnNavMesh &&
                NavMesh.SamplePosition(desired, out var hit, .6f, agent.areaMask))
                desired = hit.position;
            landingFrom = transform.position;
            landingTo = desired;
            landingStart = Time.time;
            landingUntil = Time.time + .12f;
            Play("FightIdle", 1);
        }

        void ResolveContact(Vector3 from, Vector3 to)
        {
            int count = Physics.OverlapCapsuleNonAlloc(from, to, activeStrike.contactRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
            // A tracked player weapon between the limb and body takes the strike first.
            for (int i = 0; i < count; i++)
            {
                var weapon = contacts[i] ? contacts[i].GetComponentInParent<PhysicalMeleeWeapon>() : null;
                if (weapon && weapon.State == PhysicalMeleeWeapon.HoldState.HeldByPlayer) return;
            }
            var defendingPlayer = brain.target ? brain.target.GetComponentInParent<ScavengerPlayerHealth>() : null;
            if (defendingPlayer && !scored.Contains(defendingPlayer))
                foreach (var hand in defendingPlayer.GetComponentsInChildren<PlayerMartialHand>(true))
                    if (hand.TryIntercept(from, to, activeStrike.contactRadius, transform.position, out Vector3 impact))
                    {
                        scored.Add(defendingPlayer);
                        defendingPlayer.ApplyDamage(new DamagePacket {
                            BaseDamage = activeStrike.damage, DamageType = DamageType.Kinetic,
                            HitPointWorld = impact, HitNormalWorld = (impact - from).normalized,
                            Source = gameObject, WeaponId = activeStrike.stateName, Timestamp = Time.time
                        });
                        return;
                    }
            for (int i = 0; i < count; i++)
            {
                var shape = contacts[i];
                if (!shape || shape.transform.IsChildOf(transform)) continue;
                var enemy = shape.GetComponentInParent<ScavengerHealth>();
                var player = shape.GetComponentInParent<ScavengerPlayerHealth>();
                IDamageable receiver = enemy ? enemy : player;
                if (receiver == null || enemy == brain.health || scored.Contains(receiver)) continue;
                if (enemy && enemy.IsDead || player && player.IsDead) continue;
                Vector3 travel = to - from;
                Vector3 contact = from + travel * Mathf.Clamp01(Vector3.Dot(shape.bounds.center - from, travel) /
                    Mathf.Max(travel.sqrMagnitude, .000001f));
                Vector3 point = shape.ClosestPoint(contact);
                if ((point - contact).sqrMagnitude > activeStrike.contactRadius * activeStrike.contactRadius) continue;
                scored.Add(receiver);
                receiver.ApplyDamage(new DamagePacket {
                    BaseDamage = activeStrike.damage * CharacterCombatMath.DamageDealt(GetComponent<CharacterAttributes>(), PhysicalMeleeWeapon.WeaponProficiencyClass.None),
                    DamageType = DamageType.Kinetic, HitPointWorld = point,
                    HitNormalWorld = (point - to).normalized, Source = gameObject,
                    WeaponId = activeStrike.stateName, Timestamp = Time.time
                });
                if (enemy)
                {
                    var martial = enemy.GetComponent<NpcMartialArts>();
                    if (martial) martial.ReceiveStrikeStagger(activeStrike.staggerStrength);
                }
            }
        }

        public bool Blocks(Vector3 point, Vector3 attackerPosition)
        {
            if (!IsGuarding || point.y < transform.position.y + .8f) return false;
            Vector3 direction = Vector3.ProjectOnPlane(attackerPosition - transform.position, Vector3.up);
            return direction.sqrMagnitude > .001f && Vector3.Dot(transform.forward, direction.normalized) > .45f;
        }

        public void SuccessfulBlock()
        {
            if (profile && UnityEngine.Random.value < profile.counterChance)
            { guardUntil = Time.time; nextAttack = Time.time; }
        }

        bool IncomingThreat()
        {
            var opponent = brain.target.GetComponentInParent<StarvingBandit>();
            return opponent && (opponent.state == StarvingBandit.State.Anticipate || opponent.state == StarvingBandit.State.Strike);
        }

        float FindDuration(string stateName)
        {
            // Profile timings are in normalized clip space; authored clips play at their imported speed.
            if (!animator.runtimeAnimatorController) return .8f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip && clip.name == stateName) return clip.length;
            return .8f;
        }

        static void Stop(NavMeshAgent agent)
        { if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true; }
        static void Move(NavMeshAgent agent, Vector3 position, float speed)
        {
            if (!agent || !agent.enabled || !agent.isOnNavMesh) return;
            agent.speed = speed;
            agent.isStopped = false;
            agent.SetDestination(position);
        }
        void Play(string stateName, int state)
        {
            if (!animator || visualState == state) return;
            visualState = state;
            animator.CrossFadeInFixedTime(stateName, .1f);
        }
    }
}
