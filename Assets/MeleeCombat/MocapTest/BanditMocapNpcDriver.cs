using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace KenshiVR.MeleeCombat
{
    // Animator owns full-body motion; NavMeshAgent owns displacement; the existing
    // mocap grip solver attaches the rigid visual weapon after animation evaluation.
    [DefaultExecutionOrder(240), RequireComponent(typeof(Animator))]
    public sealed class BanditMocapNpcDriver : MonoBehaviour
    {
        public StarvingBandit brain;
        public NavMeshAgent agent;
        public ScavengerHealth health;
        public ScavengerRagdoll ragdoll;
        public MocapStaffPreview grip;
        public bool useDirectionalStaffAttacks;
        public bool useImportedCombatAttacks;
        public bool useImportedCombatGuards;
        public bool useImportedCombatReactions;
        [Min(.1f)] public float locomotionReferenceSpeed = 3.2f;
        [Min(0)] public float blendSeconds = .12f;
        [Min(0)] public float inPlaceAttackStep = .35f;
        Animator animator;
        StarvingBandit.State previousState;
        bool reacting;
        float reactionUntil;
        float blockWeight;
        float parryWeight;
        int hitVariant;
        Transform shieldVisual;
        float fallbackStepApplied;
        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int Locomotion = Animator.StringToHash("Locomotion");
        static readonly int Attack = Animator.StringToHash("Attack");
        static readonly int AttackMirrored = Animator.StringToHash("AttackMirrored");
        static readonly int Hit = Animator.StringToHash("Hit");
        static readonly int HitLeft = Animator.StringToHash("HitLeft");
        static readonly int HitRight = Animator.StringToHash("HitRight");
        static readonly int HitGut = Animator.StringToHash("HitGut");
        static readonly int Block = Animator.StringToHash("Block");
        static readonly int Parry = Animator.StringToHash("Parry");
        static readonly int[] StaffDirections = {
            Animator.StringToHash("AttackDirection0"), Animator.StringToHash("AttackDirection1"),
            Animator.StringToHash("AttackDirection2"), Animator.StringToHash("AttackDirection3"),
            Animator.StringToHash("AttackDirection4"), Animator.StringToHash("AttackDirection5"),
            Animator.StringToHash("AttackDirection6"), Animator.StringToHash("AttackDirection7") };

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (!grip) grip = GetComponent<MocapStaffPreview>();
            if (!brain) brain = GetComponentInParent<StarvingBandit>();
            if (!agent && brain) agent = brain.agent;
            if (!health && brain) health = brain.health;
            if (!ragdoll && brain) ragdoll = brain.GetComponent<ScavengerRagdoll>();
            animator.applyRootMotion = useImportedCombatAttacks;
            if (brain) brain.useAnimatedAttackDisplacement = useImportedCombatAttacks;
            if (grip && grip.staff) shieldVisual = grip.staff.transform.Find("Temporary shield visual");
        }
        void OnEnable()
        {
            if (!health && brain) health = brain.health;
            if (health) health.onDamaged.AddListener(OnDamaged);
        }
        void OnDisable()
        {
            if (health) health.onDamaged.RemoveListener(OnDamaged);
            if (animator) animator.speed = 1f;
        }
        void OnDamaged()
        {
            if (!animator || (health && health.IsDead)) return;
            reacting = true;
            var toughness = brain ? CharacterCombatMath.Offset(
                brain.GetComponent<CharacterAttributes>(), CharacterAttribute.Toughness) : 0f;
            reactionUntil = Time.time + Mathf.Clamp(.45f * (1f - .2f * toughness), .32f, .55f);
            int state = !useImportedCombatReactions ? Hit : (++hitVariant % 3) switch
            {
                0 => HitLeft, 1 => HitRight, _ => HitGut
            };
            animator.CrossFadeInFixedTime(state, .06f);
            animator.speed = 1f;
        }
        void Update()
        {
            if (!animator || !brain || (health && health.IsDead) || (ragdoll && ragdoll.Active)) return;
            Vector3 velocity = agent && agent.enabled && agent.isOnNavMesh ? agent.velocity : Vector3.zero;
            Vector3 local = brain.transform.InverseTransformDirection(velocity) / locomotionReferenceSpeed;
            // Spacing retreats use NavMeshAgent.Move rather than a destination,
            // so agent.velocity can read as zero despite the backward step.
            if (useImportedCombatAttacks && brain.state == StarvingBandit.State.Retreat)
            { local.x = 0f; local.z = -1f; }
            animator.SetFloat(MoveX, Mathf.Clamp(local.x, -1, 1), .1f, Time.deltaTime);
            animator.SetFloat(MoveY, Mathf.Clamp(local.z, -1, 1), .1f, Time.deltaTime);
            bool attacking = brain.state == StarvingBandit.State.Anticipate ||
                brain.state == StarvingBandit.State.Strike ||
                brain.state == StarvingBandit.State.FollowThrough ||
                brain.state == StarvingBandit.State.Recover;
            animator.speed = attacking && !reacting ? brain.AttackAnimationSpeed : 1f;
            if (reacting)
            {
                if (Time.time < reactionUntil) { previousState = brain.state; return; }
                reacting = false;
                animator.CrossFadeInFixedTime(Locomotion, blendSeconds);
            }
            bool wasAttacking = previousState == StarvingBandit.State.Anticipate ||
                previousState == StarvingBandit.State.Strike ||
                previousState == StarvingBandit.State.FollowThrough ||
                previousState == StarvingBandit.State.Recover;
            if (attacking && !wasAttacking)
            {
                fallbackStepApplied = 0f;
                // Mirror only two-hand animations. Mirroring a one-hand clip would
                // transfer the grip to the wrong hand and detach its weapon.
                if (useDirectionalStaffAttacks || useImportedCombatAttacks)
                    animator.CrossFadeInFixedTime(StaffDirections[Mathf.Clamp(brain.CurrentAttack, 0, 7)], blendSeconds);
                else
                {
                    bool mirror = grip && grip.secondaryHandGrips && (brain.CurrentAttack == 2 ||
                        brain.CurrentAttack == 3 || brain.CurrentAttack == 4 || brain.CurrentAttack == 5);
                    animator.CrossFadeInFixedTime(mirror ? AttackMirrored : Attack, blendSeconds);
                }
            }
            else if (!attacking && wasAttacking) animator.CrossFadeInFixedTime(Locomotion, blendSeconds);
            if (!attacking && useImportedCombatGuards)
            {
                bool guarding = brain.state == StarvingBandit.State.Block || brain.state == StarvingBandit.State.Parry;
                bool wasGuarding = previousState == StarvingBandit.State.Block || previousState == StarvingBandit.State.Parry;
                if (guarding && brain.state != previousState)
                    animator.CrossFadeInFixedTime(brain.state == StarvingBandit.State.Block ? Block : Parry, .08f);
                else if (!guarding && wasGuarding)
                    animator.CrossFadeInFixedTime(Locomotion, blendSeconds);
            }
            previousState = brain.state;
        }
        void OnAnimatorMove()
        {
            if (!useImportedCombatAttacks || !brain || !animator ||
                (health && health.IsDead) || (ragdoll && ragdoll.Active)) return;
            bool attacking = brain.state == StarvingBandit.State.Anticipate ||
                brain.state == StarvingBandit.State.Strike ||
                brain.state == StarvingBandit.State.FollowThrough ||
                brain.state == StarvingBandit.State.Recover;
            if (!attacking) return;

            Vector3 step = Vector3.ProjectOnPlane(animator.deltaPosition, Vector3.up);
            // Some imported clips contain foot motion but no root translation. Give
            // those attacks a modest displacement synchronized to the strike phase.
            if (step.sqrMagnitude < .000001f)
            {
                float fraction = brain.CurrentPhase == StarvingBandit.AttackPhase.Windup ?
                    .15f * Mathf.SmoothStep(0f, 1f, brain.AttackProgress) :
                    brain.CurrentPhase == StarvingBandit.AttackPhase.Strike ?
                    Mathf.Lerp(.15f, 1f, Mathf.SmoothStep(0f, 1f, brain.AttackProgress)) : 1f;
                float wanted = Mathf.Max(brain.PlannedApproachStep, inPlaceAttackStep) * fraction;
                float amount = Mathf.Max(0f, wanted - fallbackStepApplied);
                step = brain.transform.forward * amount;
                fallbackStepApplied += amount;
            }
            if (step.sqrMagnitude > .25f) step = step.normalized * .5f;
            if (agent && agent.enabled && agent.isOnNavMesh) agent.Move(step);
            else brain.transform.position += step;
        }
        void LateUpdate()
        {
            if (!animator || !animator.enabled || (health && health.IsDead) || (ragdoll && ragdoll.Active)) return;
            if (!grip) return;
            if (useDirectionalStaffAttacks && brain && (brain.state == StarvingBandit.State.Anticipate ||
                brain.state == StarvingBandit.State.Strike || brain.state == StarvingBandit.State.FollowThrough ||
                brain.state == StarvingBandit.State.Recover))
                BoStaffFootGrounding.Apply(animator, brain.transform.position.y);
            float targetWeight = brain && brain.state == StarvingBandit.State.Block ? 1f : 0f;
            blockWeight = Mathf.MoveTowards(blockWeight, targetWeight, Time.deltaTime * 5f);
            float parryTarget = brain && brain.state == StarvingBandit.State.Parry ? 1f : 0f;
            parryWeight = Mathf.MoveTowards(parryWeight, parryTarget, Time.deltaTime * 8f);
            grip.runtimeWeaponOffsetWeight = 1f;
            grip.runtimeWeaponOffsetWorld = brain ? brain.transform.TransformDirection(
                new Vector3(.18f, .22f, .18f) * blockWeight + new Vector3(0, .16f, .22f) * parryWeight) : Vector3.zero;
            grip.runtimeWeaponFaceWeight = shieldVisual ? blockWeight : 0f;
            grip.runtimeWeaponAxisWeight = parryWeight;
            if (brain)
            {
                if (grip.secondaryHandGrips)
                    grip.runtimeWeaponAxisLocal = grip.leftGripLocal - grip.rightGripLocal;
                else if (grip.positiveTip && grip.negativeTip)
                    grip.runtimeWeaponAxisLocal = grip.staff.transform.InverseTransformPoint(grip.positiveTip.position) -
                        grip.staff.transform.InverseTransformPoint(grip.negativeTip.position);
                grip.runtimeWeaponAxisWorld = grip.secondaryHandGrips ? brain.transform.right :
                    (brain.transform.right + brain.transform.up * .45f).normalized;
            }
            if (shieldVisual && brain)
            {
                grip.runtimeWeaponFaceLocal = grip.staff.transform.InverseTransformDirection(shieldVisual.up);
                grip.runtimeWeaponFacingWorld = brain.transform.forward;
            }
            grip.ApplyGripFromCurrentPose();
        }
    }
}
