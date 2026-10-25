using UnityEngine;

namespace JunkerEquipment
{
    // A single Inspector surface for the ordinary equipment slots on the player.
    // The item fields reference scene/prefab children, so the loadout is visible
    // and editable before Play and still uses JunkerEquipmentSlot at runtime.
    public sealed class PlayerStartingItems : MonoBehaviour
    {
        public JunkerEquipmentSlot leftHip, rightHip, leftBackpack, rightBackpack;
        [Header("Starting item prefabs (visible before Play)")]
        public GameObject leftHipPrefab, rightHipPrefab, leftBackpackPrefab, rightBackpackPrefab;
        public JunkerEquipmentItem leftHipItem, rightHipItem, leftBackpackItem, rightBackpackItem;

        public void Apply()
        {
            Set(leftHip, leftHipItem); Set(rightHip, rightHipItem);
            Set(leftBackpack, leftBackpackItem); Set(rightBackpack, rightBackpackItem);
        }
        static void Set(JunkerEquipmentSlot slot, JunkerEquipmentItem item)
        {
            if (!slot) return;
            slot.startingItem = item;
            if (item && item.transform.parent != slot.transform)
                item.transform.SetParent(slot.transform, false);
        }
        void Awake()
        {
            leftHipItem = Ensure(leftHip, leftHipItem, leftHipPrefab);
            rightHipItem = Ensure(rightHip, rightHipItem, rightHipPrefab);
            leftBackpackItem = Ensure(leftBackpack, leftBackpackItem, leftBackpackPrefab);
            rightBackpackItem = Ensure(rightBackpack, rightBackpackItem, rightBackpackPrefab);
            Apply();
        }
        static JunkerEquipmentItem Ensure(JunkerEquipmentSlot slot, JunkerEquipmentItem item, GameObject prefab)
        {
            if (item || !slot || !prefab) return item;
            var instance = Instantiate(prefab, slot.transform);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance.GetComponentInChildren<JunkerEquipmentItem>(true);
        }
    }
}
