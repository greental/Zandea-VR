using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Retargeted attack clips carry vertical hip motion from the source actor.
    // Translate the animated skeleton as a whole so its lowest foot remains on
    // the NPC's ground plane. Limb rotations and torso weight transfer are kept.
    public static class BoStaffFootGrounding
    {
        public static float Apply(Animator animator, float groundY)
        {
            if (!animator || !animator.isHuman) return 0;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!hips || !left || !right) return 0;
            float lowestFoot = Mathf.Min(left.position.y, right.position.y);
            float shift = Mathf.Clamp(groundY + .08f - lowestFoot, -.55f, .25f);
            hips.position += Vector3.up * shift;
            return shift;
        }
    }
}
