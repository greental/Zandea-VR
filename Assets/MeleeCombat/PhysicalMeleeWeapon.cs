using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using JunkerBody;
using JunkerEquipment;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    // The visible weapon is always dynamic. A kinematic, collider-free target is driven by
    // the hand or animation; a finite-strength joint lets world contacts arrest the blade.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicalMeleeWeapon : MonoBehaviour
    {
        public enum HoldState { World, HeldByPlayer, HeldByNpc }
        public enum PhysicsPreset { Custom, LightSword, HeavySword, BoStaff, HeavyPolearm }
        public enum WeaponProficiencyClass { None, Staff }
        [Header("Shared ownership")]
        public string weaponId = "bo-staff";
        public WeaponProficiencyClass proficiencyClass;
        [SerializeField] HoldState currentState;
        public HoldState State { get => currentState; private set {
            currentState = value;
            if (groundSupport && useMeshContactShape) groundSupport.enabled = value == HoldState.World;
        } }
        [Tooltip("Use the fitted mesh colliders while held; the broad loot support box is only for dropped weapons.")]
        public bool useMeshContactShape;
        BoxCollider groundSupport;
        public Component Owner { get; private set; }
        public Component Reservation { get; private set; }
        float reservationUntil;
        public static readonly HashSet<PhysicalMeleeWeapon> Active = new();
        [Header("Authored player grips")]
        public Transform rightGrip, leftGrip, secondaryGrip;
        [Tooltip("Optional handed secondary markers. The second selector uses these regardless of which hand selected first.")]
        public Transform secondaryRightGrip, secondaryLeftGrip;
        [Tooltip("Player-only roll around the measured weapon length when using the NPC grip as the pickup pose.")]
        public float npcPickupRoll = 180f;
        [Tooltip("Fixed right-hand blade angle relative to the controller. Edit this or the Right Grip marker in the Inspector.")]
        public Vector3 rightGripEuler;
        [Tooltip("Right-hand grip position offset in controller space (metres).")]
        public Vector3 rightGripPosition;
        [Tooltip("Fixed left-hand blade angle relative to the controller. Edit this or the Left Grip marker in the Inspector.")]
        public Vector3 leftGripEuler;
        [Tooltip("Left-hand grip position offset in controller space (metres).")]
        public Vector3 leftGripPosition;
        [Min(0)] public float maximumTargetSpeed = 20f;
        [Min(0)] public float maximumTargetAngularSpeed = 60f;
        [Range(0, 1)] public float locomotionCarry = .75f;
        [Min(0)] public float maximumLinearVelocity = 12f;
        [Min(0)] public float maximumAngularVelocity = 35f;
        [Min(0)] public float formerOwnerCollisionDelay = .4f;
        [Header("Starting physics preset")]
        public PhysicsPreset preset = PhysicsPreset.Custom;
        [ContextMenu("Apply selected physics preset")]
        public void ApplySelectedPreset()
        {
            switch (preset)
            {
                case PhysicsPreset.LightSword:
                    mass = 1.1f; positionSpring = 1900f; positionDamper = 100f;
                    rotationSpring = 950f; rotationDamper = 60f; maximumDriveForce = 1100f;
                    maximumLinearVelocity = 14f; maximumAngularVelocity = 38f; break;
                case PhysicsPreset.HeavySword:
                    mass = 3.2f; positionSpring = 1800f; positionDamper = 140f;
                    rotationSpring = 850f; rotationDamper = 85f; maximumDriveForce = 1300f;
                    maximumLinearVelocity = 10f; maximumAngularVelocity = 25f; break;
                case PhysicsPreset.BoStaff:
                    mass = 2.1f; positionSpring = 1800f; positionDamper = 105f;
                    rotationSpring = 850f; rotationDamper = 65f; maximumDriveForce = 1200f;
                    maximumLinearVelocity = 12f; maximumAngularVelocity = 35f; break;
                case PhysicsPreset.HeavyPolearm:
                    mass = 5f; positionSpring = 2100f; positionDamper = 190f;
                    rotationSpring = 1050f; rotationDamper = 110f; maximumDriveForce = 1800f;
                    maximumLinearVelocity = 9f; maximumAngularVelocity = 20f; break;
            }
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody) rigidbody.mass = mass;
        }
        [SerializeField] float positionErrorMeters, rotationErrorDegrees;
        public float PositionError => positionErrorMeters;
        public float RotationError => rotationErrorDegrees;
        public int WeaponContactEntries { get; private set; }
        public float MaximumWeaponContactSpeed { get; private set; }
        public float MaximumWeaponContactImpulse { get; private set; }
        public enum StrikeType { Blunt, Cutting, Piercing }
        public StrikeType strikeType;
        [Tooltip("Local direction perpendicular to a cutting edge. Used only for sever checks.")]
        public Vector3 bladeEdgeLocalAxis = Vector3.right;
        public Transform grip;
        [Header("Combat reach")]
        [Tooltip("Grip-to-striking-tip reach baked into this prefab in the editor, in metres.")]
        [Min(.1f)] public float combatReachMeters = .8f;
        public float CombatReach => combatReachMeters > 0f ? combatReachMeters : .8f;
        public Transform npcGripTarget;
        public Transform ownerRoot;
        [Min(.01f)] public float mass = 1.4f;
        [Min(0)] public float positionSpring = 1800f, positionDamper = 105f;
        [Min(0)] public float rotationSpring = 850f, rotationDamper = 65f;
        [Min(0)] public float maximumDriveForce = 1200f;
        [Header("Attribute handling")]
        public bool useAttributesForNpcHandling = true;
        public bool useAttributesForPlayerHandling;
        [SerializeField] float activeControlMultiplier = 1f;
        public float ActiveControlMultiplier => activeControlMultiplier;
        [Min(0)] public float minimumImpactSpeed = 1.3f;
        [Min(0)] public float damagePerJoule = .15f;
        [Min(0)] public float damageCooldown = .2f;
        [Min(0)] public float blockedImpulseThreshold = 2.5f;
        [Min(0)] public float npcGripSlack = .055f;
        [Range(1, 45)] public float npcAngularSlack = 12f;
        public System.Action<float> Blocked;

        Rigidbody body, targetBody;
        ConfigurableJoint joint;
        XRGrabInteractable grab;
        public XRGrabInteractable Grab => grab;
        CharacterAttributes attributeSource;
        IXRSelectInteractor hand;
        IXRSelectInteractor secondaryHand;
        VRBodyController playerBody;
        Transform selectedGrip;
        Transform handPoseGrip;
        Transform secondaryPoseGrip;
        Transform selectedSecondaryGrip;
        Quaternion selectedGripOffset = Quaternion.identity;
        Vector3 selectedGripLocalPosition;
        Quaternion selectedGripLocalRotation = Quaternion.identity;
        Vector3 selectedHandOffset;
        Vector3 previousTargetPosition;
        Quaternion previousTargetRotation;
        bool targetInitialized;
        Vector3 previousOwnerPosition;
        Quaternion previousOwnerRotation;
        bool ownerPoseInitialized;
        Vector3 previousRootVelocity, previousRootAngularVelocity;
        bool deferCollisionRestore;
        bool oldTrackPosition, oldTrackRotation;
        bool droppedByNpc;
        float lastPlayerPickupAt = -100f;
        Vector3 previousNpcBodyPosition;
        Quaternion previousNpcBodyRotation;
        bool hasPreviousNpcBodyPose;
        bool externalContact;
        JunkerEquipmentItem equipmentItem;
        JunkerEquipmentRig pendingHolsterRig;
        readonly Dictionary<Collider, float> lastHit = new();
        readonly List<Collider> ignored = new();

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            var groundShape = GroundLootCollider.Ensure(gameObject);
            groundSupport = groundShape;
            if (groundSupport && useMeshContactShape) groundSupport.enabled = State == HoldState.World;
            if (useMeshContactShape)
                foreach (var shape in GetComponentsInChildren<Collider>())
                    if (shape != groundSupport && !(shape is MeshCollider) && !shape.isTrigger) shape.enabled = false;
            // Older generated bandit variants serialized the physical weapon and
            // collider but omitted the grab component entirely.
            if (!grab) grab = gameObject.AddComponent<XRGrabInteractable>();
            equipmentItem = GetComponent<JunkerEquipmentItem>();
            if (!equipmentItem) equipmentItem = gameObject.AddComponent<JunkerEquipmentItem>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 35f;
            if (grab)
            {
                EnsureSecondaryGrip();
                if (grip) grab.attachTransform = grip;
                if (grab.colliders.Count == 0)
                    foreach (var shape in GetComponentsInChildren<Collider>())
                        if (shape && !shape.isTrigger) grab.colliders.Add(shape);
                if (groundShape && !grab.colliders.Contains(groundShape)) grab.colliders.Add(groundShape);
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.useDynamicAttach = false;
                grab.selectMode = secondaryGrip ? InteractableSelectMode.Multiple : InteractableSelectMode.Single;
                grab.throwOnDetach = false;
                grab.trackPosition = grab.trackRotation = false;
                grab.selectEntered.AddListener(Selected);
                grab.selectExited.AddListener(Released);
                var collisionGuard = GetComponent<HeldLootCollisionGuard>();
                if (!collisionGuard) collisionGuard = gameObject.AddComponent<HeldLootCollisionGuard>();
                collisionGuard.Bind(grab);
                // Melee weapons participate in the same body backpack inventory as tools.
                // Add after our release listener so Drop runs before storage snapshots physics.
                if (!TryGetComponent<VRInventoryItem>(out _)) gameObject.AddComponent<VRInventoryItem>();
            }
        }

        void OnEnable() => Active.Add(this);
        void OnDisable()
        {
            pendingHolsterRig = null;
            Active.Remove(this);
            if (State != HoldState.World && (!ownerRoot || ownerRoot.gameObject.activeInHierarchy))
            {
                var delay = formerOwnerCollisionDelay;
                formerOwnerCollisionDelay = 0;
                Drop();
                formerOwnerCollisionDelay = delay;
            }
            ReleaseReservation(Reservation);
        }

        void Start()
        {
            if (npcGripTarget && ownerRoot) TryEquipNpc(ownerRoot.GetComponent<MonoBehaviour>(), npcGripTarget);
        }
        void OnDestroy()
        {
            if (grab) { grab.selectEntered.RemoveListener(Selected); grab.selectExited.RemoveListener(Released); }
            Detach();
        }
        void Selected(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            if (State == HoldState.HeldByNpc) return;
            if (State == HoldState.HeldByPlayer && secondaryGrip && args.interactorObject != hand)
            {
                secondaryHand = args.interactorObject;
                var fitSource = playerBody ? playerBody.GetComponentInParent<PlayerWeaponGripAuthoring>() : null;
                if (fitSource && fitSource.TryGet(weaponId, out var fit))
                {
                    secondaryGrip.localPosition = fit.secondaryPosition;
                    secondaryGrip.localRotation = Quaternion.Euler(fit.secondaryEuler);
                }
                bool leftSecondary = IsLeftHand(secondaryHand);
                selectedSecondaryGrip = leftSecondary
                    ? (secondaryLeftGrip ? secondaryLeftGrip : secondaryGrip)
                    : (secondaryRightGrip ? secondaryRightGrip : secondaryGrip);
                secondaryPoseGrip = new GameObject("Active secondary hand pose").transform;
                secondaryPoseGrip.SetParent(selectedSecondaryGrip, false);
                if (playerBody) playerBody.SetMeleeGrip(leftSecondary, secondaryPoseGrip);
                return;
            }
            if (equipmentItem && equipmentItem.currentSlot)
                equipmentItem.currentSlot.ReleaseItem(equipmentItem);
            pendingHolsterRig = null;
            Reservation = null;
            droppedByNpc = false;
            hand = args.interactorObject;
            playerBody = hand.transform.GetComponentInParent<VRBodyController>();
            if (!playerBody) playerBody = FindFirstObjectByType<VRBodyController>();
            var authoredFits = playerBody ? playerBody.GetComponentInParent<PlayerWeaponGripAuthoring>() : null;
            if (authoredFits && authoredFits.TryGet(weaponId, out var authoredFit))
            {
                rightGripPosition = authoredFit.rightPosition; rightGripEuler = authoredFit.rightEuler;
                leftGripPosition = authoredFit.leftPosition; leftGripEuler = authoredFit.leftEuler;
            }
            bool leftHand = IsLeftHand(hand);
            bool rightHand = playerBody && playerBody.rightController &&
                (hand.transform == playerBody.rightController || hand.transform.IsChildOf(playerBody.rightController));
            if (!leftHand && !rightHand) leftHand = hand.handedness == InteractorHandedness.Left;
            ownerRoot = playerBody ? playerBody.transform.root : hand.transform.root;
            Owner = hand.transform.GetComponentInParent<MonoBehaviour>();
            foreach (var actor in hand.transform.GetComponentsInParent<MonoBehaviour>())
                if (actor is IDisarmCombatant) { Owner = actor; break; }
            State = HoldState.HeldByPlayer;
            lastPlayerPickupAt = Time.time;
            selectedGrip = leftHand ? (leftGrip ? leftGrip : grip) : (rightGrip ? rightGrip : grip);
            if (!selectedGrip) selectedGrip = transform;
            selectedGripOffset = Quaternion.Euler(leftHand ? leftGripEuler : rightGripEuler);
            selectedHandOffset = leftHand ? leftGripPosition : rightGripPosition;
            selectedGripLocalPosition = transform.InverseTransformPoint(selectedGrip.position);
            selectedGripLocalRotation = Quaternion.Inverse(transform.rotation) * selectedGrip.rotation;
            CorrectNpcPickupRoll();
            if (handPoseGrip) Destroy(handPoseGrip.gameObject);
            handPoseGrip = new GameObject("Active hand pose").transform;
            handPoseGrip.SetParent(selectedGrip, false);
            handPoseGrip.localRotation = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * selectedGrip.rotation) * selectedGripLocalRotation * selectedGripOffset;
            oldTrackPosition = grab.trackPosition;
            oldTrackRotation = grab.trackRotation;
            grab.trackPosition = grab.trackRotation = false;
            if (playerBody)
            {
                playerBody.ClearMeleeGrip(handPoseGrip);
                playerBody.SetMeleeGrip(leftHand, handPoseGrip);
            }
            if (equipmentItem) equipmentItem.rig = FindPlayerEquipmentRig();
            ownerPoseInitialized = false;
            previousRootVelocity = previousRootAngularVelocity = Vector3.zero;
            externalContact = false;
            if (transform.parent) transform.SetParent(null, true);
            // XRI may have cached a kinematic state from storage.
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Extrapolate;
            var handPose = hand.GetAttachTransform(grab);
            if (handPose)
            {
                Quaternion heldRotation = handPose.rotation * Quaternion.Inverse(selectedGripLocalRotation * selectedGripOffset);
                Vector3 heldGrip = handPose.position + handPose.rotation * selectedHandOffset;
                transform.SetPositionAndRotation(heldGrip - heldRotation * selectedGripLocalPosition, heldRotation);
                body.linearVelocity = body.angularVelocity = Vector3.zero;
            }
            Attach(handPose);
        }
        void Released(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (args.interactorObject == secondaryHand)
            {
                ClearSecondaryGrip();
                return;
            }
            if (State == HoldState.HeldByPlayer)
            {
                if (secondaryHand != null)
                {
                    var promoted = secondaryHand;
                    var promotedGrip = selectedSecondaryGrip;
                    ClearSecondaryGrip();
                    Drop();
                    BeginPromotedHold(promoted, promotedGrip);
                    return;
                }
                var rig = FindPlayerEquipmentRig();
                Drop();
                if (equipmentItem) equipmentItem.rig = rig;
                pendingHolsterRig = rig;
            }
            grab.trackPosition = oldTrackPosition;
            grab.trackRotation = oldTrackRotation;
        }
        void ClearSecondaryGrip()
        {
            if (playerBody && secondaryPoseGrip) playerBody.ClearMeleeGrip(secondaryPoseGrip);
            if (secondaryPoseGrip) Destroy(secondaryPoseGrip.gameObject);
            secondaryPoseGrip = null;
            selectedSecondaryGrip = null;
            secondaryHand = null;
        }

        void BeginPromotedHold(IXRSelectInteractor promoted, Transform promotedGrip)
        {
            hand = promoted;
            playerBody = hand.transform.GetComponentInParent<VRBodyController>();
            if (!playerBody) playerBody = FindFirstObjectByType<VRBodyController>();
            bool left = IsLeftHand(hand);
            ownerRoot = playerBody ? playerBody.transform.root : hand.transform.root;
            Owner = hand.transform.GetComponentInParent<MonoBehaviour>();
            State = HoldState.HeldByPlayer;
            // Keep the remaining hand at the point it was already holding. Moving
            // it to the primary marker causes a visible snap when the first hand releases.
            selectedGrip = promotedGrip ? promotedGrip : left ? (leftGrip ? leftGrip : grip) : (rightGrip ? rightGrip : grip);
            if (!selectedGrip) selectedGrip = transform;
            bool keptSecondaryRole = promotedGrip != null;
            selectedGripOffset = keptSecondaryRole ? Quaternion.identity : Quaternion.Euler(left ? leftGripEuler : rightGripEuler);
            selectedHandOffset = keptSecondaryRole ? Vector3.zero : left ? leftGripPosition : rightGripPosition;
            selectedGripLocalPosition = transform.InverseTransformPoint(selectedGrip.position);
            selectedGripLocalRotation = Quaternion.Inverse(transform.rotation) * selectedGrip.rotation;
            CorrectNpcPickupRoll();
            handPoseGrip = new GameObject("Active hand pose").transform;
            handPoseGrip.SetParent(selectedGrip, false);
            handPoseGrip.localRotation = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * selectedGrip.rotation) * selectedGripLocalRotation * selectedGripOffset;
            if (playerBody) playerBody.SetMeleeGrip(left, handPoseGrip);
            body.interpolation = RigidbodyInterpolation.Extrapolate;
            Attach(hand.GetAttachTransform(grab));
        }
        void LateUpdate()
        {
            if (!pendingHolsterRig) return;
            var rig = pendingHolsterRig;
            pendingHolsterRig = null;
            if (equipmentItem && State == HoldState.World) rig.TryHolster(equipmentItem);
        }
        JunkerEquipmentRig FindPlayerEquipmentRig()
        {
            var rig = playerBody ? playerBody.GetComponentInChildren<JunkerEquipmentRig>(true) : null;
            if (!rig && playerBody) rig = playerBody.GetComponentInParent<JunkerEquipmentRig>();
            if (!rig && playerBody)
                foreach (var candidate in FindObjectsByType<JunkerEquipmentRig>(FindObjectsSortMode.None))
                    if (candidate.body == playerBody) { rig = candidate; break; }
            return rig;
        }
        public bool TryReserve(Component npc, float seconds = 5f)
        {
            if (!npc || State != HoldState.World) return false;
            if (Reservation && Reservation != npc && Time.time < reservationUntil) return false;
            Reservation = npc; reservationUntil = Time.time + Mathf.Max(.1f, seconds); return true;
        }
        public void ReleaseReservation(Component npc)
        {
            if (Reservation == npc) { Reservation = null; reservationUntil = 0; }
        }
        public bool TryEquipNpc(Component npc, Transform target)
        {
            if (!npc || !target || State != HoldState.World || (grab && grab.isSelected)) return false;
            if (Reservation && Reservation != npc && Time.time < reservationUntil) return false;
            Reservation = null; Owner = npc; State = HoldState.HeldByNpc;
            ownerRoot = npc.transform; npcGripTarget = target; hand = null;
            selectedGrip = useMeshContactShape ? grip : null;
            if (grab) grab.enabled = false;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            body.isKinematic = false; body.useGravity = false;
            if (selectedGrip)
            {
                var localPosition = transform.InverseTransformPoint(selectedGrip.position);
                var localRotation = Quaternion.Inverse(transform.rotation) * selectedGrip.rotation;
                var rotation = target.rotation * Quaternion.Inverse(localRotation);
                transform.SetPositionAndRotation(target.position - rotation * localPosition, rotation);
            }
            else transform.SetPositionAndRotation(target.position, target.rotation);
            Attach(target);
            return true;
        }
        public void Drop()
        {
            if (State == HoldState.World) return;
            var former = State;
            ClearSecondaryGrip();
            if (playerBody && handPoseGrip) playerBody.ClearMeleeGrip(handPoseGrip);
            playerBody = null; selectedGrip = null; hand = null;
            if (handPoseGrip) Destroy(handPoseGrip.gameObject);
            handPoseGrip = null;
            deferCollisionRestore = former == HoldState.HeldByNpc && formerOwnerCollisionDelay > 0;
            Detach();
            deferCollisionRestore = false;
            transform.SetParent(null, true);
            Owner = null; ownerRoot = null; npcGripTarget = null; State = HoldState.World;
            body.isKinematic = false; body.useGravity = true; body.WakeUp();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (former == HoldState.HeldByNpc)
            {
                droppedByNpc = true;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, 3f);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, 8f);
            }
            if (grab) grab.enabled = true;
        }
        void Attach(Transform pose)
        {
            Detach();
            if (!pose) return;
            var target = new GameObject(name + " physical grip target");
            target.hideFlags = HideFlags.HideAndDontSave;
            target.transform.SetPositionAndRotation(pose.position,
                State == HoldState.HeldByPlayer && selectedGrip ? pose.rotation * Quaternion.Inverse(selectedGripLocalRotation * selectedGripOffset) : pose.rotation);
            targetBody = target.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.interpolation = RigidbodyInterpolation.Interpolate;
            joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = targetBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = State == HoldState.HeldByPlayer ? selectedGripLocalPosition :
                transform.InverseTransformPoint(selectedGrip ? selectedGrip.position : transform.position);
            joint.connectedAnchor = Vector3.zero;
            if (State == HoldState.HeldByPlayer)
            {
                // Keep the hand joint physical while preventing small tracking and
                // solver errors from building into visible walk-cycle oscillation.
                body.solverIterations = Mathf.Max(body.solverIterations, 16);
                body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 8);
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = .01f;
                joint.projectionAngle = 1.5f;
            }
            bool npcHeld = pose == npcGripTarget;
            attributeSource = CharacterAttributes.Find(ownerRoot);
            if (attributeSource) attributeSource.Changed += OnAttributeChanged;
            joint.xMotion = joint.yMotion = joint.zMotion = npcHeld ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = npcHeld ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free;
            if (npcHeld)
            {
                body.solverIterations = Mathf.Max(body.solverIterations, 16);
                body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 8);
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = useMeshContactShape ? .005f : .015f;
                joint.projectionAngle = useMeshContactShape ? .75f : 2f;
                float linearSlack = useMeshContactShape ? Mathf.Min(npcGripSlack, .012f) : npcGripSlack;
                float angularSlack = useMeshContactShape ? Mathf.Min(npcAngularSlack, 3f) : npcAngularSlack;
                joint.linearLimit = new SoftJointLimit { limit = linearSlack };
                joint.lowAngularXLimit = new SoftJointLimit { limit = -angularSlack };
                joint.highAngularXLimit = new SoftJointLimit { limit = angularSlack };
                joint.angularYLimit = joint.angularZLimit = new SoftJointLimit { limit = angularSlack };
            }
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            ConfigureJointDrive();
            FollowPose = pose;
            targetInitialized = false;
            IgnoreOwner(true);
        }
        void OnAttributeChanged(CharacterAttribute attribute, int value) => ConfigureJointDrive();
        void ConfigureJointDrive()
        {
            if (!joint) return;
            bool useAttributes = State == HoldState.HeldByNpc ? useAttributesForNpcHandling : useAttributesForPlayerHandling;
            activeControlMultiplier = useAttributes ?
                CharacterCombatMath.WeaponControl(attributeSource, body.mass, proficiencyClass) : 1f;
            float linearSpring = positionSpring * activeControlMultiplier;
            float linearDamper = positionDamper * Mathf.Sqrt(activeControlMultiplier);
            if (State == HoldState.HeldByNpc && useMeshContactShape)
            {
                linearSpring *= 3f;
                linearDamper = Mathf.Max(linearDamper, 2f * Mathf.Sqrt(linearSpring * body.mass));
            }
            if (State == HoldState.HeldByPlayer)
            {
                linearSpring *= 2.5f;
                linearDamper = Mathf.Max(linearDamper, 2f * Mathf.Sqrt(linearSpring * body.mass));
            }
            joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive {
                positionSpring = linearSpring,
                positionDamper = linearDamper,
                maximumForce = maximumDriveForce * activeControlMultiplier *
                    (State == HoldState.HeldByPlayer ? 3f : State == HoldState.HeldByNpc && useMeshContactShape ? 5f : 1f) };
            joint.slerpDrive = new JointDrive {
                positionSpring = rotationSpring * activeControlMultiplier *
                    (State == HoldState.HeldByPlayer ? 2f : State == HoldState.HeldByNpc && useMeshContactShape ? 3f : 1f),
                positionDamper = rotationDamper * Mathf.Sqrt(activeControlMultiplier),
                maximumForce = maximumDriveForce * activeControlMultiplier *
                    (State == HoldState.HeldByPlayer ? 3f : State == HoldState.HeldByNpc && useMeshContactShape ? 5f : 1f) };
        }
        Transform FollowPose { get; set; }
        void FixedUpdate()
        {
            if (!FollowPose || !targetBody) return;
            if (State == HoldState.HeldByPlayer && ownerRoot)
            {
                Vector3 rootStep = ownerPoseInitialized ? ownerRoot.position - previousOwnerPosition : Vector3.zero;
                bool contactedWorld = externalContact;
                externalContact = false;
                if (!contactedWorld && rootStep.sqrMagnitude > .000001f && rootStep.sqrMagnitude < .0225f &&
                    !body.SweepTest(rootStep.normalized, out _, rootStep.magnitude, QueryTriggerInteraction.Ignore))
                    body.position += rootStep;
                if (ownerPoseInitialized)
                {
                    // The XR origin moves in render frames. A raw fixed-step
                    // derivative alternates between one and two render frames
                    // even at steady walking speed, which shakes the blade.
                    float carryBlend = 1f - Mathf.Exp(-18f * Time.fixedDeltaTime);
                    Vector3 sampledRootVelocity = (ownerRoot.position - previousOwnerPosition) / Time.fixedDeltaTime;
                    Vector3 rootVelocity = Vector3.Lerp(previousRootVelocity, sampledRootVelocity, carryBlend);
                    var turn = ownerRoot.rotation * Quaternion.Inverse(previousOwnerRotation);
                    turn.ToAngleAxis(out var angle, out var axis);
                    if (angle > 180f) angle -= 360f;
                    Vector3 sampledAngularVelocity = axis.sqrMagnitude > .5f ?
                        axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime) * locomotionCarry : Vector3.zero;
                    Vector3 angular = Vector3.Lerp(previousRootAngularVelocity, sampledAngularVelocity, carryBlend);
                    previousRootAngularVelocity = angular;
                    previousRootVelocity = rootVelocity;
                }
                previousOwnerPosition = ownerRoot.position;
                previousOwnerRotation = ownerRoot.rotation;
                ownerPoseInitialized = true;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maximumLinearVelocity);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, maximumAngularVelocity);
            }
            var gripLocal = State == HoldState.HeldByPlayer ? selectedGripLocalPosition : Vector3.zero;
            var gripRotation = State == HoldState.HeldByPlayer ? selectedGripLocalRotation * selectedGripOffset : Quaternion.identity;
            var desiredRotation = State == HoldState.HeldByPlayer ? FollowPose.rotation * Quaternion.Inverse(gripRotation) : FollowPose.rotation;
            var desiredPosition = State == HoldState.HeldByPlayer ? FollowPose.position + FollowPose.rotation * selectedHandOffset : FollowPose.position;
            if (State == HoldState.HeldByPlayer && secondaryHand != null && selectedSecondaryGrip)
            {
                var otherPose = secondaryHand.GetAttachTransform(grab);
                Vector3 gripAxis = transform.InverseTransformPoint(selectedSecondaryGrip.position) - selectedGripLocalPosition;
                Vector3 handAxis = otherPose ? otherPose.position - desiredPosition : Vector3.zero;
                if (gripAxis.sqrMagnitude > .0025f && handAxis.sqrMagnitude > .0025f)
                {
                    Quaternion correction = Quaternion.FromToRotation(desiredRotation * gripAxis, handAxis);
                    desiredRotation = Quaternion.RotateTowards(Quaternion.identity, correction, 65f) * desiredRotation;
                }
            }
            if (targetInitialized && State != HoldState.HeldByPlayer)
            {
                // The kinematic target supplies the joint with a world-space velocity.
                // Large tracking recenter jumps are bounded without weakening ordinary locomotion.
                desiredPosition = Vector3.MoveTowards(previousTargetPosition, desiredPosition,
                    Mathf.Max(maximumTargetSpeed, useMeshContactShape ? 60f : 0f) * Time.fixedDeltaTime);
                desiredRotation = Quaternion.RotateTowards(previousTargetRotation, desiredRotation,
                    Mathf.Max(maximumTargetAngularSpeed, useMeshContactShape ? 150f : 0f) *
                    Mathf.Rad2Deg * Time.fixedDeltaTime);
            }
            previousTargetPosition = desiredPosition; previousTargetRotation = desiredRotation; targetInitialized = true;
            if (State == HoldState.HeldByPlayer)
            {
                // The target has no colliders. Write the latest tracked pose in this
                // physics step; MovePosition's queued step made the joint chase an
                // alternating old/new pose during locomotion.
                targetBody.position = desiredPosition;
                targetBody.rotation = desiredRotation;
            }
            else
            {
                // The NPC target has no collider. Give the joint this fixed step's
                // hand pose directly; queued MovePosition/MoveRotation lagged behind
                // fast staff strikes and let the shaft wobble through the torso.
                targetBody.position = desiredPosition;
                targetBody.rotation = desiredRotation;
            }
            positionErrorMeters = Vector3.Distance(selectedGrip ? selectedGrip.position : transform.TransformPoint(gripLocal), desiredPosition);
            rotationErrorDegrees = Quaternion.Angle(transform.rotation, desiredRotation);
        }
        void Detach()
        {
            if (attributeSource) attributeSource.Changed -= OnAttributeChanged;
            attributeSource = null;
            activeControlMultiplier = 1f;
            if (deferCollisionRestore)
            {
                var formerShapes = ignored.ToArray();
                ignored.Clear();
                StartCoroutine(RestoreFormerOwner(formerShapes));
            }
            else IgnoreOwner(false);
            FollowPose = null;
            if (joint) Destroy(joint);
            if (targetBody) Destroy(targetBody.gameObject);
            joint = null; targetBody = null;
            ownerPoseInitialized = false;
            previousRootVelocity = previousRootAngularVelocity = Vector3.zero;
            externalContact = false;
        }
        IEnumerator RestoreFormerOwner(Collider[] formerShapes)
        {
            yield return new WaitForSeconds(formerOwnerCollisionDelay);
            var ownShapes = GetComponentsInChildren<Collider>();
            foreach (var shape in formerShapes)
                if (shape) foreach (var own in ownShapes) if (own) Physics.IgnoreCollision(own, shape, false);
        }
        public void DropFromNpc()
        {
            if (State == HoldState.HeldByNpc) Drop();
        }
        void IgnoreOwner(bool ignore)
        {
            if (!ignore)
            {
                foreach (var shape in ignored)
                    if (shape) foreach (var own in GetComponentsInChildren<Collider>()) if (own) Physics.IgnoreCollision(own, shape, false);
                ignored.Clear(); return;
            }
            if (!ownerRoot) return;
            foreach (var shape in ownerRoot.GetComponentsInChildren<Collider>())
            {
                if (shape.transform.IsChildOf(transform)) continue;
                foreach (var own in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(own, shape, true);
                ignored.Add(shape);
            }
        }
        void OnCollisionEnter(Collision collision) => Impact(collision, true);
        void OnCollisionStay(Collision collision) => Impact(collision, false);
        void OnCollisionExit(Collision collision)
        {
            var otherWeapon = collision.collider.GetComponentInParent<PhysicalMeleeWeapon>();
            if (otherWeapon && otherWeapon != this)
                WeaponDisarmResolver.Active?.NotifySeparation(this, otherWeapon);
        }
        void Impact(Collision collision, bool newContact)
        {
            if (State == HoldState.HeldByPlayer && (!ownerRoot || !collision.collider.transform.IsChildOf(ownerRoot)))
                externalContact = true;
            if (State == HoldState.HeldByPlayer && ownerRoot)
            {
                var knockout = ownerRoot.GetComponentInChildren<Zandea.Opening.PlayerKnockout>();
                if (knockout && knockout.State != Zandea.Opening.PlayerConsciousness.Conscious) return;
            }
            if (equipmentItem && equipmentItem.currentSlot) return;
            if (droppedByNpc) return;
            if (collision.contactCount == 0) return;
            var other = collision.collider;
            if (ownerRoot && other.transform.IsChildOf(ownerRoot)) return;
            var contact = collision.GetContact(0);
            float speed = collision.relativeVelocity.magnitude;
            if (State == HoldState.HeldByNpc && useMeshContactShape && hasPreviousNpcBodyPose)
            {
                var localPoint = transform.InverseTransformPoint(contact.point);
                var previousPoint = previousNpcBodyPosition + previousNpcBodyRotation * localPoint;
                speed = Mathf.Max(speed, Vector3.Distance(contact.point, previousPoint) /
                    Mathf.Max(Time.fixedDeltaTime, .001f));
            }
            float impulse = collision.impulse.magnitude;
            var otherWeapon = other.GetComponentInParent<PhysicalMeleeWeapon>();
            bool weaponContact = otherWeapon && otherWeapon != this;
            if (weaponContact && newContact)
            {
                WeaponContactEntries++;
                MaximumWeaponContactSpeed = Mathf.Max(MaximumWeaponContactSpeed, speed);
                MaximumWeaponContactImpulse = Mathf.Max(MaximumWeaponContactImpulse, impulse);
                WeaponDisarmResolver.Active?.Evaluate(this, otherWeapon, speed, impulse, contact.normal);
                if (impulse >= .5f && GetInstanceID() < otherWeapon.GetInstanceID())
                {
                    var metal = ownerRoot ? ownerRoot.GetComponent<BanditCombatAudio>() : null;
                    if (!metal && otherWeapon.ownerRoot)
                        metal = otherWeapon.ownerRoot.GetComponent<BanditCombatAudio>();
                    metal?.PlayParry();
                }
            }
            if (speed < minimumImpactSpeed) return;
            if (weaponContact)
            {
                if (ownerRoot) ownerRoot.GetComponent<StarvingBandit>()?.RegisterWeaponParry(otherWeapon);
                if (otherWeapon.ownerRoot) otherWeapon.ownerRoot.GetComponent<StarvingBandit>()?.RegisterWeaponParry(this);
            }
            if (weaponContact && impulse >= blockedImpulseThreshold)
                Blocked?.Invoke(impulse);
            if (hand is XRBaseInputInteractor inputHand)
                inputHand.SendHapticImpulse(Mathf.Clamp01(impulse / 12f), .045f);
            if (weaponContact || !newContact) return;
            var npcAttacker = ownerRoot ? ownerRoot.GetComponent<StarvingBandit>() : null;
            if (npcAttacker)
            {
                var scoring = npcAttacker.GetComponent<BanditDuelStrikeDamage>();
                if (scoring) scoring.TryScoreContact(this, other, contact.point, contact.normal, speed);
                return;
            }
            if (lastHit.TryGetValue(other, out var previous) && Time.time - previous < damageCooldown) return;
            // Both speed and solver impulse must indicate a strike. Sustained contact cannot
            // repeatedly score damage just because the controller keeps pushing.
            float energy = .5f * body.mass * speed * speed;
            var attackerAttributes = ownerRoot ? CharacterAttributes.Find(ownerRoot) : null;
            float damage = Mathf.Min(60f, energy * damagePerJoule * Mathf.Clamp01(impulse / .5f) *
                CharacterCombatMath.DamageDealt(attackerAttributes, proficiencyClass));
            if (damage < .5f) return;
            var receiver = other.GetComponentInParent<IDamageable>();
            if (receiver == null) return;
            var merchantVictim = other.GetComponentInParent<Zandea.FriendlyNPC.FriendlyNpcInteraction>();
            if (merchantVictim && State == HoldState.World) return;
            if (merchantVictim && State == HoldState.HeldByPlayer &&
                !merchantVictim.AcceptDeliberateWeaponStrike(
                    speed, impulse, Time.time - lastPlayerPickupAt)) return;
            var defendingBandit = other.GetComponentInParent<StarvingBandit>();
            // Bandits can brush past allied weapons on patrol. Deliberate
            // strike-only ally damage can be added with attack phase gating later.
            var attackingBandit = ownerRoot ? ownerRoot.GetComponent<StarvingBandit>() : null;
            if (defendingBandit && attackingBandit) return;
            if (attackingBandit && attackingBandit.state != StarvingBandit.State.Strike) return;
            if (defendingBandit && defendingBandit.DeflectsDamageFrom(this)) return;
            lastHit[other] = Time.time;
            var packet = new DamagePacket { BaseDamage = damage, DamageType = DamageType.Kinetic,
                PhysicalImpact = strikeType == StrikeType.Blunt ? PhysicalImpactKind.Blunt : PhysicalImpactKind.Sharp,
                HitPointWorld = contact.point, HitNormalWorld = contact.normal,
                Source = ownerRoot ? ownerRoot.gameObject : gameObject, WeaponId = name, Timestamp = Time.time };
            Vector3 pointVelocity = body.GetPointVelocity(contact.point);
            Vector3 carriedVelocity = State == HoldState.HeldByPlayer ?
                previousRootVelocity * locomotionCarry : Vector3.zero;
            Vector3 slashVelocity = pointVelocity - carriedVelocity;
            // Collision.relativeVelocity retains the incoming strike speed when the
            // solver has already slowed the rigidbody by the time this callback runs.
            Vector3 collisionStrike = collision.relativeVelocity - carriedVelocity;
            if (slashVelocity.sqrMagnitude > .01f && Vector3.Dot(collisionStrike, slashVelocity) < 0f)
                collisionStrike = -collisionStrike;
            if (collisionStrike.sqrMagnitude > slashVelocity.sqrMagnitude)
                slashVelocity = collisionStrike;
            receiver.ApplyDamage(packet);
            var victimHealth = other.GetComponentInParent<ScavengerNPC.ScavengerHealth>();
            if (victimHealth && victimHealth.LastDamageBlocked) return;
            // Health stops accepting damage after death, but a physical sword
            // strike must still be able to sever a corpse.
            float effectiveDamage = victimHealth && !victimHealth.IsDead ? victimHealth.LastDamageTaken : damage;
            var anatomy = other.GetComponentInParent<DismemberableCharacter>();
            if (anatomy) anatomy.TrySever(contact.point, slashVelocity,
                bladeEdgeLocalAxis.sqrMagnitude > .01f ? transform.TransformDirection(bladeEdgeLocalAxis) : transform.right,
                effectiveDamage,
                strikeType == StrikeType.Cutting, collision.rigidbody ? collision.rigidbody.linearVelocity : Vector3.zero);
            if (strikeType == StrikeType.Cutting || strikeType == StrikeType.Piercing)
            {
                var blood = other.GetComponentInParent<GoreImpactSystem>();
                if (blood)
                {
                    blood.RegisterImpact(packet, slashVelocity, effectiveDamage);
                    var staining = GetComponent<WeaponBloodSurface>();
                    if (!staining) staining = gameObject.AddComponent<WeaponBloodSurface>();
                    staining.AddBlood(effectiveDamage, contact.point);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            DrawGrip(rightGrip, Color.cyan);
            DrawGrip(leftGrip, Color.magenta);
            DrawGrip(secondaryGrip, Color.green);
            if (FollowPose)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(selectedGrip ? selectedGrip.position : transform.position, FollowPose.position);
                Gizmos.DrawWireSphere(FollowPose.position, .025f);
            }
        }
        void CorrectNpcPickupRoll()
        {
            if (rightGrip || leftGrip || Mathf.Approximately(npcPickupRoll, 0f)) return;
            if (StaffAxisCalibration.TryMeasure(transform, out var positive, out var negative))
                selectedGripLocalRotation = Quaternion.AngleAxis(npcPickupRoll, (positive - negative).normalized) * selectedGripLocalRotation;
        }

        void EnsureSecondaryGrip()
        {
            // Older Bo Staff authoring accidentally pointed secondaryGrip at the
            // left primary marker. Treat any primary alias as missing so the two
            // selectors can never collapse onto the same point.
            if (secondaryGrip && secondaryGrip != leftGrip && secondaryGrip != rightGrip) return;
            string label = name.ToLowerInvariant();
            if (!label.Contains("bostaff") && !label.Contains("bo-staff") && !label.Contains("bo staff") &&
                !label.Contains("sawcleaver") && !label.Contains("rusteddoubleaxe") &&
                !label.Contains("poleblade") && !label.Contains("longblade") &&
                !label.Contains("long blade") && !label.Contains("greatsword") &&
                !label.Contains("great sword") && !label.Contains("heavycleaver")) return;
            var marker = new GameObject("Secondary Grip").transform;
            marker.SetParent(transform, false);
            Vector3 local = grip ? transform.InverseTransformPoint(grip.position) : Vector3.zero;
            if (StaffAxisCalibration.TryMeasure(transform, out var positive, out var negative))
            {
                var farEnd = Vector3.Distance(local, positive) > Vector3.Distance(local, negative) ? positive : negative;
                var nearEnd = farEnd == positive ? negative : positive;
                // Sword grips belong toward the pommel; a second hand on the blade
                // would steer from an implausible point and fight the primary grip.
                bool sword = label.Contains("longblade") || label.Contains("long blade") ||
                    label.Contains("greatsword") || label.Contains("great sword");
                local = Vector3.Lerp(local, sword ? nearEnd : farEnd, sword ? .65f : .3f);
            }
            else local += Vector3.up * .35f;
            marker.localPosition = local;
            secondaryGrip = marker;
        }

        bool IsLeftHand(IXRSelectInteractor interactor)
        {
            if (interactor == null) return false;
            if (playerBody && playerBody.leftController &&
                (interactor.transform == playerBody.leftController || interactor.transform.IsChildOf(playerBody.leftController))) return true;
            if (playerBody && playerBody.rightController &&
                (interactor.transform == playerBody.rightController || interactor.transform.IsChildOf(playerBody.rightController))) return false;
            return interactor.handedness == InteractorHandedness.Left;
        }
        static void DrawGrip(Transform marker, Color color)
        {
            if (!marker) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(marker.position, .025f);
            Gizmos.DrawRay(marker.position, marker.forward * .12f);
        }
    }
}
