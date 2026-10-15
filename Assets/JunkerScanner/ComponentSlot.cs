using System;
using System.Collections.Generic;
using UnityEngine;
using JunkerEquipment;

namespace JunkerScanner
{
    // Logical machine socket. Optional equipmentMount delegates physical XRI mounting to the existing system.
    public sealed class ComponentSlot : ScanInfoProvider, IHoverBikeDriveRequirement
    {
        public string componentType, equipmentTag;
        public Transform installationTransform;
        public bool accessible = true;
        public ServiceCompartment compartment;
        public ScanHighlightTarget scannerTarget;
        public JunkerEquipmentSlot equipmentMount;
        [Min(.02f), Tooltip("Release a compatible part this close to snap it into the socket. Distance includes the part's grab collider.")]
        public float installRadius = .6f;
        [SerializeField] SalvageComponent installedComponent;
        static readonly HashSet<ComponentSlot> activeSlots = new();
        public SalvageComponent InstalledComponent => installedComponent;
        public bool IsAccessible => accessible && (!compartment || compartment.IsOpen);
        public ScanStatus Status => installedComponent ? installedComponent.FunctionalStatus : ScanStatus.Missing;
        public bool AllowsDrive(float minimumCondition)
        {
            var part=equipmentMount ? (equipmentMount.Occupant ? equipmentMount.Occupant.GetComponent<SalvageComponent>() : null) : installedComponent;
            return part && part.Condition>=minimumCondition;
        }
        public event Action Changed;
        void OnEnable() { activeSlots.Add(this); }
        void OnDisable() { activeSlots.Remove(this); }
        public static ComponentSlot NearestAccepting(SalvageComponent part)
        {
            ComponentSlot best = null; float distance = float.PositiveInfinity;
            foreach (var slot in activeSlots)
            {
                if (!slot || !slot.IsAccessible || slot.installedComponent || !slot.Accepts(part)) continue;
                // A failed thrust controller removed for replacement must not reclaim
                // the empty bay when the player drops it beside the bike.
                if (slot.componentType == "Thrust Controller" && part.Condition <= 0) continue;
                float candidate = slot.DistanceTo(part);
                if (candidate <= slot.installRadius && candidate < distance) { best = slot; distance = candidate; }
            }
            return best;
        }
        public static ComponentSlot NearestApproachable(SalvageComponent part, float distanceLimit)
        {
            ComponentSlot best=null; float distance=float.PositiveInfinity;
            foreach(var slot in activeSlots)
            {
                if(!slot || !slot.IsAccessible || slot.installedComponent || !slot.Accepts(part))continue;
                float candidate=slot.DistanceTo(part);
                if(candidate<distance && candidate<=distanceLimit){best=slot;distance=candidate;}
            }
            return best;
        }
        float DistanceTo(SalvageComponent part)
        {
            var pose=installationTransform ? installationTransform : transform;
            float distance=Vector3.Distance(pose.position,part.transform.position);
            foreach(var shape in part.GetComponentsInChildren<Collider>(true))
                if(shape && shape.enabled && !shape.isTrigger)
                    distance=Mathf.Min(distance,Vector3.Distance(pose.position,shape.ClosestPoint(pose.position)));
            return distance;
        }
        void Update()
        {
            if (!equipmentMount) return;
            var occupant=equipmentMount.Occupant ? equipmentMount.Occupant.GetComponent<SalvageComponent>() : null;
            if(occupant==installedComponent)return;
            if(installedComponent) installedComponent.InstalledSlot=null;
            installedComponent=occupant;
            if(occupant) { occupant.InstalledSlot=this; occupant.DockToSlot(this); }
            Changed?.Invoke();
        }
        void Awake()
        {
            if (!installedComponent) return;
            if (installedComponent.InstalledSlot && installedComponent.InstalledSlot != this)
            { installedComponent = null; return; }
            installedComponent.InstalledSlot = this;
            if(!equipmentMount) installedComponent.DockToSlot(this);
        }
        void Start()
        {
            if(!equipmentMount || !installedComponent)return;
            var item=installedComponent.GetComponent<JunkerEquipmentItem>();
            if(equipmentMount.Occupant!=item) equipmentMount.TryMount(item);
            installedComponent.DockToSlot(this);
        }
        public bool Accepts(SalvageComponent part) => part &&
            (string.IsNullOrEmpty(componentType) || componentType == part.componentType) &&
            (string.IsNullOrEmpty(equipmentTag) || Array.IndexOf(part.compatibleEquipmentTags, equipmentTag) >= 0);
        public bool TryInstall(SalvageComponent part)
        {
            if (!IsAccessible || installedComponent || !Accepts(part) || part.IsInstalled) return false;
            var inventory = part.GetComponent<VRInventoryItem>();
            if (inventory && inventory.CurrentContainer) return false;
            if (equipmentMount)
            {
                var item = part.GetComponent<JunkerEquipmentItem>();
                if (!item || !equipmentMount.TryMount(item)) return false;
            }
            installedComponent = part; part.InstalledSlot = this;
            part.DockToSlot(this);
            Changed?.Invoke(); return true;
        }
        public bool TryRemove(out SalvageComponent part)
        {
            part = null;
            if (!IsAccessible || !installedComponent || !installedComponent.removable) return false;
            part = installedComponent;
            if (equipmentMount) equipmentMount.ReleaseItem(part.GetComponent<JunkerEquipmentItem>());
            installedComponent = null; part.InstalledSlot = null; part.UndockFromSlot();
            Changed?.Invoke(); return true;
        }
        internal void RemovePhysicallyGrabbed(SalvageComponent part)
        {
            if(!part || installedComponent!=part)return;
            installedComponent=null; part.InstalledSlot=null; part.UndockFromSlot(); Changed?.Invoke();
        }
        internal void ForgetDestroyed(SalvageComponent part)
        { if (installedComponent != part) return; installedComponent = null; Changed?.Invoke(); }
        void OnDestroy() { if (installedComponent && installedComponent.InstalledSlot == this) installedComponent.InstalledSlot = null; }
        public override void Populate(ScanInfo info) { info.Status = Status; }
    }
}
