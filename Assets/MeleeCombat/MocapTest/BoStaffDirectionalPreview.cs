using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Play-mode scrubber for the eight directional controller states. It samples
    // the authored mocap clips and then runs the same rigid grip fit as gameplay.
    [DefaultExecutionOrder(280), RequireComponent(typeof(Animator))]
    public sealed class BoStaffDirectionalPreview : MonoBehaviour
    {
        [Range(0, 7)] public int direction;
        [Range(0, 1)] public float normalizedTime;
        public bool playing = true;
        [Range(.1f, 2f)] public float playbackSpeed = 1f;
        public MocapStaffPreview grip;
        public AnimationClip[] directionClips;
        Animator animator;
        static readonly string[] StateNames = {
            "AttackDirection0", "AttackDirection1", "AttackDirection2", "AttackDirection3",
            "AttackDirection4", "AttackDirection5", "AttackDirection6", "AttackDirection7" };

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (!grip) grip = GetComponent<MocapStaffPreview>();
            animator.applyRootMotion = false;
        }
        void LateUpdate()
        {
            if (!animator || !grip || !animator.runtimeAnimatorController) return;
            int selected = Mathf.Clamp(direction, 0, 7);
            var clip = directionClips != null && directionClips.Length > selected ? directionClips[selected] : null;
            if (playing && clip && clip.length > 0)
                normalizedTime = Mathf.Repeat(normalizedTime + Time.deltaTime * playbackSpeed / clip.length, 1f);
            animator.Play(StateNames[selected], 0, Mathf.Clamp01(normalizedTime));
            animator.Update(0);
            BoStaffFootGrounding.Apply(animator, transform.root.position.y);
            grip.ApplyGripFromCurrentPose();
        }
    }
}
