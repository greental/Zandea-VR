using System;
using System.Collections.Generic;
using JunkerBody;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Zandea.Opening
{
    public enum OpeningPhase { Dormant, Awakening, EncounterActive, Resolving, TitlePending, Handoff, Completed }

    [Serializable]
    public sealed class OpeningRoute
    {
        public OpeningOutcome outcome;
        public bool showTitle = true;
        public UnityEvent onResolved = new UnityEvent();
        public UnityEvent onHandoff = new UnityEvent();
    }

    // Story-only observer. Damage, knockout, capture, and navigation remain in reusable systems.
    public sealed class OpeningDirector : MonoBehaviour
    {
        public VRBodyController player;
        public ScavengerPlayerHealth playerHealth;
        public PlayerKnockout knockout;
        public PlayerSurrenderGesture surrender;
        public SlaverUltimatum ultimatum;
        public StarvingBandit[] slavers;
        public OpeningTitlePresentation presentation;
        public Transform encounterCenter;
        [Min(0f)] public float awakeningSeconds = 2f;
        [Min(1f)] public float escapeDistance = 35f;
        [Min(0f)] public float escapeUnseenSeconds = 6f;
        [Min(0f)] public float escapeHoldSeconds = 3f;
        public LayerMask visibilityMask = ~0;
        public OpeningRoute[] routes;
        public UnityEvent onOpeningStarted = new UnityEvent();
        public UnityEvent onOpeningCompleted = new UnityEvent();
        public event Action<OpeningOutcome> Resolved;
        public OpeningPhase Phase { get; private set; } = OpeningPhase.Dormant;
        public OpeningOutcome? Outcome { get; private set; }
        float phaseUntil, lastVisible, outsideSince = -1f;
        readonly Dictionary<Behaviour, bool> transitionActions = new Dictionary<Behaviour, bool>();

        void Awake()
        {
            if (!player || !player.hmd || !knockout || !presentation || slavers == null || slavers.Length != 3)
            {
                Debug.LogError("Opening director requires player, knockout, presentation and exactly three slavers.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (knockout) knockout.Unconscious += OnPlayerUnconscious;
        }

        void OnDisable()
        {
            if (knockout) knockout.Unconscious -= OnPlayerUnconscious;
            ReleaseTransitionActions();
        }

        void Start()
        {
            if (!enabled) return;
            Phase = OpeningPhase.Awakening;
            phaseUntil = Time.time + awakeningSeconds;
            lastVisible = Time.time;
            presentation.BeginAwakening(awakeningSeconds);
            onOpeningStarted.Invoke();
        }

        void Update()
        {
            if (Phase == OpeningPhase.Awakening && Time.time >= phaseUntil)
            {
                Phase = OpeningPhase.EncounterActive;
                if (ultimatum) ultimatum.Begin();
            }
            if (Phase != OpeningPhase.EncounterActive) return;
            if (AllSlaversDefeated()) { Resolve(OpeningOutcome.SlaversDefeated); return; }
            CheckEscape();
        }

        void OnPlayerUnconscious()
        {
            if (Phase == OpeningPhase.EncounterActive || Phase == OpeningPhase.Awakening)
            {
                knockout.HoldUnconscious();
                Resolve(OpeningOutcome.CapturedUnconscious);
            }
        }

        public void ReportEscortArrival() => Resolve(OpeningOutcome.CapturedCompliant);
        public void ReportAbort() => Resolve(OpeningOutcome.Aborted);

        public bool Resolve(OpeningOutcome outcome)
        {
            if (Outcome.HasValue || Phase == OpeningPhase.Dormant || Phase == OpeningPhase.Completed) return false;
            Outcome = outcome;
            Phase = OpeningPhase.Resolving;
            GateTransitionActions();
            Debug.Log("OPENING_RESOLVED " + outcome, this);
            Resolved?.Invoke(outcome);
            var route = RouteFor(outcome);
            route?.onResolved.Invoke();
            if (route != null && route.showTitle && presentation)
            {
                Phase = OpeningPhase.TitlePending;
                presentation.ShowTitle(() => Handoff(route));
            }
            else Handoff(route);
            return true;
        }

        OpeningRoute RouteFor(OpeningOutcome outcome)
        {
            if (routes != null)
                foreach (var route in routes)
                    if (route != null && route.outcome == outcome) return route;
            Debug.LogWarning("Opening outcome has no configured route: " + outcome, this);
            return null;
        }

        void Handoff(OpeningRoute route)
        {
            if (Phase == OpeningPhase.Completed) return;
            Phase = OpeningPhase.Handoff;
            route?.onHandoff.Invoke();
            Phase = OpeningPhase.Completed;
            onOpeningCompleted.Invoke();
            ReleaseTransitionActions();
        }

        void GateTransitionActions()
        {
            if (!player) return;
            foreach (var item in player.GetComponentsInChildren<ContinuousMoveProvider>(true)) Gate(item);
            foreach (var item in player.GetComponentsInChildren<ContinuousTurnProvider>(true)) Gate(item);
            foreach (var item in player.GetComponentsInChildren<JumpProvider>(true)) Gate(item);
            foreach (var item in player.GetComponentsInChildren<PlayerMartialHand>(true)) Gate(item);
            foreach (var item in player.GetComponentsInChildren<XRDirectInteractor>(true)) Gate(item);
        }

        void Gate(Behaviour component)
        {
            if (!component || transitionActions.ContainsKey(component)) return;
            transitionActions.Add(component, component.enabled);
            if (component.enabled) component.enabled = false;
        }

        void ReleaseTransitionActions()
        {
            bool safeToRestore = !knockout || knockout.State == PlayerConsciousness.Conscious;
            foreach (var item in transitionActions)
                if (safeToRestore && item.Key && item.Value) item.Key.enabled = true;
            transitionActions.Clear();
        }

        bool AllSlaversDefeated()
        {
            if (slavers == null || slavers.Length == 0) return false;
            int present = 0;
            foreach (var slaver in slavers)
            {
                if (!slaver) continue;
                present++;
                if (!slaver.health || slaver.health.State == NpcConsciousness.Conscious) return false;
            }
            return present > 0;
        }

        void CheckEscape()
        {
            if (!player || !player.hmd) return;
            var center = encounterCenter ? encounterCenter.position : transform.position;
            float distance = Vector3.Distance(player.hmd.position, center);
            bool visible = false;
            foreach (var slaver in slavers)
            {
                if (!slaver || !slaver.health || slaver.health.State != NpcConsciousness.Conscious) continue;
                var eye = slaver.transform.position + Vector3.up * 1.6f;
                var toPlayer = player.hmd.position - eye;
                if (Vector3.Angle(slaver.transform.forward, toPlayer) > 70f) continue;
                if (!Physics.Raycast(eye, toPlayer.normalized, out var hit, toPlayer.magnitude, visibilityMask, QueryTriggerInteraction.Ignore) ||
                    hit.transform.IsChildOf(player.transform)) { visible = true; break; }
            }
            if (visible) lastVisible = Time.time;
            if (distance < escapeDistance) { outsideSince = -1f; return; }
            if (outsideSince < 0f) outsideSince = Time.time;
            if (Time.time - outsideSince >= escapeHoldSeconds && Time.time - lastVisible >= escapeUnseenSeconds)
                Resolve(OpeningOutcome.Escaped);
        }
    }
}
