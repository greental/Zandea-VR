using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.Missions
{
    [RequireComponent(typeof(Rigidbody),typeof(XRGrabInteractable))]
    public sealed class CarryWeight : MonoBehaviour
    {
        [Min(0)] public float effectiveWeight = 20f;
        XRGrabInteractable grab;
        void Awake() => grab=GetComponent<XRGrabInteractable>();
        void OnEnable() { grab.selectEntered.AddListener(Selected); grab.selectExited.AddListener(Released); }
        void OnDisable() { grab.selectEntered.RemoveListener(Selected); grab.selectExited.RemoveListener(Released); Carried.Remove(this); }
        void Selected(SelectEnterEventArgs _) => Carried.Add(this);
        void Released(SelectExitEventArgs _) { if(!grab.isSelected) Carried.Remove(this); }
        static readonly HashSet<CarryWeight> Carried=new();
        public static float Total { get { float n=0; foreach(var item in Carried) if(item && item.grab && item.grab.isSelected)n+=item.effectiveWeight; return n; } }
    }
}
