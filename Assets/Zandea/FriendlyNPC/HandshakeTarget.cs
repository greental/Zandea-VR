using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.FriendlyNPC
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class HandshakeTarget : MonoBehaviour
    {
        public VendorCounter counter;
        XRSimpleInteractable interactable;
        bool armed;
        bool used;
        XRDirectInteractor hovering;
        public bool IsArmed => armed;
        void Awake() => interactable = GetComponent<XRSimpleInteractable>();
        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHover);
            interactable.hoverExited.AddListener(OnHoverExit);
            interactable.selectEntered.AddListener(OnGrip);
            interactable.enabled = armed;
        }
        void OnDisable()
        {
            FriendlyInputReservation.Set(hovering, false);
            hovering = null;
            interactable.hoverEntered.RemoveListener(OnHover);
            interactable.hoverExited.RemoveListener(OnHoverExit);
            interactable.selectEntered.RemoveListener(OnGrip);
        }
        public void Arm(bool ready)
        {
            armed = ready;
            if (!ready)
            {
                used = false;
                FriendlyInputReservation.Set(hovering, false);
                hovering = null;
            }
            if (interactable) interactable.enabled = ready;
        }
        void OnHover(HoverEnterEventArgs args)
        {
            if (armed && args.interactorObject is XRDirectInteractor hand)
            { hovering = hand; FriendlyInputReservation.Set(hand, true); }
        }
        void OnHoverExit(HoverExitEventArgs args)
        {
            if (args.interactorObject is XRDirectInteractor hand)
            { FriendlyInputReservation.Set(hand, false); if (hovering == hand) hovering = null; }
        }
        void OnGrip(SelectEnterEventArgs args)
        {
            if (!armed || used || args.interactorObject is not XRDirectInteractor hand) return;
            used = true;
            FriendlyInputReservation.Set(hand, false);
            if (!counter || !counter.TryCommit()) used = false;
        }
    }
}
