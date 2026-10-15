using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public sealed class NpcWorldItem : MonoBehaviour
    {
        readonly List<NpcItemInstance> items = new();
        public NpcItemInstance Item => items.Count > 0 ? items[0] : null;
        public IReadOnlyList<NpcItemInstance> Items => items;
        public static event Action<NpcItemInstance, Vector3> WorldObjectDestroyed;
        public void Bind(NpcItemInstance item)
        {
            items.Clear();
            if (item != null) items.Add(item);
        }
        public void BindMany(IReadOnlyList<NpcItemInstance> group)
        {
            items.Clear();
            if (group != null) for (int i = 0; i < group.Count; i++) if (group[i] != null) items.Add(group[i]);
        }
        void OnDestroy()
        {
            foreach (var item in items)
                if (item != null && item.WorldObject == gameObject)
                {
                    WorldObjectDestroyed?.Invoke(item, transform.position);
                    item.SetLocation(null, NpcEquipmentSlot.None, null, null);
                }
        }
    }
}
