using System;
using System.Collections.Generic;
using System.Linq;
using KenshiVR.CharacterCreator;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    public sealed class NpcInventory : MonoBehaviour
    {
        public NpcLootTable lootTable;
        public int seed;
        public bool logChanges;
        [SerializeField] bool generated;
        [SerializeField] List<NpcItemInstance> items = new();
        public IReadOnlyList<NpcItemInstance> Items => items;
        public event Action<NpcItemInstance> Changed;

        void Awake()
        {
            foreach (var item in items) Restore(item);
            if (generated) return;
            if (seed == 0) seed = Guid.NewGuid().GetHashCode();
            Generate();
        }
        void Start() => SyncClothing();
        void OnEnable() { if (generated) SyncClothing(); }
        void SyncClothing()
        {
            if (!lootTable && items.Count == 0) return;
            var cosmetics = GetComponent<CharacterCosmeticsController>();
            if (!cosmetics) return;
            var torso = Equipped(NpcEquipmentSlot.Torso)?.Definition?.equippedAppearance;
            var legs = Equipped(NpcEquipmentSlot.Legs)?.Definition?.equippedAppearance;
            var head = Equipped(NpcEquipmentSlot.Headwear)?.Definition?.equippedAppearance;
            cosmetics.torso = torso;
            cosmetics.legs = legs;
            cosmetics.headwear = head;
            cosmetics.Refresh();
        }
        void Restore(NpcItemInstance item)
        {
            item.SetLocation(this, item.EquippedSlot, item.ContainerId, null);
            foreach (var child in item.Contents) Restore(child);
        }
        void AssignTree(NpcItemInstance item, NpcInventory destination)
        {
            item.SetLocation(destination, item.EquippedSlot, item.ContainerId, item.WorldObject);
            foreach (var child in item.Contents) AssignTree(child, destination);
        }
        void Generate()
        {
            generated = true;
            if (!lootTable) return;
            var random = new System.Random(seed);
            foreach (var entry in lootTable.guaranteed) Roll(entry, random, true);
            foreach (var entry in lootTable.rolls) Roll(entry, random, false);
            for (int i = 0; i < lootTable.weightedPicks; i++)
            {
                float total = 0;
                foreach (var entry in lootTable.weighted) if (entry != null && entry.item) total += Mathf.Max(0, entry.weight);
                double roll = random.NextDouble() * total;
                foreach (var entry in lootTable.weighted)
                {
                    if (entry == null || !entry.item) continue;
                    roll -= Mathf.Max(0, entry.weight);
                    if (roll <= 0) { Roll(entry, random, true); break; }
                }
            }
        }
        void Roll(NpcLootTable.Entry entry, System.Random random, bool guaranteed)
        {
            if (entry == null || !entry.item || (!guaranteed && random.NextDouble() > entry.probability)) return;
            int count = random.Next(Mathf.Max(0, entry.minimum), Mathf.Max(entry.minimum, entry.maximum) + 1);
            for (int i = 0; i < count; i++)
            {
                var item = new NpcItemInstance(entry.item);
                if (!Add(item)) continue;
                if (entry.pocketOf != NpcEquipmentSlot.None)
                {
                    var container = Equipped(entry.pocketOf);
                    if (container != null) PutInside(item, container);
                }
                else if (entry.slot != NpcEquipmentSlot.None) Equip(item, entry.slot);
            }
        }
        public NpcItemInstance Equipped(NpcEquipmentSlot slot)
        {
            foreach (var item in items) if (item.EquippedSlot == slot) return item;
            return null;
        }
        public bool Add(NpcItemInstance item)
        {
            if (item == null || !item.Definition || item.Owner != null || item.WorldObject || items.Contains(item)) return false;
            items.Add(item); item.SetLocation(this, NpcEquipmentSlot.None, null, null);
            foreach (var child in item.Contents) AssignTree(child, this);
            Notify(item); return true;
        }
        public bool Remove(NpcItemInstance item)
        {
            if (item == null || item.Owner != this) return false;
            if (item.ContainerId != null)
            {
                var parent = Find(item.ContainerId);
                if (parent == null || !parent.RemoveContained(item)) return false;
            }
            else if (!items.Remove(item)) return false;
            item.SetLocation(null, NpcEquipmentSlot.None, null, null);
            foreach (var child in item.Contents) AssignTree(child, null);
            Notify(item); return true;
        }
        public bool Equip(NpcItemInstance item, NpcEquipmentSlot slot)
        {
            if (item == null || item.Owner != this || item.ContainerId != null || slot == NpcEquipmentSlot.None ||
                Equipped(slot) != null || !Compatible(item.Definition.category, slot)) return false;
            item.SetLocation(this, slot, null, null); Notify(item); return true;
        }
        public bool Unequip(NpcItemInstance item)
        {
            if (item == null || item.Owner != this || item.EquippedSlot == NpcEquipmentSlot.None) return false;
            item.SetLocation(this, NpcEquipmentSlot.None, null, null); Notify(item); return true;
        }
        public bool PutInside(NpcItemInstance item, NpcItemInstance container)
        {
            if (item == null || container == null || item.Owner != this || container.Owner != this ||
                item.ContainerId != null || item.EquippedSlot != NpcEquipmentSlot.None ||
                container.ContainerId != null || container.Definition.category != NpcItemCategory.Clothing ||
                !items.Contains(item) || !container.AddContained(item)) return false;
            items.Remove(item); item.SetLocation(this, NpcEquipmentSlot.None, container.InstanceId, null); Notify(item); return true;
        }
        public bool TransferTo(NpcItemInstance item, NpcInventory destination)
        {
            if (!destination || destination == this || item == null || item.Owner != this ||
                !item.Definition || destination.items.Contains(item)) return false;
            if (item.ContainerId != null)
            {
                var parent = Find(item.ContainerId);
                if (parent == null || !parent.RemoveContained(item)) return false;
            }
            else if (!items.Remove(item)) return false;
            destination.items.Add(item);
            item.SetLocation(destination, NpcEquipmentSlot.None, null, null);
            foreach (var child in item.Contents) AssignTree(child, destination);
            Notify(item); destination.Notify(item); return true;
        }
        public bool Drop(NpcItemInstance item, Vector3 position, Quaternion rotation, GameObject existing = null)
        {
            if (item == null || item.Owner != this || !existing && !item.Definition.worldPrefab) return false;
            if (existing && existing.TryGetComponent<NpcWorldItem>(out var attached) && attached.Item != null && attached.Item != item) return false;
            var physical = existing ? existing : Instantiate(item.Definition.worldPrefab, position, rotation);
            if (!Remove(item)) { if (!existing) Destroy(physical); return false; }
            var marker = physical.GetComponent<NpcWorldItem>();
            if (!marker) marker = physical.AddComponent<NpcWorldItem>();
            marker.Bind(item); item.SetLocation(null, NpcEquipmentSlot.None, null, physical); Notify(item); return true;
        }
        public bool TakeWorld(NpcWorldItem marker)
        {
            if (!marker || marker.Item == null || marker.Item.WorldObject != marker.gameObject) return false;
            var group = new List<NpcItemInstance>(marker.Items);
            foreach (var item in group)
                if (item.Owner != null || item.WorldObject != marker.gameObject || !item.Definition || items.Contains(item)) return false;
            foreach (var item in group)
            {
                item.SetLocation(null, NpcEquipmentSlot.None, null, null);
                items.Add(item);
                item.SetLocation(this, NpcEquipmentSlot.None, null, null);
            }
            marker.Bind(null);
            foreach (var item in group) Notify(item);
            return true;
        }
        public bool SpillWorldContents(NpcItemInstance container, IReadOnlyList<NpcItemInstance> group, GameObject physical)
        {
            if (container == null || container.Owner != null || !container.WorldObject || !physical || group == null || group.Count == 0) return false;
            foreach (var item in group)
                if (item == null || item.Owner != null || item.ContainerId != container.InstanceId || !container.Contents.Contains(item)) return false;
            foreach (var item in group)
            {
                container.RemoveContained(item);
                item.SetLocation(null, NpcEquipmentSlot.None, null, physical);
            }
            var marker = physical.GetComponent<NpcWorldItem>();
            if (!marker) marker = physical.AddComponent<NpcWorldItem>();
            marker.BindMany(group);
            foreach (var item in group) Notify(item);
            return true;
        }
        NpcItemInstance Find(string id)
        {
            foreach (var item in items) if (item.InstanceId == id) return item;
            return null;
        }
        static bool Compatible(NpcItemCategory category, NpcEquipmentSlot slot) =>
            slot == NpcEquipmentSlot.MainWeapon ? category == NpcItemCategory.Weapon : category == NpcItemCategory.Clothing;
        void Notify(NpcItemInstance item)
        {
            Changed?.Invoke(item);
            if (item.Definition && item.Definition.category == NpcItemCategory.Clothing && Application.isPlaying)
                SyncClothing();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (logChanges) Debug.Log($"Inventory {name}: {item.Definition.displayName} {item.InstanceId} owner={item.Owner?.name} slot={item.EquippedSlot} container={item.ContainerId}", this);
#endif
        }
        [ContextMenu("Log inventory")]
        void LogInventory()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach (var item in items)
            {
                Debug.Log($"{name}: {item.Definition.displayName} [{item.InstanceId}] {item.EquippedSlot}", this);
                foreach (var child in item.Contents) Debug.Log($"  pocket: {child.Definition.displayName} [{child.InstanceId}]", this);
            }
#endif
        }
    }
}
