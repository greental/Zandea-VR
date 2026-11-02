using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
        GameObject cue;
        bool leftReserved, rightReserved;
        [Min(.15f)] public float gripRadius = .42f;
        public bool IsArmed => armed;
        void Awake()
        {
            gripRadius = Mathf.Max(gripRadius, .42f);
            interactable = GetComponent<XRSimpleInteractable>();
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
        }
        void OnDisable()
        {
            FriendlyInputReservation.Set(hovering, false);
            hovering = null;
            interactable.hoverEntered.RemoveListener(OnHover);
            interactable.hoverExited.RemoveListener(OnHoverExit);
            interactable.selectEntered.RemoveListener(OnGrip);
            ClearHandReservations();
        }

        void Update()
        {
            if (counter && (!counter.Trading || !counter.Ready) &&
                armed != counter.MissionHandshakeAvailable)
                Arm(counter.MissionHandshakeAvailable);
            if (!armed || used) return;
            if (!hands) hands = counter && counter.merchant && counter.merchant.choices &&
                counter.merchant.choices.isActiveAndEnabled
                ? counter.merchant.choices : FindFirstObjectByType<PalmDialogueChoices>();
            if (!hands) return;
            CheckHand(hands.leftHand, ref leftReserved);
            if (!used) CheckHand(hands.rightHand, ref rightReserved);
        }

        void CheckHand(XRDirectInteractor hand, ref bool reserved)
        {
            if (!hand) return;
            bool close = (hand.transform.position - transform.position).sqrMagnitude <= gripRadius * gripRadius;
            bool reserve = close && !hand.hasSelection;
            if (reserve != reserved)
            {
                // Near the offered hand, keep counter stock from taking the
                // select press. The proximity path below owns this handshake.
                FriendlyInputReservation.Set(hand, reserve, true);
                reserved = reserve;
            }
            if (!close || hand.hasSelection ||
                !hand.selectInput.ReadWasPerformedThisFrame() &&
                !hand.logicalSelectState.wasPerformedThisFrame) return;
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
            { hovering = hand; FriendlyInputReservation.Set(hand, true, true); }
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
            bool committed = counter && counter.TryCommit();
            // An uncollected contract pouch can be presented again, using its
            // stable ID. Only the first settlement creates or debits money.
            used = committed && !counter.MissionHandshakeAvailable;
        }
    }
}
