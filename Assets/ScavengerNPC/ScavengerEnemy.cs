using UnityEngine;
using UnityEngine.AI;
using JunkerBody;

namespace ScavengerNPC
{
    [RequireComponent(typeof(NavMeshAgent), typeof(ScavengerHealth))]
    public sealed class ScavengerEnemy : MonoBehaviour
    {
        public enum Action { Standing, Casual, Patrol, Sitting, Squatting, SeekingCover, Hiding, Peeking, Attacking, Searching, Dead }
        public Action idleAction = Action.Patrol;
        public Transform target, eyes;
        [Tooltip("Optional player rig root, used to recognize its colliders during sight checks.")]
        public Transform targetRoot;
        public Transform[] patrolPoints;
        public Transform seat;
        public LayerMask sightMask = ~0;
        [Min(1)] public float sightRange = 25;
        [Range(1, 360)] public float fieldOfView = 115;
        public float walkSpeed = 1.3f, runSpeed = 3.5f, memorySeconds = 8;
        public float coverSearchRadius = 15, hideSeconds = 1.8f, peekSeconds = 2.2f;
        [Min(0)] public float hideTimeVariation = 1.15f, peekTimeVariation = 1.25f;
        [Min(0)] public float rushAttackRange = 5f;
        public Animator animator;
        public ScavengerWeapon weapon;
        public Action CurrentAction { get; private set; }
        public bool IsCombat => CurrentAction >= Action.SeekingCover && CurrentAction <= Action.Searching;
        public bool CanShoot => (!health || health.State == NpcConsciousness.Conscious) && weapon &&
            (CurrentAction == Action.Peeking || CurrentAction == Action.Attacking) &&
            (CurrentAction != Action.Peeking || !cover || !cover.IsOver(peekIndex) || !pose || pose.CrouchAmount < .15f);
        public Vector3 AimPoint => target ? target.position : lastSeen;
        NavMeshAgent agent;
        ScavengerCover cover;
        ScavengerPose pose;
        ScavengerHealth health;
        int peekIndex = -1;
        Vector3 home, lastSeen;
        float lastSeenTime = -1000, stateUntil, nextSense, nextCover, nextDestination, rushAttackUntil;
        int patrolIndex;
        bool visible;
        NavMeshPath path;
        public Vector3 EyePosition => eyes ? eyes.position : transform.position + Vector3.up * 1.5f;
        public bool IsCrouching => CurrentAction == Action.Squatting ||
            cover && cover.crouchWhenHiding && CurrentAction == Action.Hiding;
        public float CrouchDepth => cover ? cover.crouchDepth : .35f;
        public int CurrentPeekIndex => peekIndex;
        bool Navigable => agent && agent.enabled && agent.isOnNavMesh;

        void Awake() { path = new NavMeshPath(); agent = GetComponent<NavMeshAgent>(); pose = GetComponent<ScavengerPose>(); health = GetComponent<ScavengerHealth>(); home = transform.position; CurrentAction = idleAction; }
        void Start()
        {
            ResolveTarget();
            ResolveWeapon();
            if (!Navigable) Debug.LogWarning("Scavenger needs a baked NavMesh to move. It can still detect and attack.", this);
        }
        void ResolveWeapon()
        {
            if (!weapon) weapon = GetComponentInChildren<ScavengerWeapon>(true);
            if (!weapon) weapon = gameObject.AddComponent<ScavengerWeapon>();
            weapon.owner = this;
            weapon.ResolvePistol();
        }
        void ResolveTarget()
        {
            // The VR rig can initialize after this NPC. Keep replacing a fallback
            // scene camera or demo target once the tracked Junker becomes available.
            var body = FindFirstObjectByType<VRBodyController>();
            if (body && body.hmd && body.hmd.gameObject.activeInHierarchy)
            {
                target = body.hmd;
                targetRoot = body.transform.root;
                return;
            }
            var camera = Camera.main;
            if (camera && (!target || !target.gameObject.activeInHierarchy))
            {
                target = camera.transform;
                targetRoot = camera.transform.root;
            }
            else if (target && !target.gameObject.activeInHierarchy) target = null;
        }
        float HideDuration() => Mathf.Max(.35f, hideSeconds + Random.Range(-hideTimeVariation, hideTimeVariation));
        float PeekDuration() => Mathf.Max(.65f, peekSeconds + Random.Range(-peekTimeVariation, peekTimeVariation));
        void Update()
        {
            if (CurrentAction == Action.Dead || health && health.IsUnconscious) return;
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + .15f;
                ResolveTarget();
                visible = target && Vector3.Distance(EyePosition, target.position) <= sightRange &&
                    (IsCombat || Vector3.Angle(transform.forward, target.position - EyePosition) <= fieldOfView * .5f) &&
                    ClearLine(EyePosition, target.position);
                if (visible)
                {
                    lastSeen = target.position; lastSeenTime = Time.time;
                    if (!IsCombat) { ReleaseCover(); CurrentAction = Action.Attacking; nextCover = 0; }
                }
            }
            if (IsCombat) Combat(); else Idle();
            if (animator) animator.SetFloat("Speed", Navigable ? agent.velocity.magnitude : 0, .12f, Time.deltaTime);
        }
        void Combat()
        {
            if (!target || Time.time - lastSeenTime > memorySeconds)
            {
                ReleaseCover(); CurrentAction = idleAction; nextDestination = 0; return;
            }
            if (CurrentAction == Action.SeekingCover)
            {
                if (!cover || !Navigable || Time.time > stateUntil) { ReleaseCover(); CurrentAction = Action.Attacking; }
                else if (Arrived()) { Stop(); CurrentAction = Action.Hiding; stateUntil = Time.time + HideDuration(); }
                return;
            }
            if (CurrentAction == Action.Hiding)
            {
                Face(lastSeen);
                if (target && (visible ||
                    (Vector3.Distance(transform.position, target.position) <= rushAttackRange &&
                     ClearLine(transform.position + Vector3.up * 1.5f, target.position))))
                {
                    lastSeen = target.position;
                    lastSeenTime = Time.time;
                    ReleaseCover();
                    CurrentAction = Action.Attacking;
                    rushAttackUntil = Time.time + 2.5f;
                    nextCover = rushAttackUntil;
                    return;
                }
                if (!cover || cover.PeekCount == 0) { ReleaseCover(); CurrentAction = Action.Attacking; }
                else if (Time.time >= stateUntil)
                {
                    int selected = ChoosePeek(cover, peekIndex);
                    if (selected < 0) { ReleaseCover(); CurrentAction = Action.Attacking; return; }
                    peekIndex = selected;
                    var marker = cover.GetPeek(peekIndex);
                    if (marker.style == ScavengerCover.PeekStyle.Over || Move(marker.point.position, runSpeed))
                    { CurrentAction = Action.Peeking; stateUntil = Time.time + PeekDuration() + (marker.style == ScavengerCover.PeekStyle.Over ? 0 : 1); }
                    else { ReleaseCover(); CurrentAction = Action.Attacking; }
                }
                return;
            }
            if (CurrentAction == Action.Peeking)
            {
                Face(lastSeen);
                // The animated eye can still be below a half wall as the NPC rises.
                // The weapon checks the actual muzzle-to-target firing lane.
                if (Arrived()) { Stop(); if (weapon && CanShoot && target) weapon.TryShoot(target.position); }
                if (Time.time >= stateUntil)
                {
                    if (cover && cover.IsOver(peekIndex)) { CurrentAction = Action.Hiding; stateUntil = Time.time + HideDuration(); }
                    else if (cover && Move(cover.transform.position, runSpeed)) { CurrentAction = Action.SeekingCover; stateUntil = Time.time + 6; }
                    else { ReleaseCover(); CurrentAction = Action.Attacking; }
                }
                return;
            }
            if (Time.time < rushAttackUntil && target)
            {
                CurrentAction = Action.Attacking;
                Stop();
                Face(target.position);
                if (weapon) weapon.TryShoot(target.position);
                return;
            }
            if (Time.time >= nextCover && Time.time >= rushAttackUntil && Navigable)
            {
                nextCover = Time.time + 4;
                if (FindCover()) return;
            }
            if (visible)
            {
                CurrentAction = Action.Attacking; Stop(); Face(lastSeen);
                if (weapon) weapon.TryShoot(target.position);
            }
            else
            {
                CurrentAction = Action.Searching;
                if (Time.time >= nextDestination) { Move(lastSeen, runSpeed); nextDestination = Time.time + .6f; }
            }
        }
        void Idle()
        {
            CurrentAction = idleAction;
            if (idleAction == Action.Standing) { Stop(); return; }
            if (idleAction == Action.Squatting) { Stop(); return; }
            if (idleAction == Action.Sitting)
            {
                if (seat && Vector3.Distance(transform.position, seat.position) > .3f)
                { if (Time.time >= nextDestination) { Move(seat.position, walkSpeed); nextDestination = Time.time + 1; } }
                else { Stop(); if (seat) transform.rotation = seat.rotation; }
                return;
            }
            if (!Arrived() || Time.time < nextDestination) return;
            if (idleAction == Action.Patrol && patrolPoints != null && patrolPoints.Length > 0)
            {
                var point = patrolPoints[patrolIndex++ % patrolPoints.Length];
                if (point) Move(point.position, walkSpeed);
            }
            else
            {
                Vector2 random = Random.insideUnitCircle * 5;
                Move(home + new Vector3(random.x, 0, random.y), walkSpeed);
            }
            nextDestination = Time.time + Random.Range(3f, 6f);
        }
        bool FindCover()
        {
            ScavengerCover best = null; float score = float.PositiveInfinity;
            foreach (var candidate in ScavengerCover.Active)
            {
                if (!candidate || candidate.PeekCount == 0 || !candidate.Available(this)) continue;
                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance > coverSearchRadius || distance >= score) continue;
                float hiddenHeight = candidate.crouchWhenHiding ? candidate.crouchedSightHeight : 1.4f;
                if (ClearLine(candidate.transform.position + Vector3.up * hiddenHeight, lastSeen) ||
                    !Reachable(candidate.transform.position) || !HasUsablePeek(candidate)) continue;
                score = distance; best = candidate;
            }
            if (!best || !best.Claim(this)) return false;
            cover = best;
            peekIndex = -1;
            if (!Move(cover.transform.position, runSpeed)) { ReleaseCover(); return false; }
            CurrentAction = Action.SeekingCover; stateUntil = Time.time + 8; return true;
        }
        int ChoosePeek(ScavengerCover candidate, int previous)
        {
            int fallback = -1;
            for (int step = 1; step <= candidate.PeekCount; step++)
            {
                int index = (previous + step) % candidate.PeekCount;
                var marker = candidate.GetPeek(index);
                if (marker == null || !marker.point) continue;
                if (marker.style == ScavengerCover.PeekStyle.Side && !Reachable(marker.point.position)) continue;
                if (fallback < 0) fallback = index;
                Vector3 eye = marker.point.position + Vector3.up * ScavengerCover.EffectiveSightHeight(marker);
                if (!ClearLine(eye, lastSeen)) continue;
                return index;
            }
            return fallback;
        }
        bool HasUsablePeek(ScavengerCover candidate)
        {
            for (int i = 0; i < candidate.PeekCount; i++)
            {
                var marker = candidate.GetPeek(i);
                if (marker != null && marker.point &&
                    (marker.style == ScavengerCover.PeekStyle.Over || Reachable(marker.point.position))) return true;
            }
            return false;
        }
        bool Reachable(Vector3 point) => NavMesh.SamplePosition(point, out var hit, 1, agent.areaMask) &&
            agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete;
        bool Move(Vector3 point, float speed)
        {
            if (!Navigable || !NavMesh.SamplePosition(point, out var hit, 1, agent.areaMask) ||
                !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            agent.speed = speed; agent.isStopped = false; return agent.SetPath(path);
        }
        bool Arrived() => !Navigable || (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + .1f));
        void Stop() { if (Navigable) { agent.ResetPath(); agent.isStopped = true; } }
        void Face(Vector3 point)
        {
            Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 240 * Time.deltaTime);
        }
        public bool ClearLine(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, sightMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (target && (hit.transform == target || target.IsChildOf(hit.transform))) continue;
                if (targetRoot && hit.transform.IsChildOf(targetRoot)) continue;
                return false;
            }
            return true;
        }
        public void Alert(Transform attacker)
        {
            if (CurrentAction == Action.Dead) return;
            ResolveTarget();
            // Player weapons are held objects: retain the HMD as aim target when available.
            if (!target && attacker) target = attacker;
            if (!target) return;
            lastSeen = target.position; lastSeenTime = Time.time; CurrentAction = Action.Attacking;
            ReleaseCover(); nextCover = 0;
        }
        void ReleaseCover() { if (cover) cover.Release(this); cover = null; peekIndex = -1; }
        public void OnKnockout()
        {
            Stop(); ReleaseCover(); CurrentAction = idleAction;
            if (weapon) { weapon.DropAsLoot(); weapon = null; }
            if (pose) pose.gun = null;
        }
        public void Die()
        {
            Stop(); ReleaseCover(); CurrentAction = Action.Dead;
            if (agent) agent.enabled = false;
            var belt = GetComponent<ScavengerAmmoBelt>();
            if (belt) belt.DropRemaining();
            if (weapon) weapon.DropAsLoot();
            if (pose) pose.gun = null;
        }
        void OnDisable() { ReleaseCover(); }
    }
}
