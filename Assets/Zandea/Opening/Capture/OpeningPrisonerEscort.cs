using System;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.AI;

namespace Zandea.Opening.Capture
{
    public enum EscortState { Idle, Moving, Waiting, Completed, Interrupted }

    /// <summary>NPCs navigate; the prisoner keeps ordinary voluntary locomotion.</summary>
    public sealed class OpeningPrisonerEscort : MonoBehaviour
    {
        public OpeningCaptureGroup capture;
        public PlayerWristRestraints restraints;
        public Transform destination;
        public AudioClip moveLine, waitLine, warningLine;
        [Min(.2f)] public float leaderSpeed = 1.2f;
        [Min(.5f)] public float followRadius = 3f;
        [Min(1f)] public float escapeDistance = 12f;
        [Min(.1f)] public float escapeDuration = 4f;
        [Min(.5f)] public float refusalSeconds = 12f;
        [Min(.1f)] public float destinationRadius = 1.5f;
        [Min(.1f)] public float pathFailureSeconds = 3f;
        public EscortState State { get; private set; }
        public StarvingBandit Leader { get; private set; }
        public event Action EscortStarted, DestinationReached, PrisonerEscaped;
        public event Action<string> EscortInterrupted;

        float waitingSince, escapeSince = -1f, pathFailureSince = -1f;
        bool warningPlayed;
        NavMeshPath path;

        public bool Begin()
        {
            if (State != EscortState.Idle || !capture || !capture.prisoner || !restraints ||
                !restraints.IsRestrained || !destination || capture.State != CaptureGroupState.Binding) return false;
            if (!SelectLeader() || !PathAvailable()) { Interrupt("escort path unavailable"); return false; }
            capture.MarkEscortStarted();
            State = EscortState.Moving;
            EscortStarted?.Invoke();
            Play(moveLine);
            return true;
        }

        public void NotifyAggression(Vector3 position)
        {
            if (State != EscortState.Moving && State != EscortState.Waiting) return;
            Interrupt("prisoner attacked escort");
            capture.NotifyAggression(position);
        }

        public void Interrupt(string reason)
        {
            if (State == EscortState.Completed || State == EscortState.Interrupted) return;
            State = EscortState.Interrupted;
            EscortInterrupted?.Invoke(reason);
            if (Leader && Leader.agent && Leader.agent.isOnNavMesh) Leader.agent.ResetPath();
        }

        void Update()
        {
            if (State != EscortState.Moving && State != EscortState.Waiting) return;
            if (!capture || !capture.prisoner || !restraints || !restraints.IsRestrained)
            { Interrupt("prisoner or restraint unavailable"); return; }
            if (!Available(Leader) && !SelectLeader()) { Interrupt("no escort leader"); return; }
            if (!destination) { Interrupt("escort destination unavailable"); return; }
            if (!PathAvailable())
            {
                if (pathFailureSince < 0f) pathFailureSince = Time.time;
                if (Time.time - pathFailureSince >= pathFailureSeconds) Interrupt("escort path failed");
                return;
            }
            pathFailureSince = -1f;
            var player = capture.prisoner.transform;
            float distance = Vector3.Distance(player.position, Leader.transform.position);
            bool far = distance >= escapeDistance;
            if (far && !CanSeePlayer())
            {
                if (escapeSince < 0f) escapeSince = Time.time;
                if (Time.time - escapeSince >= escapeDuration)
                {
                    State = EscortState.Interrupted;
                    PrisonerEscaped?.Invoke();
                    capture.EnterCombat("prisoner escaped escort");
                    return;
                }
            }
            else escapeSince = -1f;
            if (Vector3.Distance(player.position, destination.position) <= destinationRadius &&
                Vector3.Distance(Leader.transform.position, destination.position) <= destinationRadius * 1.5f)
            {
                State = EscortState.Completed;
                DestinationReached?.Invoke();
                return;
            }
            if (distance > followRadius)
            {
                if (State != EscortState.Waiting)
                {
                    State = EscortState.Waiting;
                    waitingSince = Time.time;
                    warningPlayed = false;
                    Play(waitLine);
                }
                Leader.groupDestination = Leader.transform.position;
                if (!warningPlayed && Time.time - waitingSince > refusalSeconds * .5f)
                { warningPlayed = true; Play(warningLine); }
                if (Time.time - waitingSince >= refusalSeconds)
                {
                    Interrupt("prisoner refused escort");
                    capture.EnterCombat("prisoner refused escort");
                }
            }
            else
            {
                if (State == EscortState.Waiting) { State = EscortState.Moving; Play(moveLine); }
                Leader.groupDestination = destination.position;
                Leader.groupWalkSpeed = leaderSpeed;
            }
            PlaceGuards(player.position);
        }

        bool SelectLeader()
        {
            Leader = Available(capture.Captor) ? capture.Captor : null;
            if (!Leader && capture.members != null)
                foreach (var member in capture.members)
                    if (Available(member)) { Leader = member; break; }
            return Leader;
        }

        static bool Available(StarvingBandit member) => member && member.gameObject.activeInHierarchy &&
            member.health && member.health.State == NpcConsciousness.Conscious &&
            member.agent && member.agent.enabled && member.agent.isOnNavMesh;

        bool PathAvailable()
        {
            if (!Available(Leader) || !destination) return false;
            path ??= new NavMeshPath();
            return Leader.agent.CalculatePath(destination.position, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        bool CanSeePlayer()
        {
            if (!Leader || !capture || !capture.prisoner) return false;
            Vector3 from = Leader.transform.position + Vector3.up * 1.5f;
            Vector3 to = capture.prisoner.hmd ? capture.prisoner.hmd.position : capture.prisoner.transform.position + Vector3.up;
            Vector3 direction = to - from;
            var hits = Physics.RaycastAll(from, direction.normalized, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Transform first = null;
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.collider.transform.IsChildOf(Leader.transform)) continue;
                if (hit.distance < nearest) { nearest = hit.distance; first = hit.collider.transform; }
            }
            return !first || first.IsChildOf(capture.prisoner.transform);
        }

        void PlaceGuards(Vector3 player)
        {
            if (capture.members == null) return;
            int side = 0;
            Vector3 right = Vector3.Cross(Vector3.up, (destination.position - player).normalized);
            foreach (var member in capture.members)
            {
                if (!Available(member) || member == Leader) continue;
                member.groupDestination = player - (destination.position - player).normalized * 1.2f +
                    (side++ % 2 == 0 ? right : -right) * 1.4f;
                member.groupWalkSpeed = leaderSpeed;
            }
        }

        void Play(AudioClip clip)
        {
            if (clip && Leader) AudioSource.PlayClipAtPoint(clip, Leader.transform.position);
            else if (!clip) Debug.Log("Escort line has no recorded clip.", this);
        }

        void OnDisable()
        {
            if (State == EscortState.Moving || State == EscortState.Waiting) Interrupt("escort component disabled");
        }
    }
}
