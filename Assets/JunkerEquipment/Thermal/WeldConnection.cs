using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerScanner;

namespace JunkerEquipment.Thermal
{
    public enum WeldState { Open, PartiallyWelded, Welded, PartiallyCut }
    public sealed class WeldConnection : ThermalWorkTarget
    {
        public Rigidbody bodyA, bodyB;
        [Tooltip("Matching positions at the authored pipe ends. Rotation is corrected automatically on snap.")]
        public Transform anchorA, anchorB;
        public XRGrabInteractable grabA, grabB;
        [Min(.001f)] public float positionTolerance=.045f;
        [Min(.001f), Tooltip("Distance from pipe A's end to the surface of pipe B that starts a snap, regardless of B's rotation.")]
        public float snapDistance=.10f;
        public bool snapOnRelease=true;
        [Tooltip("Start the authored pipe as one grabbable welded assembly. Cutting still releases B normally.")]
        public bool startWelded;
        public UnityEvent weldBroken=new();
        [SerializeField] WeldState state;
        Transform originalParent;
        bool originalKinematic, originalGravity, originalDetectCollisions;
        RigidbodyInterpolation originalInterpolation;
        Collider[] pieceColliders;
        bool[] colliderEnabled;
        Collider[] snapColliders;
        bool grabAEnabled,grabBEnabled;
        public WeldState State => state;
        public bool Attached => state==WeldState.Welded || state==WeldState.PartiallyCut;
        bool Held => (grabA && grabA.isSelected) || (grabB && grabB.isSelected);
        public bool Aligned => bodyA && bodyB && bodyA!=bodyB && anchorA && anchorB &&
            Vector3.Distance(anchorA.position,anchorB.position)<=positionTolerance;
        public override bool Accepts(ThermalMode mode) => bodyA && bodyB && !Held && (Attached ? mode==ThermalMode.Cut : mode==ThermalMode.Weld && Aligned);
        void Start()
        {
            if(!startWelded || Attached || !bodyA || !bodyB || !anchorA || !anchorB) return;
            AlignBodyB();
            Attach(false);
        }
        void FixedUpdate()
        {
            if(Attached) { if(!bodyA || !bodyB || bodyB.transform.parent!=bodyA.transform) Break(); return; }
            if(Progress>0) state=WeldState.PartiallyWelded;
            if(!snapOnRelease || snapBlocked || Held || !bodyA || !bodyB || bodyB.isKinematic || !anchorA || !anchorB) return;
            if(!TouchingEnd()) return;
            // Correct both position and twist when the released piece touches the work end.
            AlignBodyB();
            bodyB.linearVelocity=bodyA.GetPointVelocity(bodyB.position); bodyB.angularVelocity=bodyA.angularVelocity;
        }
        void AlignBodyB()
        {
            Quaternion rotation=anchorA.rotation*Quaternion.Inverse(anchorB.rotation);
            Vector3 offset=anchorB.position-bodyB.position;
            bodyB.rotation=rotation*bodyB.rotation;
            bodyB.position=anchorA.position-rotation*offset;
        }
        bool TouchingEnd()
        {
            if(!anchorA || !bodyB) return false;
            float range=Mathf.Max(.001f,snapDistance);
            if(snapColliders==null) snapColliders=bodyB.GetComponentsInChildren<Collider>(true);
            foreach(var shape in snapColliders)
                if(shape && shape.enabled && shape.GetComponentInParent<Rigidbody>()==bodyB &&
                    Vector3.Distance(anchorA.position,shape.ClosestPoint(anchorA.position))<=range) return true;
            return false;
        }
        void Update() { if(Attached && Progress>0) state=WeldState.PartiallyCut; }
        protected override void Finish(ThermalMode mode)
        {
            if(mode==ThermalMode.Cut) { Break(); return; }
            if(!Aligned || Held || Attached) return;
            Attach(true);
        }
        void Attach(bool notify)
        {
            // A welded piece is a non-simulated child of A. This avoids solver impulses
            // from a held cutter pulling a separate Rigidbody away from its weld.
            originalParent=bodyB.transform.parent;
            originalKinematic=bodyB.isKinematic; originalGravity=bodyB.useGravity;
            originalDetectCollisions=bodyB.detectCollisions; originalInterpolation=bodyB.interpolation;
            pieceColliders=System.Array.FindAll(bodyB.GetComponentsInChildren<Collider>(true),c=>c && c.GetComponentInParent<Rigidbody>()==bodyB);
            colliderEnabled=new bool[pieceColliders.Length];
            for(int i=0;i<pieceColliders.Length;i++) { colliderEnabled[i]=pieceColliders[i].enabled; pieceColliders[i].enabled=false; }
            bodyB.linearVelocity=Vector3.zero; bodyB.angularVelocity=Vector3.zero;
            bodyB.isKinematic=true; bodyB.useGravity=false; bodyB.detectCollisions=false; bodyB.interpolation=RigidbodyInterpolation.None;
            bodyB.transform.SetParent(bodyA.transform,true);
            grabAEnabled=grabA && grabA.enabled; grabBEnabled=grabB && grabB.enabled;
            // A remains grabbable; B has no independent physics or grab while welded.
            if(grabB) grabB.enabled=false;
            state=WeldState.Welded; ResetPaths(); if(notify) completed.Invoke();
        }
        public void Break()
        {
            if(!Attached) return;
            if(bodyB)
            {
                Vector3 worldPosition=bodyB.transform.position;
                Quaternion worldRotation=bodyB.transform.rotation;
                Vector3 releasedVelocity=bodyA?bodyA.GetPointVelocity(worldPosition):Vector3.zero;
                Vector3 releasedAngularVelocity=bodyA?bodyA.angularVelocity:Vector3.zero;
                bodyB.transform.SetParent(originalParent,true);
                bodyB.position=worldPosition; bodyB.rotation=worldRotation;
                bodyB.detectCollisions=originalDetectCollisions;
                bodyB.isKinematic=originalKinematic; bodyB.useGravity=originalGravity; bodyB.interpolation=originalInterpolation;
                for(int i=0;pieceColliders!=null && i<pieceColliders.Length;i++)
                    if(pieceColliders[i]) pieceColliders[i].enabled=colliderEnabled[i];
                if(!bodyB.isKinematic) { bodyB.linearVelocity=releasedVelocity; bodyB.angularVelocity=releasedAngularVelocity; bodyB.WakeUp(); }
            }
            state=WeldState.Open; ResetPaths();
            if(grabA) grabA.enabled=grabAEnabled; if(grabB) grabB.enabled=grabBEnabled;
            // Stay detached until someone handles the loose pipe again.
            snapBlocked=true;
            weldBroken.Invoke();
        }
        bool snapBlocked;
        void LateUpdate() { if(snapBlocked && Held) snapBlocked=false; }
        public void RearmSnap() { snapBlocked=false; }
        void OnDisable() { if(Attached) Break(); }
        public override void Populate(ScanInfo info)
        {
            info.Status=Attached?ScanStatus.Operational:ScanStatus.Repairable;
            info.Hint=Attached?(Progress>0?"Partially cut - continue seam":"Welded - CUT seam to separate"):Aligned?(Progress>0?"Partially welded - continue seam":"Ready to weld - release pipe and trace seam"):"Disconnected - align matching pipe ends";
            base.Populate(info);
        }
        void OnDrawGizmosSelected() { if(anchorA) { Gizmos.color=Color.cyan; Gizmos.DrawWireSphere(anchorA.position,positionTolerance); } if(anchorB && anchorA) Gizmos.DrawLine(anchorA.position,anchorB.position); }
    }
}
