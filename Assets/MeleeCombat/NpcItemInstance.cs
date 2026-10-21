using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    [Serializable]
    public sealed class NpcItemInstance
    {
        [SerializeField] string instanceId;
        [SerializeField] NpcItemDefinition definition;
        [SerializeField, Range(0, 1)] float condition = 1;
        [SerializeField] NpcEquipmentSlot equippedSlot;
        [SerializeField] string containerId;
        [SerializeField] List<NpcItemInstance> contents = new();
        [NonSerialized] NpcInventory owner;
        [NonSerialized] GameObject worldObject;

        public string InstanceId => instanceId;
        public NpcItemDefinition Definition => definition;
        internal void SetDefinition(NpcItemDefinition value) => definition = value;
        public float Condition { get => condition; set => condition = Mathf.Clamp01(value); }
        public NpcEquipmentSlot EquippedSlot => equippedSlot;
        public string ContainerId => containerId;
        public IReadOnlyList<NpcItemInstance> Contents => contents;
        public NpcInventory Owner => owner;
        public GameObject WorldObject => worldObject;

        public NpcItemInstance(NpcItemDefinition item)
        {
            instanceId = Guid.NewGuid().ToString("N");
            definition = item;
        }
        // POI-created items keep the same identity when their physical presentation streams.
        public NpcItemInstance(NpcItemDefinition item, string stableId)
        {
            instanceId = string.IsNullOrEmpty(stableId) ? Guid.NewGuid().ToString("N") : stableId;
            definition = item;
        }
        internal void SetLocation(NpcInventory inventory, NpcEquipmentSlot slot, string container, GameObject physical)
        { owner = inventory; equippedSlot = slot; containerId = container; worldObject = physical; }
        internal bool AddContained(NpcItemInstance item)
        {
            if (item == null || item == this || item.contents.Count != 0 || contents.Contains(item)) return false;
            contents.Add(item); return true;
        }
        internal bool RemoveContained(NpcItemInstance item) => contents.Remove(item);
    }
}
