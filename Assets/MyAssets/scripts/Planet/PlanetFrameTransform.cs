using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>
    /// A rigid frame conversion from one coordinate frame into another.
    /// Positions receive translation + rotation. Directions, velocities and angular velocities receive rotation only.
    /// </summary>
    [System.Serializable]
    public struct PlanetFrameTransform
    {
        public Vector3 fromOrigin;
        public Vector3 toOrigin;
        public Quaternion rotation;

        public PlanetFrameTransform(Vector3 fromOrigin, Vector3 toOrigin, Quaternion rotation)
        {
            this.fromOrigin = fromOrigin;
            this.toOrigin = toOrigin;
            this.rotation = Normalize(rotation);
        }

        public static PlanetFrameTransform Identity => new PlanetFrameTransform(Vector3.zero, Vector3.zero, Quaternion.identity);

        public Vector3 TransformPoint(Vector3 point)
        {
            return toOrigin + rotation * (point - fromOrigin);
        }

        public Vector3 TransformDirection(Vector3 direction)
        {
            return rotation * direction;
        }

        public Quaternion TransformRotation(Quaternion value)
        {
            return Normalize(rotation * value);
        }

        public PlanetFrameTransform Inverse()
        {
            Quaternion inverseRotation = Quaternion.Inverse(rotation);
            return new PlanetFrameTransform(toOrigin, fromOrigin, inverseRotation);
        }

        public static PlanetFrameTransform Compose(PlanetFrameTransform second, PlanetFrameTransform first)
        {
            // Returns a transform equivalent to second(first(point)).
            Quaternion composedRotation = Normalize(second.rotation * first.rotation);
            Vector3 composedFromOrigin = first.fromOrigin;
            Vector3 composedToOrigin = second.TransformPoint(first.toOrigin);
            return new PlanetFrameTransform(composedFromOrigin, composedToOrigin, composedRotation);
        }

        public bool IsFinite()
        {
            return IsFinite(fromOrigin) && IsFinite(toOrigin) && IsFinite(rotation);
        }

        public static Quaternion Normalize(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag <= 0.000001f || float.IsNaN(mag) || float.IsInfinity(mag))
                return Quaternion.identity;

            float inv = 1f / mag;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
