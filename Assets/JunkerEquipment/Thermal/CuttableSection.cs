using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerScanner;
namespace JunkerEquipment.Thermal
{
    public sealed class CuttableSection : ThermalWorkTarget
    {
        public Rigidbody cutout;
        public XRGrabInteractable cutoutGrab;
        public CuttableAccessPoint scannerAccess;
        [SerializeField] bool released;
        public bool Released => released;
        void Start() { if(!released && cutout) { cutout.isKinematic=true; cutout.useGravity=false; if(cutoutGrab) cutoutGrab.enabled=false; } }
        public override bool Accepts(ThermalMode mode) => mode==ThermalMode.Cut && !released && cutout;
        protected override void Finish(ThermalMode mode)
        {
            if(released || !cutout) return;
            released=true;
            cutout.transform.SetParent(null,true);
            cutout.isKinematic=false; cutout.useGravity=true; cutout.WakeUp();
            if(cutoutGrab) cutoutGrab.enabled=true;
            if(scannerAccess) scannerAccess.SetOpened(true);
            completed.Invoke();
        }
        public override void Populate(ScanInfo info) { info.Status=released?ScanStatus.Operational:ScanStatus.Locked; info.Hint=released?"Access open":"Trace cut guide from green marker"; base.Populate(info); }
    }
}
