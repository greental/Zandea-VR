using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using KenshiVR.MeleeCombat;

namespace JunkerEquipment
{
    public enum VRInventoryState { World, Held, Holstered, StoredInBackpack }

    /// <summary>Storage owns a pose and a reversible physics snapshot, never an item copy.</summary>
    [DefaultExecutionOrder(11500), DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public sealed class VRInventoryItem : MonoBehaviour
    {
        public bool CanStoreInBackpack = true;
        public Transform InventoryAnchor;
        public BackpackInventory CurrentContainer { get; private set; }
        public bool IsHeld => grab && grab.isSelected;
        public Vector3 AnchorPosition => InventoryAnchor ? InventoryAnchor.position : transform.position;
        public VRInventoryState CurrentState => CurrentContainer ? VRInventoryState.StoredInBackpack : IsHeld ? VRInventoryState.Held :
            equipment && equipment.currentSlot ? VRInventoryState.Holstered : VRInventoryState.World;
        XRGrabInteractable grab;
        Rigidbody body;
        JunkerEquipmentItem equipment;
        BackpackStorageVolume candidate;
        XRBaseInputInteractor hand;
        bool normalKinematic, normalGravity, normalDetect;
        bool savedRetainParent, parentPolicySuppressed;
        RigidbodyInterpolation normalInterpolation;
        bool restoreAfterStoredGrab, savedThrow, throwSuppressed;
        bool snapshotValid;
        bool releaseArmed;
        Vector3 storedPosition;
        Quaternion storedRotation;
        Vector3 uncontainedWorldScale;

        struct ShapeState { public Collider shape; public bool trigger, enabled; }
        struct VisualState { public Renderer renderer; public bool forcedOff; }
        struct InteractionState { public XRBaseInteractable interactable; public InteractionLayerMask layers; }
        struct BodyState { public Rigidbody body; public bool kinematic, gravity, detect; public RigidbodyInterpolation interpolation; }
        readonly List<ShapeState> shapes = new();
        readonly List<VisualState> visuals = new();
        readonly List<InteractionState> interactions = new();
        readonly List<BodyState> bodies = new();

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>(); body = GetComponent<Rigidbody>(); equipment = GetComponent<JunkerEquipmentItem>();
            normalKinematic = body.isKinematic; normalGravity = body.useGravity;
            normalDetect = body.detectCollisions; normalInterpolation = body.interpolation;
            grab.selectEntered.AddListener(Selected);
            grab.selectExited.AddListener(Released);
        }
        void OnDisable() { releaseArmed = false; SetCandidate(null); }
        void OnDestroy()
        {
            SetCandidate(null);
            if (CurrentContainer) CurrentContainer.Forget(this);
            if (grab) { grab.selectEntered.RemoveListener(Selected); grab.selectExited.RemoveListener(Released); }
        }
        void Update() => SetCandidate(IsHeld && !CurrentContainer ? BackpackStorageVolume.FindFor(this) : null);
        void SetCandidate(BackpackStorageVolume value)
        {
            if (candidate == value) return;
            if (candidate) candidate.SetCandidate(this, false);
            candidate = value;
            if (candidate)
            {
                candidate.SetCandidate(this, true);
                if (hand) hand.SendHapticImpulse(.18f, .035f);
            }
        }
        void Selected(SelectEnterEventArgs args)
        {
            releaseArmed = true;
            hand = args.interactorObject as XRBaseInputInteractor;
            if (CurrentContainer) ReleaseFromContainer(true);
        }
        void Released(SelectExitEventArgs args) => TryStoreOnRelease(args);

        // Called by the existing equipment release callback before it considers a body holster.
        internal bool TryStoreOnRelease(SelectExitEventArgs args)
        {
            if (CurrentContainer) return true;
            if (IsHeld) return false;
            if (!releaseArmed) return false;
            releaseArmed = false;
            if (restoreAfterStoredGrab)
            {
                RestoreNormalBody();
                restoreAfterStoredGrab = false;
            }
            var volume = !args.isCanceled && isActiveAndEnabled ? BackpackStorageVolume.FindFor(this) : null;
            SetCandidate(null);
            return volume && volume.inventory.TryStoreReleased(this, volume);
        }

        internal void StoreIn(BackpackInventory container)
        {
            // XRI Drop has already run; these are release-time settings, not held-time settings.
            normalKinematic = body.isKinematic; normalGravity = body.useGravity;
            normalDetect = body.detectCollisions; normalInterpolation = body.interpolation;
            shapes.Clear(); visuals.Clear(); interactions.Clear(); bodies.Clear();
            foreach (var shape in GetComponentsInChildren<Collider>(true))
                shapes.Add(new ShapeState { shape = shape, trigger = shape.isTrigger, enabled = shape.enabled });
            foreach (var visual in GetComponentsInChildren<Renderer>(true))
                visuals.Add(new VisualState { renderer = visual, forcedOff = visual.forceRenderingOff });
            foreach (var interaction in GetComponentsInChildren<XRBaseInteractable>(true))
                interactions.Add(new InteractionState { interactable = interaction, layers = interaction.interactionLayers });
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                bodies.Add(new BodyState { body = rb, kinematic = rb.isKinematic, gravity = rb.useGravity, detect = rb.detectCollisions, interpolation = rb.interpolation });
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            }
            snapshotValid = true;
            CurrentContainer = container;
            uncontainedWorldScale = transform.lossyScale;
            transform.SetParent(container.storedItemsRoot, true);
            storedPosition = transform.localPosition; storedRotation = transform.localRotation;
            savedThrow = grab.throwOnDetach; grab.throwOnDetach = false; throwSuppressed = true;
            if (equipment) equipment.SetInventoryStored(true);
            SetAccessible(container.IsUsable, container.hideContentsOnBack);
        }

        internal void SetAccessible(bool accessible, bool hide)
        {
            if (!snapshotValid) return;
            foreach (var state in shapes)
                if (state.shape)
                {
                    // Non-convex mesh colliders cannot become triggers. They remain disabled in storage.
                    bool supported = !(state.shape is MeshCollider mesh) || mesh.convex;
                    if (supported) state.shape.isTrigger = true;
                    state.shape.enabled = accessible && supported && state.enabled;
                }
            foreach (var state in visuals) if (state.renderer) state.renderer.forceRenderingOff = state.forcedOff || (!accessible && hide);
            foreach (var state in interactions)
                if (state.interactable) state.interactable.interactionLayers = accessible && state.interactable == grab ? state.layers : (InteractionLayerMask)0;
            var garment = GetComponent<WearableLoot>();
            if (garment) garment.SetStoredPresentation(true, accessible, hide);
        }

        internal void ReleaseFromContainer(bool held)
        {
            var container = CurrentContainer;
            CurrentContainer = null;
            if (container) container.Forget(this);
            transform.SetParent(null, true);
            if (snapshotValid) transform.localScale = uncontainedWorldScale;
            if (snapshotValid)
            {
                foreach (var state in shapes) if (state.shape) { state.shape.isTrigger = state.trigger; state.shape.enabled = state.enabled; }
                foreach (var state in visuals) if (state.renderer) state.renderer.forceRenderingOff = state.forcedOff;
                foreach (var state in interactions) if (state.interactable) state.interactable.interactionLayers = state.layers;
                foreach (var state in bodies)
                    if (state.body)
                    { state.body.isKinematic = state.kinematic; state.body.useGravity = state.gravity; state.body.detectCollisions = state.detect; state.body.interpolation = state.interpolation; }
                snapshotValid = false;
            }
            if (equipment) equipment.SetInventoryStored(false);
            var garment = GetComponent<WearableLoot>();
            if (garment) garment.SetStoredPresentation(false, true, false);
            RestoreThrow();
            if (held)
            {
                // XRI remembered StoredItemsRoot before selectEntered. Prevent its next Drop
                // from reparenting a removed item back into the backpack.
                if (!parentPolicySuppressed) savedRetainParent = grab.retainTransformParent;
                grab.retainTransformParent = false;
                parentPolicySuppressed = true;
                // Selection already applied XRI motion settings. Its cached drop state was the stored
                // kinematic state, so restore the pre-storage baseline on the next release as well.
                body.isKinematic = grab.movementType != XRBaseInteractable.MovementType.VelocityTracking;
                body.useGravity = false;
                restoreAfterStoredGrab = true;
            }
        }
        void RestoreNormalBody()
        {
            body.isKinematic = normalKinematic; body.useGravity = normalGravity;
            body.detectCollisions = normalDetect; body.interpolation = normalInterpolation;
        }
        void RestoreThrow() { if (!throwSuppressed || !grab) return; grab.throwOnDetach = savedThrow; throwSuppressed = false; }
        void LateUpdate()
        {
            // Runs after XRI's deferred detach and the equipment holster pass, before rendering.
            RestoreThrow();
            if (parentPolicySuppressed && !IsHeld)
            {
                grab.retainTransformParent = savedRetainParent;
                parentPolicySuppressed = false;
            }
            if (!CurrentContainer) return;
            transform.SetLocalPositionAndRotation(storedPosition, storedRotation);
        }
    }
}
