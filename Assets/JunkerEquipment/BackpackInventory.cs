using System.Collections.Generic;
using UnityEngine;

namespace JunkerEquipment
{
    public enum BackpackState { OnBack, Held, Open }

    [DefaultExecutionOrder(12000), DisallowMultipleComponent]
    public sealed class BackpackInventory : MonoBehaviour
    {
        public JunkerEquipmentItem equipment;
        public GameObject closedVisual, openVisual;
        public Transform storedItemsRoot;
        public BackpackStorageVolume storageVolume;
        [Header("Handle and return to back")]
        public Transform topHandle;
        public JunkerEquipmentSlot backSlot;
        [Min(.05f), Tooltip("Release near the back slot to lock the bag onto it. Not limited by a tool's holster radius.")]
        public float returnRadius = .35f;
        [Tooltip("Hide stored renderers while mounted. Interaction and collision are always blocked.")]
        public bool hideContentsOnBack = true;
        readonly HashSet<VRInventoryItem> items = new();
        bool lastUsable;
        Collider grabShell;
        bool floating;
        Vector3 floatingPosition;
        Quaternion floatingRotation;
        public IReadOnlyCollection<VRInventoryItem> Items => items;
        public BackpackState State => equipment && equipment.currentSlot ? BackpackState.OnBack :
            equipment && equipment.Grab && equipment.Grab.isSelected ? BackpackState.Held : BackpackState.Open;
        // Held is already usable; no separate opening gesture is required.
        public bool IsUsable => isActiveAndEnabled && State != BackpackState.OnBack;

        void Awake()
        {
            grabShell = GetComponent<Collider>();
            if (!equipment) equipment = GetComponent<JunkerEquipmentItem>();
            if (!equipment || !equipment.Grab) return;
            var grab = equipment.Grab;
            if (topHandle) grab.attachTransform = topHandle;
            // Position attaches at the handle; dynamic rotation preserves the bag's current
            // orientation on pickup, then follows the player's wrist naturally.
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = false;
            grab.matchAttachRotation = true;
            grab.snapToColliderVolume = false;
            grab.attachEaseInTime = 0;
            grab.throwOnDetach = false;
        }
        void Start() => RefreshState(true);
        void OnEnable() => RefreshState(true);
        void LateUpdate()
        {
            if (equipment && (equipment.currentSlot || equipment.Grab.isSelected)) floating = false;
            else if (floating)
            {
                FreezeBody();
                transform.SetPositionAndRotation(floatingPosition, floatingRotation);
            }
            RefreshState(false);
        }

        internal void HoldReleasedPose()
        {
            floatingPosition = transform.position;
            floatingRotation = transform.rotation;
            floating = true;
            FreezeBody();
        }
        void FreezeBody()
        {
            var body = equipment.Body;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
        }
        internal bool TryReturnToBack()
        {
            if (!backSlot || !backSlot.isActiveAndEnabled || backSlot.Occupant || !equipment || equipment.Grab.isSelected) return false;
            Vector3 target = backSlot.transform.TransformPoint(equipment.holsterPosition);
            float distance = Vector3.Distance(transform.position, target);
            if (topHandle)
            {
                Vector3 localHandle = transform.InverseTransformPoint(topHandle.position);
                Vector3 targetHandle = target + backSlot.transform.rotation * Quaternion.Euler(equipment.holsterEuler) * Vector3.Scale(localHandle, transform.lossyScale);
                distance = Mathf.Min(distance, Vector3.Distance(topHandle.position, targetHandle));
            }
            if (distance > Mathf.Max(returnRadius, backSlot.radius) || !backSlot.TryMount(equipment)) return false;
            floating = false;
            return true;
        }
        void OnDisable() { foreach (var item in items) if (item) item.SetAccessible(false, hideContentsOnBack); }
        void OnDestroy()
        {
            // Detach surviving contents before destroying their container, never instantiate replacements.
            foreach (var item in new List<VRInventoryItem>(items)) if (item) item.ReleaseFromContainer(false);
            items.Clear();
        }
        void RefreshState(bool force)
        {
            // The bag floats when released; its shell never blocks insertion into the cavity.
            if (grabShell) grabShell.isTrigger = true;
            items.RemoveWhere(item => !item);
            bool usable = IsUsable;
            if (!force && usable == lastUsable) return;
            lastUsable = usable;
            if (closedVisual) closedVisual.SetActive(!usable);
            if (openVisual) openVisual.SetActive(usable);
            foreach (var item in items) item.SetAccessible(usable, hideContentsOnBack);
        }
        public bool CanAccept(VRInventoryItem item) => IsUsable && storedItemsRoot && item &&
            item.isActiveAndEnabled && item.CanStoreInBackpack && !item.CurrentContainer &&
            item.transform != transform && !transform.IsChildOf(item.transform) && !item.GetComponent<BackpackInventory>();

        internal bool TryStoreReleased(VRInventoryItem item, BackpackStorageVolume volume)
        {
            if (!CanAccept(item) || item.IsHeld || !volume || volume.inventory != this || !volume.Contains(item.AnchorPosition)) return false;
            if (!items.Add(item)) return false;
            item.StoreIn(this);
            return true;
        }
        internal void Forget(VRInventoryItem item) => items.Remove(item);
        void OnDrawGizmosSelected()
        {
            if (!backSlot || !equipment) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(backSlot.transform.TransformPoint(equipment.holsterPosition), Mathf.Max(returnRadius, backSlot.radius));
        }
    }
}
