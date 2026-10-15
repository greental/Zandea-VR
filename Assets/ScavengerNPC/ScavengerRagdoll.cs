using System.Collections;
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
        [Min(.1f)] public float knockdownSeconds = 2f;
        [Min(0f)] public float knockdownImpulse = 5f;
        public bool Active { get; private set; }
        Coroutine recovery;
        NavMeshAgent agent;
        bool restoreAgent;
        bool restorePose;
        bool restoreCapsule;
        Transform hips;
        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (animator && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var body in bones)
                if (body) { body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None; }
            // Keep limb hitboxes enabled for player hits, but exclude intra-ragdoll contacts.
            for (int i = 0; i < boneColliders.Length; i++)
                for (int j = i + 1; j < boneColliders.Length; j++)
                    if (boneColliders[i] && boneColliders[j]) Physics.IgnoreCollision(boneColliders[i], boneColliders[j]);
        }
        public void Activate(Vector3 hitPoint, Vector3 impulse)
        {
            if (recovery != null) { StopCoroutine(recovery); recovery = null; }
            if (Active) { ApplyImpulse(hitPoint, impulse); return; }
            Active = true;
            restoreAgent = agent && agent.enabled;
            restorePose = pose && pose.enabled;
            restoreCapsule = capsule && capsule.enabled;
            if (restoreAgent) agent.enabled = false;
            if (animator) animator.enabled = false;
            if (pose)
            {
                pose.enabled = false;
            }
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

        public void KnockDown(Vector3 hitPoint, Vector3 direction)
        {
            if (Active) return;
            Vector3 away = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (away.sqrMagnitude < .01f) away = transform.forward;
            Activate(hitPoint, (away + Vector3.up * .35f).normalized * knockdownImpulse);
            recovery = StartCoroutine(RecoverAfterKnockdown());
        }

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

        IEnumerator RecoverAfterKnockdown()
        {
            yield return new WaitForSeconds(knockdownSeconds);
            Vector3 landing = hips ? hips.position : transform.position;
            Vector3 rootPosition = transform.position;
            if (NavMesh.SamplePosition(landing, out var hit, 2f, NavMesh.AllAreas))
                rootPosition = hit.position;
            foreach (var body in bones)
            {
                if (!body) continue;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
            transform.position = rootPosition;
            if (animator) { animator.enabled = true; animator.Rebind(); animator.Update(0f); }
            if (pose) pose.enabled = restorePose;
            if (capsule) capsule.enabled = restoreCapsule;
            if (agent && restoreAgent)
            {
                agent.enabled = true;
                if (agent.isOnNavMesh) agent.Warp(rootPosition);
            }
            Active = false;
            recovery = null;
        }
    }
}
