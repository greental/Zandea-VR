using System.Collections.Generic;

namespace Zandea.FriendlyNPC
{
    // Survives POI unload/reload during one play session. A save system may serialize it later.
    public static class MerchantSessionState
    {
        public sealed class VendorState
        {
            public int cats;
            public readonly HashSet<string> transferredSourceIds = new();
            public readonly Dictionary<string, int> outstandingPayouts = new();
        }

        static readonly Dictionary<string, VendorState> Vendors = new();
        static readonly Dictionary<string, CurrencyPouch> LivePayouts = new();

        public static VendorState Get(string vendorId, int startingCats)
        {
            if (!Vendors.TryGetValue(vendorId, out var state))
            {
                state = new VendorState { cats = startingCats };
                Vendors.Add(vendorId, state);
            }
            return state;
        }

        public static bool WasTransferred(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return false;
            foreach (var state in Vendors.Values)
                if (state.transferredSourceIds.Contains(sourceId)) return true;
            return false;
        }

        public static void RecordCommit(string vendorId, int cats, IEnumerable<string> sourceIds,
            string payoutId, int payoutCats)
        {
            var state = Get(vendorId, cats);
            state.cats = cats;
            foreach (var id in sourceIds) state.transferredSourceIds.Add(id);
            if (payoutCats > 0) state.outstandingPayouts[payoutId] = payoutCats;
        }

        public static void RegisterLive(CurrencyPouch pouch)
        {
            if (pouch && !pouch.personal && pouch.Cats > 0 && !string.IsNullOrEmpty(pouch.stableId))
                LivePayouts[pouch.stableId] = pouch;
        }
        public static void UnregisterLive(CurrencyPouch pouch)
        {
            if (pouch && LivePayouts.TryGetValue(pouch.stableId, out var current) && current == pouch)
                LivePayouts.Remove(pouch.stableId);
        }
        public static bool IsLive(string id) => LivePayouts.TryGetValue(id, out var pouch) && pouch;

        public static void MarkPayoutCollected(string id)
        {
            foreach (var state in Vendors.Values) state.outstandingPayouts.Remove(id);
            LivePayouts.Remove(id);
        }
    }
}
