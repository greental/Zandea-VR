using JunkerBody;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using KenshiVR.MeleeCombat;
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
        public float floorPickupReach=.55f;
        XRDirectInteractor[] hands;
        PlayerMartialHand[] martialHands;
        bool[] gripWasPressed;
        bool[] assistedGrab;
        readonly Collider[] pickupHits=new Collider[64];

        void Start()
        {
            if(!body || !body.xrOrigin)return;
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
                    var item=FindFloorPickup(hand.transform.position);
                    if(item && hand.CanSelect((IXRSelectInteractable)item.Grab))
                    {
                        hand.StartManualInteraction((IXRSelectInteractable)item.Grab);
                        assistedGrab[i]=hand.isPerformingManualInteraction;
                    }
                }
                gripWasPressed[i]=pressed;
            }
        }
        JunkerEquipmentItem FindFloorPickup(Vector3 handPosition)
        {
            int count=Physics.OverlapSphereNonAlloc(handPosition,floorPickupReach,pickupHits,~0,QueryTriggerInteraction.Ignore);
            JunkerEquipmentItem closest=null;
            float bestDistance=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var shape=pickupHits[i];
                if(!shape)continue;
                var item=shape.GetComponentInParent<JunkerEquipmentItem>();
                if(!item || !item.isActiveAndEnabled || item.state!=EquipmentState.World || !item.Grab || !item.Grab.isActiveAndEnabled || item.Grab.isSelected)continue;
                // Keep this assist for items resting on a surface, not objects being thrown.
                if(!item.Body || item.Body.linearVelocity.sqrMagnitude>2.25f || !IsRestingOnSurface(item,shape))continue;
                float distance=(shape.ClosestPoint(handPosition)-handPosition).sqrMagnitude;
                if(distance<bestDistance){bestDistance=distance;closest=item;}
            }
            return closest;
        }
        public bool HasAvailableFloorPickup(XRDirectInteractor hand)
        {
            if (!hand) return false;
            var item=FindFloorPickup(hand.transform.position);
            return item && hand.CanSelect((IXRSelectInteractable)item.Grab);
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
