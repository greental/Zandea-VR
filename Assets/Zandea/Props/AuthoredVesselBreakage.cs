using UnityEngine;

namespace Zandea.Props
{
    // Authored Main/Review layouts contain some direct FBX mesh instances rather
    // than instances of the reusable pot prefabs. Upgrade those in memory.
    public static class AuthoredVesselBreakage
    {
        public static int Apply(Transform layout)
        {
            var definition = Resources.Load<VesselBreakageDefinition>("ZandeaVesselBreakage");
            if (!definition || !layout) return 0;
            int count = 0;
            foreach (var filter in layout.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || filter.GetComponentInParent<BreakableProp>()) continue;
                GameObject debris = null;
                foreach (var entry in definition.authoredPuebloMeshes)
                    if (entry != null && entry.sourceMesh == filter.sharedMesh)
                    {
                        debris = entry.fracturedPrefab;
                        break;
                    }
                if (!debris) continue;
                var inheritedBody = filter.GetComponentInParent<Rigidbody>();
                var target = inheritedBody ? inheritedBody.gameObject : filter.gameObject;
                if (!target.GetComponentInChildren<Collider>())
                {
                    var box = filter.gameObject.AddComponent<BoxCollider>();
                    box.center = filter.sharedMesh.bounds.center;
                    box.size = filter.sharedMesh.bounds.size;
                }
                var body = target.GetComponent<Rigidbody>();
                if (!body)
                {
                    body = target.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                }
                var breakable = target.AddComponent<BreakableProp>();
                breakable.fracturedPrefab = debris;
                breakable.minimumImpactSpeed = 2.5f;
                breakable.collisionEnergyThreshold = 7f;
                breakable.meleeDamageThreshold = 4f;
                count++;
            }
            return count;
        }
    }
}
