using UnityEngine;
using UnityEngine.AI;

namespace Zandea.POI
{
#pragma warning disable 0618 // Legacy link required by the installed navigation runtime.
    // The imported four-step collision rises 1.25 m at each riser, too high for
    // the player capsule or NavMesh agent. A continuous ramp follows its treads.
    [DisallowMultipleComponent]
    public sealed class OutpostStairRamp : MonoBehaviour
    {
        Mesh rampMesh;
        OffMeshLink navigationJoin;

        void Awake()
        {
            foreach (Transform child in transform)
                if (child.name.StartsWith("Step "))
                    foreach (var box in child.GetComponents<BoxCollider>()) box.enabled = false;

            float halfWidth = 5.1174316f * .5f;
            rampMesh = new Mesh { name = "Adobe stairs walkable ramp" };
            rampMesh.vertices = new[]
            {
                new Vector3(-halfWidth, 1f, 5f), new Vector3(halfWidth, 1f, 5f),
                new Vector3(halfWidth, 4.85f, -4f), new Vector3(-halfWidth, 4.85f, -4f),
                new Vector3(-halfWidth, -.3f, 5f), new Vector3(halfWidth, -.3f, 5f),
                new Vector3(halfWidth, -.3f, -4f), new Vector3(-halfWidth, -.3f, -4f),
                // Level approach overlaps the surrounding terrain NavMesh after
                // agent-radius erosion without adding a visible step at the foot.
                new Vector3(-halfWidth, .84f, 6.5f), new Vector3(halfWidth, .84f, 6.5f),
                new Vector3(-halfWidth, .54f, 6.5f), new Vector3(halfWidth, .54f, 6.5f)
            };
            rampMesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, // walkable slope
                4, 5, 6, 4, 6, 7, // underside
                8, 1, 9, 8, 0, 1, // level approach
                10, 11, 5, 10, 5, 4, // approach underside
                8, 9, 11, 8, 11, 10, // foot
                3, 7, 6, 3, 6, 2, // landing
                0, 4, 7, 0, 7, 3, // left side
                1, 2, 6, 1, 6, 5, // right side
                8, 10, 4, 8, 4, 0, // approach left side
                9, 1, 5, 9, 5, 11 // approach right side
            };
            rampMesh.RecalculateNormals();
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = rampMesh;
        }

        public void ConnectNavigation()
        {
            // Terrain and the imported staircase are separate physics sources.
            // Recast leaves two adjacent walkable islands at their shallow seam,
            // so join those exact sampled surfaces after the outpost bake.
            var groundProbe = transform.TransformPoint(new Vector3(0f, .84f, 6.5f));
            var rampProbe = transform.TransformPoint(new Vector3(0f, 2.925f, .5f));
            if (!NavMesh.SamplePosition(groundProbe, out var ground, 2f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(rampProbe, out var ramp, 2f, NavMesh.AllAreas))
                return;
            if (!navigationJoin)
            {
                navigationJoin = gameObject.AddComponent<OffMeshLink>();
                navigationJoin.startTransform = new GameObject("Stair ground nav join").transform;
                navigationJoin.endTransform = new GameObject("Stair ramp nav join").transform;
                navigationJoin.startTransform.SetParent(transform, true);
                navigationJoin.endTransform.SetParent(transform, true);
                navigationJoin.biDirectional = true;
                navigationJoin.autoUpdatePositions = true;
            }
            navigationJoin.startTransform.position = ground.position;
            navigationJoin.endTransform.position = ramp.position;
            navigationJoin.activated = true;
            navigationJoin.UpdatePositions();
        }

        void OnDestroy() { if (rampMesh) Destroy(rampMesh); }
    }
#pragma warning restore 0618
}
