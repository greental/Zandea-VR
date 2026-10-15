using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScavengerNPC;
using JunkerBody;

namespace KenshiVR.MeleeCombat
{
    [Serializable]
    public sealed class StaffCombatPose
    {
        [Tooltip("Visible shaft midpoint offset from its saved guard position, in bandit-local meters.")]
        public Vector3 gripMidpointOffset;
        [Tooltip("Weapon root rotation relative to its saved guard rotation.")]
        public Vector3 staffEuler;
        public float hipYaw, torsoYaw, torsoPitch, shoulderYaw, shoulderLift;
        public float forwardLunge, weightShift;
        [Tooltip("Zero preserves the elbow direction captured from the authored guard.")]
        public Vector3 leftElbowHint, rightElbowHint;
        public Vector3 desiredTipApproach = Vector3.forward;
    }

    [Serializable]
    public sealed class StaffAttackDefinition
    {
        public string clockDirection;
        public Vector3 windupOffset, controlA, controlB, impactOffset, followOffset;
        public Vector3 windupEuler, impactEuler, followEuler;
        [Header("Striking tip path in bandit-local space")]
        public bool useNegativeTip;
        public Vector3 windupTipLocal, tipControlA, impactTipLocal, impactApproachLocal, followTipLocal;
        [Min(.05f)] public float approachDistance = .3f;
        [Min(.05f)] public float windupSeconds = .48f, strikeSeconds = .3f, followSeconds = .18f, recoverySeconds = .55f;
        public AnimationCurve windupEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public AnimationCurve strikeEase = new AnimationCurve(new Keyframe(0, 0, 0, .3f), new Keyframe(1, 1, 1.8f, 0));
        public AnimationCurve followEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float windupYaw, impactYaw, followYaw, windupLean, impactLean, followLean, weightShift;
        [Header("Coordinated full-body attack poses")]
        public int poseVersion;
        public bool dominantRight = true;
        [Min(.6f)] public float comfortableRange = 1.5f;
        [Range(0f, .4f)] public float maximumApproachStep = .28f;
        [Range(.15f, .65f)] public float earlyStrikeTime = .38f;
        public StaffCombatPose guardPose = new StaffCombatPose();
        public StaffCombatPose windupPose = new StaffCombatPose();
        public StaffCombatPose earlyStrikePose = new StaffCombatPose();
        public StaffCombatPose impactPose = new StaffCombatPose();
        public StaffCombatPose followPose = new StaffCombatPose();
        public StaffCombatPose recoveryPose = new StaffCombatPose();
    }

    // Desired staff path, physical staff, and hand IK remain separate stages.
    public sealed class StarvingBandit : MonoBehaviour
    {
        public enum State { Idle, Threaten, Approach, Circle, Anticipate, Strike, Recover, Stagger, Retreat, Dead, FollowThrough, Dodge, Block, Parry }
        public enum AttackPhase { Windup, Strike, FollowThrough, Recovery }
        public State state;
        public Transform target, weaponGoal;
        public PhysicalMeleeWeapon staff;
        public NpcMartialArts martialArts;
        [Header("Optional tip markers on the staff")]
        public Transform positiveStrikingTip, negativeStrikingTip;
        public ScavengerHealth health;
        public NavMeshAgent agent;
        ScavengerRagdoll ragdoll;
        [NonSerialized] public bool groupControlled;
        [NonSerialized] public bool groupEngaged;
        [NonSerialized] public Vector3 groupDestination;
        [NonSerialized] public float groupWalkSpeed = 1.5f;
        [Header("Guard pose authoring")] public bool guardAuthoringMode;
        public float detectRange = 12f, attackRange = 1.5f;
        public float runUntilRange = 3.1f, runSpeed = 3.2f, chasseSpeed = .85f;
        [Header("Combat spacing")]
        [Min(.1f)] public float minimumFightDistance = 1.65f;
        [Min(.1f)] public float preferredFightDistance = 2.15f;
        [Min(.1f)] public float spacingRetreatSpeed = 2.1f;
        [Min(.1f)] public float postAttackRetreatSeconds = 1.5f;
        [Header("Mocap evasive movement")]
        public bool dodgeIncomingNpcAttacks;
        [Range(0, 1)] public float dodgeChance = .55f;
        [Min(.1f)] public float dodgeDistance = 1.2f;
        [Min(.1f)] public float dodgeCooldown = 2f;
        [Header("Shield preview defense")]
        public bool blockIncomingNpcAttacks;
        [Range(0, 1)] public float blockChance = .8f;
        [Header("Two-hand visual parry")]
        public bool parryIncomingNpcAttacks;
        [Range(0, 1)] public float parryChance = .7f;
        [Header("Arena attack showcase")]
        public bool cycleAttackDirections;
        [Range(0, 7)] public int firstAttackDirection;
        [Min(0)] public float initialAttackDelay;
        [Header("Staff paths, in clock order")] public StaffAttackDefinition[] attacks;
        [HideInInspector] public int attackDefaultsVersion;
        [Range(0, 30)] public float maximumAimYaw = 12f;
        [Range(.01f, .2f)] public float reachMargin = .035f;
        public bool showTrajectory = true;
        [Header("Play mode preview")] public bool previewEnabled;
        public Vector3 previewTargetLocal = new Vector3(0, 1.45f, 1.5f);
        [Range(0, 7)] public int previewDirection;
        public AttackPhase previewPhase;
        [Range(0, 1)] public float previewProgress;
        public bool previewApplyRootStep;
        public bool showRangeGizmos = true;
        float until, nextAttack, rangeToTarget, frozenAimYaw;
        float dodgeUntil, nextDodge;
        float blockUntil;
        float parryUntil, nextParry;
        float lastWeaponParry;
        PhysicalMeleeWeapon parriedWeapon;
        BanditCombatAudio combatAudio;
        CharacterAttributes attributes;
        public float AttackAnimationSpeed => CharacterCombatMath.AttackSpeed(attributes,
            staff ? staff.proficiencyClass : PhysicalMeleeWeapon.WeaponProficiencyClass.None);
        public void RegisterWeaponParry(PhysicalMeleeWeapon incoming)
        {
            if (state != State.Parry || !incoming || incoming == staff) return;
            parriedWeapon = incoming;
            lastWeaponParry = Time.time;
            combatAudio?.PlayParry();
        }
        public bool DeflectsDamageFrom(PhysicalMeleeWeapon incoming) =>
            incoming && incoming == parriedWeapon && Time.time - lastWeaponParry < .3f;
        Vector3 dodgeDestination;
        bool threatWasAttacking;
        bool threatWasStriking;
        bool spacingRetreat;
        float plannedStep, appliedStep;
        Vector3 previewRootPosition;
        bool dropped, guardCaptured, previewBodyWasKinematic, previewActive;
        int attackIndex;
        int nextShowcaseDirection;
        Vector3 guardLocalPosition;
        Quaternion guardLocalRotation;
        BanditRigMotion rigMotion;
        Rigidbody staffBody;
        GameObject previewDummy;
        Material previewDummyMaterial;
        Vector3 desiredStaffPosition, desiredTipPosition;
        Quaternion desiredStaffRotation;
        bool hasDesiredPose;
        float staffHalfLength = .95f;
        readonly List<Vector3> actualTipTrail = new();
        int trailDirection = -1;
        AttackPhase trailPhase;
        float trailProgress;
        public Vector3 DesiredStaffPosition => desiredStaffPosition;
        public Quaternion DesiredStaffRotation => desiredStaffRotation;
        public Vector3 DesiredTipPosition => desiredTipPosition;
        public Vector3 ActualTipPosition => staff ? staff.transform.TransformPoint(StrikingTipLocal(attacks != null && attacks.Length > attackIndex && attacks[attackIndex].useNegativeTip)) : Vector3.zero;
        public float CurrentOrientationError => hasDesiredPose && staff ? Quaternion.Angle(desiredStaffRotation, staff.transform.rotation) : 0f;
        public void GetTipLocals(out Vector3 positive, out Vector3 negative)
        {
            positive = positiveStrikingTip && staff ? staff.transform.InverseTransformPoint(positiveStrikingTip.position) : Vector3.zero;
            negative = negativeStrikingTip && staff ? staff.transform.InverseTransformPoint(negativeStrikingTip.position) : Vector3.zero;
            if (positiveStrikingTip && negativeStrikingTip) return;
            if (staff && StaffAxisCalibration.TryMeasure(staff.transform, out var measuredPositive, out var measuredNegative))
            {
                if (!positiveStrikingTip) positive = measuredPositive;
                if (!negativeStrikingTip) negative = measuredNegative;
            }
            else
            {
                if (!positiveStrikingTip) positive = Vector3.forward * staffHalfLength;
                if (!negativeStrikingTip) negative = -Vector3.forward * staffHalfLength;
            }
        }
        public Vector3 StrikingTipLocal(bool negative)
        {
            GetTipLocals(out var positive, out var minus);
            return negative ? minus : positive;
        }
        public void EnsureTipMarkers()
        {
            if (!staff) return;
            GetTipLocals(out var positive, out var negative);
            if (!positiveStrikingTip) positiveStrikingTip = NewTipMarker("PositiveStrikingTip", positive);
            if (!negativeStrikingTip) negativeStrikingTip = NewTipMarker("NegativeStrikingTip", negative);
        }
        Transform NewTipMarker(string markerName, Vector3 localPosition)
        {
            var marker = new GameObject(markerName).transform;
            marker.SetParent(staff.transform, false);
            marker.localPosition = localPosition;
            marker.localRotation = Quaternion.identity;
            return marker;
        }
        public float DesiredAngularTravel { get; private set; }
        public float AchievedAngularTravel { get; private set; }
        public float DesiredTipTravel { get; private set; }
        public float AchievedTipTravel { get; private set; }
        public int CurrentAttack => attackIndex;
        public float CurrentAttackAngle => attackIndex * 45f;
        public static readonly string[] ClockDirections = { "12", "1:30", "3", "4:30", "6", "7:30", "9", "10:30" };
        public float AttackProgress { get; private set; }
        public AttackPhase CurrentPhase { get; private set; }
        public bool IsChasse => (state == State.Approach && rangeToTarget <= runUntilRange) ||
            (groupControlled && !groupEngaged && state == State.Idle);
        public bool IsRunning => state == State.Approach && rangeToTarget > runUntilRange;
        public float ReachError { get; private set; }
        public float ReachCorrectionDistance { get; private set; }
        public float ReachCorrectionAngle { get; private set; }
        public string ReachCorrectionReason { get; private set; } = "None";
        public float CurrentTipError => hasDesiredPose ? Vector3.Distance(desiredTipPosition, ActualTipPosition) : 0f;
        public float RequiredApproachDistance => attacks != null && attacks.Length > attackIndex ? Mathf.Max(0, rangeToTarget - attacks[attackIndex].comfortableRange) : 0;
        public float PlannedApproachStep => plannedStep;
        // Imported clips are displaced by BanditMocapNpcDriver after Animator evaluation.
        public bool useAnimatedAttackDisplacement;

        void Reset() { EnsureAttacks(); attackDefaultsVersion = 12; }
        void OnValidate()
        {
            if (attackDefaultsVersion < 12) attacks = null;
            EnsureAttacks();
            attackDefaultsVersion = 12;
        }
        void EnsureAttacks()
        {
            if (attacks == null || attacks.Length != 8)
            {
                var old = attacks;
                attacks = new StaffAttackDefinition[8];
                for (int i = 0; i < 8; i++) attacks[i] = old != null && i < old.Length && old[i] != null ? old[i] : DefaultAttack(i);
            }
            for (int i = 0; i < 8; i++) if (attacks[i] == null) attacks[i] = DefaultAttack(i);
            for (int i = 0; i < 8; i++) if (attacks[i].poseVersion < 4) GeneratePoseDefaults(i);
        }
        public void GeneratePoseDefaults(int index)
        {
            if (attacks == null || index < 0 || index >= attacks.Length || attacks[index] == null) return;
            var a = attacks[index];
            a.comfortableRange = 1.5f;
            // Each family has its own preparation, strike plane and body drive. Values are
            // deliberately conservative so the authored guard and fixed grips remain reachable.
            float side = index <= 4 ? 1f : -1f;
            bool overhead = index == 0, rising = index == 4;
            bool diagonal = index == 1 || index == 3 || index == 5 || index == 7;
            bool descending = index == 0 || index == 1 || index == 7;
            bool low = index == 3 || index == 4 || index == 5;
            float lateral = overhead || rising ? 0 : side * (diagonal ? .13f : .19f);
            a.dominantRight = side >= 0;
            a.guardPose = new StaffCombatPose();
            a.windupPose = new StaffCombatPose {
                gripMidpointOffset = new Vector3(lateral, overhead ? .23f : low ? -.16f : .09f, -.09f),
                staffEuler = new Vector3(descending ? -15f : low ? 16f : 1f, -side * 12f, side * (diagonal ? 12f : 19f)),
                hipYaw = -side * 9f, torsoYaw = -side * 17f, torsoPitch = overhead ? -7f : low ? 7f : -3f,
                shoulderYaw = -side * 8f, shoulderLift = overhead ? 13f : low ? -5f : 4f,
                weightShift = -side * .035f, forwardLunge = -.015f,
                desiredTipApproach = new Vector3(-side * .35f, descending ? -.65f : low ? .6f : 0, .8f).normalized
            };
            a.earlyStrikePose = new StaffCombatPose {
                gripMidpointOffset = new Vector3(lateral * .45f, overhead ? .17f : low ? -.09f : .05f, .065f),
                staffEuler = new Vector3(descending ? 8f : low ? -8f : 3f, side * 3f, -side * 5f),
                hipYaw = side * 5f, torsoYaw = side * 7f, torsoPitch = descending ? 5f : low ? -4f : 0,
                shoulderYaw = side * 5f, shoulderLift = overhead ? 8f : 2f,
                weightShift = side * .015f, forwardLunge = .055f,
                desiredTipApproach = a.windupPose.desiredTipApproach
            };
            a.impactPose = new StaffCombatPose {
                gripMidpointOffset = new Vector3(-lateral * .38f, overhead ? -.07f : low ? .015f : .015f, -.02f),
                staffEuler = new Vector3(descending ? 29f : low ? -27f : 7f, side * 15f, -side * (diagonal ? 18f : 27f)),
                hipYaw = side * 17f, torsoYaw = side * 22f, torsoPitch = descending ? 13f : low ? -10f : 5f,
                shoulderYaw = side * 12f, shoulderLift = overhead ? -3f : low ? 7f : 2f,
                weightShift = side * .035f, forwardLunge = .115f,
                desiredTipApproach = a.windupPose.desiredTipApproach
            };
            a.followPose = new StaffCombatPose {
                gripMidpointOffset = new Vector3(-lateral * .65f, overhead ? -.13f : low ? .08f : .01f, .03f),
                staffEuler = new Vector3(descending ? 40f : low ? -38f : 10f, side * 20f, -side * (diagonal ? 27f : 38f)),
                hipYaw = side * 20f, torsoYaw = side * 27f, torsoPitch = descending ? 15f : low ? -11f : 6f,
                shoulderYaw = side * 14f, shoulderLift = low ? 9f : 0,
                weightShift = side * .04f, forwardLunge = .13f,
                desiredTipApproach = a.windupPose.desiredTipApproach
            };
            a.recoveryPose = new StaffCombatPose {
                gripMidpointOffset = new Vector3(-lateral * .25f, 0, .08f),
                staffEuler = new Vector3(5f, side * 4f, -side * 6f),
                hipYaw = side * 5f, torsoYaw = side * 7f, torsoPitch = 2f,
                forwardLunge = .03f, weightShift = side * .01f
            };
            // Aim the measured striking half of the shaft, rather than assuming
            // that the imported mesh's local Z axis points along the pole.
            GetTipLocals(out var positiveTip, out var negativeTip);
            Quaternion guardRotation = staff ? Quaternion.Inverse(transform.rotation) * staff.transform.rotation : Quaternion.identity;
            Vector3 shaft = positiveTip - (positiveTip + negativeTip) * .5f;
            Vector3 impactDirection = new Vector3(.15f,
                descending ? -.06f : low ? .10f : 0, 1f).normalized;
            Quaternion impactTurn = Quaternion.FromToRotation(guardRotation * shaft, impactDirection);
            a.impactPose.staffEuler = (Quaternion.Inverse(guardRotation) * impactTurn * guardRotation).eulerAngles;
            Quaternion windupTurn = Quaternion.FromToRotation(guardRotation * shaft,
                new Vector3(side * (overhead || rising ? .1f : .55f), descending ? 1f : low ? -.65f : .15f, .35f).normalized);
            a.windupPose.staffEuler = (Quaternion.Inverse(guardRotation) * windupTurn * guardRotation).eulerAngles;
            a.earlyStrikePose.staffEuler = Quaternion.Slerp(Quaternion.Euler(a.windupPose.staffEuler), Quaternion.Euler(a.impactPose.staffEuler), .48f).eulerAngles;
            a.followPose.staffEuler = (Quaternion.Euler(a.impactPose.staffEuler) * Quaternion.Euler(descending ? 10f : low ? -10f : 3f, side * 5f, -side * 8f)).eulerAngles;
            a.recoveryPose.staffEuler = Quaternion.Slerp(Quaternion.Euler(a.followPose.staffEuler), Quaternion.identity, .65f).eulerAngles;
            a.poseVersion = 4;
        }
        StaffAttackDefinition DefaultAttack(int index)
        {
            // Clock directions are viewed by the player facing the bandit.
            float angle = index * 45f * Mathf.Deg2Rad, side = -Mathf.Sin(angle), high = Mathf.Cos(angle);
            var a = new StaffAttackDefinition { clockDirection = ClockDirections[index] };
            // The upper end is the reachable striking end in this authored two-hand grip.
            a.useNegativeTip = false;
            a.windupOffset = new Vector3(side * .04f, high * .06f, -.02f);
            a.impactOffset = new Vector3(-side * .015f, .05f, .06f);
            a.followOffset = new Vector3(-side * .025f, -high * .03f, .08f);
            a.controlA = Vector3.Lerp(a.windupOffset, a.impactOffset, .3f) + new Vector3(side * .01f, high * .012f, .003f);
            a.controlB = Vector3.Lerp(a.windupOffset, a.impactOffset, .75f) + new Vector3(-side * .007f, -high * .01f, .008f);
            a.windupEuler = new Vector3(-high * 25f, side * 20f, side * 8f);
            a.impactEuler = new Vector3(high * 14f, -side * 15f, -side * 8f);
            a.followEuler = new Vector3(high * 18f, -side * 18f, -side * 11f);
            GetTipLocals(out var positiveTip, out var negativeTip);
            float radius = Vector3.Distance(positiveTip, negativeTip) * .5f;
            Vector3 midpoint = (positiveTip + negativeTip) * .5f;
            Vector3 authoredCenter = staff ? transform.InverseTransformPoint(staff.transform.position) : new Vector3(0, 1.18f, .34f);
            Quaternion authoredRotation = staff ? Quaternion.Inverse(transform.rotation) * staff.transform.rotation : Quaternion.identity;
            Vector3 windupDirection = new Vector3(side * .95f, high * .95f, .12f - high * .2f).normalized;
            a.windupTipLocal = authoredCenter + authoredRotation * midpoint + a.windupOffset + windupDirection * radius;
            a.impactTipLocal = previewTargetLocal + new Vector3(0, 0, -.18f);
            a.impactApproachLocal = new Vector3(-side * .7f, -high * .7f, .65f).normalized;
            a.tipControlA = Vector3.Lerp(a.windupTipLocal, a.impactTipLocal, .25f) + new Vector3(side * .1f, high * .12f, -.03f);
            a.followTipLocal = a.impactTipLocal + a.impactApproachLocal * .3f;
            float drive = Mathf.Abs(side) < .2f ? (index == 0 ? .5f : -.5f) : side;
            a.windupYaw = -18f * drive; a.impactYaw = 22f * drive; a.followYaw = 25f * drive;
            a.windupLean = -8f * high; a.impactLean = 11f * high; a.followLean = 13f * high;
            a.weightShift = .04f * drive;
            return a;
        }
        void Awake()
        {
            if (!martialArts) martialArts = GetComponent<NpcMartialArts>();
            attributes = GetComponent<CharacterAttributes>();
            nextShowcaseDirection = Mathf.Clamp(firstAttackDirection, 0, 7);
            if (staff) EnsureTipMarkers();
            if (attackDefaultsVersion < 12) attacks = null;
            EnsureAttacks();
            attackDefaultsVersion = 12;
            if (!health) health = GetComponent<ScavengerHealth>();
            ragdoll = GetComponent<ScavengerRagdoll>();
            combatAudio = GetComponent<BanditCombatAudio>();
            if (!combatAudio) combatAudio = gameObject.AddComponent<BanditCombatAudio>();
            combatAudio.Initialize(this);
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (agent) agent.updateRotation = false;
            rigMotion = GetComponent<BanditRigMotion>();
            if (staff)
            {
                staff.Blocked += OnBlocked; staffBody = staff.GetComponent<Rigidbody>();
                var shape = staff.GetComponent<CapsuleCollider>();
                if (shape) staffHalfLength = shape.height * .5f;
            }
        }
        void Start()
        {
            CaptureGuard();
            nextAttack = Time.time + initialAttackDelay;
        }
        public void ResetAttackSchedule() => nextAttack = Time.time + initialAttackDelay;
        public void InterruptCombatForKnockout()
        {
            StopPreview();
            state = State.Stagger;
            if (agent && agent.enabled && agent.isOnNavMesh)
            { agent.ResetPath(); agent.isStopped = true; }
        }
        public void OnRecoveredFromKnockout()
        {
            state = State.Idle;
            ResetAttackSchedule();
        }
        void OnDestroy() { if (staff) staff.Blocked -= OnBlocked; if (previewDummy) Destroy(previewDummy); if (previewDummyMaterial) Destroy(previewDummyMaterial); }
        public void SetEquippedStaff(PhysicalMeleeWeapon value, Transform goal)
        {
            if (staff) staff.Blocked -= OnBlocked;
            staff = value; weaponGoal = goal; guardCaptured = false;
            staffBody = staff ? staff.GetComponent<Rigidbody>() : null;
            if (staff)
            {
                staff.Blocked += OnBlocked;
                positiveStrikingTip = staff.transform.Find("PositiveStrikingTip") ??
                    staff.transform.Find("Positive striking tip");
                negativeStrikingTip = staff.transform.Find("NegativeStrikingTip") ??
                    staff.transform.Find("Negative striking tip");
                var shape = staff.GetComponent<CapsuleCollider>();
                if (shape) staffHalfLength = shape.height * .5f;
            }
            if (rigMotion) rigMotion.staff = staff;
            CaptureGuard();
        }
        void CaptureGuard()
        {
            if (guardCaptured || !staff || !weaponGoal) return;
            guardLocalPosition = transform.InverseTransformPoint(staff.transform.position);
            guardLocalRotation = Quaternion.Inverse(transform.rotation) * staff.transform.rotation;
            weaponGoal.SetPositionAndRotation(staff.transform.position, staff.transform.rotation);
            guardCaptured = true;
        }
        public void CaptureGuardForPreview() => CaptureGuard();
        void OnBlocked(float impulse)
        {
            if (state != State.Strike) return;
            state = State.Stagger;
            until = Time.time + (impulse > 5f ? 1.1f : attacks[attackIndex].recoverySeconds);
        }
        void Update()
        {
            if (health && health.IsUnconscious || ragdoll && ragdoll.Active && (!health || !health.IsDead)) return;
            if (ragdoll && ragdoll.Active && (!health || !health.IsDead)) return;
            if (health && health.IsDead)
            {
                StopPreview(); state = State.Dead;
                if (agent && agent.enabled) { agent.isStopped = true; agent.enabled = false; }
                // Legacy embedded-staff prefabs keep their existing death behavior.
                // Shared-prefab NPCs release through NpcGeneratedWeapon's death event.
                if (!dropped && staff && !GetComponent<NpcGeneratedWeapon>())
                { staff.DropFromNpc(); dropped = true; }
                return;
            }
            CaptureGuard();
            if (!previewEnabled && previewActive) StopPreview();
            if (previewEnabled) { PreviewTick(); return; }
            if (guardAuthoringMode) { state = State.Circle; Move(false); return; }
            if (!target)
            {
                var body = FindFirstObjectByType<VRBodyController>();
                if (body && body.hmd) target = body.hmd;
                else { var camera = Camera.main; if (camera) target = camera.transform; }
            }
            if (target)
            {
                var targetHealth = target.GetComponentInParent<ScavengerHealth>();
                if (targetHealth && (targetHealth.IsDead || targetHealth.IsUnconscious))
                { state = State.Idle; Move(false); ReturnToGuard(); return; }
                var playerHealth = target.GetComponentInParent<ScavengerPlayerHealth>();
                if (playerHealth && playerHealth.IsDead)
                { state = State.Idle; Move(false); ReturnToGuard(); return; }
            }
            if (health && health.ShouldFlee && target)
            {
                state = State.Retreat;
                Vector3 away = Vector3.ProjectOnPlane(transform.position - target.position, Vector3.up).normalized;
                Move(true, transform.position + away * 4f);
                return;
            }
            if (!staff || !weaponGoal)
            {
                if (martialArts && martialArts.enabled) martialArts.TickCombat();
                else { state = State.Idle; Move(false); }
                return;
            }
            if (groupControlled && !groupEngaged)
            {
                state = State.Idle;
                if (agent && agent.enabled && agent.isOnNavMesh)
                {
                    agent.speed = groupWalkSpeed;
                    agent.isStopped = false;
                    if (!agent.hasPath || Vector3.Distance(agent.destination, groupDestination) > .4f)
                        agent.SetDestination(groupDestination);
                    Vector3 travel = Vector3.ProjectOnPlane(agent.desiredVelocity, Vector3.up);
                    if (travel.sqrMagnitude > .04f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(travel), 240f * Time.deltaTime);
                }
                ReturnToGuard();
                return;
            }
            if (!target) return;
            var direction = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            rangeToTarget = direction.magnitude;
            if (IsAttacking()) { Move(false); TickAttack(); return; }
            if (!groupControlled && rangeToTarget > detectRange) { state = State.Idle; Move(false); ReturnToGuard(); return; }
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 180f * Time.deltaTime);
            if (state == State.Dodge)
            {
                if (Time.time < dodgeUntil) { Move(true, dodgeDestination); ReturnToGuard(); return; }
                state = State.Circle;
                nextAttack = Mathf.Max(nextAttack, Time.time + .2f);
            }
            if (state == State.Block)
            {
                if (Time.time < blockUntil) { Move(false); ReturnToGuard(); return; }
                state = State.Circle;
                nextAttack = Mathf.Max(nextAttack, Time.time + .2f);
            }
            if (state == State.Parry)
            {
                if (Time.time < parryUntil) { Move(false); ReturnToGuard(); return; }
                state = State.Circle;
                nextAttack = Mathf.Max(nextAttack, Time.time + .2f);
            }
            var opponent = target.GetComponentInParent<StarvingBandit>();
            bool threatAttacking = opponent && opponent != this &&
                (opponent.state == State.Anticipate || opponent.state == State.Strike);
            bool threatStriking = opponent && opponent != this && opponent.state == State.Strike;
            bool newStrike = threatStriking && !threatWasStriking;
            threatWasStriking = threatStriking;
            bool newThreat = threatAttacking && !threatWasAttacking;
            threatWasAttacking = threatAttacking;
            if (parryIncomingNpcAttacks && newStrike && rangeToTarget < 2.5f &&
                Time.time >= nextParry && UnityEngine.Random.value < Mathf.Clamp01(parryChance *
                    CharacterCombatMath.DefensiveDecision(attributes)))
            {
                parryUntil = Time.time + .45f * CharacterCombatMath.DefensiveDecision(attributes);
                nextParry = Time.time + 1.5f / CharacterCombatMath.DefensiveDecision(attributes);
                state = State.Parry;
                Move(false);
                ReturnToGuard();
                return;
            }
            if (blockIncomingNpcAttacks && newThreat && rangeToTarget < 2.5f &&
                UnityEngine.Random.value < Mathf.Clamp01(blockChance *
                    CharacterCombatMath.DefensiveDecision(attributes)))
            {
                blockUntil = Time.time + .65f;
                state = State.Block;
                Move(false);
                ReturnToGuard();
                return;
            }
            if (dodgeIncomingNpcAttacks && newThreat && rangeToTarget < 2.5f &&
                Time.time >= nextDodge && UnityEngine.Random.value < Mathf.Clamp01(dodgeChance *
                    CharacterCombatMath.DefensiveDecision(attributes)))
            {
                float side = UnityEngine.Random.value < .5f ? -1f : 1f;
                var destination = transform.position + transform.right * (side * dodgeDistance) - transform.forward * .3f;
                if (NavMesh.SamplePosition(destination, out var dodgeHit, .8f, NavMesh.AllAreas))
                {
                    dodgeDestination = dodgeHit.position;
                    dodgeUntil = Time.time + .5f;
                    nextDodge = Time.time + dodgeCooldown;
                    state = State.Dodge;
                    Move(true, dodgeDestination);
                    ReturnToGuard();
                    return;
                }
            }
            if (state == State.Retreat)
            {
                float desiredRange = spacingRetreat ? preferredFightDistance : preferredFightDistance + .4f;
                if (Time.time < until && rangeToTarget < desiredRange)
                {
                    Vector3 away = direction.sqrMagnitude > .0001f ? -direction.normalized : -transform.forward;
                    if (spacingRetreat) MoveSpacingRetreat(away);
                    else Move(true, transform.position + away * Mathf.Max(1.5f, desiredRange - rangeToTarget + 1.3f));
                    ReturnToGuard();
                    return;
                }
                spacingRetreat = false;
                state = State.Circle;
            }
            if (Time.time < until) { Move(false); ReturnToGuard(); return; }
            if (health && health.Current < health.maxHealth * .25f && UnityEngine.Random.value < .003f)
            { state = State.Retreat; spacingRetreat = false; until = Time.time + 1.3f; Move(true, transform.position - direction.normalized * 2f); return; }
            if (rangeToTarget < minimumFightDistance)
            {
                state = State.Retreat;
                spacingRetreat = true;
                until = Time.time + postAttackRetreatSeconds;
                MoveSpacingRetreat(direction.sqrMagnitude > .0001f ? -direction.normalized : -transform.forward);
                ReturnToGuard();
                return;
            }
            float widestRange = attackRange;
            for (int i = 0; i < attacks.Length; i++) widestRange = Mathf.Max(widestRange, attacks[i].comfortableRange + attacks[i].maximumApproachStep);
            if (rangeToTarget > widestRange) { state = State.Approach; Move(true, target.position); ReturnToGuard(); return; }
            Move(false); state = State.Circle; ReturnToGuard();
            if (Time.time >= nextAttack)
            {
                int chosen = cycleAttackDirections ? nextShowcaseDirection : ChooseAttackIndex();
                var candidate = attacks[chosen];
                if (rangeToTarget > candidate.comfortableRange + candidate.maximumApproachStep)
                { state = State.Approach; Move(true, target.position); return; }
                StartAttack(chosen);
                if (cycleAttackDirections) nextShowcaseDirection = (nextShowcaseDirection + 1) & 7;
            }
        }
        bool IsAttacking() => state == State.Anticipate || state == State.Strike || state == State.FollowThrough || state == State.Recover;
        int ChooseAttackIndex()
        {
            int samples = attributes ? Mathf.Clamp(1 + Mathf.FloorToInt(
                attributes.Get(CharacterAttribute.MeleeAttack) / 30f), 1, 4) : 1;
            int chosen = UnityEngine.Random.Range(0, 8);
            float best = ImpactReachError(chosen);
            for (int i = 1; i < samples; i++)
            {
                int candidate = UnityEngine.Random.Range(0, 8);
                float error = ImpactReachError(candidate);
                if (error < best) { best = error; chosen = candidate; }
            }
            return chosen;
        }
        float ImpactReachError(int index) => target && attacks[index] != null ?
            Vector3.Distance(transform.TransformPoint(attacks[index].impactTipLocal),
                target.position + Vector3.up * 1.1f) : 0f;
        void StartAttack(int index)
        {
            attackIndex = Mathf.Clamp(index, 0, 7);
            var toTarget = target ? Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up) : transform.forward;
            frozenAimYaw = toTarget.sqrMagnitude > .001f ? Mathf.Clamp(Vector3.SignedAngle(transform.forward, toTarget, Vector3.up), -maximumAimYaw, maximumAimYaw) : 0;
            plannedStep = Mathf.Clamp(rangeToTarget - attacks[attackIndex].comfortableRange, 0, attacks[attackIndex].maximumApproachStep);
            appliedStep = 0;
            CurrentPhase = AttackPhase.Windup; AttackProgress = 0; state = State.Anticipate;
            until = Time.time + PhaseDuration(attacks[attackIndex], AttackPhase.Windup);
        }
        void TickAttack()
        {
            var a = attacks[attackIndex];
            float duration = PhaseDuration(a, CurrentPhase);
            AttackProgress = 1f - Mathf.Clamp01((until - Time.time) / duration);
            float stepFraction = CurrentPhase == AttackPhase.Windup ? .15f * Mathf.SmoothStep(0, 1, AttackProgress) :
                CurrentPhase == AttackPhase.Strike ? Mathf.Lerp(.15f, 1f, Mathf.SmoothStep(0, 1, AttackProgress)) : 1f;
            float deltaStep = plannedStep * stepFraction - appliedStep;
            if (!useAnimatedAttackDisplacement && deltaStep > .0001f)
            {
                if (agent && agent.enabled && agent.isOnNavMesh) agent.Move(transform.forward * deltaStep);
                else transform.position += transform.forward * deltaStep;
                appliedStep += deltaStep;
            }
            ApplyAttackPose(CurrentPhase, AttackProgress);
            if (Time.time < until) return;
            if (CurrentPhase == AttackPhase.Recovery)
            {
                state = State.Retreat;
                spacingRetreat = true;
                until = Time.time + postAttackRetreatSeconds;
                nextAttack = until + UnityEngine.Random.Range(.2f, .5f) *
                    CharacterCombatMath.AttackInterval(attributes,
                        staff ? staff.proficiencyClass : PhysicalMeleeWeapon.WeaponProficiencyClass.None);
                ReturnToGuard(); return;
            }
            CurrentPhase++;
            state = CurrentPhase switch { AttackPhase.Strike => State.Strike, AttackPhase.FollowThrough => State.FollowThrough, _ => State.Recover };
            if (CurrentPhase == AttackPhase.Strike) combatAudio?.PlayAttack();
            until = Time.time + PhaseDuration(a, CurrentPhase);
        }
        float PhaseDuration(StaffAttackDefinition a, AttackPhase p) => Mathf.Max(.05f, (p switch {
            AttackPhase.Windup => a.windupSeconds, AttackPhase.Strike => a.strikeSeconds,
            AttackPhase.FollowThrough => a.followSeconds, _ => a.recoverySeconds }) /
            AttackAnimationSpeed);
        void ReturnToGuard()
        {
            hasDesiredPose = false;
            if (!guardCaptured || !weaponGoal) return;
            float blend = 1f - Mathf.Exp(-Time.deltaTime * 7f);
            weaponGoal.localPosition = Vector3.Lerp(weaponGoal.localPosition, guardLocalPosition, blend);
            weaponGoal.localRotation = Quaternion.Slerp(weaponGoal.localRotation, guardLocalRotation, blend);
        }
        void ApplyAttackPose(AttackPhase phase, float progress)
        {
            if (!guardCaptured || !weaponGoal) return;
            GetTipLocals(out var positive, out var negative);
            EvaluatePose(attacks[attackIndex], phase, progress, guardLocalPosition, guardLocalRotation,
                positive, negative, out var offset, out var rotation, out _);
            float aimBlend = phase == AttackPhase.Windup ? Mathf.SmoothStep(0, 1, progress) :
                phase == AttackPhase.Recovery ? 1f - Mathf.SmoothStep(0, 1, progress) : 1f;
            rotation = Quaternion.Euler(0, frozenAimYaw * aimBlend, 0) * rotation;
            desiredStaffPosition = transform.TransformPoint(guardLocalPosition + offset);
            desiredStaffRotation = transform.rotation * guardLocalRotation * rotation;
            desiredTipPosition = desiredStaffPosition + desiredStaffRotation *
                (attacks[attackIndex].useNegativeTip ? negative : positive);
            hasDesiredPose = true;
            UpdateTravelReadout();
        }
        public void ResolveDesiredStaffPoseAfterBody()
        {
            if (!hasDesiredPose || !weaponGoal || !staff) return;
            Vector3 position = desiredStaffPosition;
            Quaternion rotation = desiredStaffRotation;
            ReachCorrectionReason = "None";
            float error;
            string reason;
            if (rigMotion) rigMotion.ConstrainStaffPose(ref position, ref rotation,
                transform.TransformPoint(guardLocalPosition), transform.rotation * guardLocalRotation,
                reachMargin, out error, out reason);
            else { error = 0f; reason = "No rig solver"; }
            ReachError = error;
            ReachCorrectionReason = reason;
            ReachCorrectionDistance = Vector3.Distance(desiredStaffPosition, position);
            ReachCorrectionAngle = Quaternion.Angle(desiredStaffRotation, rotation);
            weaponGoal.SetPositionAndRotation(position, rotation);
            if (previewEnabled && staffBody && staffBody.isKinematic)
                staff.transform.SetPositionAndRotation(position, rotation);
            if (previewEnabled)
            {
                if (trailDirection != attackIndex || trailPhase != previewPhase || previewProgress + .001f < trailProgress)
                    actualTipTrail.Clear();
                trailDirection = attackIndex; trailPhase = previewPhase; trailProgress = previewProgress;
                if (actualTipTrail.Count == 0 || Vector3.Distance(actualTipTrail[actualTipTrail.Count - 1], ActualTipPosition) > .005f)
                    actualTipTrail.Add(ActualTipPosition);
                if (actualTipTrail.Count > 96) actualTipTrail.RemoveAt(0);
            }
        }
        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }
        public static void EvaluatePose(StaffAttackDefinition a, AttackPhase phase, float progress,
            Vector3 guardPosition, Quaternion guardRotation, Vector3 positiveTipLocal, Vector3 negativeTipLocal,
            out Vector3 offset, out Quaternion rotation, out Vector3 requestedTip)
        {
            if (a.poseVersion > 0)
            {
                StaffCombatPose p = SampleCombatPose(a, phase, progress);
                Vector3 midpointLocal = (positiveTipLocal + negativeTipLocal) * .5f;
                Quaternion rootRotation = guardRotation * Quaternion.Euler(p.staffEuler);
                Vector3 rootPosition = guardPosition + guardRotation * midpointLocal + p.gripMidpointOffset - rootRotation * midpointLocal;
                offset = rootPosition - guardPosition;
                rotation = Quaternion.Inverse(guardRotation) * rootRotation;
                requestedTip = rootPosition + rootRotation * (a.useNegativeTip ? negativeTipLocal : positiveTipLocal);
                return;
            }
            float t = Mathf.Clamp01(progress);
            Vector3 midpoint = (positiveTipLocal + negativeTipLocal) * .5f;
            Vector3 tipLocal = a.useNegativeTip ? negativeTipLocal : positiveTipLocal;
            Vector3 localShaft = tipLocal - midpoint;
            Quaternion canonical = StaffAxisCalibration.CanonicalBasis(positiveTipLocal, negativeTipLocal);
            Vector3 guardTip = guardPosition + guardRotation * tipLocal;
            if ((phase == AttackPhase.Windup && t <= 0f) || (phase == AttackPhase.Recovery && t >= 1f))
            {
                offset = Vector3.zero;
                rotation = Quaternion.identity;
                requestedTip = guardTip;
                return;
            }
            switch (phase)
            {
                case AttackPhase.Windup:
                    t = a.windupEase.Evaluate(t); offset = Vector3.Lerp(Vector3.zero, a.windupOffset, t);
                    requestedTip = Vector3.Lerp(guardTip, a.windupTipLocal, t); break;
                case AttackPhase.Strike:
                    t = a.strikeEase.Evaluate(t); offset = Bezier(a.windupOffset, a.controlA, a.controlB, a.impactOffset, t);
                    Vector3 approach = a.impactApproachLocal.sqrMagnitude > .001f ? a.impactApproachLocal.normalized : Vector3.forward;
                    requestedTip = Bezier(a.windupTipLocal, a.tipControlA,
                        a.impactTipLocal - approach * a.approachDistance, a.impactTipLocal, t); break;
                case AttackPhase.FollowThrough:
                    t = a.followEase.Evaluate(t); offset = Vector3.Lerp(a.impactOffset, a.followOffset, t);
                    requestedTip = Vector3.Lerp(a.impactTipLocal, a.followTipLocal, t); break;
                default:
                    t = Mathf.SmoothStep(0, 1, t); offset = Vector3.Lerp(a.followOffset, Vector3.zero, t);
                    requestedTip = Vector3.Lerp(a.followTipLocal, guardTip, t); break;
            }
            Vector3 desiredMidpoint = guardPosition + guardRotation * midpoint + offset;
            Vector3 wantedShaft = requestedTip - desiredMidpoint;
            Quaternion delta = wantedShaft.sqrMagnitude > .0001f
                ? Quaternion.FromToRotation(guardRotation * localShaft, wantedShaft.normalized)
                : Quaternion.identity;
            Quaternion desiredCanonical = delta * guardRotation * canonical;
            Quaternion desiredRoot = desiredCanonical * Quaternion.Inverse(canonical);
            Vector3 desiredRootPosition = desiredMidpoint - desiredRoot * midpoint;
            offset = desiredRootPosition - guardPosition;
            rotation = Quaternion.Inverse(guardRotation) * desiredRoot;
            requestedTip = desiredRootPosition + desiredRoot * tipLocal;
        }
        static StaffCombatPose BlendPose(StaffCombatPose x, StaffCombatPose y, float t)
        {
            t = Mathf.Clamp01(t);
            return new StaffCombatPose {
                gripMidpointOffset = Vector3.Lerp(x.gripMidpointOffset, y.gripMidpointOffset, t),
                staffEuler = Quaternion.Slerp(Quaternion.Euler(x.staffEuler), Quaternion.Euler(y.staffEuler), t).eulerAngles,
                hipYaw = Mathf.Lerp(x.hipYaw, y.hipYaw, t), torsoYaw = Mathf.Lerp(x.torsoYaw, y.torsoYaw, t),
                torsoPitch = Mathf.Lerp(x.torsoPitch, y.torsoPitch, t), shoulderYaw = Mathf.Lerp(x.shoulderYaw, y.shoulderYaw, t),
                shoulderLift = Mathf.Lerp(x.shoulderLift, y.shoulderLift, t), forwardLunge = Mathf.Lerp(x.forwardLunge, y.forwardLunge, t),
                weightShift = Mathf.Lerp(x.weightShift, y.weightShift, t),
                leftElbowHint = Vector3.Lerp(x.leftElbowHint, y.leftElbowHint, t),
                rightElbowHint = Vector3.Lerp(x.rightElbowHint, y.rightElbowHint, t),
                desiredTipApproach = Vector3.Slerp(x.desiredTipApproach, y.desiredTipApproach, t)
            };
        }
        public static StaffCombatPose SampleCombatPose(StaffAttackDefinition a, AttackPhase phase, float progress)
        {
            float t = Mathf.Clamp01(progress);
            if (phase == AttackPhase.Windup) return BlendPose(a.guardPose, a.windupPose, a.windupEase.Evaluate(t));
            if (phase == AttackPhase.Strike)
            {
                t = a.strikeEase.Evaluate(t);
                float split = Mathf.Clamp(a.earlyStrikeTime, .15f, .65f);
                return t < split ? BlendPose(a.windupPose, a.earlyStrikePose, Mathf.SmoothStep(0, 1, t / split)) :
                    BlendPose(a.earlyStrikePose, a.impactPose, Mathf.SmoothStep(0, 1, (t - split) / (1 - split)));
            }
            if (phase == AttackPhase.FollowThrough) return BlendPose(a.impactPose, a.followPose, a.followEase.Evaluate(t));
            return t < .5f ? BlendPose(a.followPose, a.recoveryPose, Mathf.SmoothStep(0, 1, t * 2)) :
                BlendPose(a.recoveryPose, a.guardPose, Mathf.SmoothStep(0, 1, (t - .5f) * 2));
        }
        public StaffCombatPose CurrentCombatPose => (IsAttacking() || previewEnabled) && attacks != null && attacks.Length > attackIndex
            ? SampleCombatPose(attacks[attackIndex], CurrentPhase, AttackProgress) : new StaffCombatPose();
        void UpdateTravelReadout()
        {
            if (!guardCaptured || attacks == null || attacks[attackIndex] == null) return;
            var a = attacks[attackIndex];
            GetTipLocals(out var positive, out var negative);
            Vector3 tipLocal = StrikingTipLocal(a.useNegativeTip);
            EvaluatePose(a, AttackPhase.Windup, 1, guardLocalPosition, guardLocalRotation, positive, negative,
                out var startOffset, out var startRotation, out _);
            Quaternion start = guardLocalRotation * startRotation;
            Vector3 firstTip = guardLocalPosition + startOffset + start * tipLocal;
            DesiredAngularTravel = DesiredTipTravel = 0f;
            Vector3 previousTip = firstTip;
            foreach (AttackPhase phase in new[] { AttackPhase.Strike, AttackPhase.FollowThrough })
                for (int sample = 1; sample <= 20; sample++)
                {
                    EvaluatePose(a, phase, sample / 20f, guardLocalPosition, guardLocalRotation, positive, negative,
                        out var offset, out var relative, out _);
                    Quaternion orientation = guardLocalRotation * relative;
                    Vector3 tip = guardLocalPosition + offset + orientation * tipLocal;
                    DesiredAngularTravel = Mathf.Max(DesiredAngularTravel, Quaternion.Angle(start, orientation));
                    DesiredTipTravel += Vector3.Distance(previousTip, tip);
                    previousTip = tip;
                }
            AchievedAngularTravel = Quaternion.Angle(start, Quaternion.Inverse(transform.rotation) * staff.transform.rotation);
            AchievedTipTravel = Vector3.Distance(transform.TransformPoint(firstTip), ActualTipPosition);
        }
        public void GetBodyPose(out float yaw, out float lean, out float shift)
        {
            if (attacks != null && attacks.Length > attackIndex && attacks[attackIndex].poseVersion > 0)
            {
                var pose = CurrentCombatPose;
                yaw = pose.torsoYaw; lean = pose.torsoPitch; shift = pose.weightShift;
                return;
            }
            yaw = lean = shift = 0;
            if (!IsAttacking() && !previewEnabled) return;
            var a = attacks[attackIndex]; float t = Mathf.SmoothStep(0, 1, AttackProgress);
            switch (CurrentPhase)
            {
                case AttackPhase.Windup: yaw = a.windupYaw * t; lean = a.windupLean * t; shift = -a.weightShift * t; break;
                case AttackPhase.Strike: yaw = Mathf.Lerp(a.windupYaw, a.impactYaw, t); lean = Mathf.Lerp(a.windupLean, a.impactLean, t); shift = Mathf.Lerp(-a.weightShift, a.weightShift, t); break;
                case AttackPhase.FollowThrough: yaw = Mathf.Lerp(a.impactYaw, a.followYaw, t); lean = Mathf.Lerp(a.impactLean, a.followLean, t); shift = a.weightShift; break;
                default: yaw = Mathf.Lerp(a.followYaw, 0, t); lean = Mathf.Lerp(a.followLean, 0, t); shift = Mathf.Lerp(a.weightShift, 0, t); break;
            }
        }
        void PreviewTick()
        {
            Move(false);
            EnsurePreviewDummy();
            if (!staffBody || !staff) return;
            if (!previewActive) { previewBodyWasKinematic = staffBody.isKinematic; previewActive = true; staffBody.isKinematic = true; previewRootPosition = transform.position; }
            attackIndex = Mathf.Clamp(previewDirection, 0, 7);
            rangeToTarget = previewTargetLocal.z;
            plannedStep = Mathf.Clamp(rangeToTarget - attacks[attackIndex].comfortableRange, 0, attacks[attackIndex].maximumApproachStep);
            float fraction = previewPhase == AttackPhase.Windup ? .15f * Mathf.SmoothStep(0, 1, previewProgress) :
                previewPhase == AttackPhase.Strike ? Mathf.Lerp(.15f, 1f, Mathf.SmoothStep(0, 1, previewProgress)) : 1f;
            if (previewApplyRootStep) transform.position = previewRootPosition + transform.forward * plannedStep * fraction;
            else transform.position = previewRootPosition;
            CurrentPhase = previewPhase; AttackProgress = previewProgress; frozenAimYaw = 0;
            state = previewPhase switch { AttackPhase.Windup => State.Anticipate, AttackPhase.Strike => State.Strike,
                AttackPhase.FollowThrough => State.FollowThrough, _ => State.Recover };
            ApplyAttackPose(previewPhase, previewProgress);
        }
        void EnsurePreviewDummy()
        {
            if (!previewDummy)
            {
                previewDummy = new GameObject("Attack Preview Target (runtime only)");
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) shader = Shader.Find("Standard");
                previewDummyMaterial = shader ? new Material(shader) { color = new Color(.9f, .22f, .13f, .8f) } : null;
                CreateDummyPart("Chest", PrimitiveType.Capsule, new Vector3(0, -.12f, 0), new Vector3(.42f, .65f, .42f), previewDummyMaterial);
                CreateDummyPart("Head", PrimitiveType.Sphere, new Vector3(0, .38f, 0), new Vector3(.28f, .28f, .28f), previewDummyMaterial);
                CreateDummyPart("Impact marker", PrimitiveType.Sphere, new Vector3(0, 0, -.18f), Vector3.one * .09f, previewDummyMaterial);
            }
            previewDummy.transform.SetPositionAndRotation((previewActive ? previewRootPosition : transform.position) + transform.rotation * previewTargetLocal, transform.rotation);
            previewDummy.SetActive(true);
        }
        void CreateDummyPart(string name, PrimitiveType shape, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(previewDummy.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var collider = part.GetComponent<Collider>();
            if (collider) Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            if (renderer && material) renderer.sharedMaterial = material;
        }
        public void StopPreview()
        {
            if (previewDummy) previewDummy.SetActive(false);
            actualTipTrail.Clear();
            if (!previewActive) return;
            hasDesiredPose = false;
            previewEnabled = false;
            previewActive = false;
            transform.position = previewRootPosition;
            if (staffBody) staffBody.isKinematic = previewBodyWasKinematic;
            if (guardCaptured && staff)
            {
                staff.transform.SetPositionAndRotation(transform.TransformPoint(guardLocalPosition), transform.rotation * guardLocalRotation);
                weaponGoal.SetPositionAndRotation(staff.transform.position, staff.transform.rotation);
            }
            if (rigMotion) rigMotion.RestoreAuthoredGuard();
            state = State.Circle;
        }
        void Move(bool moving, Vector3 destination = default)
        {
            if (!agent || !agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = !moving;
            if (moving)
            {
                agent.speed = state == State.Retreat ? (spacingRetreat ? spacingRetreatSpeed : runSpeed) :
                    state == State.Dodge || rangeToTarget > runUntilRange ? runSpeed : chasseSpeed;
                destination.y = transform.position.y; agent.SetDestination(destination);
            }
        }
        void MoveSpacingRetreat(Vector3 away)
        {
            if (agent && agent.enabled && agent.isOnNavMesh)
            {
                // SetDestination stopped short here because the agent's normal
                // stopping distance (1.3 m) exceeded the short retreat path.
                agent.ResetPath();
                agent.isStopped = false;
                agent.Move(away * spacingRetreatSpeed * Time.deltaTime);
            }
            else transform.position += away * spacingRetreatSpeed * Time.deltaTime;
        }
        void OnDrawGizmosSelected()
        {
            if (!showTrajectory || !staff || attacks == null || attacks.Length != 8) return;
            int index = Application.isPlaying ? (previewEnabled ? previewDirection : attackIndex) : Mathf.Clamp(previewDirection, 0, 7);
            var a = attacks[index]; if (a == null) return;
            Vector3 origin = Application.isPlaying && guardCaptured ? guardLocalPosition : transform.InverseTransformPoint(staff.transform.position);
            Quaternion baseRotation = Application.isPlaying && guardCaptured ? transform.rotation * guardLocalRotation : staff.transform.rotation;
            Vector3 previous = transform.TransformPoint(origin);
            Vector3 previousOpposite = previous + baseRotation * StrikingTipLocal(!a.useNegativeTip);
            Vector3 previousStrike = previous + baseRotation * StrikingTipLocal(a.useNegativeTip);
            Vector3 actualPositive = staff.transform.TransformPoint(StrikingTipLocal(false));
            Vector3 actualNegative = staff.transform.TransformPoint(StrikingTipLocal(true));
            Gizmos.color = Color.white;
            Gizmos.DrawLine(actualNegative, actualPositive);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(actualPositive, .035f);
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(actualNegative, .035f);
            foreach (AttackPhase phase in Enum.GetValues(typeof(AttackPhase)))
                for (int i = 0; i <= 16; i++)
                {
                    EvaluatePose(a, phase, i / 16f, origin, Quaternion.Inverse(transform.rotation) * baseRotation,
                        StrikingTipLocal(false), StrikingTipLocal(true), out var offset, out var rotation, out _);
                    Vector3 point = transform.TransformPoint(origin + offset);
                    Quaternion poseRotation = baseRotation * rotation;
                    Vector3 opposite = point + poseRotation * StrikingTipLocal(!a.useNegativeTip);
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(previous, point); previous = point;
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(previousOpposite, opposite); previousOpposite = opposite;
                    Gizmos.color = new Color(1f, .4f, .1f);
                    Vector3 strike = point + poseRotation * StrikingTipLocal(a.useNegativeTip);
                    Gizmos.DrawLine(previousStrike, strike); previousStrike = strike;
                }
            if (Application.isPlaying && hasDesiredPose)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(desiredTipPosition, .05f);
                Gizmos.DrawLine(desiredStaffPosition + desiredStaffRotation * StrikingTipLocal(true),
                    desiredStaffPosition + desiredStaffRotation * StrikingTipLocal(false));
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(ActualTipPosition, .05f);
                Gizmos.DrawLine(desiredTipPosition, ActualTipPosition);
                Gizmos.DrawSphere(staff.transform.position, .025f);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.TransformPoint(a.impactTipLocal), .12f);
                Gizmos.color = Color.green;
                for (int i = 1; i < actualTipTrail.Count; i++)
                    Gizmos.DrawLine(actualTipTrail[i - 1], actualTipTrail[i]);
            }
            if (rigMotion) rigMotion.DrawGripGizmos();
            if (showRangeGizmos)
            {
                Vector3 center = transform.position + Vector3.up * .025f;
                Gizmos.color = Color.green; Gizmos.DrawWireSphere(center, a.comfortableRange);
                Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(center, a.comfortableRange + a.maximumApproachStep);
                Gizmos.color = Color.red; Gizmos.DrawLine(center, center + transform.forward * Mathf.Max(0, previewTargetLocal.z - a.comfortableRange));
                Gizmos.DrawWireSphere(transform.TransformPoint(previewTargetLocal + new Vector3(0, 0, -.18f)), .09f);
            }
        }
    }
}
