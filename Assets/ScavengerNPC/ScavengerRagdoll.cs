using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;
using KenshiVR.MeleeCombat;

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
        [Min(1f)] public float carriedMassMultiplier = 1.8f;
        [Min(0f)] public float carriedLinearDamping = .8f;
        [Min(0f)] public float carriedAngularDamping = 1.2f;
        [Min(0f)] public float knockdownImpulse = 5f;
        [Min(.1f)] public float maximumNavMeshRecoveryDistance = .8f;
        [Min(.1f)] public float getUpBlendSeconds = .65f;
        public string faceUpGetUpState;
        public string faceDownGetUpState;
        public bool Active { get; private set; }
        public XRGrabInteractable CorpseGrab { get; private set; }
        readonly List<XRGrabInteractable> corpsePartGrabs = new();

        NavMeshAgent agent;
        Transform hips;
        Vector3[] recoveryPositions;
        Quaternion[] recoveryRotations;
        float recoveryStart;
        bool recovering, recoveryComplete, restoreAgent, restorePose, restoreCapsule;
        bool carryPhysicsPrepared;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (animator && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            // Variant prefabs may serialize the old per-bone interactables. Awake
            // runs before their OnEnable registration, so retire them here too.
            foreach (var body in bones)
            {
                if (!body || body.transform == hips) continue;
                var oldGrab = body.GetComponent<XRGrabInteractable>();
                if (oldGrab) oldGrab.enabled = false;
            }
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
                EnableCorpseGrab();
                return;
            }
            Active = true;
            PrepareCarryPhysics();
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
                body.linearDamping = Mathf.Max(deathLinearDamping, carriedLinearDamping);
                body.angularDamping = Mathf.Max(deathAngularDamping, carriedAngularDamping);
            }
            ApplyImpulse(hitPoint, impulse);
            EnableCorpseGrab();
        }
        void PrepareCarryPhysics()
        {
            if(carryPhysicsPrepared)return;carryPhysicsPrepared=true;
            foreach(var part in bones)if(part)
            {part.mass*=carriedMassMultiplier;part.linearDamping=Mathf.Max(part.linearDamping,carriedLinearDamping);part.angularDamping=Mathf.Max(part.angularDamping,carriedAngularDamping);part.maxAngularVelocity=8f;}
            foreach(var joint in GetComponentsInChildren<CharacterJoint>(true))
            {joint.enableProjection=true;joint.projectionDistance=.03f;joint.projectionAngle=12f;}
            foreach(var joint in GetComponentsInChildren<ConfigurableJoint>(true))
            {joint.projectionMode=JointProjectionMode.PositionAndRotation;joint.projectionDistance=.03f;joint.projectionAngle=12f;}
        }

        void EnableCorpseGrab()
        {
            corpsePartGrabs.RemoveAll(value => !value);
            RefreshCorpseGrabColliders();
        }

        public void RefreshCorpseGrabColliders()
        {
            if (boneColliders == null) return;
            foreach (var grab in corpsePartGrabs) if (grab) { grab.colliders.Clear(); grab.enabled = false; }
            // One body interactable owns the connected ragdoll. Separate grabs on
            // ancestor bones claim the same child colliders in XR Interaction
            // Toolkit, leaving limbs registered to the wrong interactable.
            Rigidbody anchor = hips ? hips.GetComponent<Rigidbody>() : null;
            if (!anchor)
                foreach (var body in bones) if (body) { anchor = body; break; }
            if (!anchor) return;
            // Existing bandit variants can already carry per-bone grab components.
            // Disable those as well, including ones this instance did not create.
            foreach (var body in bones)
            {
                if (!body || body == anchor) continue;
                var oldGrab = body.GetComponent<XRGrabInteractable>();
                if (!oldGrab) continue;
                oldGrab.enabled = false;
                oldGrab.colliders.Clear();
            }
            var bodyGrab = anchor.GetComponent<XRGrabInteractable>();
            if (!bodyGrab) bodyGrab = anchor.gameObject.AddComponent<XRGrabInteractable>();
            if (!corpsePartGrabs.Contains(bodyGrab)) corpsePartGrabs.Add(bodyGrab);
            bodyGrab.enabled = false;
            bodyGrab.colliders.Clear();
            foreach (var shape in boneColliders)
            {
                if (!shape || !shape.enabled || shape.isTrigger) continue;
                bodyGrab.colliders.Add(shape);
            }
            bodyGrab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            bodyGrab.useDynamicAttach = true;
            bodyGrab.selectMode = InteractableSelectMode.Multiple;
            bodyGrab.throwOnDetach = false;
            bodyGrab.attachEaseInTime = .12f;
            bodyGrab.smoothPosition = true;
            bodyGrab.smoothRotation = true;
            bodyGrab.smoothPositionAmount = 6f;
            bodyGrab.smoothRotationAmount = 6f;
            bodyGrab.enabled = bodyGrab.colliders.Count > 0;
            var guard = anchor.GetComponent<HeldLootCollisionGuard>();
            if (!guard) guard = anchor.gameObject.AddComponent<HeldLootCollisionGuard>();
            guard.Bind(bodyGrab);
            CorpseGrab = bodyGrab;
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
            if (corpsePartGrabs.Exists(value => value && value.isSelected)) return false;
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
            foreach (var grab in corpsePartGrabs) if (grab) grab.enabled = false;
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
