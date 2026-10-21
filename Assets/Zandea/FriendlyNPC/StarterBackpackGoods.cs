using JunkerEquipment;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    // Test-scene fixture: one authored item is placed through normal backpack storage.
    public sealed class StarterBackpackGoods : MonoBehaviour
    {
        public BackpackInventory backpack;
        public VRInventoryItem startingItemPrefab;
        void Start()
        {
            if (!backpack || !startingItemPrefab) return;
            var source = startingItemPrefab.GetComponent<TradeItem>();
            if (source && MerchantSessionState.WasTransferred(source.InstanceId)) return;
            var item = Instantiate(startingItemPrefab);
            if (!backpack.RestoreStored(item))
            {
                item.transform.position = backpack.transform.position + Vector3.up * .5f;
                Debug.LogWarning("Starter trade item could not be stored in backpack", this);
            }
        }
    }
}
