using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.InputSystem;
using TMPro;

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
        PalmDialogueChoices hands;
        InputAction leftGrip, rightGrip;
        GameObject cue;
        bool leftReserved, rightReserved;
        [Min(.15f)] public float gripRadius = .28f;
        public bool IsArmed => armed;
        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            leftGrip = new InputAction("Merchant handshake left grip", InputActionType.Button,
                "<XRController>{LeftHand}/gripPressed");
            rightGrip = new InputAction("Merchant handshake right grip", InputActionType.Button,
                "<XRController>{RightHand}/gripPressed");
            if (!Application.isPlaying) return;
            cue = new GameObject("Handshake cue");
            cue.transform.SetParent(transform, false);
            cue.transform.localPosition = new Vector3(0f, .18f, 0f);
            cue.transform.localScale = Vector3.one * .045f;
            var text = cue.AddComponent<TextMeshPro>();
            text.text = "Grip to agree";
            text.fontSize = 2.2f;
            text.alignment = TextAlignmentOptions.Center;
            cue.AddComponent<BillboardLabel>();
            cue.SetActive(false);
        }
        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHover);
            interactable.hoverExited.AddListener(OnHoverExit);
            interactable.selectEntered.AddListener(OnGrip);
            interactable.enabled = armed;
            leftGrip?.Enable(); rightGrip?.Enable();
        }
        void OnDisable()
        {
            FriendlyInputReservation.Set(hovering, false);
            hovering = null;
            interactable.hoverEntered.RemoveListener(OnHover);
            interactable.hoverExited.RemoveListener(OnHoverExit);
            interactable.selectEntered.RemoveListener(OnGrip);
            leftGrip?.Disable(); rightGrip?.Disable();
            ClearHandReservations();
        }
        void OnDestroy() { leftGrip?.Dispose(); rightGrip?.Dispose(); }

        void Update()
        {
            if (!armed || used) return;
            if (!hands) hands = FindFirstObjectByType<PalmDialogueChoices>();
            if (!hands) return;
            CheckHand(hands.leftHand, leftGrip, ref leftReserved);
            if (!used) CheckHand(hands.rightHand, rightGrip, ref rightReserved);
        }

        void CheckHand(XRDirectInteractor hand, InputAction grip, ref bool reserved)
        {
            if (!hand) return;
            bool close = (hand.transform.position - transform.position).sqrMagnitude <= gripRadius * gripRadius;
            if (close != reserved)
            {
                FriendlyInputReservation.Set(hand, close);
                reserved = close;
            }
            if (!close || hand.hasSelection || grip == null || !grip.WasPressedThisFrame()) return;
            Confirm();
        }

        void ClearHandReservations()
        {
            if (hands)
            {
                if (leftReserved) FriendlyInputReservation.Set(hands.leftHand, false);
                if (rightReserved) FriendlyInputReservation.Set(hands.rightHand, false);
            }
            leftReserved = rightReserved = false;
        }
        public void Arm(bool ready)
        {
            armed = ready;
            if (!ready)
            {
                used = false;
                FriendlyInputReservation.Set(hovering, false);
                hovering = null;
                ClearHandReservations();
            }
            if (interactable) interactable.enabled = ready;
            if (cue) cue.SetActive(ready);
        }
        void OnHover(HoverEnterEventArgs args)
        {
            if (armed && args.interactorObject is XRDirectInteractor hand)
            { hovering = hand; FriendlyInputReservation.Set(hand, true); }
        }
        void OnHoverExit(HoverExitEventArgs args)
        {
            if (args.interactorObject is XRDirectInteractor hand)
            {
                FriendlyInputReservation.Set(hand, false);
                if (hovering == hand) hovering = null;
                if (hands && hand == hands.leftHand) leftReserved = false;
                if (hands && hand == hands.rightHand) rightReserved = false;
            }
        }
        void OnGrip(SelectEnterEventArgs args)
        {
            if (args.interactorObject is not XRDirectInteractor) return;
            if (!armed) return;
            if (used) return;
            Confirm();
        }
        void Confirm()
        {
            if (!armed || used) return;
            used = true;
            if (!counter || !counter.TryCommit()) used = false;
        }
    }
}
