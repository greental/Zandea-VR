using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Props
{
    [RequireComponent(typeof(HingeJoint))]
    public sealed class PropHingedLid : MonoBehaviour
    {
        public PropContainer container;
        [Range(5, 85)] public float openAngle = 32f;
        HingeJoint hinge;
        bool lastOpen;
        void Awake() => hinge = GetComponent<HingeJoint>();
        void Update()
        {
            if (!container || !hinge) return;
            bool open = Mathf.Abs(hinge.angle) >= openAngle;
            if (open == lastOpen) return;
            lastOpen = open;
            container.SetOpen(open);
        }
    }
}
