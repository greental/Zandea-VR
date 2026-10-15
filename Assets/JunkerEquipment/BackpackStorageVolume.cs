using System.Collections.Generic;
using UnityEngine;

namespace JunkerEquipment
{
    /// <summary>Registered box volumes are queried by held items only; collider overlap is not acceptance.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class BackpackStorageVolume : MonoBehaviour
    {
        static readonly HashSet<BackpackStorageVolume> active = new();
        readonly HashSet<VRInventoryItem> candidates = new();
        public BackpackInventory inventory;
        public GameObject validStorageIndicator;
        BoxCollider box;
        public BoxCollider Box => box ? box : box = GetComponent<BoxCollider>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() => active.Clear();
        void Awake() { Box.isTrigger = true; }
        void OnEnable() => active.Add(this);
        void OnDisable() { active.Remove(this); candidates.Clear(); RefreshIndicator(); }

        public bool Contains(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled || !Box.enabled) return false;
            Vector3 p = transform.InverseTransformPoint(worldPoint) - Box.center;
            Vector3 half = Box.size * .5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        internal static BackpackStorageVolume FindFor(VRInventoryItem item)
        {
            BackpackStorageVolume best = null;
            float distance = float.PositiveInfinity;
            foreach (var volume in active)
            {
                if (!volume || !volume.inventory || !volume.inventory.CanAccept(item) || !volume.Contains(item.AnchorPosition)) continue;
                float d = (volume.transform.TransformPoint(volume.Box.center) - item.AnchorPosition).sqrMagnitude;
                if (d < distance) { distance = d; best = volume; }
            }
            return best;
        }

        internal void SetCandidate(VRInventoryItem item, bool valid)
        {
            if (valid) candidates.Add(item); else candidates.Remove(item);
            RefreshIndicator();
        }
        void LateUpdate() => RefreshIndicator();
        void RefreshIndicator()
        {
            candidates.RemoveWhere(item => !item || !item.isActiveAndEnabled || !item.IsHeld);
            if (validStorageIndicator) validStorageIndicator.SetActive(inventory && inventory.IsUsable && candidates.Count > 0);
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(Box.center, Box.size);
        }
    }
}
