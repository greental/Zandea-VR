using Zandea.Opening.Capture;
using UnityEngine;

namespace Zandea.Opening
{
    // The only opening-specific bridge between reusable gesture, capture, escort and story systems.
    public sealed class OpeningFlow : MonoBehaviour
    {
        public PlayerSurrenderGesture surrender;
        public SlaverUltimatum ultimatum;
        public OpeningCaptureGroup capture;
        public OpeningBindingAction binder;
        public OpeningPrisonerEscort escort;
        public OpeningDirector director;
        [Min(1f)] public float bindingReachTimeout = 8f;
        bool enteringCombat;
        bool subscribed;
        float bindingReachSince = -1f;

        void Update()
        {
            if (bindingReachSince < 0f || !capture || !binder || capture.State != CaptureGroupState.CaptorApproach ||
                !capture.ComplianceHeld || director && director.Outcome.HasValue) return;
            if (binder.Begin()) { bindingReachSince = -1f; return; }
            if (Time.time - bindingReachSince >= bindingReachTimeout)
            {
                bindingReachSince = -1f;
                capture.EnterCombat("binding reach timeout");
            }
        }

        void OnEnable() => Subscribe();
        void Start() => Subscribe();

        void Subscribe()
        {
            if (subscribed || !surrender || !ultimatum || !capture || !binder || !escort) return;
            subscribed = true;
            if (surrender) { surrender.SurrenderConfirmed += OnSurrender; surrender.SurrenderCancelled += OnCancellation; }
            if (ultimatum) { ultimatum.CaptureIntent += OnCapture; ultimatum.CombatIntent += OnCombat; ultimatum.PursuitIntent += OnPursuit; }
            if (capture) { capture.CaptorInReach += OnCaptorInReach; capture.CaptureInterrupted += OnCaptureInterrupted; }
            if (binder) { binder.Restrained += OnRestrained; binder.Interrupted += OnBindingInterrupted; }
            if (escort) { escort.DestinationReached += OnDestination; escort.PrisonerEscaped += OnEscape; escort.EscortInterrupted += OnEscortInterrupted; }
            if (director) director.Resolved += OnResolved;
        }

        void OnDisable()
        {
            if (!subscribed) return;
            subscribed = false;
            if (surrender) { surrender.SurrenderConfirmed -= OnSurrender; surrender.SurrenderCancelled -= OnCancellation; }
            if (ultimatum) { ultimatum.CaptureIntent -= OnCapture; ultimatum.CombatIntent -= OnCombat; ultimatum.PursuitIntent -= OnPursuit; }
            if (capture) { capture.CaptorInReach -= OnCaptorInReach; capture.CaptureInterrupted -= OnCaptureInterrupted; }
            if (binder) { binder.Restrained -= OnRestrained; binder.Interrupted -= OnBindingInterrupted; }
            if (escort) { escort.DestinationReached -= OnDestination; escort.PrisonerEscaped -= OnEscape; escort.EscortInterrupted -= OnEscortInterrupted; }
            if (director) director.Resolved -= OnResolved;
        }

        void OnSurrender() => capture?.SetCompliance(true);

        void OnCancellation(SurrenderCancelReason reason)
        {
            bindingReachSince = -1f;
            capture?.SetCompliance(false);
            if (reason == SurrenderCancelReason.Attack)
            {
                var position = surrender && surrender.body ? surrender.body.transform.position : transform.position;
                capture?.NotifyAggression(position);
                ultimatum?.RegisterAggression();
                escort?.NotifyAggression(position);
            }
            if (binder && binder.IsBinding) binder.Interrupt("compliance lost");
        }

        void OnCapture()
        {
            if (!capture) { director?.ReportAbort(); return; }
            capture.SetCompliance(true);
            if (!capture.BeginCapture()) director?.ReportAbort();
            bindingReachSince = -1f;
        }

        void OnCombat() { bindingReachSince = -1f; capture?.EnterCombat("ultimatum refused"); }
        void OnPursuit() { bindingReachSince = -1f; capture?.EnterCombat("prisoner fled ultimatum"); }
        void OnCaptorInReach()
        {
            if (!binder) { capture?.EnterCombat("binding unavailable"); return; }
            if (!binder.Begin() && bindingReachSince < 0f) bindingReachSince = Time.time;
        }
        void OnRestrained()
        {
            if (!escort || !escort.Begin()) director?.ReportAbort();
        }
        void OnBindingInterrupted(string reason)
        {
            if (director && director.Outcome.HasValue) return;
            if (enteringCombat || !capture || capture.State == CaptureGroupState.Combat) return;
            if (reason == "captor unavailable") { bindingReachSince = -1f; return; }
            if (reason.Contains("unavailable") || reason.Contains("failed"))
            {
                enteringCombat = true;
                capture.EnterCombat(reason);
                enteringCombat = false;
            }
            else capture.NotifyAggression(surrender && surrender.body ? surrender.body.transform.position : transform.position);
        }
        void OnCaptureInterrupted(string reason)
        {
            if (reason.Contains("unavailable") || reason.Contains("no captor")) director?.ReportAbort();
        }
        void OnDestination() => director?.ReportEscortArrival();
        void OnEscape() => director?.Resolve(OpeningOutcome.Escaped);
        void OnEscortInterrupted(string reason)
        {
            if (director && director.Outcome.HasValue) return;
            if (reason.Contains("path") || reason.Contains("leader") || reason.Contains("destination")) director?.ReportAbort();
            else capture?.EnterCombat(reason);
        }

        void OnResolved(OpeningOutcome outcome)
        {
            bindingReachSince = -1f;
            if (binder && binder.IsBinding) binder.Interrupt("opening resolved");
            if (outcome == OpeningOutcome.Escaped || outcome == OpeningOutcome.SlaversDefeated ||
                outcome == OpeningOutcome.CapturedUnconscious || outcome == OpeningOutcome.CapturedCompliant)
            {
                if (capture && capture.group) capture.group.enabled = false;
                if (capture && capture.members != null)
                    foreach (var member in capture.members)
                    {
                        if (!member) continue;
                        member.groupEngaged = false;
                        member.groupControlled = true;
                        member.groupDestination = member.transform.position;
                        if (member.agent && member.agent.enabled && member.agent.isOnNavMesh)
                            member.agent.ResetPath();
                        if (outcome == OpeningOutcome.Escaped) member.target = null;
                    }
            }
        }
    }
}
