using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    public static class ShieldArcLocalizer
    {
        public static ShieldArc ResolveArc(Transform targetTransform, Vector3 hitPointWorld)
        {
            Vector3 local = targetTransform.InverseTransformPoint(hitPointWorld);
            float absX = Mathf.Abs(local.x);
            float absZ = Mathf.Abs(local.z);

            if (absZ >= absX)
            {
                return local.z >= 0f ? ShieldArc.Front : ShieldArc.Rear;
            }

            return local.x >= 0f ? ShieldArc.Right : ShieldArc.Left;
        }
    }
}
