using System;
using UnityEngine;

namespace Zandea.Props
{
    [CreateAssetMenu(menuName = "Zandea/Props/Remaining prop catalog")]
    public sealed class RemainingPropCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string category;
            public GameObject prefab;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public GameObject Find(string id)
        {
            foreach (var entry in entries)
                if (entry != null && entry.id == id) return entry.prefab;
            return null;
        }
    }
}
