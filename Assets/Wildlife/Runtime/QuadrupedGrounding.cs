using System;
using UnityEngine;
using MyAssets.Planet;
namespace Zandea.Wildlife
{
    [DefaultExecutionOrder(100)]
    public sealed class QuadrupedGrounding : MonoBehaviour, IFloatingOriginListener
    {
        [Serializable] public sealed class Leg
        {
            public Transform upper, lower, ankle, foot;
            public float walkPhase, trotPhase;
            [NonSerialized] public bool planted;
            [NonSerialized] public Vector3 plant, correction;
        }
        public QuadrupedMotor motor;
        public Leg[] legs;
        Transform neck, head;
        float lookYaw;
        void OnEnable() { lookYaw = 0; if (legs != null) foreach (var l in legs) { l.planted = false; l.correction = Vector3.zero; } }
        void LateUpdate()
        {
            if (!motor || motor.Dead || legs == null) return;
            // Animator supplies the pitch and subtle chin movement. Add a signed
            // world-up glance after animation so the head leads a change of heading.
            if (!head && motor.animator)
                foreach (Transform bone in motor.animator.GetComponentsInChildren<Transform>(true))
                { if (bone.name == "Neck02") neck = bone; else if (bone.name == "Head") head = bone; }
            lookYaw = Mathf.MoveTowards(lookYaw, motor.Turn * 25f, 115f * Time.deltaTime);
            if (neck && head)
            {
                neck.rotation = Quaternion.AngleAxis(lookYaw * .35f, motor.transform.up) * neck.rotation;
                head.rotation = Quaternion.AngleAxis(lookYaw * .65f, motor.transform.up) * head.rotation;
            }
            float blend = 1 - Mathf.Exp(-motor.profile.groundingSharpness * Time.deltaTime);
            // Read the actual blended Animator cycle, avoiding a free-running IK clock drifting from clips.
            var state = motor.animator.GetCurrentAnimatorStateInfo(0);
            bool gait = state.IsName("Walk") || state.IsName("Trot") || state.IsName("Turn");
            bool trot = state.IsName("Trot");
            float phase = Mathf.Repeat(state.normalizedTime, 1);
            foreach (var leg in legs)
            {
                if (!leg.upper || !leg.lower || !leg.ankle || !leg.foot) continue;
                float p = Mathf.Repeat(phase + (trot ? leg.trotPhase : leg.walkPhase), 1);
                bool stance = !gait || p < (trot ? .55f : .68f);
                Vector3 animated = leg.foot.position;
                bool groundFound = motor.Ground(animated, motor.profile.footProbeHeight, 1.2f, out var hit);
                if (!groundFound) { leg.planted = false; continue; }
                Vector3 grounded = hit.point + Vector3.up * .08f;
                if (stance && !leg.planted) leg.plant = grounded;
                if (stance)
                {
                    // Re-query the contact each frame: recycled/missing tiles must never retain stale collider references.
                    if (motor.Ground(leg.plant, .8f, 1, out var contact)) leg.plant.y = contact.point.y + .08f;
                    else { leg.planted = false; continue; }
                }
                Vector3 desired = stance ? leg.plant : animated + Vector3.up * Mathf.Max(0, grounded.y - animated.y);
                if ((desired - animated).magnitude > motor.profile.footCorrection)
                { desired = animated + Vector3.ClampMagnitude(desired - animated, motor.profile.footCorrection); leg.plant = desired; }
                leg.correction = Vector3.Lerp(leg.correction, desired - animated, blend);
                Quaternion ankleRotation = leg.ankle.rotation;
                Solve(leg.upper, leg.lower, leg.ankle, leg.ankle.position + leg.correction, motor.transform.forward);
                leg.ankle.rotation = Quaternion.Slerp(ankleRotation, Quaternion.FromToRotation(Vector3.up, hit.normal) * ankleRotation, .65f);
                leg.planted = stance;
            }
        }
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c);
            Vector3 delta = target - a; float length = delta.magnitude;
            if (length < .0001f || l1 < .0001f || l2 < .0001f) return;
            Vector3 axis = delta / length;
            float d = Mathf.Clamp(length, Mathf.Abs(l1 - l2) + .001f, l1 + l2 - .001f);
            Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(upper.right, axis).normalized;
            float x = (l1 * l1 - l2 * l2 + d * d) / (2 * d);
            Vector3 knee = a + axis * x + bend * Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            upper.rotation = Quaternion.FromToRotation(b - a, knee - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + axis * d - lower.position) * lower.rotation;
        }
        public void OnOriginShift(Vector3 shift) { if (legs != null) foreach (var leg in legs) leg.plant -= shift; }
    }
}
