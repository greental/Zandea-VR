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
        public bool stabilizeAttackFeet;
        public bool presentLongBladeGuard;
        [HideInInspector] public bool reviewLocomotionOverride;
        [HideInInspector] public Vector2 reviewLocomotion;
        [Tooltip("Weapon-family combat idle arm rotations, baked by the armed bandit pass.")]
        public Quaternion[] armedGuardRotations;
        public static readonly HumanBodyBones[] GuardBones = {
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
        float armedGuardWeight;
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
        public void ApplyWeaponProfile(BanditMocapNpcDriver source)
        {
            if (!source) return;
            if (!source.grip) source.grip = source.GetComponent<MocapStaffPreview>();
            if (!grip) grip = GetComponent<MocapStaffPreview>();
            if (!source.grip || !grip) return;
            if (!brain) brain = GetComponentInParent<StarvingBandit>();
            var sourceBrain = source.brain ? source.brain : source.GetComponentInParent<StarvingBandit>();
            if (!animator) animator = GetComponent<Animator>();
            animator.runtimeAnimatorController = source.GetComponent<Animator>().runtimeAnimatorController;
            useDirectionalStaffAttacks = source.useDirectionalStaffAttacks;
            useImportedCombatAttacks = source.useImportedCombatAttacks;
            useImportedCombatGuards = source.useImportedCombatGuards;
            useImportedCombatReactions = source.useImportedCombatReactions;
            stabilizeAttackFeet = source.stabilizeAttackFeet;
            presentLongBladeGuard = source.presentLongBladeGuard;
            armedGuardRotations = source.armedGuardRotations == null ? null : (Quaternion[])source.armedGuardRotations.Clone();
            grip.primaryHandIsLeft = source.grip.primaryHandIsLeft;
            grip.secondaryHandGrips = source.grip.secondaryHandGrips;
            grip.attachment = source.grip.attachment;
            grip.driveVisualStaff = source.grip.driveVisualStaff;
            grip.rightGripLocal = source.grip.rightGripLocal;
            grip.leftGripLocal = source.grip.leftGripLocal;
            grip.rightHandFromStaff = source.grip.rightHandFromStaff;
            grip.leftHandFromStaff = source.grip.leftHandFromStaff;
            if (grip.secondaryVisualWeapon)
            {
                if (grip.secondaryPhysicalWeapon) grip.secondaryPhysicalWeapon.DropFromNpc();
                Destroy(grip.secondaryVisualWeapon.gameObject);
                grip.secondaryVisualWeapon = null;
                grip.secondaryPhysicalWeapon = null;
            }
            if (source.grip.secondaryVisualWeapon)
            {
                grip.secondaryVisualWeapon = Instantiate(source.grip.secondaryVisualWeapon, brain.transform);
                grip.secondaryWeaponFromRightHand = source.grip.secondaryWeaponFromRightHand;
                if (!grip.secondaryWeaponTarget)
                {
                    grip.secondaryWeaponTarget = new GameObject("Saber physical target").transform;
                    grip.secondaryWeaponTarget.SetParent(transform, false);
                }
                grip.secondaryPhysicalWeapon = grip.secondaryVisualWeapon.GetComponent<PhysicalMeleeWeapon>();
                if (grip.secondaryPhysicalWeapon)
                {
                    grip.secondaryPhysicalWeapon.ownerRoot = brain.transform;
                    grip.secondaryPhysicalWeapon.npcGripTarget = grip.secondaryWeaponTarget;
                    grip.secondaryPhysicalWeapon.TryEquipNpc(brain, grip.secondaryWeaponTarget);
                }
            }
            shieldVisual = grip.staff ? grip.staff.transform.Find("Temporary shield visual") : null;
            if (brain && sourceBrain && sourceBrain.attacks != null)
            {
                brain.attacks = System.Array.ConvertAll(sourceBrain.attacks,
                    a => JsonUtility.FromJson<StaffAttackDefinition>(JsonUtility.ToJson(a)));
                brain.useAnimatedAttackDisplacement = useImportedCombatAttacks;
                brain.blockIncomingNpcAttacks = sourceBrain.blockIncomingNpcAttacks;
                brain.parryIncomingNpcAttacks = sourceBrain.parryIncomingNpcAttacks;
            }
            if (grip.staff && grip.staff.grip && grip.staff.npcGripTarget)
            {
                var target = grip.staff.npcGripTarget;
                target.localPosition = grip.staff.grip.localPosition;
                target.localRotation = grip.staff.grip.localRotation;
            }
            animator.applyRootMotion = useImportedCombatAttacks;
            animator.Rebind();
            previousState = StarvingBandit.State.Idle;
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
            if (brain && brain.martialArts && !brain.staff) return;
            if (!animator || health && health.State != NpcConsciousness.Conscious || ragdoll && ragdoll.Active) return;
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
            if (brain.martialArts && brain.martialArts.IsUnarmed) return;
            Vector3 velocity = agent && agent.enabled && agent.isOnNavMesh ? agent.velocity : Vector3.zero;
            Vector3 local = brain.transform.InverseTransformDirection(velocity) / locomotionReferenceSpeed;
            if (reviewLocomotionOverride)
            { local.x = reviewLocomotion.x; local.z = reviewLocomotion.y; }
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
                if (useDirectionalStaffAttacks || useImportedCombatAttacks)
                    animator.CrossFadeInFixedTime(StaffDirections[Mathf.Clamp(brain.CurrentAttack, 0, 7)], blendSeconds);
                else
                {
                    // The legacy fallback has no hand-specific mirrored states.
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
            // The comparison scene plays attacks in place so a clip's baked root
            // translation cannot make one variant jump out of its review lane.
            if (reviewLocomotionOverride) return;
            if (brain && brain.martialArts && brain.martialArts.IsUnarmed) return;
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
                float amount = Mathf.Min(Mathf.Max(0f, wanted - fallbackStepApplied),
                    Mathf.Max(.01f, brain.runSpeed * Time.deltaTime));
                step = brain.transform.forward * amount;
                fallbackStepApplied += amount;
            }
            float maxStep = Mathf.Max(.01f, brain.runSpeed * Time.deltaTime);
            if (step.magnitude > maxStep) step = step.normalized * maxStep;
            if (agent && agent.enabled && agent.isOnNavMesh) agent.Move(step);
            else brain.transform.position += step;
        }
        void LateUpdate()
        {
            if (brain && brain.martialArts && brain.martialArts.IsUnarmed) return;
            if (grip && grip.secondaryPhysicalWeapon &&
                (!brain || !brain.staff || health && health.State != NpcConsciousness.Conscious || ragdoll && ragdoll.Active))
                grip.secondaryPhysicalWeapon.DropFromNpc();
            if (!animator || !animator.enabled || (health && health.IsDead) || (ragdoll && ragdoll.Active)) return;
            if (!grip) return;
            ApplyArmedGuard(Time.deltaTime);
            if ((useDirectionalStaffAttacks || stabilizeAttackFeet) && brain && (brain.state == StarvingBandit.State.Anticipate ||
                brain.state == StarvingBandit.State.Strike || brain.state == StarvingBandit.State.FollowThrough ||
                brain.state == StarvingBandit.State.Recover))
                BoStaffFootGrounding.Apply(animator, brain.transform.position.y);
            if (stabilizeAttackFeet && brain && brain.state == StarvingBandit.State.Strike)
                KeepAttackFeetOnTheirOwnSide();
            float targetWeight = brain && brain.state == StarvingBandit.State.Block ? 1f : 0f;
            blockWeight = Mathf.MoveTowards(blockWeight, targetWeight, Time.deltaTime * 5f);
            float parryTarget = brain && brain.state == StarvingBandit.State.Parry ? 1f : 0f;
            parryWeight = Mathf.MoveTowards(parryWeight, parryTarget, Time.deltaTime * 8f);
            grip.runtimeWeaponOffsetWeight = 1f;
            grip.runtimeWeaponOffsetWorld = brain ? brain.transform.TransformDirection(
                new Vector3(.18f, .22f, .18f) * blockWeight + new Vector3(0, .16f, .22f) * parryWeight +
                (grip.secondaryHandGrips ? new Vector3(0, .08f, .22f) * armedGuardWeight : Vector3.zero)) : Vector3.zero;
            grip.runtimeWeaponFaceWeight = shieldVisual ? blockWeight : 0f;
            grip.runtimeWeaponAxisWeight = parryWeight;
            if (brain)
            {
                if (grip.secondaryHandGrips &&
                    (brain.state == StarvingBandit.State.Anticipate || brain.state == StarvingBandit.State.Strike ||
                     brain.state == StarvingBandit.State.FollowThrough))
                    grip.runtimeWeaponOffsetWorld += brain.transform.forward * .12f;
                if (grip.secondaryHandGrips)
                    grip.runtimeWeaponAxisLocal = grip.leftGripLocal - grip.rightGripLocal;
                else if (grip.positiveTip && grip.negativeTip)
                    grip.runtimeWeaponAxisLocal = grip.staff.transform.InverseTransformPoint(grip.positiveTip.position) -
                        grip.staff.transform.InverseTransformPoint(grip.negativeTip.position);
                grip.runtimeWeaponAxisWorld = grip.secondaryHandGrips ? brain.transform.right :
                    (brain.transform.right + brain.transform.up * .45f).normalized;
                if (presentLongBladeGuard && brain.staff &&
                    (brain.state == StarvingBandit.State.Idle || brain.state == StarvingBandit.State.Approach ||
                     brain.state == StarvingBandit.State.Retreat))
                {
                    Vector3 wrist = grip.primaryHandIsLeft ? grip.leftGripLocal : grip.rightGripLocal;
                    grip.runtimeWeaponAxisLocal = brain.StrikingTipLocal(
                        brain.attacks[brain.CurrentAttack].useNegativeTip) - wrist;
                    grip.runtimeWeaponAxisWorld = (brain.transform.forward + brain.transform.up * .2f).normalized;
                    grip.runtimeWeaponAxisWeight = 1f;
                }
            }
            if (shieldVisual && brain)
            {
                grip.runtimeWeaponFaceLocal = grip.staff.transform.InverseTransformDirection(shieldVisual.up);
                grip.runtimeWeaponFacingWorld = brain.transform.forward;
            }
            grip.runtimeStrikeAimWeight = 0f;
            if (brain && brain.target && brain.state == StarvingBandit.State.Strike && brain.staff)
            {
                grip.runtimeStrikeTargetWorld = brain.StrikeTargetPoint();
                grip.runtimeStrikeTipLocal = grip.secondaryPhysicalWeapon ? Vector3.zero :
                    brain.StrikingTipLocal(brain.attacks[brain.CurrentAttack].useNegativeTip);
                grip.runtimeStrikeAimWeight = Mathf.Sin(Mathf.PI * Mathf.Clamp01(brain.AttackProgress));
            }
            grip.ApplyGripFromCurrentPose();
        }

        void KeepAttackFeetOnTheirOwnSide()
        {
            if (!animator.isHuman) return;
            var root = brain.transform;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var rightShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (!left || !right) return;
            Vector3 leftLocal = root.InverseTransformPoint(left.position);
            Vector3 rightLocal = root.InverseTransformPoint(right.position);
            if (leftLocal.x > -.09f || leftLocal.y > .16f)
            {
                leftLocal.x = Mathf.Min(leftLocal.x, -.09f);
                leftLocal.y = Mathf.Min(leftLocal.y, .16f);
                ScavengerPose.Solve(leftThigh, leftShin, left,
                    root.TransformPoint(leftLocal), root.forward);
            }
            if (rightLocal.x < .09f || rightLocal.y > .16f)
            {
                rightLocal.x = Mathf.Max(rightLocal.x, .09f);
                rightLocal.y = Mathf.Min(rightLocal.y, .16f);
                ScavengerPose.Solve(rightThigh, rightShin, right,
                    root.TransformPoint(rightLocal), root.forward);
            }
        }

        public void ApplyArmedGuard(float deltaTime)
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman || !brain || !brain.staff ||
                armedGuardRotations == null || armedGuardRotations.Length != GuardBones.Length) return;
            bool locomotion = !reacting && (brain.state == StarvingBandit.State.Idle ||
                brain.state == StarvingBandit.State.Approach || brain.state == StarvingBandit.State.Circle ||
                brain.state == StarvingBandit.State.Retreat);
            armedGuardWeight = Mathf.MoveTowards(armedGuardWeight, locomotion ? 1f : 0f,
                deltaTime / Mathf.Max(.04f, blendSeconds));
            for (int i = 0; i < GuardBones.Length; i++)
            {
                var bone = animator.GetBoneTransform(GuardBones[i]);
                if (bone) bone.localRotation = Quaternion.Slerp(bone.localRotation, armedGuardRotations[i], armedGuardWeight);
            }
            if (grip && !grip.secondaryHandGrips && armedGuardWeight > 0)
            {
                ReadyHand(grip.primaryHandIsLeft, true);
                ReadyHand(!grip.primaryHandIsLeft, grip.secondaryVisualWeapon);
            }
        }

        void ReadyHand(bool left, bool holdingWeapon)
        {
            var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!chest || !upper || !lower || !hand) return;
            var forward = brain.transform.forward;
            var side = brain.transform.right * (left ? -1 : 1);
            var target = chest.position + side * .22f - brain.transform.up * .12f + forward * (holdingWeapon ? .36f : .22f);
            var rotation = hand.rotation;
            if (holdingWeapon && !(left && grip.secondaryVisualWeapon))
            {
                var offset = left ? grip.leftHandFromStaff : grip.rightHandFromStaff;
                var localAxis = left ? grip.leftGripLocal - grip.rightGripLocal : grip.rightGripLocal - grip.leftGripLocal;
                var weaponRotation = rotation * Quaternion.Inverse(offset);
                rotation = Quaternion.FromToRotation(weaponRotation * localAxis, brain.transform.up * .8f + forward * .6f) * rotation;
            }
            var wantedRotation = Quaternion.Slerp(hand.rotation, rotation, armedGuardWeight);
            ScavengerPose.Solve(upper, lower, hand, Vector3.Lerp(hand.position, target, armedGuardWeight), side - brain.transform.up);
            hand.rotation = wantedRotation;
        }
    }
}
