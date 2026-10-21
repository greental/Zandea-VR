using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [CreateAssetMenu(menuName = "Zandea/Friendly NPC/Vendor")]
    public sealed class VendorConfiguration : ScriptableObject
    {
        public string vendorId = "merchant-daran";
        public List<NpcItemDefinition> startingStock = new();
        public List<NpcItemCategory> acceptedCategories = new();
        [Min(0)] public int startingCats = 500;
        [Min(.01f)] public float purchaseMultiplier = 1f;
        [Min(.01f)] public float saleMultiplier = .5f;

        // A future reputation service can adjust this result without changing physical trade logic.
        public int Quote(TradeItem item, bool buying, float reputationMultiplier = 1f)
        {
            if (!item || !item.definition || item.definition.valueCats <= 0 ||
                item.definition.category == NpcItemCategory.Currency ||
                !buying && acceptedCategories.Count > 0 && !acceptedCategories.Contains(item.definition.category)) return 0;
            float multiplier = (buying ? purchaseMultiplier : saleMultiplier) * reputationMultiplier;
            return Mathf.Max(1, Mathf.RoundToInt(item.definition.valueCats * multiplier));
        }
    }
}
