using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerBody;
namespace JunkerEquipment
{
    public enum PoweredScrewdriverMode { Tighten, Loosen }
    [RequireComponent(typeof(JunkerEquipmentItem))]
    public sealed class JunkerScrewdriver : MonoBehaviour
    {
        public Transform tip, spinningBit;
        public Vector3 spinningAxis = Vector3.forward;
        public JunkerPistolBatterySocket batterySocket;
        public bool powered;
        public PoweredToolAudio motorAudio;
        [HideInInspector] public bool reverse=true;
        public PoweredScrewdriverMode poweredMode = PoweredScrewdriverMode.Tighten;
        [Tooltip("Right stick click toggles direction only while the right hand holds this tool.")]
        public InputActionProperty directionToggle = new InputActionProperty(new InputAction("Toggle screwdriver direction",InputActionType.Button,"<XRController>{RightHand}/{Primary2DAxisClick}"));
        [Tooltip("Left stick click toggles direction only while the left hand holds this tool.")]
        public InputActionProperty leftDirectionToggle = new InputActionProperty(new InputAction("Toggle screwdriver direction left",InputActionType.Button,"<XRController>{LeftHand}/{Primary2DAxisClick}"));
        public bool IsLoosening => poweredMode == PoweredScrewdriverMode.Loosen;
        public float contactTolerance=.025f, angleTolerance=22, turnsPerSecond=2, wristSensitivity=1;
        [Tooltip("Optional legacy targets. Nearby screws are detected automatically, including copied panels.")]
        public JunkerScrew[] screws = System.Array.Empty<JunkerScrew>();
        public bool detectNearbyScrews = true;
        [Tooltip("Engage by tip proximity on meshes whose visible axis differs from the grip axis.")] public bool useTipProximityOnly;
        public LayerMask screwLayers = ~0;
        public JunkerScrew Engaged { get; private set; }
        JunkerEquipmentItem item;
        Vector3 previousUp;
        float nextHaptic;
        bool ownsToggleAction, ownsLeftToggleAction;
        XRGrabInteractable grab;
        UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness holdingHand;
        Collider[] nearbyScrews = new Collider[16];
        void Awake() { item=GetComponent<JunkerEquipmentItem>(); grab=GetComponent<XRGrabInteractable>(); }
        void OnEnable()
        {
            if(!powered) return;
            if(directionToggle.action==null)
                directionToggle=new InputActionProperty(new InputAction("Toggle screwdriver direction",InputActionType.Button,"<XRController>{RightHand}/{Primary2DAxisClick}"));
            if(leftDirectionToggle.action==null)
                leftDirectionToggle=new InputActionProperty(new InputAction("Toggle screwdriver direction left",InputActionType.Button,"<XRController>{LeftHand}/{Primary2DAxisClick}"));
            ownsToggleAction=!directionToggle.action.enabled;
            if(ownsToggleAction) directionToggle.action.Enable();
            ownsLeftToggleAction=!leftDirectionToggle.action.enabled;
            if(ownsLeftToggleAction) leftDirectionToggle.action.Enable();
            if(!grab) grab=GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(Grabbed);
            grab.selectExited.AddListener(Dropped);
        }
        void OnDisable()
        {
            if(motorAudio) motorAudio.SetRunning(false);
            Disengage(); ClearHand();
            if(grab) { grab.selectEntered.RemoveListener(Grabbed); grab.selectExited.RemoveListener(Dropped); }
            if(ownsToggleAction) directionToggle.action?.Disable();
            if(ownsLeftToggleAction) leftDirectionToggle.action?.Disable();
            ownsToggleAction=ownsLeftToggleAction=false;
        }
        void Grabbed(SelectEnterEventArgs args)
        {
            ClearHand();
            holdingHand=args.interactorObject.handedness;
            if(holdingHand!=UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.None)
                JunkerSprint.ReserveStickClick(this,holdingHand==UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left,true);
        }
        void Dropped(SelectExitEventArgs args) { if(!grab || !grab.isSelected) ClearHand(); }
        void ClearHand()
        {
            JunkerSprint.ReserveStickClick(this,true,false);
            JunkerSprint.ReserveStickClick(this,false,false);
            holdingHand=UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.None;
        }
        void Disengage() { if(Engaged) Engaged.Release(this); Engaged=null; }
        void Update()
        {
            bool running=powered && item && item.Grab && item.Grab.isSelected && item.Activated && (!batterySocket || batterySocket.TryConsumeToolTime(Time.deltaTime));
            if(motorAudio) motorAudio.SetRunning(running);
            bool modeClick=holdingHand==UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left
                ? leftDirectionToggle.action?.WasPressedThisFrame() ?? false
                : holdingHand==UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right && (directionToggle.action?.WasPressedThisFrame() ?? false);
            if(powered && item.Grab && item.Grab.isSelected && modeClick)
            {
                poweredMode=poweredMode==PoweredScrewdriverMode.Tighten ? PoweredScrewdriverMode.Loosen : PoweredScrewdriverMode.Tighten;
                item.Pulse(.3f,.06f);
            }
            // The bit points into the screw, opposite the screw's outward axis.
            if(running && spinningBit) spinningBit.Rotate(spinningAxis,(poweredMode==PoweredScrewdriverMode.Tighten?-1:1)*turnsPerSecond*360*Time.deltaTime,Space.Self);
            if(!item.Grab || !item.Grab.isSelected || !tip) { Disengage(); return; }
            JunkerScrew candidate=FindContact();
            if(candidate!=Engaged)
            {
                Disengage();
                if(candidate && candidate.Claim(this)) { Engaged=candidate; previousUp=Vector3.ProjectOnPlane(tip.up,candidate.transform.forward).normalized; item.Pulse(); }
            }
            if(!Engaged) return;
            var up=Vector3.ProjectOnPlane(tip.up,Engaged.transform.forward).normalized;
            // JunkerScrew increases progress for tightening and decreases it for loosening.
            float turns=powered?(running?(poweredMode==PoweredScrewdriverMode.Tighten?1:-1)*turnsPerSecond*Time.deltaTime:0)
                :Vector3.SignedAngle(previousUp,up,Engaged.transform.forward)/360*wristSensitivity;
            previousUp=up;
            Engaged.Turn(turns);
            if(Mathf.Abs(turns)>.0001f && Time.time>=nextHaptic) { item.Pulse(.1f,.02f); nextHaptic=Time.time+.12f; }
        }
        public JunkerScrew FindContact()
        {
            if(!tip) return null;
            JunkerScrew candidate=null; float nearest=Mathf.Max(.001f,contactTolerance);
            void Consider(JunkerScrew screw)
            {
                if(!screw || !screw.CanTurn || (screw.Owner && screw.Owner!=this)) return;
                float distance=Vector3.Distance(tip.position,screw.Contact);
                if(distance<nearest && (useTipProximityOnly || Vector3.Angle(tip.forward,-screw.Axis)<angleTolerance)) { candidate=screw; nearest=distance; }
            }
            if(screws!=null) foreach(var screw in screws) Consider(screw);
            if(detectNearbyScrews)
            {
                int count;
                while((count=Physics.OverlapSphereNonAlloc(tip.position,Mathf.Max(.001f,contactTolerance),nearbyScrews,screwLayers,
                    QueryTriggerInteraction.Collide))==nearbyScrews.Length) System.Array.Resize(ref nearbyScrews,nearbyScrews.Length*2);
                for(int i=0;i<count;i++) Consider(nearbyScrews[i].GetComponentInParent<JunkerScrew>());
            }
            return candidate;
        }
    }
}
