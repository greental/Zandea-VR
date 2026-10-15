using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
namespace JunkerEquipment
{
    public sealed class JunkerEquipmentSlot : MonoBehaviour
    {
        [FormerlySerializedAs("acceptedItemId")] public string legacyAcceptedItemId;
        // Kept as a source-compatible alias for the existing setup tooling.
        public string acceptedItemId { get => legacyAcceptedItemId; set => legacyAcceptedItemId = value; }
        [Tooltip("If non-empty, only these item IDs may be mounted.")] public List<string> approvedItemIds = new();
        [Tooltip("These item IDs are always rejected, even if approved above.")] public List<string> blockedItemIds = new();
        public JunkerEquipmentItem startingItem;
        [Tooltip("Keep mounted physics outside a moving vehicle hierarchy; the item follows this slot's pose.")]
        public bool detachMountedItem;
        [Min(.01f)] public float radius=.18f;
        public JunkerEquipmentItem Occupant { get; private set; }
        void Awake()
        {
            if(detachMountedItem && TryGetComponent<Rigidbody>(out var strayBody))
            { strayBody.isKinematic=true; strayBody.useGravity=false; }
            if (!string.IsNullOrWhiteSpace(legacyAcceptedItemId) && !approvedItemIds.Contains(legacyAcceptedItemId))
                approvedItemIds.Add(legacyAcceptedItemId);
        }
        void Start() { if(startingItem) TryMount(startingItem); }
        public bool Accepts(JunkerEquipmentItem item)
        {
            if (!item || blockedItemIds.Contains(item.itemId)) return false;
            return approvedItemIds.Count == 0 || approvedItemIds.Contains(item.itemId);
        }
        public bool TryMount(JunkerEquipmentItem item)
        {
            if(Occupant || !Accepts(item) || !item.Mount(this)) return false;
            Occupant=item; return true;
        }
        public void ReleaseItem(JunkerEquipmentItem item) { if(Occupant!=item) return; Occupant=null; item.Unmount(this); }
        void OnDisable() { if(Occupant) ReleaseItem(Occupant); }
        void OnDrawGizmosSelected() { Gizmos.color=Color.cyan; Gizmos.DrawWireSphere(transform.position,radius); }
    }
}
