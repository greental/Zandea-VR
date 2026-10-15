using UnityEngine;
namespace JunkerEquipment
{
    public sealed class JunkerScrew : MonoBehaviour
    {
        [Range(0,1)] public float progress=1;
        [Min(.1f)] public float turnsToComplete=3;
        public float travel=.025f;
        public Transform visual;
        [SerializeField, HideInInspector] JunkerRepairPanel panel;
        [Tooltip("Small trigger at the moving screw head for nearby screwdriver detection.")]
        public SphereCollider contactTrigger;
        public JunkerScrewdriver Owner { get; private set; }
        public bool IsLoose=>progress<=.001f;
        public bool IsTight=>progress>=.999f;
        public Vector3 Contact=>visual?visual.position:transform.position;
        public Vector3 Axis => transform.forward;
        public bool CanTurn => isActiveAndEnabled && (!panel || panel.CanTurnScrews);
        Vector3 rest; Quaternion restRotation;
        void Awake()
        {
            if(!visual) visual=transform;
            rest=visual.localPosition; restRotation=visual.localRotation;
            if(!panel) panel=GetComponentInParent<JunkerRepairPanel>();
            // Old scene screws have no collider; copies need no tool-side wiring.
            if(!contactTrigger) contactTrigger=visual.gameObject.AddComponent<SphereCollider>();
            contactTrigger.isTrigger=true; contactTrigger.center=Vector3.zero;
            var scale=visual.lossyScale;
            contactTrigger.radius=.012f/Mathf.Max(.001f,Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            Apply();
        }
        public void BindToPanel(JunkerRepairPanel owner) { panel=owner; }
        public bool Claim(JunkerScrewdriver tool) { if(!tool || !CanTurn || (Owner && Owner!=tool)) return false; Owner=tool; return true; }
        public void Release(JunkerScrewdriver tool) { if(Owner==tool) Owner=null; }
        public void Turn(float turns)
        {
            if(!CanTurn) return;
            progress=Mathf.Clamp01(progress+turns/Mathf.Max(.1f,turnsToComplete)); Apply();
        }
        void Apply()
        {
            var offset=Axis*((1-progress)*travel);
            visual.localPosition=rest+(visual.parent?visual.parent.InverseTransformVector(offset):offset);
            visual.localRotation=restRotation*Quaternion.AngleAxis(progress*turnsToComplete*360,Vector3.forward);
        }
        void OnDisable() { Owner=null; }
        void OnDrawGizmosSelected() { Gizmos.color=Color.yellow; Gizmos.DrawWireSphere(Contact,.012f); Gizmos.DrawRay(Contact,Axis*.06f); }
    }
}
