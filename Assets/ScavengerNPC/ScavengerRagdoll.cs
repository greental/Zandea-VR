using UnityEngine;
using UnityEngine.AI;

namespace ScavengerNPC
{
    public sealed class ScavengerRagdoll : MonoBehaviour
    {
        public Animator animator;
        public ScavengerPose pose;
        public Collider capsule;
        public Rigidbody[] bones;
        public Collider[] boneColliders;
        [Min(.1f)] public float maxDepenetrationSpeed = 2.5f;
        [Min(0f)] public float deathLinearDamping = .15f;
        [Min(0f)] public float deathAngularDamping = .3f;
        [Min(0f)] public float knockdownImpulse = 5f;
        [Min(.1f)] public float maximumNavMeshRecoveryDistance = .8f;
        [Min(.1f)] public float getUpBlendSeconds = .65f;
        public string faceUpGetUpState;
        public string faceDownGetUpState;
        public bool Active { get; private set; }

        NavMeshAgent agent;
        Transform hips;
        Vector3[] recoveryPositions;
        Quaternion[] recoveryRotations;
        float recoveryStart;
        bool recovering, recoveryComplete, restoreAgent, restorePose, restoreCapsule;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (animator && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            recoveryPositions = new Vector3[bones.Length];
            recoveryRotations = new Quaternion[bones.Length];
            foreach (var body in bones)
                if (body) { body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None; }
            for (int i = 0; i < boneColliders.Length; i++)
                for (int j = i + 1; j < boneColliders.Length; j++)
                    if (boneColliders[i] && boneColliders[j]) Physics.IgnoreCollision(boneColliders[i], boneColliders[j]);
        }

        public void Activate(Vector3 hitPoint, Vector3 impulse)
        {
            if (Active)
            {
                if (recovering)
                {
                    recovering = recoveryComplete = false;
                    if (animator) animator.enabled = false;
                    if (pose) pose.enabled = false;
                    if (capsule) capsule.enabled = false;
                    foreach (var body in bones)
                        if (body) { body.isKinematic = false; body.interpolation = RigidbodyInterpolation.Interpolate; }
                }
                ApplyImpulse(hitPoint, impulse);
                return;
            }
            Active = true;
            recoveryComplete = false;
            restoreAgent = agent && agent.enabled;
            restorePose = pose && pose.enabled;
            restoreCapsule = capsule && capsule.enabled;
            if (restoreAgent) agent.enabled = false;
            if (animator) animator.enabled = false;
            if (pose) pose.enabled = false;
            if (capsule) capsule.enabled = false;
            Physics.SyncTransforms();
            foreach (var body in bones)
            {
                if (!body) continue;
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.maxDepenetrationVelocity = maxDepenetrationSpeed;
                body.linearDamping = deathLinearDamping;
                body.angularDamping = deathAngularDamping;
            }
            ApplyImpulse(hitPoint, impulse);
        }

        public void ApplyImpact(Vector3 hitPoint, Vector3 impulse)
        { if (Active && !recovering) ApplyImpulse(hitPoint, impulse); }

        void ApplyImpulse(Vector3 hitPoint, Vector3 impulse)
        {
            Rigidbody nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var body in bones)
            {
                if (!body) continue;
                float d = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (d < distance) { distance = d; nearest = body; }
            }
            if (nearest) nearest.AddForceAtPosition(Vector3.ClampMagnitude(impulse, 8f), hitPoint, ForceMode.Impulse);
        }

        public bool Recover()
        {
            if (!Active) return false;
            if (recoveryComplete)
            {
                recovering = recoveryComplete = false;
                if (pose) pose.enabled = restorePose;
                if (capsule) capsule.enabled = restoreCapsule;
                if (agent && restoreAgent)
                {
                    agent.enabled = true;
                    if (agent.isOnNavMesh) agent.Warp(transform.position);
                }
                Active = false;
                return true;
            }
            if (recovering) return false;
            Vector3 landing = hips ? hips.position : transform.position;
            if (!NavMesh.SamplePosition(landing, out var hit, maximumNavMeshRecoveryDistance, NavMesh.AllAreas)) return false;
            bool faceUp = hips && Vector3.Dot(hips.forward, Vector3.up) > 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                var body = bones[i];
                if (!body) continue;
                recoveryPositions[i] = body.position;
                recoveryRotations[i] = body.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
            transform.position = hit.position;
            if (animator)
            {
                animator.enabled = true;
                animator.Rebind();
                string state = faceUp ? faceUpGetUpState : faceDownGetUpState;
                if (!string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state)))
                    animator.Play(state, 0, 0f);
                animator.Update(0f);
            }
            recoveryStart = Time.time;
            recovering = true;
            return false;
        }

        void LateUpdate()
        {
            if (!recovering || recoveryComplete) return;
            float progress = Mathf.Clamp01((Time.time - recoveryStart) / getUpBlendSeconds);
            for (int i = 0; i < bones.Length; i++)
            {
                var body = bones[i];
                if (!body) continue;
                var bone = body.transform;
                bone.SetPositionAndRotation(Vector3.Lerp(recoveryPositions[i], bone.position, progress),
                    Quaternion.Slerp(recoveryRotations[i], bone.rotation, progress));
            }
            if (progress >= 1f) recoveryComplete = true;
        }
    }
}
