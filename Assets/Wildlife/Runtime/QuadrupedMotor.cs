using UnityEngine;
using MyAssets.Planet;
namespace Zandea.Wildlife
{
    // Sole root movement authority. Animator is in-place; Rigidbody is a kinematic hit receiver.
    [DefaultExecutionOrder(-100)]
    public sealed class QuadrupedMotor : MonoBehaviour, IFloatingOriginListener
    {
        public enum MoveBlock { None, Obstacle, NoGround, Step }
        public MoveBlock LastBlock { get; private set; }
        public float RequestedSpeed => requestedSpeed;
        public CreatureProfile profile;
        public Animator animator;
        public Transform visual;
        public bool Dead { get; private set; }
        public float Speed { get; private set; }
        public float Cycle { get; private set; }
        public float Turn { get; private set; }
        public bool Trotting => Speed > profile.walkSpeed * 1.35f;
        public Vector3 Velocity { get; private set; }
        Vector3 intention;
        float requestedSpeed;
        Rigidbody body;
        readonly RaycastHit[] hits = new RaycastHit[24];
        static readonly int SpeedId = Animator.StringToHash("Speed"), RateId = Animator.StringToHash("Playback"), TurnId = Animator.StringToHash("Turning");
        public void SetIntent(Vector3 direction, float speed)
        {
            intention = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            requestedSpeed = Mathf.Clamp(speed, 0, profile.runSpeed);
        }
        public void Kill() { Dead = true; Speed = requestedSpeed = 0; Velocity = Vector3.zero; animator.SetBool("Dead", true); }
        void Awake() { body = GetComponent<Rigidbody>(); }
        void OnEnable() { Dead = false; Speed = Cycle = requestedSpeed = 0; Velocity = Vector3.zero; }
        public bool Ground(Vector3 point, float up, float down, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * up, Vector3.down, hits, up + down,
                profile.groundLayers, QueryTriggerInteraction.Ignore);
            best = default; float distance = float.MaxValue;
            for (int i = 0; i < count; i++)
                if (!hits[i].transform.IsChildOf(transform) && hits[i].distance < distance && Vector3.Angle(hits[i].normal, Vector3.up) <= profile.maximumSlope)
                { best = hits[i]; distance = best.distance; }
            return distance < float.MaxValue;
        }
        void Update()
        {
            if (!profile || !animator || Dead) return;
            float dt = Time.deltaTime;
            float angle = intention.sqrMagnitude > .001f ? Vector3.SignedAngle(transform.forward, intention, Vector3.up) : 0;
            Turn = Mathf.Clamp(angle / 60, -1, 1);
            // Turning in place uses a stepping clip; larger turns reduce forward speed.
            float desired = requestedSpeed * Mathf.Clamp01(1 - Mathf.Abs(angle) / 115);
            Speed = Mathf.MoveTowards(Speed, desired, (Speed < desired ? profile.acceleration : profile.deceleration) * dt);
            float yaw = Mathf.Clamp(angle, -profile.turnRate * dt, profile.turnRate * dt);
            Vector3 previous = body ? body.position : transform.position;
            Vector3 next = previous + Quaternion.AngleAxis(yaw, Vector3.up) * transform.forward * Speed * dt;
            bool blocked = false;
            int count = Physics.SphereCastNonAlloc(previous + Vector3.up * 1.1f, .55f, transform.forward, hits,
                .65f + Speed * dt, profile.groundLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (!hits[i].transform.IsChildOf(transform) && hits[i].normal.y < .55f) blocked = true;
            bool found = Ground(next, 1.2f, 1.4f, out var ground);
            LastBlock = blocked ? MoveBlock.Obstacle : !found ? MoveBlock.NoGround :
                Mathf.Abs(ground.point.y - previous.y) >= .65f ? MoveBlock.Step : MoveBlock.None;
            if (LastBlock == MoveBlock.None)
            {
                next.y = Mathf.Lerp(previous.y, ground.point.y + profile.bodyHeightOffset, 1 - Mathf.Exp(-10 * dt));
                if (body) body.position = next; else transform.position = next;
                if (visual)
                {
                    Vector3 normal = Quaternion.Inverse(transform.rotation) * ground.normal;
                    Quaternion slope = Quaternion.FromToRotation(Vector3.up, normal);
                    visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.RotateTowards(Quaternion.identity, slope, 16), 1 - Mathf.Exp(-3 * dt));
                }
            }
            else Speed = 0;
            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * transform.rotation;
            if (body) body.rotation = rotation; else transform.rotation = rotation;
            Velocity = ((body ? body.position : transform.position) - previous) / Mathf.Max(dt, .0001f);
            float nominal = Trotting ? profile.trotSpeed : profile.walkSpeed;
            float period = Trotting ? .95f : 1.6f;
            Cycle = Mathf.Repeat(Cycle + Speed / nominal / period * dt, 1);
            animator.SetFloat(SpeedId, Speed);
            animator.SetFloat(RateId, Speed > .03f ? Mathf.Max(.05f, Speed / nominal) : 1);
            animator.SetFloat(TurnId, Mathf.Abs(Turn));
        }
        public void OnOriginShift(Vector3 shift) { } // Intent and velocity are vectors, not stored world positions.
    }
}
