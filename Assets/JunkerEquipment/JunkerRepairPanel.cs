using System.Collections.Generic;
using JunkerScanner;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment
{
    public enum RepairPanelPlacement { Closed, Held, Stored, Free }

    [DefaultExecutionOrder(12000), DisallowMultipleComponent]
    public sealed class JunkerRepairPanel : MonoBehaviour
    {
        [Tooltip("Collect child screws on startup. Duplicate or delete screw children freely.")]
        public bool autoCollectScrews = true;
        public JunkerScrew[] screws = System.Array.Empty<JunkerScrew>();
        public XRGrabInteractable cover;
        public TextMesh status;
        [Header("Docking poses (outside the movable cover)")]
        [Tooltip("Desired cover-root position/rotation when closed.")]
        public Transform closedAnchor;
        [Tooltip("Desired cover-root pose while stored. Move this child in the Inspector.")]
        public Transform storageAnchor;
        [Min(.01f)] public float snapDistance = .15f;
        [Range(0,180)] public float snapAngle = 40;
        [Tooltip("Release outside the closed snap zone to return the cover to storage automatically.")]
        public bool storeOnRelease = true;
        [Header("Optional scanner access")]
        public ServiceCompartment compartment;
        public UnityEvent<bool> openChanged = new();
        public RepairPanelPlacement Placement { get; private set; } = RepairPanelPlacement.Closed;
        public bool IsSeated => Placement == RepairPanelPlacement.Closed;
        public bool CanTurnScrews => isActiveAndEnabled && IsSeated && cover && !cover.isSelected;
        public bool Unlocked
        {
            get { foreach(var screw in screws) if(screw && screw.isActiveAndEnabled && !screw.IsLoose) return false; return true; }
        }
        Rigidbody body;
        bool pendingRelease, accessOpen;
        Vector3 coverWorldScale;
        readonly List<(Collider a, Collider b)> ignoredPairs = new();

        void Awake()
        {
            if(!cover) { enabled=false; return; }
            body=cover.GetComponent<Rigidbody>();
            if(!body) { enabled=false; return; }
            // Keep the physical root outside the machine's scaled hierarchy. Repeated
            // preserve-world reparenting through nonuniform scale shears/shrinks it.
            cover.transform.SetParent(null,true);
            coverWorldScale=cover.transform.localScale;
            if(!closedAnchor)
            {
                closedAnchor=new GameObject("Closed Anchor").transform;
                closedAnchor.SetParent(transform,false);
                closedAnchor.SetPositionAndRotation(cover.transform.position,cover.transform.rotation);
            }
            if(autoCollectScrews) RefreshScrews();
            else foreach(var screw in screws) if(screw) screw.BindToPanel(this);
            cover.retainTransformParent=false;
            cover.movementType=XRBaseInteractable.MovementType.Instantaneous;
            cover.throwOnDetach=false;
            Dock(RepairPanelPlacement.Closed,closedAnchor);
        }
        void OnEnable()
        {
            if(!cover) return;
            cover.selectEntered.AddListener(Grabbed);
            cover.selectExited.AddListener(Released);
            IgnoreFrameCollisions();
        }
        [ContextMenu("Refresh panel screws")]
        public void RefreshScrews()
        {
            var found=new HashSet<JunkerScrew>();
            foreach(var screw in GetComponentsInChildren<JunkerScrew>(true))
                if(screw.GetComponentInParent<JunkerRepairPanel>()==this) found.Add(screw);
            if(cover) foreach(var screw in cover.GetComponentsInChildren<JunkerScrew>(true)) found.Add(screw);
            screws=new JunkerScrew[found.Count]; found.CopyTo(screws);
            foreach(var screw in screws) screw.BindToPanel(this);
        }
        void Grabbed(SelectEnterEventArgs args)
        {
            pendingRelease=false; Placement=RepairPanelPlacement.Held;
            SetAccess(true);
        }
        void Released(SelectExitEventArgs args)
        {
            if(cover.isSelected) return;
            pendingRelease=!args.isCanceled;
            if(args.isCanceled) Placement=RepairPanelPlacement.Free;
        }
        void LateUpdate()
        {
            if(!body || !cover) return;
            // After XRI's detach bookkeeping. Never snap a held cover.
            if(pendingRelease && !cover.isSelected)
            {
                pendingRelease=false;
                if(!TrySeat() && !(storeOnRelease && TryStore()))
                {
                    if(!Near(storageAnchor) || !TryStore())
                    { Placement=RepairPanelPlacement.Free; body.isKinematic=false; body.useGravity=true; }
                }
            }
            if(cover.isSelected) return;
            if(IsSeated) Follow(closedAnchor);
            else if(Placement==RepairPanelPlacement.Stored) Follow(storageAnchor);
            cover.enabled=!IsSeated || Unlocked;
            if(status) status.text=IsSeated?(Unlocked?"UNFASTENED - grip to remove":"Loosen screws to open / tighten to secure"):
                Placement==RepairPanelPlacement.Stored?"PANEL STORED - grip and return to opening":"Return panel to opening and release";
        }
        bool ValidAnchor(Transform anchor) => anchor && cover && anchor!=cover.transform && !anchor.IsChildOf(cover.transform);
        bool Near(Transform anchor) => ValidAnchor(anchor) && Vector3.Distance(cover.transform.position,anchor.position)<=snapDistance &&
            Quaternion.Angle(cover.transform.rotation,anchor.rotation)<=snapAngle;
        public bool TrySeat()
        {
            if(!body || !cover || cover.isSelected || !Unlocked || !Near(closedAnchor)) return false;
            Dock(RepairPanelPlacement.Closed,closedAnchor); return true;
        }
        public bool TryStore()
        {
            if(!body || !cover || cover.isSelected || !Unlocked || !ValidAnchor(storageAnchor)) return false;
            Dock(RepairPanelPlacement.Stored,storageAnchor); return true;
        }
        void Dock(RepairPanelPlacement placement,Transform anchor)
        {
            if(!ValidAnchor(anchor)) return;
            Placement=placement;
            if(!body.isKinematic) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; }
            body.isKinematic=true; body.useGravity=false;
            if(cover.transform.parent) cover.transform.SetParent(null,true);
            cover.transform.localScale=coverWorldScale;
            Follow(anchor);
            cover.enabled=!IsSeated || Unlocked;
            SetAccess(!IsSeated);
        }
        void Follow(Transform anchor)
        {
            if(!ValidAnchor(anchor)) return;
            body.position=anchor.position; body.rotation=anchor.rotation;
            cover.transform.SetPositionAndRotation(anchor.position,anchor.rotation);
        }
        void SetAccess(bool open)
        {
            if(compartment) compartment.SetOpen(open);
            if(accessOpen==open) return;
            accessOpen=open; openChanged.Invoke(open);
        }
        void IgnoreFrameCollisions()
        {
            // A parked kinematic cover must not push its vehicle's hull.
            var host=GetComponentInParent<Rigidbody>();
            var frameColliders=host?host.GetComponentsInChildren<Collider>(true):GetComponentsInChildren<Collider>(true);
            foreach(var a in cover.GetComponentsInChildren<Collider>(true))
                foreach(var b in frameColliders)
                {
                    if(!a || !b || a.isTrigger || b.isTrigger || b.transform.IsChildOf(cover.transform) || Physics.GetIgnoreCollision(a,b)) continue;
                    Physics.IgnoreCollision(a,b,true); ignoredPairs.Add((a,b));
                }
        }
        void OnDisable()
        {
            pendingRelease=false;
            if(cover) { cover.selectEntered.RemoveListener(Grabbed); cover.selectExited.RemoveListener(Released); }
            foreach(var pair in ignoredPairs) if(pair.a && pair.b) Physics.IgnoreCollision(pair.a,pair.b,false);
            ignoredPairs.Clear();
        }
        void OnDestroy()
        {
            // The parked cover is a scene root at runtime. Do not leave an orphan
            // when its owning vehicle/panel is destroyed.
            if(cover && !cover.isSelected && Application.isPlaying) Destroy(cover.gameObject);
        }
        void OnDrawGizmosSelected()
        {
            if(closedAnchor) { Gizmos.color=Color.green; Gizmos.DrawWireSphere(closedAnchor.position,snapDistance); Gizmos.DrawRay(closedAnchor.position,closedAnchor.forward*.15f); }
            if(storageAnchor) { Gizmos.color=Color.cyan; Gizmos.DrawWireCube(storageAnchor.position,Vector3.one*.12f); Gizmos.DrawRay(storageAnchor.position,storageAnchor.forward*.15f); }
        }
    }
}
