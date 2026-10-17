using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using KenshiVR.MeleeCombat;

namespace JunkerEquipment
{
    public enum EquipmentState { World, Held, Holstered, StoredInBackpack, Mounted }
    [DefaultExecutionOrder(11000), DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public sealed class JunkerEquipmentItem : MonoBehaviour
    {
        [SerializeField] EquipmentState currentState;
        public EquipmentState state => currentState;
        public string itemId = "item";
        public Transform gripPose, muzzle;
        [Header("Grip tuning")]
        [Tooltip("Use the Grip child transform as the exact hand attachment pose. Edit it in Prefab Mode with Move and Rotate tools.")]
        public bool useGripTransformDirectly;
        [Tooltip("Moves the item relative to the hand attach point. Use small values such as 0.01 to 0.05 m.")]
        public Vector3 gripPositionOffset;
        [Tooltip("Rotates the item relative to the hand attach point, in degrees.")]
        public Vector3 gripRotationOffset;
        [Header("Held motion")]
        [Tooltip("Uses XRI's direct tracked pose with optional smoothing. This is more stable than moving a dynamic Rigidbody toward the hand.")]
        public bool useSmoothHeldPose = true;
        [Tooltip("Keep this item's colliders as triggers while held, so locomotion and gravity cannot treat it as a floor. Useful for large rifles.")]
        public bool heldCollidersAreTriggers;
        [Range(1f, 40f)] public float heldPositionSmoothness = 24f;
        [Range(1f, 40f)] public float heldRotationSmoothness = 24f;
        public Vector3 holsterPosition, holsterEuler;
        [Min(.01f)] public float holsterRadius = .18f;
        public JunkerEquipmentRig rig;
        public JunkerEquipmentSlot currentSlot { get; private set; }
        public XRGrabInteractable Grab { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool Activated { get; private set; }
        Collider[] shapes;
        readonly System.Collections.Generic.List<Collider> heldPlayerColliders = new();
        bool[] triggers;
        bool pendingRelease;
        XRBaseInputInteractor lastHand;
        PhysicalMeleeWeapon meleeWeapon;
        [SerializeField, HideInInspector] Vector3 gripBasePosition;
        [SerializeField, HideInInspector] Quaternion gripBaseRotation = Quaternion.identity;
        [SerializeField, HideInInspector] bool gripBaseCaptured;

        void Awake()
        {
            Body = GetComponent<Rigidbody>(); Grab = GetComponent<XRGrabInteractable>();
            // Nested grabbables (such as an R4 cell) own their own colliders and physics.
            // Do not change those colliders when the pistol is grabbed or holstered.
            var ownShapes = new System.Collections.Generic.List<Collider>();
            foreach (var shape in GetComponentsInChildren<Collider>())
                if (shape.GetComponentInParent<Rigidbody>() == Body) ownShapes.Add(shape);
            shapes = ownShapes.ToArray(); triggers = new bool[shapes.Length];
            for (int i=0;i<shapes.Length;i++) triggers[i]=shapes[i].isTrigger;
            meleeWeapon = GetComponent<PhysicalMeleeWeapon>();
            if (meleeWeapon)
            {
                // The melee component owns the physical hand joint. This component
                // supplies the existing equipment slot and backpack lifecycle only.
                itemId = meleeWeapon.weaponId;
                gripPose = meleeWeapon.grip;
                useGripTransformDirectly = true;
                holsterRadius = Mathf.Max(holsterRadius, .4f);
                return;
            }
            Grab.selectMode = InteractableSelectMode.Single;
            Grab.retainTransformParent = false;
            // Kinematic MovePosition can visibly fight the headset/controller update loop.
            // Instantaneous uses XRI's tracked attach pose; smoothing removes high-frequency jitter
            // without parenting the item into the hand hierarchy or breaking throw/release.
            Grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            Grab.trackPosition = true; Grab.trackRotation = true;
            Grab.smoothPosition = useSmoothHeldPose;
            Grab.smoothRotation = useSmoothHeldPose;
            Grab.smoothPositionAmount = heldPositionSmoothness;
            Grab.smoothRotationAmount = heldRotationSmoothness;
            Grab.tightenPosition = .95f;
            Grab.tightenRotation = .95f;
            if (gripPose)
            {
                if (!useGripTransformDirectly)
                {
                    if (!gripBaseCaptured) { gripBasePosition=gripPose.localPosition; gripBaseRotation=gripPose.localRotation; gripBaseCaptured=true; }
                    ApplyGripOffset();
                }
                Grab.attachTransform=gripPose; Grab.useDynamicAttach=false;
            }
            Grab.selectEntered.AddListener(OnGrabbed); Grab.selectExited.AddListener(OnReleased);
            Grab.activated.AddListener(OnActivated); Grab.deactivated.AddListener(OnDeactivated);
        }
        void Start()
        {
            IgnorePlayerCollisions();
        }
        void OnDestroy()
        {
            RestoreHeldPlayerCollisions();
            if(currentSlot) currentSlot.ReleaseItem(this);
            if (!Grab) return;
            Grab.selectEntered.RemoveListener(OnGrabbed); Grab.selectExited.RemoveListener(OnReleased);
            Grab.activated.RemoveListener(OnActivated); Grab.deactivated.RemoveListener(OnDeactivated);
        }
        void OnDisable() { RestoreHeldPlayerCollisions(); pendingRelease=false; Activated=false; if(currentSlot) currentSlot.ReleaseItem(this); }
        void OnActivated(ActivateEventArgs _) { Activated=Grab.isSelected; }
        void OnDeactivated(DeactivateEventArgs _) { Activated=false; }
        void OnGrabbed(SelectEnterEventArgs args)
        {
            pendingRelease=false;
            if(currentSlot) currentSlot.ReleaseItem(this);
            // Starting items live below the moving shoulder slot in the prefab.
            // A released gun must no longer inherit that moving player's transform.
            if(rig && transform.IsChildOf(rig.transform)) transform.SetParent(null,true);
            currentSlot=null; currentState=EquipmentState.Held;
            SetTriggers(heldCollidersAreTriggers); Body.detectCollisions=true;
            IgnorePlayerCollisions();
            // Unmount must not replace the movement settings XRI has just applied.
            Body.isKinematic=Grab.movementType!=XRBaseInteractable.MovementType.VelocityTracking;
            Body.useGravity=false;
            lastHand=args.interactorObject as XRBaseInputInteractor;
            IgnoreHeldPlayerCollisions(args.interactorObject.transform);
            Pulse(.18f,.035f);
        }
        void OnReleased(SelectExitEventArgs args)
        {
            Activated=false;
            if(Grab.isSelected) return;
            // A canceled XRI detach (for example when the closed bay disables
            // its grab component) cannot turn a still-mounted item into a loose body.
            if(currentSlot) return;
            RestoreHeldPlayerCollisions();
            currentState=EquipmentState.World;
            // XRI may restore the kinematic settings cached while in the holster.
            Body.isKinematic=false; Body.useGravity=true; SetTriggers(false);
            IgnorePlayerCollisions();
            var inventoryItem = GetComponent<VRInventoryItem>();
            if (inventoryItem && inventoryItem.TryStoreOnRelease(args)) { pendingRelease=false; return; }
            var backpack = GetComponent<BackpackInventory>();
            if (backpack) backpack.HoldReleasedPose();
            pendingRelease=!args.isCanceled;
        }
        void IgnoreHeldPlayerCollisions(Transform hand)
        {
            RestoreHeldPlayerCollisions();
            IgnorePlayerCollisions();
            var origin=hand ? hand.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() : null;
            if(!origin) return;
            foreach(var player in origin.GetComponentsInChildren<Collider>(true))
            {
                if(!player || player.transform.IsChildOf(transform) || player.isTrigger ||
                    player.GetComponentInParent<XRBaseInteractor>()) continue;
                heldPlayerColliders.Add(player);
                foreach(var shape in shapes) if(shape && !shape.isTrigger) Physics.IgnoreCollision(player,shape,true);
            }
        }
        void RestoreHeldPlayerCollisions()
        {
            foreach(var player in heldPlayerColliders)
                foreach(var shape in shapes)
                    if(player && shape && !IsPlayerCollider(player))
                        Physics.IgnoreCollision(player,shape,false);
            heldPlayerColliders.Clear();
        }
        bool IsPlayerCollider(Collider collider)
        {
            return rig && ((rig.playerColliders != null && System.Array.IndexOf(rig.playerColliders,collider) >= 0) ||
                (rig.body && rig.body.xrOrigin && collider.transform.IsChildOf(rig.body.xrOrigin)));
        }
        void IgnorePlayerCollisions()
        {
            if(!rig) return;
            if(rig.playerColliders != null)
                foreach(var player in rig.playerColliders) IgnoreCollisionWith(player);
            if(rig.body && rig.body.xrOrigin)
                foreach(var player in rig.body.xrOrigin.GetComponentsInChildren<Collider>(true)) IgnoreCollisionWith(player);
        }
        void IgnoreCollisionWith(Collider player)
        {
            // Direct-interactor trigger overlaps are how the hands find holstered tools.
            // Ignoring them here makes every body item impossible to grip.
            if(!player || player.isTrigger || player.GetComponentInParent<XRBaseInteractor>()) return;
            foreach(var shape in shapes)
                if(shape && player != shape && !player.transform.IsChildOf(transform))
                    Physics.IgnoreCollision(player,shape,true);
        }
        void LateUpdate()
        {
            if (!useGripTransformDirectly && !meleeWeapon) ApplyGripOffset();
            // After XRI's deferred throw, so a snap cannot be overwritten by detach.
            if(pendingRelease)
            {
                pendingRelease=false;
                if (!Grab.isSelected)
                {
                    var backpack = GetComponent<BackpackInventory>();
                    if (backpack) backpack.TryReturnToBack();
                    else if (rig) rig.TryHolster(this);
                }
            }
            if(currentSlot && !Grab.isSelected)
            {
                currentState=EquipmentState.Holstered;
                Body.isKinematic=true; Body.useGravity=false;
                transform.SetPositionAndRotation(currentSlot.transform.TransformPoint(holsterPosition), currentSlot.transform.rotation*Quaternion.Euler(holsterEuler));
            }
        }
        void FixedUpdate()
        {
            if(currentSlot && !Grab.isSelected) { Body.isKinematic=true; Body.useGravity=false; }
        }
        void ApplyGripOffset()
        {
            if (!gripPose || !gripBaseCaptured) return;
            gripPose.localPosition = gripBasePosition + gripPositionOffset;
            gripPose.localRotation = gripBaseRotation * Quaternion.Euler(gripRotationOffset);
        }
        internal bool Mount(JunkerEquipmentSlot slot)
        {
            if(!slot || !isActiveAndEnabled || Grab.isSelected || currentSlot) return false;
            var inventoryItem = GetComponent<VRInventoryItem>();
            if (inventoryItem && inventoryItem.CurrentContainer) return false;
            pendingRelease=false; Activated=false; currentSlot=slot; currentState=EquipmentState.Holstered;
            if(slot.detachMountedItem && transform.parent) transform.SetParent(null,true);
            if(!Body.isKinematic) { Body.linearVelocity=Vector3.zero; Body.angularVelocity=Vector3.zero; }
            Body.isKinematic=true; Body.useGravity=false; Body.detectCollisions=true; SetTriggers(true);
            IgnorePlayerCollisions();
            transform.SetPositionAndRotation(slot.transform.TransformPoint(holsterPosition),slot.transform.rotation*Quaternion.Euler(holsterEuler));
            Pulse(.25f,.05f); return true;
        }
        internal void Unmount(JunkerEquipmentSlot slot)
        {
            if(currentSlot!=slot) return;
            currentSlot=null; currentState=EquipmentState.World;
            if(Body && !Grab.isSelected) { Body.isKinematic=false; Body.useGravity=true; }
            SetTriggers(false);
        }
        void SetTriggers(bool holstered) { for(int i=0;i<shapes.Length;i++) if(shapes[i]) shapes[i].isTrigger=holstered || triggers[i]; }
        internal void SetInventoryStored(bool stored)
        {
            pendingRelease=false; Activated=false;
            currentState=stored ? EquipmentState.StoredInBackpack : Grab.isSelected ? EquipmentState.Held : EquipmentState.World;
        }
        public void Pulse(float amplitude=.15f,float duration=.03f) { if(lastHand) lastHand.SendHapticImpulse(amplitude,duration); }
        public bool Raycast(Transform origin,float range,LayerMask mask,out RaycastHit hit)
        {
            hit=default; float nearest=range;
            foreach(var h in Physics.RaycastAll(origin.position,origin.forward,range,mask,QueryTriggerInteraction.Ignore))
            {
                if(h.collider.transform.IsChildOf(transform) || (rig && rig.body && h.collider.transform.IsChildOf(rig.body.xrOrigin))) continue;
                if(h.distance<nearest) { nearest=h.distance; hit=h; }
            }
            return hit.collider;
        }
    }
}
