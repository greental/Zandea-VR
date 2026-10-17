using UnityEngine;

namespace KenshiVR.Bandaging
{
    public enum WrapRegion { LeftForearm, RightForearm, LeftUpperArm, RightUpperArm, LeftThigh, RightThigh, LeftCalf, RightCalf, Head, Chest }

    // Cylinder approximation lives in the bone's reference frame. Axis is local +Y.
    public sealed class WoundWrapZone : MonoBehaviour
    {
        public WrapRegion region;
        public Transform bone;
        public Vector3 localCenter;
        public Quaternion localFrame = Quaternion.identity;
        [Min(.02f)] public float radius = .055f;
        [Min(.02f)] public float length = .18f;
        [Range(-.5f, .5f)] public float taper;
        [Min(.02f)] public float snapDistance = .11f;
        [Min(0f)] public float surfaceOffset = .004f;
        [Range(.01f, .5f)] public float radialTolerance = .12f;
        [Range(.01f, .5f)] public float axialTolerance = .08f;
        public bool physicalWrapping = true;

        public Vector3 ToFrame(Vector3 world) => Quaternion.Inverse(bone.rotation * localFrame) * (world - bone.TransformPoint(localCenter));
        public Vector3 ToWorld(Vector3 point) => bone.TransformPoint(localCenter) + bone.rotation * localFrame * point;
        public float RadiusAt(float axial) => Mathf.Max(.02f, radius * (1f + taper * axial / Mathf.Max(length, .02f)));
        public bool Contains(Vector3 world, float padding = 0f)
        {
            if (!bone) return false;
            var p = ToFrame(world);
            float radial = new Vector2(p.x, p.z).magnitude;
            return Mathf.Abs(p.y) <= length * .5f + padding &&
                   Mathf.Abs(radial - RadiusAt(p.y)) <= radialTolerance + padding;
        }
        public Vector3 Surface(Vector3 world)
        {
            var p = ToFrame(world);
            var radial = new Vector2(p.x, p.z);
            if (radial.sqrMagnitude < .0001f) radial = Vector2.right;
            radial.Normalize();
            p.y = Mathf.Clamp(p.y, -length * .5f, length * .5f);
            float r = RadiusAt(p.y) + surfaceOffset;
            return ToWorld(new Vector3(radial.x * r, p.y, radial.y * r));
        }
        void OnDrawGizmosSelected()
        {
            if (!bone) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(ToWorld(Vector3.down * length * .5f), ToWorld(Vector3.up * length * .5f));
            Gizmos.DrawWireSphere(ToWorld(Vector3.zero), radius);
        }
    }
}
