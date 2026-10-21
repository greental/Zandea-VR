using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.Props
{
    [RequireComponent(typeof(HingeJoint))]
    public sealed class PropHingedLid : MonoBehaviour
    {
        public PropContainer container;
        [Range(5, 85)] public float openAngle = 32f;
        HingeJoint hinge;
        Mesh runtimeMesh;
        bool lastOpen;
        void Awake()
        {
            hinge = GetComponent<HingeJoint>();
            var body = GetComponent<Rigidbody>();
            var grab = GetComponent<XRGrabInteractable>();
            if (!container || !body || !grab) return;
            var baseBody = container.GetComponent<Rigidbody>();
            if (!baseBody) return;
            hinge.connectedBody = null;
            body.isKinematic = true;
            var filter = GetComponent<MeshFilter>();
            // The imported lid uses a 100x transform around a centimetre mesh.
            // PhysX joints drift badly on a Rigidbody at that scale. Bake only
            // this runtime instance to unit scale before configuring the hinge.
            if (filter && filter.sharedMesh && (transform.localScale - Vector3.one).sqrMagnitude > .0001f)
            {
                var scale = transform.localScale;
                runtimeMesh = Instantiate(filter.sharedMesh);
                runtimeMesh.name = filter.sharedMesh.name + " (physical lid)";
                var vertices = runtimeMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], scale);
                runtimeMesh.vertices = vertices;
                runtimeMesh.RecalculateBounds();
                filter.sharedMesh = runtimeMesh;
                transform.localScale = Vector3.one;
            }
            if (transform.parent != container.transform)
                transform.SetParent(container.transform, true);
            GroundLootCollider.Ensure(gameObject);
            // The source FBX places the lid mesh vertically below the box. Pose it
            // flat on the rim first, then anchor its rear edge to the chest body.
            if (filter && filter.sharedMesh)
            {
                var meshBounds = filter.sharedMesh.bounds;
                var baseShapes = container.GetComponents<BoxCollider>();
                if (baseShapes.Length > 0)
                {
                    var boxBounds = new Bounds(baseShapes[0].center, baseShapes[0].size);
                    for (int i = 1; i < baseShapes.Length; i++)
                    {
                        boxBounds.Encapsulate(baseShapes[i].center - baseShapes[i].size * .5f);
                        boxBounds.Encapsulate(baseShapes[i].center + baseShapes[i].size * .5f);
                    }
                    var closedRotation = Quaternion.Euler(90f, 0f, 0f);
                    var closedCenter = new Vector3(boxBounds.center.x,
                        boxBounds.max.y + meshBounds.size.z * .5f, boxBounds.center.z);
                    var origin = closedCenter - closedRotation * meshBounds.center;
                    transform.SetPositionAndRotation(container.transform.TransformPoint(origin),
                        container.transform.rotation * closedRotation);
                    var pivot = container.transform.TransformPoint(new Vector3(
                        closedCenter.x, closedCenter.y, closedCenter.z - meshBounds.size.y * .5f));
                    hinge.anchor = transform.InverseTransformPoint(pivot);
                    hinge.connectedAnchor = baseBody.transform.InverseTransformPoint(pivot);
                    hinge.axis = transform.InverseTransformDirection(-container.transform.right);
                    hinge.autoConfigureConnectedAnchor = false;
                }
            }
            hinge.connectedBody = baseBody;
            hinge.useLimits = true;
            hinge.limits = new JointLimits { min = -3f, max = 105f };
            hinge.useSpring = true;
            hinge.spring = new JointSpring { spring = 18f, damper = 2f, targetPosition = 0f };
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;
            var collisionGuard = GetComponent<HeldLootCollisionGuard>();
            if (!collisionGuard) collisionGuard = gameObject.AddComponent<HeldLootCollisionGuard>();
            collisionGuard.Bind(grab);
            body.position = transform.position;
            body.rotation = transform.rotation;
            body.isKinematic = false;
        }
        void Update()
        {
            if (!container || !hinge) return;
            bool open = Mathf.Abs(hinge.angle) >= openAngle;
            if (open == lastOpen) return;
            lastOpen = open;
            container.SetOpen(open);
        }
        void OnDestroy() { if (runtimeMesh) Destroy(runtimeMesh); }
    }
}
