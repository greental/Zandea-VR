using ScavengerNPC;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [DefaultExecutionOrder(2000)]
    public sealed class MerchantHandshakePose : MonoBehaviour
    {
        public FriendlyNpcInteraction merchant;
        public HandshakeTarget handshake;
        public Animator animator;
        public Vector3 handTargetLocal = new(.26f, 1.25f, .46f);
        public float blendSpeed = 3f;
        Transform upper, lower, hand, head;
        float weight;
        float nodStart = -10f;

        public void PlayGesture(string gestureId)
        {
            if (gestureId == "nod") nodStart = Time.time;
        }

        void Awake()
        {
            if (!merchant) merchant = GetComponent<FriendlyNpcInteraction>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!animator || !animator.isHuman) return;
            upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        void LateUpdate()
        {
            if (!merchant || !animator || !animator.enabled) return;
            float target = handshake && handshake.IsArmed && merchant.IsTrading ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);
            if (merchant.IsConversing && head && merchant.playerHead)
            {
                var look = merchant.playerHead.position - head.position;
                if (look.sqrMagnitude > .04f)
                    head.rotation = Quaternion.RotateTowards(head.rotation,
                        Quaternion.LookRotation(look, transform.up), 30f);
            }
            if (head && Time.time - nodStart < .7f && weight < .1f)
            {
                float progress = (Time.time - nodStart) / .7f;
                head.localRotation *= Quaternion.Euler(Mathf.Sin(progress * Mathf.PI * 2f) * 4f, 0f, 0f);
            }
            if (weight <= .001f || !upper || !lower || !hand) return;
            Vector3 desired = transform.TransformPoint(handTargetLocal);
            Vector3 targetPoint = Vector3.Lerp(hand.position, desired, weight);
            ScavengerPose.Solve(upper, lower, hand, targetPoint, -transform.up + transform.right * .3f);
            hand.rotation = Quaternion.Slerp(hand.rotation,
                Quaternion.LookRotation(transform.forward, transform.up), weight);
        }
    }
}
