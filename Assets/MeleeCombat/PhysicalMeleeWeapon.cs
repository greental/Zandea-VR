using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using JunkerBody;
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
        [Header("Shared ownership")]
        public string weaponId = "bo-staff";
        [SerializeField] HoldState currentState;
        public HoldState State { get => currentState; private set => currentState = value; }
        public Component Owner { get; private set; }
        public Component Reservation { get; private set; }
        float reservationUntil;
        public static readonly HashSet<PhysicalMeleeWeapon> Active = new();
        [Header("Authored player grips")]
        public Transform rightGrip, leftGrip, secondaryGrip;
        [Min(0)] public float maximumTargetSpeed = 7f;
        [Min(0)] public float maximumTargetAngularSpeed = 24f;
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
        public Transform grip;
        public Transform npcGripTarget;
        public Transform ownerRoot;
        [Min(.01f)] public float mass = 1.4f;
        [Min(0)] public float positionSpring = 1800f, positionDamper = 105f;
        [Min(0)] public float rotationSpring = 850f, rotationDamper = 65f;
        [Min(0)] public float maximumDriveForce = 1200f;
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
        IXRSelectInteractor hand;
        VRBodyController playerBody;
        Transform selectedGrip;
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
        readonly Dictionary<Collider, float> lastHit = new();
        readonly List<Collider> ignored = new();

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = 35f;
            if (grab)
            {
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.useDynamicAttach = false;
                grab.selectMode = InteractableSelectMode.Single;
                grab.throwOnDetach = false;
                grab.trackPosition = grab.trackRotation = false;
                grab.selectEntered.AddListener(Selected);
                grab.selectExited.AddListener(Released);
            }
        }

        void OnEnable() => Active.Add(this);
        void OnDisable()
        {
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
            Reservation = null;
            droppedByNpc = false;
            hand = args.interactorObject;
            ownerRoot = hand.transform.root;
            Owner = hand.transform.GetComponentInParent<MonoBehaviour>();
            foreach (var actor in hand.transform.GetComponentsInParent<MonoBehaviour>())
                if (actor is IDisarmCombatant) { Owner = actor; break; }
            State = HoldState.HeldByPlayer;
            selectedGrip = hand.handedness == InteractorHandedness.Left ? (leftGrip ? leftGrip : grip) : (rightGrip ? rightGrip : grip);
            if (!selectedGrip) selectedGrip = transform;
            oldTrackPosition = grab.trackPosition;
            oldTrackRotation = grab.trackRotation;
            grab.trackPosition = grab.trackRotation = false;
            playerBody = hand.transform.GetComponentInParent<VRBodyController>();
            if (!playerBody) playerBody = FindFirstObjectByType<VRBodyController>();
            if (playerBody) playerBody.SetMeleeGrip(hand.handedness == InteractorHandedness.Left, selectedGrip);
            ownerPoseInitialized = false;
            previousRootVelocity = previousRootAngularVelocity = Vector3.zero;
            if (transform.parent) transform.SetParent(null, true);
            // XRI may have cached a kinematic state from storage.
            body.isKinematic = false;
            Attach(hand.GetAttachTransform(grab));
        }
        void Released(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (State == HoldState.HeldByPlayer) Drop();
            grab.trackPosition = oldTrackPosition;
            grab.trackRotation = oldTrackRotation;
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
            if (playerBody && selectedGrip) playerBody.ClearMeleeGrip(selectedGrip);
            playerBody = null; selectedGrip = null; hand = null;
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
                State == HoldState.HeldByPlayer && selectedGrip ? pose.rotation * Quaternion.Inverse(selectedGrip.localRotation) : pose.rotation);
            targetBody = target.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.interpolation = RigidbodyInterpolation.Interpolate;
            joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = targetBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = transform.InverseTransformPoint(selectedGrip ? selectedGrip.position : transform.position);
            joint.connectedAnchor = Vector3.zero;
            bool npcHeld = pose == npcGripTarget;
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
            joint.xDrive = joint.yDrive = joint.zDrive = new JointDrive { positionSpring = positionSpring, positionDamper = positionDamper, maximumForce = maximumDriveForce };
            joint.slerpDrive = new JointDrive { positionSpring = rotationSpring, positionDamper = rotationDamper, maximumForce = maximumDriveForce };
            FollowPose = pose;
            targetInitialized = false;
            IgnoreOwner(true);
        }
        Transform FollowPose { get; set; }
        void FixedUpdate()
        {
            if (!FollowPose || !targetBody) return;
            if (State == HoldState.HeldByPlayer && ownerRoot)
            {
                if (ownerPoseInitialized)
                {
                    Vector3 rootVelocity = (ownerRoot.position - previousOwnerPosition) / Time.fixedDeltaTime;
                    body.linearVelocity += Vector3.ClampMagnitude((rootVelocity - previousRootVelocity) * locomotionCarry, maximumLinearVelocity);
                    var turn = ownerRoot.rotation * Quaternion.Inverse(previousOwnerRotation);
                    turn.ToAngleAxis(out var angle, out var axis);
                    if (angle > 180f) angle -= 360f;
                    if (axis.sqrMagnitude > .5f)
                    {
                        var angular = axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime) * locomotionCarry;
                        body.angularVelocity += Vector3.ClampMagnitude(angular - previousRootAngularVelocity, maximumAngularVelocity);
                        body.linearVelocity += Vector3.ClampMagnitude(Vector3.Cross(angular - previousRootAngularVelocity, body.position - ownerRoot.position), maximumLinearVelocity);
                        previousRootAngularVelocity = angular;
                    }
                    else previousRootAngularVelocity = Vector3.zero;
                    previousRootVelocity = rootVelocity;
                }
                previousOwnerPosition = ownerRoot.position;
                previousOwnerRotation = ownerRoot.rotation;
                ownerPoseInitialized = true;
                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maximumLinearVelocity);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, maximumAngularVelocity);
            }
            var gripLocal = selectedGrip ? selectedGrip.localPosition : Vector3.zero;
            var gripRotation = selectedGrip ? selectedGrip.localRotation : Quaternion.identity;
            var desiredRotation = State == HoldState.HeldByPlayer ? FollowPose.rotation * Quaternion.Inverse(gripRotation) : FollowPose.rotation;
            var desiredPosition = FollowPose.position;
            if (targetInitialized)
            {
                // The kinematic target supplies the joint with a world-space velocity.
                // Large tracking recenter jumps are bounded without weakening ordinary locomotion.
                desiredPosition = Vector3.MoveTowards(previousTargetPosition, desiredPosition,
                    maximumTargetSpeed * Time.fixedDeltaTime);
                desiredRotation = Quaternion.RotateTowards(previousTargetRotation, desiredRotation,
                    maximumTargetAngularSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime);
            }
            previousTargetPosition = desiredPosition; previousTargetRotation = desiredRotation; targetInitialized = true;
            targetBody.MovePosition(desiredPosition);
            targetBody.MoveRotation(desiredRotation);
            positionErrorMeters = Vector3.Distance(transform.TransformPoint(gripLocal), FollowPose.position);
            rotationErrorDegrees = Quaternion.Angle(transform.rotation, desiredRotation);
        }
        void Detach()
        {
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
            float damage = Mathf.Min(60f, energy * damagePerJoule * Mathf.Clamp01(impulse / .5f));
            if (damage < .5f) return;
            var receiver = other.GetComponentInParent<IDamageable>();
            if (receiver == null) return;
            var defendingBandit = other.GetComponentInParent<StarvingBandit>();
            if (defendingBandit && defendingBandit.DeflectsDamageFrom(this)) return;
            lastHit[other] = Time.time;
            receiver.ApplyDamage(new DamagePacket { BaseDamage = damage, DamageType = DamageType.Kinetic,
                HitPointWorld = contact.point, HitNormalWorld = contact.normal,
                Source = ownerRoot ? ownerRoot.gameObject : gameObject, WeaponId = name, Timestamp = Time.time });
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
