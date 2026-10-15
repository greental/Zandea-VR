using UnityEngine;

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
        public bool Active { get; private set; }
        void Awake()
        {
            foreach (var body in bones)
                if (body) { body.isKinematic = true; body.interpolation = RigidbodyInterpolation.None; }
            // Keep limb hitboxes enabled for player hits, but exclude intra-ragdoll contacts.
            for (int i = 0; i < boneColliders.Length; i++)
                for (int j = i + 1; j < boneColliders.Length; j++)
                    if (boneColliders[i] && boneColliders[j]) Physics.IgnoreCollision(boneColliders[i], boneColliders[j]);
        }
        public void Activate(Vector3 hitPoint, Vector3 impulse)
        {
            if (Active) return;
            Active = true;
            if (animator) animator.enabled = false;
            if (pose)
            {
                pose.enabled = false;
            }
            if (capsule) capsule.enabled = false;
            Physics.SyncTransforms();
            Rigidbody nearest = null; float distance = float.PositiveInfinity;
            foreach (var body in bones)
            {
                if (!body) continue;
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.maxDepenetrationVelocity = maxDepenetrationSpeed;
                body.linearDamping = deathLinearDamping;
                body.angularDamping = deathAngularDamping;
                float d = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (d < distance) { distance = d; nearest = body; }
            }
            if (nearest) nearest.AddForceAtPosition(Vector3.ClampMagnitude(impulse, 8), hitPoint, ForceMode.Impulse);
        }
    }
}
