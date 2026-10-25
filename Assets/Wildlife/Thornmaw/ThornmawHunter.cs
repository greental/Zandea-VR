using MyAssets.Planet;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace Zandea.Wildlife
{
    /// <summary>Grounded desert patrol, sight based pursuit, and timed melee bite.</summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class ThornmawHunter : MonoBehaviour, IDamageable, IFloatingOriginListener
    {
        [Header("Patrol")]
        [Min(2f)] public float patrolRadius = 22f;
        [Min(.1f)] public float walkSpeed = 1.35f;
        [Min(.1f)] public float runSpeed = 3.7f;
        [Min(1f)] public float turnDegreesPerSecond = 260f;
        [Min(0f)] public float bodyHeight = .5f;
        [Range(0f, 60f)] public float maximumSlope = 38f;
        public LayerMask groundLayers = ~0;

        [Header("Hunt")]
        [Min(1f)] public float sightRadius = 18f;
        [Range(30f, 360f)] public float fieldOfView = 230f;
        [Min(.1f)] public float senseInterval = .25f;
        [Min(.1f)] public float biteRange = 1.35f;
        [Min(.1f)] public float biteCooldown = 1.25f;
        [Min(1f)] public float biteDamage = 22f;
        [Min(1f)] public float maxHealth = 180f;
        public LayerMask sightLayers = ~0;

        public float CurrentHealth { get; private set; }
        public bool Dead => CurrentHealth <= 0f;
        public Transform Target => target;

        readonly Collider[] nearby = new Collider[96];
        readonly RaycastHit[] rayHits = new RaycastHit[48];
        Animator animator;
        Rigidbody body;
        Transform target;
        IDamageable targetDamageable;
        Collider targetCollider;
        Vector3 home, destination;
        float nextSense, nextPatrol, nextBite, impactAt, speed;
        bool bitePending;
        static readonly int RunningId = Animator.StringToHash("IsRunning");

        void Awake()
        {
            animator = GetComponent<Animator>();
            body = GetComponent<Rigidbody>();
            if (body) { body.isKinematic = true; body.useGravity = false; }
        }

        void OnEnable()
        {
            home = transform.position;
            destination = home;
            CurrentHealth = maxHealth;
            nextPatrol = Time.time;
            nextSense = Time.time + Random.Range(0f, senseInterval);
            speed = 0f;
            bitePending = false;
        }

        void Update()
        {
            if (Dead) return;
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + Mathf.Max(.1f, senseInterval);
                Sense();
            }

            if (bitePending && Time.time >= impactAt)
            {
                bitePending = false;
                Bite();
            }

            if (target)
            {
                Vector3 delta = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
                float distance = delta.magnitude;
                if (distance <= biteRange)
                {
                    Move(delta, 0f, false);
                    if (!bitePending && Time.time >= nextBite &&
                        Vector3.Angle(transform.forward, delta) < 60f && CanSee(targetCollider))
                    {
                        bitePending = true;
                        impactAt = Time.time + .22f;
                        nextBite = Time.time + biteCooldown;
                    }
                    if (bitePending && animator)
                    {
                        animator.SetBool(RunningId, true);
                        animator.speed = 1.55f;
                    }
                }
                else Move(delta, runSpeed, true);
            }
            else
            {
                Vector3 delta = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
                if (delta.sqrMagnitude < 2f || Time.time >= nextPatrol)
                {
                    PickPatrolPoint();
                    delta = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up);
                }
                Move(delta, delta.sqrMagnitude > 1f ? walkSpeed : 0f, false);
            }
        }

        void Sense()
        {
            Transform best = null;
            IDamageable bestDamageable = null;
            Collider bestCollider = null;
            float bestDistance = sightRadius * sightRadius;
            int count = Physics.OverlapSphereNonAlloc(transform.position, sightRadius, nearby, sightLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = nearby[i];
                if (!collider || collider.transform.IsChildOf(transform) ||
                    collider.GetComponentInParent<ThornmawHunter>()) continue;
                var damageable = collider.GetComponentInParent<IDamageable>();
                var component = damageable as Component;
                if (!component || damageable.GetCombatState() != ShipCombatState.CombatReady) continue;
                Vector3 delta = collider.bounds.center - transform.position;
                float distance = delta.sqrMagnitude;
                if (distance >= bestDistance || !CanSee(collider)) continue;
                best = component.transform;
                bestDamageable = damageable;
                bestCollider = collider;
                bestDistance = distance;
            }
            target = best;
            targetDamageable = bestDamageable;
            targetCollider = bestCollider;
        }

        bool CanSee(Collider candidate)
        {
            if (!candidate || candidate.GetComponentInParent<ThornmawHunter>()) return false;
            Vector3 eye = transform.position + Vector3.up * .3f;
            Vector3 direction = candidate.bounds.center - eye;
            float distance = direction.magnitude;
            if (distance < .01f || distance > sightRadius) return false;
            if (distance > biteRange && Vector3.Angle(transform.forward, direction) > fieldOfView * .5f) return false;
            int count = Physics.RaycastNonAlloc(eye, direction / distance, rayHits, distance + .1f,
                sightLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            Collider first = null;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (!hit.collider || hit.transform.IsChildOf(transform) || hit.distance >= closest) continue;
                closest = hit.distance;
                first = hit.collider;
            }
            return first && (first == candidate || first.transform.IsChildOf(candidate.transform.root));
        }

        void Bite()
        {
            if (!target || targetDamageable == null || !targetCollider ||
                targetCollider.GetComponentInParent<ThornmawHunter>() ||
                targetDamageable.GetCombatState() != ShipCombatState.CombatReady) return;
            Vector3 delta = targetCollider.bounds.center - transform.position;
            if (Vector3.ProjectOnPlane(delta, Vector3.up).magnitude > biteRange + .25f ||
                Vector3.Angle(transform.forward, delta) > 65f || !CanSee(targetCollider)) return;
            targetDamageable.ApplyDamage(new DamagePacket
            {
                BaseDamage = biteDamage,
                DamageType = DamageType.Kinetic,
                PhysicalImpact = PhysicalImpactKind.Sharp,
                HitPointWorld = targetCollider.ClosestPoint(transform.position + Vector3.up * .3f),
                HitNormalWorld = -delta.normalized,
                Source = gameObject,
                WeaponId = "Thornmaw bite",
                Timestamp = Time.time
            });
        }

        void PickPatrolPoint()
        {
            home = Vector3.Lerp(home, transform.position, .1f);
            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = Random.insideUnitCircle * patrolRadius;
                Vector3 candidate = home + new Vector3(offset.x, 0f, offset.y);
                if (!TryGround(candidate, out var point)) continue;
                destination = point;
                nextPatrol = Time.time + Random.Range(8f, 18f);
                return;
            }
            destination = transform.position;
            nextPatrol = Time.time + 2f;
        }

        bool TryGround(Vector3 point, out Vector3 ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, rayHits, 6f,
                groundLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (!hit.collider || hit.transform.IsChildOf(transform) || hit.collider.attachedRigidbody ||
                    Vector3.Angle(hit.normal, Vector3.up) > maximumSlope || hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.point;
            }
            return nearest < float.PositiveInfinity;
        }

        void Move(Vector3 direction, float requestedSpeed, bool running)
        {
            float dt = Time.deltaTime;
            if (direction.sqrMagnitude > .001f)
            {
                Quaternion facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnDegreesPerSecond * dt);
            }
            Vector3 heading = transform.forward;
            Vector3 next = transform.position + heading * requestedSpeed * dt;
            bool grounded = TryGround(next, out var ground);
            if (requestedSpeed > 0f && grounded)
            {
                // A forward feeler avoids walls while terrain probes keep the spider on streamed desert tiles.
                Vector3 origin = transform.position + Vector3.up * .25f;
                int count = Physics.RaycastNonAlloc(origin, heading, rayHits, .65f, groundLayers,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (rayHits[i].collider && !rayHits[i].transform.IsChildOf(transform) &&
                        rayHits[i].normal.y < .5f) { grounded = false; break; }
            }
            if (grounded && Mathf.Abs(ground.y + bodyHeight - transform.position.y) > .75f)
                grounded = false;
            float desired = grounded ? requestedSpeed : 0f;
            speed = Mathf.MoveTowards(speed, desired, 8f * dt);
            if (speed > .01f && grounded)
            {
                next = transform.position + heading * speed * dt;
                next.y = Mathf.Lerp(transform.position.y, ground.y + bodyHeight, 1f - Mathf.Exp(-12f * dt));
                if (body) body.MovePosition(next); else transform.position = next;
            }
            if (animator)
            {
                animator.SetBool(RunningId, running && speed > walkSpeed * 1.1f);
                animator.speed = speed > .05f ? Mathf.Clamp(speed / (running ? runSpeed : walkSpeed), .5f, 1.5f) : .05f;
            }
        }

        public void ApplyDamage(in DamagePacket packet)
        {
            if (Dead || !float.IsFinite(packet.BaseDamage) || packet.BaseDamage <= 0f) return;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - packet.BaseDamage);
            if (Dead)
            {
                bitePending = false;
                target = null;
                if (animator) animator.speed = 0f;
            }
        }

        public ShipCombatState GetCombatState() => Dead ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;

        public void OnOriginShift(Vector3 shift)
        {
            home -= shift;
            destination -= shift;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, sightRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, biteRange);
        }
    }
}
