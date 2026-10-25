using ScavengerNPC;
using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    // Runs after BanditRigMotion, which owns the rest pose and disables this rig's Animator.
    [DefaultExecutionOrder(2000)]
    public sealed class MerchantHandshakePose : MonoBehaviour
    {
        public FriendlyNpcInteraction merchant;
        public HandshakeTarget handshake;
        public Animator animator;
        public Vector3 handTargetLocal = new(.22f, 1.25f, .55f);
        [Min(.1f)] public float blendSpeed = 3f;
        [Range(0f, 30f)] public float maxHeadYaw = 20f;
        [Range(0f, 20f)] public float maxHeadPitch = 12f;

        Transform rightUpper, rightLower, rightHand;
        Transform leftUpper, leftLower, leftHand, head;
        float handshakeWeight, foldedWeight, waveWeight, presentWeight;
        float headYaw, headPitch;
        float nodStart = -10f, waveStart = -10f, presentStart = -10f;
        float nextFoldChange;
        float nextIdleGlance, idleYaw;
        bool armsFolded;

        public void PlayGesture(string gestureId)
        {
            if (gestureId == "nod") nodStart = Time.time;
            else if (gestureId == "beckon" || gestureId == "wave") waveStart = Time.time;
            else if (gestureId == "present") presentStart = Time.time;
        }

        void Awake()
        {
            if (!merchant) merchant = GetComponent<FriendlyNpcInteraction>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
            {
                rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            else
            {
                var motion = GetComponent<BanditRigMotion>();
                var root = motion && motion.visualRoot ? motion.visualRoot : transform;
                foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                {
                    switch (bone.name)
                    {
                        case "CC_Base_R_Upperarm_074": rightUpper = bone; break;
                        case "CC_Base_R_Forearm_077": rightLower = bone; break;
                        case "CC_Base_R_Hand_081": rightHand = bone; break;
                        case "CC_Base_L_Upperarm_050": leftUpper = bone; break;
                        case "CC_Base_L_Forearm_051": leftLower = bone; break;
                        case "CC_Base_L_Hand_055": leftHand = bone; break;
                        case "CC_Base_Head_038": head = bone; break;
                    }
                }
            }
            nextFoldChange = Time.time + 3f;
            nextIdleGlance = Time.time + 5f;
        }

        void LateUpdate()
        {
            if (!merchant || !head) return;
            if (merchant.health && merchant.health.State != NpcConsciousness.Conscious)
            {
                handshakeWeight = foldedWeight = waveWeight = presentWeight = 0f;
                return;
            }

            float dt = Time.deltaTime;
            bool offeringHand = handshake && handshake.IsArmed && merchant.IsTrading;
            handshakeWeight = Mathf.MoveTowards(handshakeWeight, offeringHand ? 1f : 0f, blendSpeed * dt);

            // Quiet folded-arm intervals punctuate a relaxed standing idle. The
            // arms unfold before talking, presenting goods, or offering a handshake.
            bool freeToIdle = !merchant.IsAttending && handshakeWeight < .01f;
            if (freeToIdle && Time.time >= nextFoldChange)
            {
                armsFolded = !armsFolded;
                nextFoldChange = Time.time + (armsFolded ? 4.5f : 6.5f);
            }
            if (!freeToIdle) armsFolded = false;
            foldedWeight = Mathf.MoveTowards(foldedWeight, freeToIdle && armsFolded ? .85f : 0f, dt * 2f);
            waveWeight = Mathf.MoveTowards(waveWeight,
                !offeringHand && Time.time - waveStart < 1.1f ? .72f : 0f, dt * 2.8f);
            presentWeight = Mathf.MoveTowards(presentWeight,
                !offeringHand && Time.time - presentStart < 1.5f ? .65f : 0f, dt * 2.8f);

            // BanditRigMotion supplies the base pose each frame. A world-space
            // LookRotation on this bone caused the observed full head spin.
            float targetYaw = 0f, targetPitch = 0f;
            if (merchant.IsAttending && merchant.playerHead)
            {
                Vector3 local = transform.InverseTransformDirection(
                    merchant.playerHead.position - head.position);
                float planar = new Vector2(local.x, local.z).magnitude;
                if (planar > .05f)
                {
                    targetYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg,
                        -maxHeadYaw, maxHeadYaw);
                    targetPitch = Mathf.Clamp(-Mathf.Atan2(local.y, planar) * Mathf.Rad2Deg,
                        -maxHeadPitch, maxHeadPitch);
                }
            }
            else if (!offeringHand)
            {
                if (Time.time >= nextIdleGlance)
                {
                    idleYaw = Random.Range(-5f, 5f);
                    nextIdleGlance = Time.time + Random.Range(6f, 11f);
                }
                targetYaw = idleYaw;
            }
            headYaw = Mathf.MoveTowards(headYaw, targetYaw, 55f * dt);
            headPitch = Mathf.MoveTowards(headPitch, targetPitch, 45f * dt);
            float nod = 0f;
            float nodAge = Time.time - nodStart;
            if (nodAge >= 0f && nodAge < .6f)
                nod = Mathf.Sin(nodAge / .6f * Mathf.PI) * 3f;
            head.localRotation *= Quaternion.Euler(headPitch + nod, headYaw, 0f);

            if (!rightUpper || !rightLower || !rightHand) return;
            if (foldedWeight > .001f && leftUpper && leftLower && leftHand)
            {
                Vector3 rightFold = transform.TransformPoint(new Vector3(-.17f, 1.17f, .16f));
                Vector3 leftFold = transform.TransformPoint(new Vector3(.17f, 1.12f, .19f));
                SolveBlended(rightUpper, rightLower, rightHand, rightFold,
                    transform.right + transform.forward * .25f, foldedWeight);
                SolveBlended(leftUpper, leftLower, leftHand, leftFold,
                    -transform.right + transform.forward * .25f, foldedWeight);
            }

            float rightWeight = Mathf.Max(handshakeWeight, waveWeight, presentWeight);
            if (rightWeight <= .001f) return;
            Vector3 desired;
            if (handshakeWeight > .05f)
                desired = transform.TransformPoint(handTargetLocal);
            else if (presentWeight > waveWeight)
                desired = transform.TransformPoint(new Vector3(.38f, 1.02f, .42f));
            else
            {
                float wave = Mathf.Sin(Mathf.Clamp01((Time.time - waveStart) / 1.1f) * Mathf.PI * 2f);
                desired = transform.TransformPoint(new Vector3(.37f + wave * .035f, 1.47f, .25f));
            }
            SolveBlended(rightUpper, rightLower, rightHand, desired,
                transform.right * .6f - transform.up * .3f, rightWeight);
        }

        static void SolveBlended(Transform upper, Transform lower, Transform hand,
            Vector3 target, Vector3 bend, float weight)
        {
            Quaternion upperBefore = upper.rotation, lowerBefore = lower.rotation;
            ScavengerPose.Solve(upper, lower, hand, target, bend);
            Quaternion upperSolved = upper.rotation, lowerSolved = lower.rotation;
            upper.rotation = Quaternion.Slerp(upperBefore, upperSolved, weight);
            lower.rotation = Quaternion.Slerp(lowerBefore, lowerSolved, weight);
        }
    }
}
