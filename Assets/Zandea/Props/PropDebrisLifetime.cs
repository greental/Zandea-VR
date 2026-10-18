using System.Collections.Generic;
using UnityEngine;

namespace Zandea.Props
{
    public sealed class PropDebrisLifetime : MonoBehaviour
    {
        static readonly Queue<PropDebrisLifetime> active = new();
        const int MaximumActiveBursts = 3;
        [Min(1)] public float lifetime = 12f;
        public void BeginRuntimeBurst()
        {
            active.Enqueue(this);
            while (active.Count > MaximumActiveBursts)
            {
                var oldest = active.Dequeue();
                if (oldest) Destroy(oldest.gameObject);
            }
            Destroy(gameObject, lifetime);
        }
    }
}
