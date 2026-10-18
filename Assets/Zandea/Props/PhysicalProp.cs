using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.Props
{
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public sealed class PhysicalProp : MonoBehaviour
    {
        [SerializeField] string stableId;
        public string StableId => stableId;
        public bool IsHeld => grab && grab.isSelected;
        public bool HasBeenHandled { get; private set; }
        public event Action<PhysicalProp> Taken;
        Rigidbody body;
        XRGrabInteractable grab;
        void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(OnTaken);
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
        void OnDestroy()
        {
            if (grab) grab.selectEntered.RemoveListener(OnTaken);
        }
        void OnTaken(SelectEnterEventArgs args)
        {
            HasBeenHandled = true;
            Taken?.Invoke(this);
        }
        public void AssignId(string id) => stableId = id;
        public void SetDormant(bool dormant)
        {
            if (dormant && IsHeld) return;
            if (dormant && !body.isKinematic) body.Sleep();
            gameObject.SetActive(!dormant);
        }
    }
}
