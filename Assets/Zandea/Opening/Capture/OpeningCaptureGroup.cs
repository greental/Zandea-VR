using System;
using JunkerBody;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;

namespace Zandea.Opening.Capture
{
    public enum CaptureGroupState { Idle, CaptorApproach, Binding, Escort, Combat, Aborted }

    /// <summary>Assigns one captor and keeps the other living members armed in guard slots.</summary>
    public sealed class OpeningCaptureGroup : MonoBehaviour
    {
        public BanditGroup group;
        public StarvingBandit[] members;
        public VRBodyController prisoner;
        [Min(.4f)] public float bindDistance = .9f;
        [Min(.5f)] public float guardRadius = 2f;
        [Min(0f)] public float reactionDelay = .55f;
        [Min(.1f)] public float suspicionDistance = 1.5f;
        [Min(.2f)] public float betrayalStepDistance = 1.5f;
        public LayerMask sightMask = ~0;
        public CaptureGroupState State { get; private set; }
        public StarvingBandit Captor { get; private set; }
        public bool ComplianceHeld { get; private set; }
        public event Action CaptureStarted, CaptorInReach, BindingStarted, EscortStarted, CombatStarted;
        public event Action<StarvingBandit> CaptorChanged;
        public event Action<string> CaptureInterrupted;

        readonly System.Collections.Generic.Dictionary<StarvingBandit, bool> priorDefensive = new();
        bool priorGroupEnabled;
        bool approachNotified;
        float reactionAt = float.PositiveInfinity;
        string pendingReason;
        Vector3 surrenderPoint;
        PlayerMartialHand[] playerHands;

        void Awake()
        {
            if (!group) group = GetComponent<BanditGroup>();
            if ((members == null || members.Length == 0) && group) members = group.authoredMembers;
            if (prisoner) playerHands = prisoner.GetComponentsInChildren<PlayerMartialHand>(true);
        }

        public void SetCompliance(bool held)
        {
            if (ComplianceHeld == held) return;
            ComplianceHeld = held;
            if (!held && (State == CaptureGroupState.CaptorApproach || State == CaptureGroupState.Binding))
                ScheduleReaction("surrender cancelled", prisoner ? prisoner.transform.position : transform.position);
        }

        public bool BeginCapture()
        {
            if (State != CaptureGroupState.Idle || !ComplianceHeld || !prisoner || !SelectCaptor()) return false;
            if (playerHands == null || playerHands.Length == 0)
                playerHands = prisoner.GetComponentsInChildren<PlayerMartialHand>(true);
            priorGroupEnabled = group && group.enabled;
            if (group) group.enabled = false;
            priorDefensive.Clear();
            foreach (var member in members)
            {
                if (!Available(member)) continue;
                priorDefensive[member] = member.defensiveOnly;
                member.defensiveOnly = true;
                member.groupControlled = true;
                member.groupEngaged = false;
                member.target = prisoner.hmd ? prisoner.hmd : prisoner.transform;
            }
            State = CaptureGroupState.CaptorApproach;
            surrenderPoint = prisoner.transform.position;
            approachNotified = false;
            CaptureStarted?.Invoke();
            return true;
        }

        public void MarkBindingStarted()
        {
            if (State != CaptureGroupState.CaptorApproach) return;
            State = CaptureGroupState.Binding;
            BindingStarted?.Invoke();
        }

        public void MarkEscortStarted()
        {
            if (State != CaptureGroupState.Binding) return;
            State = CaptureGroupState.Escort;
            EscortStarted?.Invoke();
        }

        public void NotifyAggression(Vector3 worldPosition) => ScheduleReaction("player aggression", worldPosition);

        public void Abort(string reason)
        {
            if (State == CaptureGroupState.Idle || State == CaptureGroupState.Aborted || State == CaptureGroupState.Combat) return;
            CaptureInterrupted?.Invoke(reason);
            RestoreCombatControl();
            State = CaptureGroupState.Aborted;
        }

        public void EnterCombat(string reason)
        {
            if (State == CaptureGroupState.Combat) return;
            if (State != CaptureGroupState.Idle) CaptureInterrupted?.Invoke(reason);
            RestoreCombatControl();
            State = CaptureGroupState.Combat;
            CombatStarted?.Invoke();
        }

        void ScheduleReaction(string reason, Vector3 origin)
        {
            if (State != CaptureGroupState.CaptorApproach && State != CaptureGroupState.Binding && State != CaptureGroupState.Escort) return;
            float latency = reactionDelay;
            bool witnessed = false;
            foreach (var member in members)
            {
                if (!Available(member)) continue;
                Vector3 eye = member.transform.position + Vector3.up * 1.5f;
                Vector3 toEvent = origin - eye;
                if (toEvent.magnitude > Mathf.Max(guardRadius * 4f, 8f)) continue;
                if (Vector3.Dot(member.transform.forward, toEvent.normalized) < -.2f) continue;
                var hits = Physics.RaycastAll(eye, toEvent.normalized, toEvent.magnitude, sightMask,
                    QueryTriggerInteraction.Ignore);
                float nearest = float.PositiveInfinity;
                Transform blocker = null;
                foreach (var hit in hits)
                {
                    if (!hit.collider || hit.collider.transform.IsChildOf(member.transform)) continue;
                    if (hit.distance < nearest) { nearest = hit.distance; blocker = hit.collider.transform; }
                }
                if (blocker && !blocker.IsChildOf(prisoner.transform)) continue;
                witnessed = true;
                break;
            }
            if (!witnessed) latency += reactionDelay;
            reactionAt = Mathf.Min(reactionAt, Time.time + latency);
            pendingReason = reason;
        }

        void Update()
        {
            if (State != CaptureGroupState.CaptorApproach && State != CaptureGroupState.Binding && State != CaptureGroupState.Escort) return;
            if (Time.time >= reactionAt) { EnterCombat(pendingReason); return; }
            if (!prisoner) { Abort("prisoner unavailable"); return; }
            if (State != CaptureGroupState.Escort)
            {
                if (Vector3.Distance(prisoner.transform.position, surrenderPoint) > betrayalStepDistance)
                    ScheduleReaction("prisoner stepped away", prisoner.transform.position);
                if (playerHands != null)
                    foreach (var hand in playerHands)
                        if (hand && (hand.State == PlayerMartialHand.HandState.ActivePunch ||
                            hand.State == PlayerMartialHand.HandState.Holding))
                        { ScheduleReaction("prisoner attacked or grabbed", hand.transform.position); break; }
            }
            if (!Available(Captor))
            {
                if (!SelectCaptor()) { Abort("no captor available"); return; }
                if (State == CaptureGroupState.Binding) State = CaptureGroupState.CaptorApproach;
                approachNotified = false;
            }
            if (State == CaptureGroupState.Escort) return;
            Vector3 target = prisoner.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(prisoner.hmd ? prisoner.hmd.forward : prisoner.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int guardIndex = 0;
            foreach (var member in members)
            {
                if (!Available(member)) continue;
                if (member == Captor)
                {
                    var approach = target + forward * bindDistance;
                    member.groupDestination = approach;
                    if (!approachNotified && Vector3.Distance(member.transform.position, target) <= bindDistance + .25f)
                    { approachNotified = true; CaptorInReach?.Invoke(); }
                }
                else
                {
                    member.groupDestination = target + (guardIndex++ % 2 == 0 ? right : -right) * guardRadius + forward * .5f;
                    Vector3 toPlayer = Vector3.ProjectOnPlane(target - member.transform.position, Vector3.up);
                    if (toPlayer.sqrMagnitude > .01f)
                        member.transform.rotation = Quaternion.RotateTowards(member.transform.rotation,
                            Quaternion.LookRotation(toPlayer), 120f * Time.deltaTime);
                }
            }
            if (State == CaptureGroupState.Binding && Vector3.Distance(Captor.transform.position, target) > bindDistance + suspicionDistance)
                ScheduleReaction("binding distance lost", target);
        }

        bool SelectCaptor()
        {
            StarvingBandit selected = null;
            float best = float.PositiveInfinity;
            if (members != null)
                foreach (var member in members)
                {
                    if (!Available(member)) continue;
                    float distance = prisoner ? Vector3.Distance(member.transform.position, prisoner.transform.position) : 0f;
                    if (distance < best) { selected = member; best = distance; }
                }
            if (!selected) return false;
            if (Captor != selected) { Captor = selected; CaptorChanged?.Invoke(selected); }
            return true;
        }

        static bool Available(StarvingBandit member) => member && member.gameObject.activeInHierarchy &&
            (!member.health || member.health.State == NpcConsciousness.Conscious);

        void RestoreCombatControl()
        {
            foreach (var item in priorDefensive) if (item.Key) item.Key.defensiveOnly = item.Value;
            priorDefensive.Clear();
            if (group) group.enabled = priorGroupEnabled;
            if (members != null)
                foreach (var member in members)
                    if (Available(member)) member.groupEngaged = true;
            reactionAt = float.PositiveInfinity;
        }

        void OnDisable()
        {
            if (State == CaptureGroupState.CaptorApproach || State == CaptureGroupState.Binding || State == CaptureGroupState.Escort)
                RestoreCombatControl();
        }
    }
}
