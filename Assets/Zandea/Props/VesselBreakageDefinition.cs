using System;
using UnityEngine;

namespace Zandea.Props
{
    public sealed class VesselBreakageDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public Mesh sourceMesh;
            public GameObject fracturedPrefab;
        }

        public Entry[] authoredPuebloMeshes = Array.Empty<Entry>();
    }
}
