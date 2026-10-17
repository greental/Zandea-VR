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
        public HoldState State { get => currentState; private set => currentState = value; }
        public Component Owner { get; private set; }
        public Component Reservation { get; private set; }
        float reservationUntil;
        public static readonly HashSet<PhysicalMeleeWeapon> Active = new();
        [Header("Authored player grips")]
        public Transform rightGrip, leftGrip, secondaryGrip;
        [Tooltip("Fixed right-hand blade angle relative to the controller. Edit this or the Right Grip marker in the Inspector.")]
        public Vector3 rightGripEuler;
        [Tooltip("Fixed left-hand blade angle relative to the controller. Edit this or the Left Grip marker in the Inspector.")]
        public Vector3 leftGripEuler;
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
        VRBodyController playerBody;
        Transform selectedGrip;
        Transform handPoseGrip;
        Quaternion selectedGripOffset = Quaternion.identity;
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
        JunkerEquipmentItem equipmentItem;
        JunkerEquipmentRig pendingHolsterRig;
        readonly Dictionary<Collider, float> lastHit = new();
        readonly List<Collider> ignored = new();

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            var groundShape = GroundLootCollider.Ensure(gameObject);
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
                if (grip) grab.attachTransform = grip;
                if (grab.colliders.Count == 0)
                    foreach (var shape in GetComponentsInChildren<Collider>())
                        if (shape && !shape.isTrigger) grab.colliders.Add(shape);
                if (groundShape && !grab.colliders.Contains(groundShape)) grab.colliders.Add(groundShape);
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.useDynamicAttach = false;
                grab.selectMode = InteractableSelectMode.Single;
                grab.throwOnDetach = false;
                grab.trackPosition = grab.trackRotation = false;
                grab.selectEntered.AddListener(Selected);
                grab.selectExited.AddListener(Released);
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
            if (State != HoldState.World)
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
            if (equipmentItem && equipmentItem.currentSlot)
                equipmentItem.currentSlot.ReleaseItem(equipmentItem);
            pendingHolsterRig = null;
            Reservation = null;
            droppedByNpc = false;
            hand = args.interactorObject;
            playerBody = hand.transform.GetComponentInParent<VRBodyController>();
            if (!playerBody) playerBody = FindFirstObjectByType<VRBodyController>();
            bool leftHand = playerBody && playerBody.leftController &&
                (hand.transform == playerBody.leftController || hand.transform.IsChildOf(playerBody.leftController));
            bool rightHand = playerBody && playerBody.rightController &&
                (hand.transform == playerBody.rightController || hand.transform.IsChildOf(playerBody.rightController));
            if (!leftHand && !rightHand) leftHand = hand.handedness == InteractorHandedness.Left;
            ownerRoot = playerBody ? playerBody.transform.root : hand.transform.root;
            Owner = hand.transform.GetComponentInParent<MonoBehaviour>();
            foreach (var actor in hand.transform.GetComponentsInParent<MonoBehaviour>())
                if (actor is IDisarmCombatant) { Owner = actor; break; }
            State = HoldState.HeldByPlayer;
            selectedGrip = leftHand ? (leftGrip ? leftGrip : grip) : (rightGrip ? rightGrip : grip);
            if (!selectedGrip) selectedGrip = transform;
            selectedGripOffset = Quaternion.Euler(leftHand ? leftGripEuler : rightGripEuler);
            if (handPoseGrip) Destroy(handPoseGrip.gameObject);
            handPoseGrip = new GameObject("Active hand pose").transform;
            handPoseGrip.SetParent(selectedGrip, false);
            handPoseGrip.localRotation = selectedGripOffset;
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
            if (transform.parent) transform.SetParent(null, true);
            // XRI may have cached a kinematic state from storage.
            body.isKinematic = false;
            Attach(hand.GetAttachTransform(grab));
        }
        void Released(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (State == HoldState.HeldByPlayer)
            {
                var rig = FindPlayerEquipmentRig();
                Drop();
                if (equipmentItem) equipmentItem.rig = rig;
                pendingHolsterRig = rig;
            }
            grab.trackPosition = oldTrackPosition;
            grab.trackRotation = oldTrackRotation;
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
            ownerRoot = npc.transform; npcGripTarget = target; hand = null; selectedGrip = null;
            if (grab) grab.enabled = false;
            body.isKinematic = false; body.useGravity = false;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(target.position, target.rotation);
            Attach(target);
            return true;
        }
        public void Drop()
        {
            if (State == HoldState.World) return;
            var former = State;
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
                State == HoldState.HeldByPlayer && selectedGrip ? pose.rotation * Quaternion.Inverse(selectedGrip.localRotation * selectedGripOffset) : pose.rotation);
            targetBody = target.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.interpolation = RigidbodyInterpolation.Interpolate;
            joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = targetBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = transform.InverseTransformPoint(selectedGrip ? selectedGrip.position : transform.position);
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
                joint.linearLimit = new SoftJointLimit { limit = npcGripSlack };
                joint.lowAngularXLimit = new SoftJointLimit { limit = -npcAngularSlack };
                joint.highAngularXLimit = new SoftJointLimit { limit = npcAngularSlack };
                joint.angularYLimit = joint.angularZLimit = new SoftJointLimit { limit = npcAngularSlack };
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
            if (State == HoldState.HeldByPlayer)
            {
                linearSpring *= 1.45f;
                linearDamper = Mathf.Max(linearDamper, 2f * Mathf.Sqrt(linearSpring * body.mass));
            }
            joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive {
                positionSpring = linearSpring,
                positionDamper = linearDamper,
                maximumForce = maximumDriveForce * activeControlMultiplier };
            joint.slerpDrive = new JointDrive {
                positionSpring = rotationSpring * activeControlMultiplier,
                positionDamper = rotationDamper * Mathf.Sqrt(activeControlMultiplier),
                maximumForce = maximumDriveForce * activeControlMultiplier };
        }
        Transform FollowPose { get; set; }
        void FixedUpdate()
        {
            if (!FollowPose || !targetBody) return;
            if (State == HoldState.HeldByPlayer && ownerRoot)
            {
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
            var gripLocal = selectedGrip ? selectedGrip.localPosition : Vector3.zero;
            var gripRotation = selectedGrip ? selectedGrip.localRotation * selectedGripOffset : Quaternion.identity;
            var desiredRotation = State == HoldState.HeldByPlayer ? FollowPose.rotation * Quaternion.Inverse(gripRotation) : FollowPose.rotation;
            var desiredPosition = FollowPose.position;
            if (targetInitialized && State != HoldState.HeldByPlayer)
            {
                // The kinematic target supplies the joint with a world-space velocity.
                // Large tracking recenter jumps are bounded without weakening ordinary locomotion.
                desiredPosition = Vector3.MoveTowards(previousTargetPosition, desiredPosition,
                    maximumTargetSpeed * Time.fixedDeltaTime);
                desiredRotation = Quaternion.RotateTowards(previousTargetRotation, desiredRotation,
                    maximumTargetAngularSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime);
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
                targetBody.MovePosition(desiredPosition);
                targetBody.MoveRotation(desiredRotation);
            }
            positionErrorMeters = Vector3.Distance(transform.TransformPoint(gripLocal), FollowPose.position);
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
            if (equipmentItem && equipmentItem.currentSlot) return;
            if (droppedByNpc) return;
            if (collision.contactCount == 0) return;
            var other = collision.collider;
            if (ownerRoot && other.transform.IsChildOf(ownerRoot)) return;
            var contact = collision.GetContact(0);
            float speed = collision.relativeVelocity.magnitude;
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
                ownerRoot?.GetComponent<StarvingBandit>()?.RegisterWeaponParry(otherWeapon);
                otherWeapon.ownerRoot?.GetComponent<StarvingBandit>()?.RegisterWeaponParry(this);
            }
            if (weaponContact && impulse >= blockedImpulseThreshold)
                Blocked?.Invoke(impulse);
            if (hand is XRBaseInputInteractor inputHand)
                inputHand.SendHapticImpulse(Mathf.Clamp01(impulse / 12f), .045f);
            if (weaponContact || !newContact) return;
            if (lastHit.TryGetValue(other, out var previous) && Time.time - previous < damageCooldown) return;
            // Both speed and solver impulse must indicate a strike. Sustained contact cannot
            // repeatedly score damage just because the controller keeps pushing.
            float energy = .5f * body.mass * speed * speed;
            float damage = Mathf.Min(60f, energy * damagePerJoule * Mathf.Clamp01(impulse / .5f) *
                CharacterCombatMath.DamageDealt(CharacterAttributes.Find(ownerRoot), proficiencyClass));
            if (damage < .5f) return;
            var receiver = other.GetComponentInParent<IDamageable>();
            if (receiver == null) return;
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
            float effectiveDamage = victimHealth ? victimHealth.LastDamageTaken : damage;
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
            if (FollowPose)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(selectedGrip ? selectedGrip.position : transform.position, FollowPose.position);
                Gizmos.DrawWireSphere(FollowPose.position, .025f);
            }
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
