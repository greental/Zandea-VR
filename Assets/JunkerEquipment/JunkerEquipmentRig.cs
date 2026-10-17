using JunkerBody;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
namespace JunkerEquipment
{
    [DefaultExecutionOrder(10500)]
    public sealed class JunkerEquipmentRig : MonoBehaviour
    {
        public VRBodyController body;
        public Collider[] playerColliders;
        public JunkerEquipmentSlot[] slots;
        [Header("Persistent body-relative placement")]
        public Vector3 bodyOffset;
        public Vector3 bodyEulerOffset;
        [Tooltip("Lower the holsters by this proportion of the HMD's crouch distance.")]
        [Range(0,1)] public float crouchFollow=.8f;
        [Header("Dropped item pickup")]
        [Min(.1f), Tooltip("Extra reach when gripping near an item resting on the ground.")]
        public float floorPickupReach=.75f;
        [Header("Melee equipment slot")]
        public Transform meleeSheath;
        public Vector3 meleeSheathLocalPosition = new Vector3(-.28f, -.42f, .06f);
        public Vector3 meleeSheathLocalEuler = new Vector3(0f, 0f, 90f);
        [Min(.1f)] public float meleeSheathRadius = .4f;
        XRDirectInteractor[] hands;
        PlayerMartialHand[] martialHands;
        bool[] gripWasPressed;
        bool[] assistedGrab;
        readonly Collider[] pickupHits=new Collider[256];
        readonly RaycastHit[] reachHits=new RaycastHit[32];

        void Start()
        {
            if(!body || !body.xrOrigin)return;
            floorPickupReach = Mathf.Max(floorPickupReach, .75f);
            if (!meleeSheath)
            {
                var anchor = new GameObject("Melee sheath").transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = meleeSheathLocalPosition;
                anchor.localRotation = Quaternion.Euler(meleeSheathLocalEuler);
                meleeSheath = anchor;
            }
            var meleeSlot = meleeSheath.GetComponent<JunkerEquipmentSlot>();
            if (!meleeSlot) meleeSlot = meleeSheath.gameObject.AddComponent<JunkerEquipmentSlot>();
            meleeSlot.meleeOnly = true;
            meleeSlot.radius = meleeSheathRadius;
            if (slots == null) slots = new[] { meleeSlot };
            else if (System.Array.IndexOf(slots, meleeSlot) < 0)
            {
                var expanded = new JunkerEquipmentSlot[slots.Length + 1];
                slots.CopyTo(expanded, 0);
                expanded[slots.Length] = meleeSlot;
                slots = expanded;
            }
            hands=body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true);
            martialHands=new PlayerMartialHand[hands.Length];
            foreach(var martial in body.GetComponentsInChildren<PlayerMartialHand>(true))
                for(int i=0;i<hands.Length;i++)if(martial.interactor==hands[i])martialHands[i]=martial;
            gripWasPressed=new bool[hands.Length];
            assistedGrab=new bool[hands.Length];
        }
        void Update()
        {
            if(hands==null)return;
            for(int i=0;i<hands.Length;i++)
            {
                var hand=hands[i];
                if(!hand || !hand.isActiveAndEnabled)continue;
                var martial=martialHands[i];
                if(martial && martial.InStance){gripWasPressed[i]=false;continue;}
                bool pressed=hand.selectInput.ReadIsPerformed();
                if(assistedGrab[i] && (!pressed || !hand.hasSelection))
                {
                    if(hand.isPerformingManualInteraction)hand.EndManualInteraction();
                    assistedGrab[i]=false;
                }
                if(pressed && !gripWasPressed[i] && !hand.hasSelection && !hand.isPerformingManualInteraction)
                {
                    IXRSelectInteractable candidate = FindPickup(hand);
                    if(candidate != null && hand.CanSelect(candidate))
                    {
                        hand.StartManualInteraction(candidate);
                        assistedGrab[i]=hand.isPerformingManualInteraction;
                    }
                }
                gripWasPressed[i]=pressed;
            }
        }
        IXRSelectInteractable FindPickup(XRDirectInteractor hand)
        {
            var point = hand.transform.position;
            int count = Physics.OverlapSphereNonAlloc(point, floorPickupReach, pickupHits, ~0, QueryTriggerInteraction.Collide);
            XRGrabInteractable closest = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var shape = pickupHits[i];
                if (!shape) continue;
                var weapon = shape.GetComponentInParent<PhysicalMeleeWeapon>();
                var loot = shape.GetComponentInParent<NpcWorldItem>();
                var clothing = shape.GetComponentInParent<NpcClothingLoot>();
                var equipment = shape.GetComponentInParent<JunkerEquipmentItem>();
                var grab = weapon ? weapon.Grab : equipment ? equipment.Grab : shape.GetComponentInParent<XRGrabInteractable>();
                if (weapon && weapon.State != PhysicalMeleeWeapon.HoldState.World) continue;
                if (equipment && (equipment.state != EquipmentState.World || !equipment.Body ||
                    equipment.Body.linearVelocity.sqrMagnitude > 2.25f || shape.isTrigger ||
                    !IsRestingOnSurface(equipment, shape))) continue;
                if (!weapon && !equipment && !loot && !clothing) continue;
                if (!grab || !grab.isActiveAndEnabled || grab.isSelected) continue;
                var contact = shape.ClosestPoint(point);
                float distance = (contact - point).sqrMagnitude;
                if (distance >= best || !hand.CanSelect((IXRSelectInteractable)grab) ||
                    !HasClearReach(point, contact, clothing ? clothing.transform : grab.transform)) continue;
                best = distance;
                closest = grab;
            }
            return closest ? (IXRSelectInteractable)closest : null;
        }
        bool HasClearReach(Vector3 from, Vector3 to, Transform targetRoot)
        {
            var delta = to - from;
            float distance = delta.magnitude;
            if (distance < .02f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, reachHits, distance - .015f,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = reachHits[i].collider;
                if (!collider || collider.transform.IsChildOf(targetRoot) ||
                    body && body.xrOrigin && collider.transform.IsChildOf(body.xrOrigin)) continue;
                var fallenNpc = collider.GetComponentInParent<ScavengerHealth>();
                if (fallenNpc && (fallenNpc.IsDead || fallenNpc.IsUnconscious)) continue;
                return false;
            }
            return true;
        }
        public bool HasAvailableFloorPickup(XRDirectInteractor hand)
        {
            return hand && FindPickup(hand) != null;
        }
        bool IsRestingOnSurface(JunkerEquipmentItem item,Collider shape)
        {
            var bounds=shape.bounds;
            var origin=bounds.center+Vector3.up*.3f;
            foreach(var hit in Physics.RaycastAll(origin,Vector3.down,bounds.size.y+.8f,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.transform.IsChildOf(item.transform))continue;
                if(body && body.xrOrigin && hit.collider.transform.IsChildOf(body.xrOrigin))continue;
                if(hit.normal.y>.5f && Mathf.Abs(bounds.min.y-hit.point.y)<.25f)return true;
            }
            return false;
        }
        void LateUpdate()
        {
            if(!body) return;
            var head=body.xrOrigin.InverseTransformPoint(body.hmd.position);
            var floor=body.xrOrigin.TransformPoint(new Vector3(head.x,0,head.z));
            float correctedStandingHeight=body.standingHeight+(body.trackingCalibration?body.trackingCalibration.floorOffset:0f);
            var down=Mathf.Max(0,correctedStandingHeight-head.y)*crouchFollow;
            transform.SetPositionAndRotation(floor-body.xrOrigin.up*down+body.avatarRoot.rotation*bodyOffset,body.avatarRoot.rotation*Quaternion.Euler(bodyEulerOffset));
        }
        public bool TryHolster(JunkerEquipmentItem item)
        {
            if(!item || item.Grab.isSelected || item.currentSlot) return false;
            JunkerEquipmentSlot best=null; float distance=float.PositiveInfinity;
            foreach(var slot in slots)
            {
                if(!slot || !slot.isActiveAndEnabled || slot.Occupant || !slot.Accepts(item)) continue;
                var target=slot.transform.TransformPoint(item.holsterPosition);
                float d=Vector3.Distance(item.transform.position,target);
                if(item.gripPose) d=Mathf.Min(d,Vector3.Distance(item.gripPose.position,target));
                if(d<=Mathf.Min(slot.radius,item.holsterRadius) && d<distance) { best=slot; distance=d; }
            }
            return best && best.TryMount(item);
        }
    }
}
