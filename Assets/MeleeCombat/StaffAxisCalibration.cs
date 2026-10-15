using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Measures the rendered staff in Rigidbody-root space. The imported mesh is
    // offset from that root, so root.forward and the root origin are insufficient.
    public static class StaffAxisCalibration
    {
        public static bool TryMeasure(Transform weaponRoot, out Vector3 positive, out Vector3 negative)
        {
            positive = negative = Vector3.zero;
            if (!weaponRoot) return false;
            var filters = weaponRoot.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter longest = null;
            Vector3 axis = Vector3.forward;
            float longestLength = 0f;
            foreach (var filter in filters)
            {
                if (!filter || !filter.sharedMesh) continue;
                Vector3 size = filter.sharedMesh.bounds.size;
                for (int dimension = 0; dimension < 3; dimension++)
                {
                    Vector3 localAxis = dimension == 0 ? Vector3.right : dimension == 1 ? Vector3.up : Vector3.forward;
                    Vector3 span = weaponRoot.InverseTransformVector(filter.transform.TransformVector(
                        localAxis * size[dimension]));
                    if (span.magnitude <= longestLength) continue;
                    longestLength = span.magnitude;
                    axis = span.normalized;
                    longest = filter;
                }
            }
            if (!longest || longestLength < .3f) return false;
            // Keep the positive end consistent with the root's original forward side.
            if (Vector3.Dot(axis, Vector3.forward) < 0f) axis = -axis;
            Vector3 center = weaponRoot.InverseTransformPoint(
                longest.transform.TransformPoint(longest.sharedMesh.bounds.center));
            Vector3 radialCenter = center - axis * Vector3.Dot(center, axis);
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (var filter in filters)
            {
                if (!filter || !filter.sharedMesh) continue;
                Bounds bounds = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    Vector3 rootLocal = weaponRoot.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    float projection = Vector3.Dot(rootLocal, axis);
                    minimum = Mathf.Min(minimum, projection);
                    maximum = Mathf.Max(maximum, projection);
                }
            }
            if (maximum - minimum < .3f) return false;
            positive = radialCenter + axis * maximum;
            negative = radialCenter + axis * minimum;
            return true;
        }

        public static Quaternion CanonicalBasis(Vector3 positive, Vector3 negative)
        {
            Vector3 forward = (positive - negative).normalized;
            Vector3 midpoint = (positive + negative) * .5f;
            Vector3 towardGrip = Vector3.ProjectOnPlane(-midpoint, forward);
            if (towardGrip.sqrMagnitude < .0001f)
                towardGrip = Vector3.ProjectOnPlane(Vector3.up, forward);
            return Quaternion.LookRotation(forward, towardGrip.normalized);
        }
    }
}
