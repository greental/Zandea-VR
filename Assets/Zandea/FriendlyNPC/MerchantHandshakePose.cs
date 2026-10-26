using ScavengerNPC;
using KenshiVR.MeleeCombat;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
        [Range(0f, .4f)] public float handshakeStep = .28f;
        [Range(0f, 15f)] public float handshakeLeanDegrees = 9f;
        [Min(.1f)] public float blendSpeed = 3f;
        [Range(0f, 30f)] public float maxHeadYaw = 20f;
        [Range(0f, 20f)] public float maxHeadPitch = 12f;

        Transform rightUpper, rightLower, rightHand;
        Transform leftUpper, leftLower, leftHand, head, upperSpine, hips;
        Transform leftThigh, leftCalf, leftFoot, rightThigh, rightCalf, rightFoot;
        NavMeshAgent navAgent;
        Vector3 leftFootLocal, rightFootLocal;
        Quaternion previousRootRotation;
        float turnWeight, turnPhase, forwardStep;
        readonly List<FingerPose> fingers = new(30);
        struct FingerPose
        {
            public Transform bone;
            public Quaternion gripRotation;
            public Vector3 openingAxis;
            public float openingDegrees;
            public bool right;
        }
        Quaternion restingHeadRotation;
        Quaternion restingSpineRotation;
        float handshakeWeight, waveWeight, presentWeight;
        float foldedWeight;
        float headYaw, headPitch;
        float nodStart = -10f, waveStart = -10f, presentStart = -10f;
        float nextIdleGlance, idleYaw;

        public void PlayGesture(string gestureId)
        {
            if (gestureId == "nod") nodStart = Time.time;
            else if (gestureId == "beckon" || gestureId == "wave") waveStart = Time.time;
            else if (gestureId == "present") presentStart = Time.time;
        }

        void Awake()
        {
            handshakeStep = Mathf.Max(.26f, handshakeStep);
            handshakeLeanDegrees = Mathf.Max(7f, handshakeLeanDegrees);
            if (!merchant) merchant = GetComponent<FriendlyNpcInteraction>();
            if (!handshake) handshake = GetComponentInChildren<HandshakeTarget>(true);
            navAgent = GetComponent<NavMeshAgent>();
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
                upperSpine = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                leftCalf = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                rightCalf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
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
                        case "CC_Base_Spine02_035": upperSpine = bone; break;
                        case "CC_Base_Hip_02": hips = bone; break;
                        case "CC_Base_L_Thigh_04": leftThigh = bone; break;
                        case "CC_Base_L_Calf_05": leftCalf = bone; break;
                        case "CC_Base_L_Foot_06": leftFoot = bone; break;
                        case "CC_Base_R_Thigh_00": rightThigh = bone; break;
                        case "CC_Base_R_Calf_021": rightCalf = bone; break;
                        case "CC_Base_R_Foot_022": rightFoot = bone; break;
                    }
                }
            }
            if (!upperSpine)
                foreach (var bone in GetComponentsInChildren<Transform>(true))
                    if (bone.name == "CC_Base_Spine02_035") { upperSpine = bone; break; }
            if (head) restingHeadRotation = head.localRotation;
            if (upperSpine) restingSpineRotation = upperSpine.localRotation;
            if (leftFoot) leftFootLocal = transform.InverseTransformPoint(leftFoot.position);
            if (rightFoot) rightFootLocal = transform.InverseTransformPoint(rightFoot.position);
            previousRootRotation = transform.rotation;
            CacheFingers();
            // Start in the merchant rest pose immediately. In a headless or
            // low-frame-rate load, a zero-weight blend can leave the inherited
            // straight staff stance visible for several frames.
            foldedWeight = 1f;
            nextIdleGlance = Time.time + 5f;
        }

        void CacheFingers()
        {
            var root = animator ? animator.transform : transform;
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
            {
                bool right = bone.name.StartsWith("CC_Base_R_");
                if (!right && !bone.name.StartsWith("CC_Base_L_")) continue;
                bool thumb = bone.name.Contains("Thumb");
                if (!thumb && !bone.name.Contains("Index") && !bone.name.Contains("Mid") &&
                    !bone.name.Contains("Ring") && !bone.name.Contains("Pinky")) continue;
                if (bone.name.EndsWith("scaleCompensation")) continue;
                int segment = 0;
                for (int i = 1; i <= 3; i++)
                    if (bone.name.Contains(i + "_")) { segment = i; break; }
                if (segment == 0) continue;
                var hand = right ? rightHand : leftHand;
                if (!hand) continue;
                // CC_Base inserts zero-length scale compensation nodes between
                // phalanges. Walk to the next displaced descendant instead of
                // treating that helper as the finger direction.
                Transform tip = bone;
                while (tip.childCount > 0 &&
                    (tip.position - bone.position).sqrMagnitude < .000025f)
                    tip = tip.GetChild(0);
                Vector3 direction = (tip.position - bone.position).normalized;
                if (direction.sqrMagnitude < .01f) direction = bone.up;
                Vector3 axis = Vector3.Cross(direction, -hand.up).normalized;
                if (axis.sqrMagnitude < .01f) continue;
                fingers.Add(new FingerPose { bone = bone, gripRotation = bone.localRotation,
                    openingAxis = bone.InverseTransformDirection(axis),
                    openingDegrees = thumb ? 18f : segment == 1 ? 38f : segment == 2 ? 42f : 24f,
                    right = right });
            }
        }

        void LateUpdate()
        {
            if (!merchant || !head) return;
            if (merchant.health && merchant.health.State != NpcConsciousness.Conscious)
            {
                handshakeWeight = waveWeight = presentWeight = 0f;
                return;
            }
            if (merchant.brain && merchant.brain.hostileToPlayer) return;

            float dt = Time.deltaTime;
            bool offeringHand = handshake && handshake.IsArmed && merchant.IsTrading;
            float targetStep = offeringHand ? handshakeStep : 0f;
            float nextStep = Mathf.MoveTowards(forwardStep, targetStep, dt * .55f);
            float stepDelta = nextStep - forwardStep;
            if (Mathf.Abs(stepDelta) > .0001f)
            {
                Vector3 movement = transform.forward * stepDelta;
                if (navAgent && navAgent.enabled && navAgent.isOnNavMesh) navAgent.Move(movement);
                else transform.position += movement;
                forwardStep = nextStep;
            }
            // A restrained breath keeps the idle alive without moving the hands
            // or rocking the whole body. BanditRigMotion restores this bone first.
            if (upperSpine)
                upperSpine.localRotation = restingSpineRotation *
                    Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * .7f +
                        handshakeLeanDegrees * handshakeWeight, 0f,
                        !offeringHand ? Mathf.Sin(Time.time * .75f) * .6f : 0f);
            float turnDegrees = Quaternion.Angle(previousRootRotation, transform.rotation);
            previousRootRotation = transform.rotation;
            turnWeight = Mathf.MoveTowards(turnWeight,
                !offeringHand && turnDegrees > dt * 12f ? 1f : 0f, dt * 4f);
            if (turnWeight > .001f && leftThigh && leftCalf && leftFoot &&
                rightThigh && rightCalf && rightFoot)
            {
                turnPhase += dt * 8f;
                float leftLift = Mathf.Max(0f, Mathf.Sin(turnPhase)) * .055f * turnWeight;
                float rightLift = Mathf.Max(0f, -Mathf.Sin(turnPhase)) * .055f * turnWeight;
                ScavengerPose.Solve(leftThigh, leftCalf, leftFoot,
                    transform.TransformPoint(leftFootLocal + new Vector3(0f, leftLift, 0f)), transform.forward);
                ScavengerPose.Solve(rightThigh, rightCalf, rightFoot,
                    transform.TransformPoint(rightFootLocal + new Vector3(0f, rightLift, 0f)), transform.forward);
            }
            handshakeWeight = Mathf.MoveTowards(handshakeWeight, offeringHand ? 1f : 0f, blendSpeed * dt);

            waveWeight = Mathf.MoveTowards(waveWeight,
                !offeringHand && Time.time - waveStart < 1.1f ? .72f : 0f, dt * 2.8f);
            presentWeight = Mathf.MoveTowards(presentWeight,
                !offeringHand && Time.time - presentStart < 1.5f ? .65f : 0f, dt * 2.8f);
            foldedWeight = Mathf.MoveTowards(foldedWeight,
                !offeringHand && waveWeight < .03f && presentWeight < .03f ? 1f : 0f,
                dt * 3f);
            foreach (var finger in fingers)
                if (finger.bone)
                {
                    float open = finger.right && offeringHand ? 1.15f : .85f;
                    finger.bone.localRotation = finger.gripRotation *
                        Quaternion.AngleAxis(-finger.openingDegrees * open, finger.openingAxis);
                }

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
            // BanditRigMotion can skip its pose reset in authoring/preview states.
            // Applying a delta to the current bone then accumulates every frame.
            head.localRotation = restingHeadRotation * Quaternion.Euler(headPitch + nod, headYaw, 0f);

            // Daran's inherited bandit rest pose still grips an absent staff.
            // Cross both forearms at rest, then yield the right arm to gestures
            // and the transaction reach without touching the combat rig driver.
            if (foldedWeight > .001f)
            {
                if (leftUpper && leftLower && leftHand)
                    SolveBlended(leftUpper, leftLower, leftHand,
                        transform.TransformPoint(new Vector3(.10f, 1.27f, .16f)),
                        -transform.right, foldedWeight);
                if (rightUpper && rightLower && rightHand)
                    SolveBlended(rightUpper, rightLower, rightHand,
                        transform.TransformPoint(new Vector3(-.10f, 1.24f, .17f)),
                        transform.right, foldedWeight);
            }
            if (!rightUpper || !rightLower || !rightHand) return;
            float rightWeight = Mathf.Max(handshakeWeight, waveWeight, presentWeight);
            if (rightWeight <= .001f) return;
            Vector3 desired;
            if (handshakeWeight > .05f)
                desired = transform.TransformPoint(new Vector3(handTargetLocal.x,
                    handTargetLocal.y, Mathf.Max(.95f, handTargetLocal.z)));
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
