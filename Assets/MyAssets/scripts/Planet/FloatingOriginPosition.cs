using System;
using UnityEngine;

namespace MyAssets.Planet
{
    /// <summary>Double precision translation; subtract before converting back to Unity floats.</summary>
    [Serializable]
    public struct FloatingOriginPosition
    {
        public double x, y, z;

        public FloatingOriginPosition(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public void Add(Vector3 delta)
        {
            x += delta.x;
            y += delta.y;
            z += delta.z;
        }

        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>
    /// Called after scene transforms move by -shift. Update cached world positions only;
    /// do not move transforms again. Inactive components receive this notification too.
    /// </summary>
    public interface IFloatingOriginListener
    {
        void OnOriginShift(Vector3 shift);
    }
}
