using UnityEngine;
using MyAssets.Planet;
namespace Zandea.Wildlife
{
    // Disable this component when a future rider takes ownership of motor.SetIntent.
    [RequireComponent(typeof(QuadrupedMotor))]
    public sealed class WildlifeBrain : MonoBehaviour, IFloatingOriginListener
    {
        public enum Activity { Idle, Wander, Graze, Alert, Flee, Recover }
        public Activity State { get; private set; }
        public Transform threat;
        public bool detectMainCamera = true;
        QuadrupedMotor motor;
        CreatureProfile profile;
        Vector3 destination, home;
        float until, nextSense, frightenedUntil, lastProgressTime;
        Vector3 lastProgressPosition;
        void Awake() { motor = GetComponent<QuadrupedMotor>(); profile = motor.profile; }
        void OnEnable() { home = transform.position; lastProgressPosition = home; lastProgressTime = Time.time; }
        void Start() { Enter(Activity.Idle); }
        public void Frighten(Transform source)
        {
            if (source) threat = source;
            frightenedUntil = Time.time + 8;
            Enter(Activity.Flee);
        }
        public void SetThreat(Transform source) { threat = source; detectMainCamera = false; }
        void Update()
        {
            if (!motor || !profile || motor.Dead) return;
            if (Time.time >= nextSense)
            {
                nextSense = Time.time + profile.perceptionInterval;
                if (!threat && detectMainCamera && Camera.main) threat = Camera.main.transform;
                float distance = threat ? Vector3.Distance(transform.position, threat.position) : float.PositiveInfinity;
                if ((distance < profile.fleeThreshold || Time.time < frightenedUntil) && State != Activity.Flee) Enter(Activity.Flee);
                else if (State == Activity.Flee && distance > profile.fleeDistance && Time.time > frightenedUntil) Enter(Activity.Recover);
                else if (distance < profile.detectionRadius && State != Activity.Flee && State != Activity.Alert) Enter(Activity.Alert);
                else if (State == Activity.Alert && distance > profile.detectionRadius * 1.2f && Time.time > until) Enter(Activity.Recover);
            }
            switch (State)
            {
                case Activity.Idle:
                case Activity.Graze:
                case Activity.Recover:
                    motor.SetIntent(Vector3.zero, 0);
                    if (Time.time > until)
                    {
                        if (State == Activity.Idle && Random.value < .45f) Enter(Activity.Graze);
                        else if (ChooseDestination()) Enter(Activity.Wander);
                        else Enter(Activity.Idle);
                    }
                    break;
                case Activity.Alert:
                    motor.SetIntent(threat ? threat.position - transform.position : Vector3.zero, 0);
                    break;
                case Activity.Wander:
                    Vector3 delta = destination - transform.position;
                    motor.SetIntent(delta, profile.walkSpeed);
                    if (Vector3.ProjectOnPlane(delta, Vector3.up).sqrMagnitude < 1.5f || Time.time > until) Enter(Activity.Idle);
                    break;
                case Activity.Flee:
                    Vector3 away = threat ? transform.position - threat.position : transform.forward;
                    if (away.sqrMagnitude < .01f) away = -transform.forward;
                    // Local feelers choose a grounded heading, with no dependency on streamed NavMesh data.
                    Vector3 heading = away.normalized;
                    if (!motor.Ground(transform.position + heading * 3, 2, 3, out _))
                        heading = Quaternion.AngleAxis(65, Vector3.up) * heading;
                    if (Time.time - lastProgressTime > 2)
                    {
                        if ((transform.position - lastProgressPosition).sqrMagnitude < .2f) heading = Quaternion.AngleAxis(75, Vector3.up) * heading;
                        lastProgressPosition = transform.position; lastProgressTime = Time.time;
                    }
                    motor.SetIntent(heading, profile.runSpeed);
                    break;
            }
        }
        bool ChooseDestination()
        {
            // Home follows the creature slowly; animals are not tied to recycled terrain objects.
            home = Vector3.Lerp(home, transform.position, .25f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 offset = Random.insideUnitCircle * profile.wanderRadius;
                Vector3 candidate = home + new Vector3(offset.x, 0, offset.y);
                if (motor.Ground(candidate, 8, 16, out var hit)) { destination = hit.point; return true; }
            }
            return false;
        }
        void Enter(Activity activity)
        {
            State = activity;
            Vector2 duration = activity == Activity.Graze ? profile.grazeSeconds : profile.idleSeconds;
            until = Time.time + (activity == Activity.Wander ? 30 : Random.Range(duration.x, duration.y));
            motor.animator.SetBool("Grazing", activity == Activity.Graze);
            motor.animator.SetBool("Alert", activity == Activity.Alert);
        }
        void OnDisable() { if (motor) { motor.SetIntent(Vector3.zero, 0); if (motor.animator) { motor.animator.SetBool("Grazing", false); motor.animator.SetBool("Alert", false); } } }
        public void OnOriginShift(Vector3 shift) { destination -= shift; home -= shift; lastProgressPosition -= shift; }
    }
}
