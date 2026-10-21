using JunkerBody;
using UnityEngine;

namespace KenshiVR.CharacterCreator
{
    // The creator mesh has no retargeted walk clip yet. Drive the existing two-bone
    // leg IK from measured XR locomotion, leaving HMD and hand tracking untouched.
    [DefaultExecutionOrder(10005)]
    public sealed class CreatorPlayerGait : MonoBehaviour
    {
        public VRBodyController body;
        public JunkerSprint sprint;
        [Min(.01f)] public float strideMeters = .16f;
        [Min(.01f)] public float footLiftMeters = .08f;
        [Min(.1f)] public float stepsPerMeter = 1.2f;
        float phase;

        void LateUpdate()
        {
            // Retargeted locomotion clips supersede this legacy fallback.
            if (body && body.animator && body.animator.runtimeAnimatorController &&
                body.animator.runtimeAnimatorController.animationClips.Length > 1) return;
            if (!body || !sprint || !body.avatarRoot || !body.leftLeg.Valid || !body.rightLeg.Valid) return;
            float speed = Mathf.Max(0f, sprint.MeasuredSpeed);
            if (speed < .05f) return;
            phase += speed * stepsPerMeter * Mathf.PI * 2f * Time.deltaTime;
            float amount = Mathf.Clamp01(speed / Mathf.Max(.1f, sprint.walkSpeed));
            Step(body.leftLeg, phase, amount);
            Step(body.rightLeg, phase + Mathf.PI, amount);
        }

        void Step(VRBodyController.Limb leg, float angle, float amount)
        {
            Vector3 rest = leg.target.position;
            float forward = Mathf.Sin(angle) * strideMeters * amount;
            float lift = Mathf.Max(0f, Mathf.Cos(angle)) * footLiftMeters * amount;
            leg.target.position = rest + body.avatarRoot.forward * forward + body.avatarRoot.up * lift;
            leg.hint.position = leg.upper.position + body.avatarRoot.rotation * leg.hintOffset;
            VRBodyController.SolveTwoBone(leg.upper, leg.lower, leg.tip,
                leg.target.position, leg.hint.position);
            leg.tip.rotation = leg.target.rotation;
        }
    }
}
