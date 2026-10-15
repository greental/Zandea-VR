using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyAssets.Planet
{
    /// <summary>
    /// Translation-only floating origin shared by all loaded gameplay scenes.
    /// Runs before other LateUpdates, after gameplay/space-surface updates, before XR rendering.
    /// The offset tracks translations, not the separate planet space/surface rotation mapping.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class FloatingOriginManager : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("Maximum displayed distance from the origin before the shared world is shifted. 10 km keeps approach travel uninterrupted while retaining millimetre-scale float precision.")]
        [SerializeField, Min(10f)] private float recenterDistance = 10000f;
        [SerializeField] private bool recenterOnStart = true;
        [Tooltip("Additional roots outside loaded scenes, for example a persistent XR rig. Descendants are deduplicated.")]
        [SerializeField] private Transform[] additionalRoots;
        [SerializeField] private FloatingOriginPosition accumulatedOffset;
        [SerializeField] private int shiftCount;

        private readonly List<GameObject> sceneRoots = new List<GameObject>();
        private readonly List<Transform> roots = new List<Transform>();
        private readonly List<BodyState> bodies = new List<BodyState>();
        private readonly List<MonoBehaviour> listeners = new List<MonoBehaviour>();
        private readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        private readonly List<TrailRenderer> trails = new List<TrailRenderer>();
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private ParticleSystem.Particle[] particleBuffer;
        private bool shifting;
        private Rigidbody targetBody;

        private Vector3 TargetPosition => targetBody != null ? targetBody.position : target.position;

        public Transform Target => target;
        public FloatingOriginPosition AccumulatedOffset => accumulatedOffset;
        public int ShiftCount => shiftCount;

        public FloatingOriginPosition ToAbsolutePosition(Vector3 displayedPosition) =>
            new FloatingOriginPosition(accumulatedOffset.x + displayedPosition.x,
                accumulatedOffset.y + displayedPosition.y, accumulatedOffset.z + displayedPosition.z);

        public Vector3 ToDisplayedPosition(FloatingOriginPosition absolutePosition) =>
            new Vector3((float)(absolutePosition.x - accumulatedOffset.x),
                (float)(absolutePosition.y - accumulatedOffset.y), (float)(absolutePosition.z - accumulatedOffset.z));

        public void Configure(Transform followTarget)
        {
            target = followTarget;
            targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
        }

        public static FloatingOriginManager EnsureFor(Transform followTarget)
        {
            if(followTarget==null || !followTarget.gameObject.scene.IsValid() || !followTarget.gameObject.scene.isLoaded) return null;
#if UNITY_EDITOR
            if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(followTarget.gameObject.scene)) return null;
#endif
            var manager = FindFirstObjectByType<FloatingOriginManager>();
            if (manager == null)
            {
                var owner = new GameObject("Floating Origin");
                SceneManager.MoveGameObjectToScene(owner, followTarget.gameObject.scene);
                manager = owner.AddComponent<FloatingOriginManager>();
            }
            manager.Configure(followTarget);
            return manager;
        }

        private void Start()
        {
            Configure(target);
            if (recenterOnStart)
                RecenterNow();
        }

        private void LateUpdate()
        {
            if (target != null && TargetPosition.sqrMagnitude > recenterDistance * recenterDistance)
                RecenterNow();
        }

        private void FixedUpdate() => RestoreInterpolation();
        private void OnDisable() => RestoreInterpolation();

        [ContextMenu("Recenter Now (Play Mode)")]
        public void RecenterNow()
        {
            if (Application.isPlaying && target != null)
                ShiftOrigin(TargetPosition);
        }

        public void ShiftOrigin(Vector3 shift)
        {
            if (!Application.isPlaying || shifting || shift.sqrMagnitude < 0.000001f ||
                !float.IsFinite(shift.x) || !float.IsFinite(shift.y) || !float.IsFinite(shift.z))
                return;

            shifting = true;
            try
            {
                RestoreInterpolation();
                CollectWorld();
                foreach (BodyState state in bodies)
                    state.body.interpolation = RigidbodyInterpolation.None;

                // Move every scene hierarchy exactly once through its root Transform.
                // This carries the ship's nested seat, joystick, and kinematic control bodies
                // with it while keeping their local links intact.
                foreach (Transform root in roots)
                    root.position -= shift;

                // Restore only independent Rigidbody roots from their captured physics poses.
                // In LateUpdate a Rigidbody Transform may be interpolated, so use the saved
                // physics pose. Nested bodies follow their parent Transform and must not be
                // repositioned independently or cockpit controls can detach from the ship.
                foreach (BodyState state in bodies)
                {
                    if (HasAncestorBody(state.body.transform)) continue;
                    state.body.position = state.position - shift;
                    state.body.rotation = state.rotation;
                    state.body.transform.SetPositionAndRotation(state.position - shift, state.rotation);
                    if (!state.body.isKinematic)
                    {
                        state.body.linearVelocity = state.velocity;
                        state.body.angularVelocity = state.angularVelocity;
                        if (state.sleeping) state.body.Sleep();
                    }
                }

                ShiftWorldSpaceEffects(shift);
                accumulatedOffset.Add(shift);
                shiftCount++;
                foreach (MonoBehaviour behaviour in listeners)
                {
                    if (behaviour is IFloatingOriginListener listener)
                        listener.OnOriginShift(shift);
                }
                Physics.SyncTransforms();
                // Restore interpolation at the next physics step so no frame blends across
                // the old and new origin. No force, rotation or velocity change is introduced.
            }
            finally
            {
                shifting = false;
            }
        }

        private void CollectWorld()
        {
            roots.Clear();
            listeners.Clear();
            particles.Clear();
            trails.Clear();
            lines.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                sceneRoots.Clear();
                scene.GetRootGameObjects(sceneRoots);
                foreach (GameObject root in sceneRoots)
                    AddRoot(root.transform);
            }
            if (target != null) AddRoot(target.root);
            foreach (var xrOrigin in FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AddRoot(xrOrigin.transform.root);
            if (additionalRoots != null)
                foreach (Transform root in additionalRoots)
                    if (root != null) AddRoot(root.root);

            foreach (Transform root in roots)
            {
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                    bodies.Add(new BodyState(body));
                listeners.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
                particles.AddRange(root.GetComponentsInChildren<ParticleSystem>(true));
                trails.AddRange(root.GetComponentsInChildren<TrailRenderer>(true));
                lines.AddRange(root.GetComponentsInChildren<LineRenderer>(true));
            }
        }

        private void AddRoot(Transform root)
        {
            if (!roots.Contains(root)) roots.Add(root);
        }

        private static bool HasAncestorBody(Transform transform)
        {
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                if (parent.GetComponent<Rigidbody>() != null) return true;
            return false;
        }

        private void RestoreInterpolation()
        {
            foreach (BodyState state in bodies)
                if (state.body != null) state.body.interpolation = state.interpolation;
            bodies.Clear();
        }

        private void ShiftWorldSpaceEffects(Vector3 shift)
        {
            foreach (ParticleSystem system in particles)
            {
                if (system.main.simulationSpace != ParticleSystemSimulationSpace.World) continue;
                int capacity = system.particleCount;
                if (capacity == 0) continue;
                if (particleBuffer == null || particleBuffer.Length < capacity)
                    particleBuffer = new ParticleSystem.Particle[capacity];
                int count = system.GetParticles(particleBuffer);
                for (int i = 0; i < count; i++) particleBuffer[i].position -= shift;
                system.SetParticles(particleBuffer, count);
            }
            foreach (TrailRenderer trail in trails)
            {
                var positions = new Vector3[trail.positionCount];
                int count = trail.GetPositions(positions);
                for (int i = 0; i < count; i++) positions[i] -= shift;
                trail.SetPositions(positions);
            }
            foreach (LineRenderer line in lines)
            {
                if (!line.useWorldSpace) continue;
                var positions = new Vector3[line.positionCount];
                line.GetPositions(positions);
                for (int i = 0; i < positions.Length; i++) positions[i] -= shift;
                line.SetPositions(positions);
            }
        }

        private readonly struct BodyState
        {
            public readonly Rigidbody body;
            public readonly Vector3 position, velocity, angularVelocity;
            public readonly Quaternion rotation;
            public readonly RigidbodyInterpolation interpolation;
            public readonly bool sleeping;

            public BodyState(Rigidbody body)
            {
                this.body = body;
                position = body.position;
                rotation = body.rotation;
                velocity = body.linearVelocity;
                angularVelocity = body.angularVelocity;
                interpolation = body.interpolation;
                sleeping = body.IsSleeping();
            }
        }
    }
}
