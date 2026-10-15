using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;
using JunkerEquipment;

namespace JunkerScanner
{
    [DefaultExecutionOrder(12000)]
    public sealed class SalvageComponent : ScanInfoProvider
    {
        public string componentId, componentType, displayName;
        [SerializeField, Range(0, 100)] float condition = 100;
        [TextArea] public string description;
        public string[] compatibleEquipmentTags = System.Array.Empty<string>();
        public bool removable = true;
        [Tooltip("Future gameplay data; scanner does not interpret this asset.")]
        public ScriptableObject gameplayData;
        public ComponentSlot InstalledSlot { get; internal set; }
        XRGrabInteractable grab;
        Rigidbody body;
        bool pendingInstall;
        bool releasedForInstall;
        Rigidbody ignoredHost;
        Transform ignoredSocket;
        readonly List<(Collider part, Collider host)> ignoredCollisions = new();
        public bool IsInstalled => InstalledSlot;
        public float Condition { get => condition; set => condition = Mathf.Clamp(value, 0, 100); }
        public ScanStatus FunctionalStatus => condition <= 0 ? ScanStatus.Fault : condition < 100 ? ScanStatus.Damaged : ScanStatus.Operational;
        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>(); body = GetComponent<Rigidbody>();
            if (grab) { grab.throwOnDetach = false; grab.retainTransformParent = false; }
        }
        void OnEnable()
        {
            if (!grab) grab = GetComponent<XRGrabInteractable>();
            if (grab)
            {
                grab.selectEntered.AddListener(Grabbed); grab.selectExited.AddListener(Dropped);
                grab.enabled = !InstalledSlot || (InstalledSlot.IsAccessible && removable);
            }
        }
        void OnDisable()
        {
            if (grab) { grab.selectEntered.RemoveListener(Grabbed); grab.selectExited.RemoveListener(Dropped); }
            pendingInstall = false;
            releasedForInstall = false;
            if (grab) grab.enabled = false;
        }
        void Grabbed(SelectEnterEventArgs args)
        {
            pendingInstall = false;
            releasedForInstall = false;
            if (InstalledSlot && !InstalledSlot.TryRemove(out _))
                InstalledSlot.RemovePhysicallyGrabbed(this);
        }
        void Dropped(SelectExitEventArgs args) { pendingInstall = releasedForInstall = !args.isCanceled; }
        void LateUpdate()
        {
            if (!grab || !body) return;
            if ((pendingInstall || releasedForInstall) && !InstalledSlot && !grab.isSelected)
            {
                pendingInstall = false;
                var inventory = GetComponent<VRInventoryItem>();
                if (inventory && inventory.CurrentContainer) { releasedForInstall=false; return; }
                var slot = ComponentSlot.NearestAccepting(this);
                if (slot != null && slot.TryInstall(this)) { releasedForInstall=false; return; }
                body.isKinematic = false; body.useGravity = true;
            }
            if (!InstalledSlot)
            {
                if (grab.isSelected)
                {
                    var nearby=ComponentSlot.NearestApproachable(this,1f);
                    if(nearby) IgnoreHostCollisions(nearby);
                }
                if (ignoredCollisions.Count > 0 &&
                    (!ignoredSocket || Vector3.Distance(transform.position, ignoredSocket.position) > 1.25f))
                    RestoreCollisions();
                return;
            }
            if (grab.enabled != (InstalledSlot.IsAccessible && removable))
                grab.enabled = InstalledSlot.IsAccessible && removable;
            if (grab.isSelected) return;
            // Equipment mounting owns the physical pose, exactly like body tool slots.
            if(InstalledSlot.equipmentMount) return;
            // Keep the module's Rigidbody outside the moving/scaled bike hierarchy.
            // A nested Rigidbody's world-pose writes can corrupt the ancestor physics pose.
            if (transform.parent) DetachPhysicsRoot();
            var pose=InstalledSlot.installationTransform ? InstalledSlot.installationTransform : InstalledSlot.transform;
            body.isKinematic=true; body.useGravity=false;
            body.position=pose.position; body.rotation=pose.rotation;
            transform.SetPositionAndRotation(pose.position,pose.rotation);
        }
        internal void DockToSlot(ComponentSlot slot)
        {
            if (!grab) grab = GetComponent<XRGrabInteractable>();
            if (!body) body = GetComponent<Rigidbody>();
            if (!body || !grab) return; // Data-only salvage parts remain supported.
            if(slot.equipmentMount)
            {
                grab.enabled=slot.IsAccessible && removable;
                IgnoreHostCollisions(slot);
                return;
            }
            DetachPhysicsRoot();
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true; body.useGravity = false;
            body.interpolation=RigidbodyInterpolation.None;
            var pose = slot.installationTransform ? slot.installationTransform : slot.transform;
            body.position=pose.position; body.rotation=pose.rotation;
            transform.SetPositionAndRotation(pose.position,pose.rotation);
            grab.enabled = slot.IsAccessible && removable;
            IgnoreHostCollisions(slot);
        }
        internal void UndockFromSlot()
        {
            DetachPhysicsRoot();
            if (grab && !grab.enabled) grab.enabled = true;
        }
        void DetachPhysicsRoot()
        {
            if (transform.parent)
            {
                var worldScale=transform.lossyScale;
                transform.SetParent(null,true);
                transform.localScale=worldScale;
            }
        }
        void IgnoreHostCollisions(ComponentSlot slot)
        {
            var host = slot.GetComponentInParent<Rigidbody>();
            if (!host || ignoredHost==host) return;
            ignoredHost=host;
            ignoredSocket=slot.installationTransform ? slot.installationTransform : slot.transform;
            var hostColliders=new List<Collider>(host.GetComponentsInChildren<Collider>(true));
            foreach(var panel in host.GetComponentsInChildren<JunkerRepairPanel>(true))
                if(panel.compartment==slot.compartment && panel.cover)
                    hostColliders.AddRange(panel.cover.GetComponentsInChildren<Collider>(true));
            foreach (var partCollider in GetComponentsInChildren<Collider>(true))
                foreach (var hostCollider in hostColliders)
                {
                    if (!partCollider || !hostCollider || partCollider == hostCollider ||
                        partCollider.isTrigger || hostCollider.isTrigger ||
                        hostCollider.transform.IsChildOf(transform) || Physics.GetIgnoreCollision(partCollider,hostCollider)) continue;
                    Physics.IgnoreCollision(partCollider,hostCollider,true);
                    ignoredCollisions.Add((partCollider,hostCollider));
                }
        }
        void RestoreCollisions()
        {
            foreach (var pair in ignoredCollisions)
                if (pair.part && pair.host) Physics.IgnoreCollision(pair.part,pair.host,false);
            ignoredCollisions.Clear();
            ignoredHost=null;
            ignoredSocket=null;
        }
        public override void Populate(ScanInfo info)
        {
            if (!string.IsNullOrEmpty(displayName)) info.Name = displayName;
            info.Category = componentType;
            info.Condition = Condition;
            info.Status = condition <= 0 ? ScanStatus.Fault : IsInstalled ? FunctionalStatus : ScanStatus.Salvageable;
            info.Description = description;
            if (compatibleEquipmentTags.Length > 0)
                info.Description += "\nCompatible: " + string.Join(", ", compatibleEquipmentTags);
            if (condition <= 0) info.Hint = "Replacement required";
        }
        void OnDestroy() { RestoreCollisions(); if (InstalledSlot) InstalledSlot.ForgetDestroyed(this); }
    }
}
