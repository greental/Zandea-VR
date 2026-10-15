using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace JunkerEquipment.Thermal
{
    [RequireComponent(typeof(JunkerEquipmentItem),typeof(XRGeneralGrabTransformer))]
    public sealed class ThermalToolController : MonoBehaviour
    {
        public Transform nozzle;
        public JunkerPistolBatterySocket batterySocket;
        public ThermalFXController effects;
        public ThermalMode mode;
        public InputActionProperty leftClick=new(new InputAction("Thermal left mode",InputActionType.Button,"<XRController>{LeftHand}/{Primary2DAxisClick}"));
        public InputActionProperty rightClick=new(new InputAction("Thermal right mode",InputActionType.Button,"<XRController>{RightHand}/{Primary2DAxisClick}"));
        public float rayDistance=.6f,beamReach=.22f,interactionRadius=.025f,guideRadius=.15f,progressionSpeed=1,power=1,modeCooldown=.2f;
        [Tooltip("Empty accepts all target material IDs.")] public string compatibleMaterial;
        public LayerMask targetLayers=~0;
        public bool haptics=true;
        public ThermalWorkTarget CurrentTarget { get; private set; }
        JunkerEquipmentItem item;
        XRGrabInteractable grab;
        IXRSelectInteractor holder;
        bool ownLeft,ownRight,leftLatched,rightLatched,fireArmed;
        float nextToggle,nextHaptic;
        RaycastHit[] hits=new RaycastHit[32];
        void Awake()
        {
            item=GetComponent<JunkerEquipmentItem>(); grab=GetComponent<XRGrabInteractable>();
            foreach(var transformer in GetComponents<XRGeneralGrabTransformer>()) { transformer.allowOneHandedScaling=false; transformer.allowTwoHandedScaling=false; }
        }
        void OnEnable()
        {
            ownLeft=leftClick.action!=null && !leftClick.action.enabled; ownRight=rightClick.action!=null && !rightClick.action.enabled;
            if(ownLeft) leftClick.action.Enable(); if(ownRight) rightClick.action.Enable();
            if(leftClick.action!=null) { leftClick.action.performed+=Left; leftClick.action.canceled+=LeftRelease; }
            if(rightClick.action!=null) { rightClick.action.performed+=Right; rightClick.action.canceled+=RightRelease; }
            leftLatched=leftClick.action?.IsPressed()??false; rightLatched=rightClick.action?.IsPressed()??false;
            grab.selectEntered.AddListener(Grabbed); grab.selectExited.AddListener(Dropped); grab.deactivated.AddListener(Released);
        }
        void Grabbed(SelectEnterEventArgs args)
        {
            ReleaseClick(); holder=args.interactorObject;
            if(holder.handedness!=InteractorHandedness.None) JunkerBody.JunkerSprint.ReserveStickClick(this,holder.handedness==InteractorHandedness.Left,true);
            fireArmed=false; Stop();
        }
        void ReleaseClick() { JunkerBody.JunkerSprint.ReserveStickClick(this,true,false); JunkerBody.JunkerSprint.ReserveStickClick(this,false,false); }
        void Dropped(SelectExitEventArgs args) { ReleaseClick(); holder=null; fireArmed=false; Stop(); }
        void Released(DeactivateEventArgs args) { Stop(); }
        void Left(InputAction.CallbackContext _) { if(!leftLatched) Toggle(InteractorHandedness.Left); leftLatched=true; }
        void Right(InputAction.CallbackContext _) { if(!rightLatched) Toggle(InteractorHandedness.Right); rightLatched=true; }
        void LeftRelease(InputAction.CallbackContext _) { leftLatched=false; }
        void RightRelease(InputAction.CallbackContext _) { rightLatched=false; }
        void Toggle(InteractorHandedness hand)
        {
            if(!grab.isSelected || holder==null || holder.handedness!=hand || Time.unscaledTime<nextToggle) return;
            nextToggle=Time.unscaledTime+modeCooldown;
            mode=mode==ThermalMode.Cut?ThermalMode.Weld:ThermalMode.Cut;
            fireArmed=false; Stop(); if(effects) effects.Switch(); if(haptics) item.Pulse(.25f,.05f);
        }
        void Update()
        {
            if(!grab.isSelected || holder==null || !nozzle || !item.isActiveAndEnabled || item.state!=EquipmentState.Held) { Stop(); return; }
            if(!item.Activated) fireArmed=true;
            bool active=item.Activated && fireArmed && (!batterySocket || batterySocket.TryConsumeToolTime(Time.deltaTime));
            // Non-alloc, nearest obstruction wins; ignore only this tool and its player.
            int count;
            while((count=Physics.RaycastNonAlloc(nozzle.position,nozzle.forward,hits,Mathf.Max(0,rayDistance),targetLayers,QueryTriggerInteraction.Ignore))==hits.Length) System.Array.Resize(ref hits,hits.Length*2);
            RaycastHit hit=default; float nearest=rayDistance;
            for(int i=0;i<count;i++)
            {
                var c=hits[i].collider;
                if(c.transform.IsChildOf(transform) || (item.rig && item.rig.body && c.transform.IsChildOf(item.rig.body.xrOrigin))) continue;
                if(hits[i].distance<=nearest) { nearest=hits[i].distance; hit=hits[i]; }
            }
            var target=hit.collider?hit.collider.GetComponentInParent<ThermalWorkTarget>():null;
            if(!target && hit.collider) { var link=hit.collider.GetComponentInParent<ThermalTargetLink>(); if(link) target=link.target; }
            if(target && (!target.isActiveAndEnabled || !target.Accepts(mode) || !target.Current || !target.Current.Near(hit.point,guideRadius))) target=null;
            // The scanner owns the red guide. The tool only draws the heated worked seam.
            CurrentTarget=target;
            bool worked=false;
            bool touching=active && target && nearest<=beamReach && target.Current.Near(hit.point,interactionRadius+target.Current.tolerance);
            if(touching)
            {
                var workingPath=target.Current;
                float before=target.Progress;
                worked=target.Work(mode,hit.point,power,compatibleMaterial,Time.deltaTime,progressionSpeed);
                if(worked && workingPath && effects)
                {
                    var heat=workingPath.GetComponent<ThermalPathHeatView>();
                    if(!heat) { heat=workingPath.gameObject.AddComponent<ThermalPathHeatView>(); heat.Initialize(effects.PathHeatMaterial); }
                    heat.Record(workingPath.Distance,hit.normal,mode);
                }
                if(worked && (!target.Current || target.Progress<before) && effects) effects.Completed(mode,hit.point);
                if(haptics && Time.unscaledTime>=nextHaptic) { item.Pulse(mode==ThermalMode.Cut ? .095f : .065f,mode==ThermalMode.Cut ? .024f : .014f); nextHaptic=Time.unscaledTime+(mode==ThermalMode.Cut ? .12f : .09f); }
            }
            if(effects) effects.Render(mode,active,touching,nozzle.position,touching?hit.point:nozzle.position+nozzle.forward*Mathf.Min(beamReach,nearest),touching?hit.normal:-nozzle.forward);
        }
        void Stop() { CurrentTarget=null; if(effects) effects.Stop(); }
        void OnDisable()
        {
            ReleaseClick(); Stop(); holder=null; fireArmed=false;
            grab.selectEntered.RemoveListener(Grabbed); grab.selectExited.RemoveListener(Dropped); grab.deactivated.RemoveListener(Released);
            if(leftClick.action!=null) { leftClick.action.performed-=Left; leftClick.action.canceled-=LeftRelease; if(ownLeft) leftClick.action.Disable(); }
            if(rightClick.action!=null) { rightClick.action.performed-=Right; rightClick.action.canceled-=RightRelease; if(ownRight) rightClick.action.Disable(); }
        }
        void OnDrawGizmosSelected() { if(!nozzle) return; Gizmos.color=mode==ThermalMode.Cut?Color.red:Color.cyan; Gizmos.DrawLine(nozzle.position,nozzle.position+nozzle.forward*beamReach); Gizmos.DrawWireSphere(nozzle.position+nozzle.forward*beamReach,interactionRadius); }
    }
}
