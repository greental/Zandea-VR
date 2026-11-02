using System;
using JunkerBody;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Zandea.FriendlyNPC;

namespace Zandea.Opening
{
    public enum SurrenderState { NotSurrendering, GestureCandidate, Surrendering }
    public enum SurrenderCancelReason { HandsLowered, HandOccupied, Attack, TrackingLost, KnockedOut, Disabled }

    /// <summary>Observes tracked, empty, open hands. It never changes controller input or hand poses.</summary>
    public sealed class PlayerSurrenderGesture : MonoBehaviour
    {
        public VRBodyController body;
        public JunkerGloveHand leftGlove, rightGlove;
        public XRDirectInteractor leftInteractor, rightInteractor;
        public PlayerMartialHand leftMartial, rightMartial;
        public PlayerKnockout knockout;
        [Header("Gesture geometry")]
        [Min(.05f)] public float shoulderDropBelowEyes = .31f;
        [Min(0f)] public float riseAboveShoulders = .09f;
        [Min(0f)] public float loweredHysteresis = .08f;
        [Min(0f)] public float lateralHandSeparation = .13f;
        [Header("Intent and timing")]
        [Range(0f, 1f)] public float maximumOpenGrip = .28f;
        [Range(0f, 1f)] public float maximumOpenTrigger = .3f;
        [Min(.1f)] public float confirmationSeconds = .8f;
        [Min(0f)] public float cancellationGraceSeconds = .12f;
        [Min(0f)] public float retryCooldownSeconds = .4f;
        public bool requireTrackedDevices = true;

        public SurrenderState State { get; private set; }
        public bool IsSurrendering => State == SurrenderState.Surrendering;
        public bool LeftEligible { get; private set; }
        public bool RightEligible { get; private set; }
        public float ConfirmationProgress => State == SurrenderState.GestureCandidate
            ? Mathf.Clamp01((Time.time - candidateSince) / Mathf.Max(.01f, confirmationSeconds))
            : IsSurrendering ? 1f : 0f;
        public event Action SurrenderStarted, SurrenderConfirmed;
        public event Action<SurrenderCancelReason> SurrenderCancelled;

        float candidateSince, invalidSince = -1f, retryAt;

        void Awake()
        {
            if (!body) body = GetComponentInChildren<VRBodyController>(true);
            if (!knockout) knockout = GetComponentInChildren<PlayerKnockout>(true);
            if (!body) return;
            foreach (var glove in body.GetComponentsInChildren<JunkerGloveHand>(true))
                if (glove.left) leftGlove = glove; else rightGlove = glove;
            foreach (var martial in body.GetComponentsInChildren<PlayerMartialHand>(true))
                if (martial.left) leftMartial = martial; else rightMartial = martial;
            foreach (var interactor in body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true))
            {
                if (body.leftController && (interactor.transform == body.leftController || interactor.transform.IsChildOf(body.leftController)))
                    leftInteractor = interactor;
                if (body.rightController && (interactor.transform == body.rightController || interactor.transform.IsChildOf(body.rightController)))
                    rightInteractor = interactor;
            }
        }

        void Update()
        {
            if (!body || !body.hmd || !body.leftController || !body.rightController) return;
            if (knockout && knockout.State != PlayerConsciousness.Conscious)
            { Cancel(SurrenderCancelReason.KnockedOut); return; }
            LeftEligible = Eligible(true, out var leftReason);
            RightEligible = Eligible(false, out var rightReason);
            if (LeftEligible && RightEligible)
            {
                invalidSince = -1f;
                if (State == SurrenderState.NotSurrendering && Time.time >= retryAt)
                { State = SurrenderState.GestureCandidate; candidateSince = Time.time; SurrenderStarted?.Invoke(); }
                else if (State == SurrenderState.GestureCandidate && Time.time - candidateSince >= confirmationSeconds)
                { State = SurrenderState.Surrendering; SurrenderConfirmed?.Invoke(); }
            }
            else if (State != SurrenderState.NotSurrendering)
            {
                var reason = leftReason == SurrenderCancelReason.Attack || rightReason == SurrenderCancelReason.Attack
                    ? SurrenderCancelReason.Attack : leftReason == SurrenderCancelReason.TrackingLost || rightReason == SurrenderCancelReason.TrackingLost
                    ? SurrenderCancelReason.TrackingLost : leftReason == SurrenderCancelReason.HandOccupied || rightReason == SurrenderCancelReason.HandOccupied
                    ? SurrenderCancelReason.HandOccupied : SurrenderCancelReason.HandsLowered;
                if (reason == SurrenderCancelReason.Attack || reason == SurrenderCancelReason.TrackingLost || reason == SurrenderCancelReason.HandOccupied)
                { Cancel(reason); return; }
                if (invalidSince < 0f) invalidSince = Time.time;
                if (Time.time - invalidSince >= cancellationGraceSeconds) Cancel(reason);
            }
        }

        bool Eligible(bool left, out SurrenderCancelReason reason)
        {
            reason = SurrenderCancelReason.HandsLowered;
            var controller = left ? body.leftController : body.rightController;
            var glove = left ? leftGlove : rightGlove;
            var interactor = left ? leftInteractor : rightInteractor;
            var martial = left ? leftMartial : rightMartial;
            if (!controller || !glove || !interactor || !controller.gameObject.activeInHierarchy ||
                requireTrackedDevices && !DeviceTracked(left))
            { reason = SurrenderCancelReason.TrackingLost; return false; }
            if (martial && martial.State == PlayerMartialHand.HandState.ActivePunch)
            { reason = SurrenderCancelReason.Attack; return false; }
            if (interactor.hasSelection || interactor.isPerformingManualInteraction || FriendlyInputReservation.IsReserved(interactor) ||
                glove.grip > maximumOpenGrip || glove.trigger > maximumOpenTrigger || martial && martial.InStance)
            { reason = SurrenderCancelReason.HandOccupied; return false; }
            float shoulderY = body.hmd.position.y - shoulderDropBelowEyes;
            float requiredY = shoulderY + riseAboveShoulders -
                (State == SurrenderState.Surrendering ? loweredHysteresis : 0f);
            if (controller.position.y < requiredY) return false;
            float side = Vector3.Dot(controller.position - body.hmd.position, body.hmd.right);
            return left ? side <= -lateralHandSeparation : side >= lateralHandSeparation;
        }

        static bool DeviceTracked(bool left)
        {
            var device = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            return device.isValid && (!device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || tracked);
        }

        public void NotifyAttack() => Cancel(SurrenderCancelReason.Attack);

        public void Cancel(SurrenderCancelReason reason)
        {
            if (State == SurrenderState.NotSurrendering) return;
            State = SurrenderState.NotSurrendering;
            invalidSince = -1f;
            retryAt = Time.time + retryCooldownSeconds;
            SurrenderCancelled?.Invoke(reason);
        }

        void OnDisable()
        {
            Cancel(SurrenderCancelReason.Disabled);
            LeftEligible = RightEligible = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            GUI.Label(new Rect(14, 110, 600, 22),
                $"Surrender: {State}  L:{LeftEligible}  R:{RightEligible}  hold:{ConfirmationProgress:P0}");
        }
#endif
    }
}
