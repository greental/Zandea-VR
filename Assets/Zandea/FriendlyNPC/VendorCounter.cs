using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.FriendlyNPC
{
    [DisallowMultipleComponent]
    public sealed class VendorCounter : MonoBehaviour
    {
        public FriendlyNpcInteraction merchant;
        public VendorConfiguration configuration;
        public TradeZone buyArea, sellArea, paymentSpot;
        public HandshakeTarget handshake;
        public TMP_Text netLabel;
        public TMP_Text priceLabelPrefab;
        public CurrencyPouch payoutPouchPrefab;
        public Transform payoutPosition;
        [SerializeField, Min(0)] int merchantCats;
        [SerializeField] int committedTransactions;
        readonly Dictionary<string, Offer> offers = new();
        CurrencyPouch paymentPurse;
        bool trading;
        bool ready;
        bool committing;
        bool acknowledgedSale;

        sealed class Offer
        {
            public TradeItem item;
            public string originalOwner;
            public TradeZone zone;
            public int quote;
            public TMP_Text label;
        }

        public int MerchantCats => merchantCats;
        public int CommittedTransactions => committedTransactions;
        public int NetCats { get; private set; }
        public bool Ready => ready;
        public bool Trading => trading;

        void Awake()
        {
            if (configuration)
                merchantCats = MerchantSessionState.Get(configuration.vendorId, configuration.startingCats).cats;
            if (!merchant) merchant = GetComponentInParent<FriendlyNpcInteraction>();
            if (handshake) handshake.Arm(false);
            if (netLabel) netLabel.gameObject.SetActive(false);
        }

        void Start()
        {
            if (!configuration || !payoutPouchPrefab || !payoutPosition) return;
            var state = MerchantSessionState.Get(configuration.vendorId, configuration.startingCats);
            foreach (var payout in state.outstandingPayouts)
            {
                if (MerchantSessionState.IsLive(payout.Key)) continue;
                var pouch = Instantiate(payoutPouchPrefab, payoutPosition.position,
                    payoutPosition.rotation, transform);
                pouch.AssignStableId(payout.Key);
                pouch.SetInitialBalance(payout.Value);
            }
        }

        void OnEnable()
        {
            TradeItem.Selected += OnItemSelected;
            TradeItem.Released += OnItemReleased;
            CurrencyPouch.Selected += OnPurseSelected;
            CurrencyPouch.Released += OnPurseReleased;
            if (merchant) merchant.TradingChanged += OnTradingChanged;
        }

        void OnDisable()
        {
            TradeItem.Selected -= OnItemSelected;
            TradeItem.Released -= OnItemReleased;
            CurrencyPouch.Selected -= OnPurseSelected;
            CurrencyPouch.Released -= OnPurseReleased;
            if (merchant) merchant.TradingChanged -= OnTradingChanged;
            Cancel();
        }

        void OnTradingChanged(bool enabled)
        {
            if (enabled) { trading = true; acknowledgedSale = false; Recalculate(); }
            else Cancel();
        }

        void OnItemSelected(TradeItem item)
        {
            if (item && offers.Remove(item.InstanceId, out var old))
            { if (old.label) Destroy(old.label.gameObject); Recalculate(); }
        }

        void OnItemReleased(TradeItem item)
        {
            if (!trading || !item || item.IsHeld || !configuration ||
                string.IsNullOrEmpty(item.InstanceId) || item.GetComponent<CurrencyPouch>()) return;
            bool buying = buyArea && buyArea.Contains(item) &&
                item.ownerId == configuration.vendorId;
            bool selling = !buying && sellArea && sellArea.Contains(item) &&
                item.ownerId == "player";
            if (!buying && !selling)
            {
                if (buyArea && buyArea.Contains(item) ||
                    sellArea && sellArea.Contains(item))
                    merchant?.PlayContextLine("item_rejected_001");
                return;
            }
            var zone = buying ? buyArea : sellArea;
            int price = configuration.Quote(item, buying);
            if (price <= 0) { merchant?.PlayContextLine("item_rejected_001"); return; }
            if (offers.ContainsKey(item.InstanceId)) return;
            var offer = new Offer { item = item, originalOwner = item.ownerId, zone = zone, quote = price };
            if (priceLabelPrefab)
            {
                offer.label = Instantiate(priceLabelPrefab, item.transform.position + Vector3.up * .22f,
                    Quaternion.identity, item.transform);
                offer.label.text = (buying ? "−" : "+") + price + " cats";
                offer.label.color = buying ? new Color(1f, .28f, .25f) : new Color(.35f, 1f, .48f);
            }
            offers.Add(item.InstanceId, offer);
            // The accepted object stays at the exact released pose, including long stock.
            // Grabbing it restores normal XR physics and removes its quote.
            item.HoldForTrade();
            Recalculate();
            if (selling && !acknowledgedSale)
            {
                acknowledgedSale = true;
                merchant?.QueueBark("sell_ack_001");
            }
        }
        public void TryOffer(TradeItem item) => OnItemReleased(item);

        void OnPurseSelected(CurrencyPouch pouch)
        {
            if (paymentPurse == pouch) { paymentPurse = null; Recalculate(); }
        }

        void OnPurseReleased(CurrencyPouch pouch)
        {
            if (!trading || !pouch || !pouch.personal || pouch.Grab.isSelected || !paymentSpot ||
                !paymentSpot.Contains(pouch.transform.position)) return;
            paymentPurse = pouch;
            Recalculate();
            if (NetCats < 0 && !ready) merchant?.PlayContextLine("insufficient_funds_001");
        }
        public void TryPresentPurse(CurrencyPouch pouch) => OnPurseReleased(pouch);

        public void Cancel()
        {
            trading = false;
            ready = false;
            if (handshake) handshake.Arm(false);
            foreach (var offer in offers.Values) if (offer.label) Destroy(offer.label.gameObject);
            // Keep cancelled goods safely suspended until their owner collects them.
            // Their offers are gone, so no cancelled item contributes to a later trade.
            offers.Clear();
            paymentPurse = null;
            acknowledgedSale = false;
            NetCats = 0;
            if (netLabel) netLabel.gameObject.SetActive(false);
        }

        void Recalculate()
        {
            bool previouslyReady = ready;
            ready = false;
            if (handshake) handshake.Arm(false);
            long net = 0;
            foreach (var offer in offers.Values)
                net += offer.zone == sellArea ? offer.quote : -offer.quote;
            if (net < int.MinValue || net > int.MaxValue) return;
            NetCats = (int)net;
            ready = Validate(out _);
            if (netLabel)
            {
                netLabel.gameObject.SetActive(trading && offers.Count > 0);
                if (NetCats < 0)
                {
                    int due = -NetCats;
                    netLabel.text = !paymentPurse ? "Pay " + due + " cats\nPlace purse on spot" :
                        !paymentPurse.CanDebit(due) ? "Pay " + due + " cats\nPurse has " + paymentPurse.Cats :
                        "Pay " + due + " cats\nGrip offered hand";
                    netLabel.color = new Color(1f, .4f, .36f);
                }
                else
                {
                    netLabel.text = NetCats > 0 ? "Receive " + net + " cats\nGrip offered hand" :
                        "Even exchange\nGrip offered hand";
                    netLabel.color = new Color(.45f, 1f, .55f);
                }
            }
            if (handshake) handshake.Arm(ready);
            if (!ready) merchant?.WithdrawContextLine("transaction_ready_001");
            if (ready && !previouslyReady) merchant?.QueueContextLine("transaction_ready_001");
            else if (!ready && NetCats > 0 && merchantCats < NetCats)
                merchant?.PlayContextLine("merchant_lacks_funds_001");
        }

        bool Validate(out string reason)
        {
            reason = null;
            if (!trading || !merchant || !merchant.IsTrading || !configuration || offers.Count == 0)
            { reason = "No active offer"; return false; }
            long net = 0;
            foreach (var pair in offers)
            {
                var offer = pair.Value;
                var item = offer.item;
                bool buying = offer.zone == buyArea;
                if (!item || !item.isActiveAndEnabled || item.IsHeld || item.InstanceId != pair.Key ||
                    item.ownerId != offer.originalOwner ||
                    offer.originalOwner != (buying ? configuration.vendorId : "player") ||
                    !offer.zone || !offer.zone.Contains(item) ||
                    configuration.Quote(item, buying) != offer.quote || item.GetComponent<CurrencyPouch>())
                { reason = "Offer changed"; return false; }
                net += buying ? -offer.quote : offer.quote;
            }
            if (net != NetCats)
            { reason = "Price changed"; return false; }
            if (net < 0 && (-net > int.MaxValue || !paymentPurse || !paymentPurse.personal || paymentPurse.Grab.isSelected ||
                !paymentSpot || !paymentSpot.Contains(paymentPurse.transform.position) ||
                !paymentPurse.CanDebit(checked((int)-net)) || merchantCats > int.MaxValue + net))
            { reason = "Player payment unavailable"; return false; }
            if (net > 0 && (merchantCats < net || !payoutPouchPrefab || !payoutPosition))
            { reason = "Merchant payment unavailable"; return false; }
            return true;
        }

        // The sole settlement path. All operations are local, prevalidated, and rolled back on error.
        public bool TryCommit()
        {
            if (committing || !ready || !Validate(out _)) { Recalculate(); return false; }
            committing = true;
            CurrencyPouch payout = null;
            int oldMerchantCats = merchantCats;
            int oldPurseCats = paymentPurse ? paymentPurse.Cats : 0;
            var changed = new List<Offer>(offers.Count);
            try
            {
                if (NetCats > 0)
                {
                    payout = Instantiate(payoutPouchPrefab, payoutPosition.position, payoutPosition.rotation, transform);
                    payout.gameObject.SetActive(false);
                    payout.personal = false;
                    payout.AssignStableId(Guid.NewGuid().ToString("N"));
                    payout.SetInitialBalance(NetCats);
                }
                // Revalidate after preparing the payout object, before changing ownership or balances.
                if (!Validate(out _)) throw new InvalidOperationException("Offer changed during settlement");
                foreach (var offer in offers.Values)
                {
                    offer.item.Transfer(offer.zone == buyArea ? "player" : configuration.vendorId);
                    changed.Add(offer);
                }
                if (NetCats < 0)
                {
                    if (!paymentPurse.TryDebit(-NetCats)) throw new InvalidOperationException("Payment changed");
                    merchantCats -= NetCats;
                }
                else if (NetCats > 0) merchantCats -= NetCats;
                int payoutCats = Mathf.Max(0, NetCats);
                string payoutId = payout ? payout.stableId : null;
                committedTransactions++;
                foreach (var offer in changed)
                    if (offer.zone == sellArea && offer.item.TryGetComponent<XRGrabInteractable>(out var grab))
                        grab.enabled = false;
                if (payout) payout.gameObject.SetActive(true);
                if (paymentPurse) paymentPurse.ReturnHomeWhenReleased();
                Cancel();
                var sourceIds = new List<string>(changed.Count);
                foreach (var offer in changed) sourceIds.Add(offer.item.InstanceId);
                MerchantSessionState.RecordCommit(configuration.vendorId, merchantCats,
                    sourceIds, payoutId, payoutCats);
                trading = merchant && merchant.IsTrading;
                merchant?.PlayContextLine("trade_complete");
                return true;
            }
            catch (Exception error)
            {
                foreach (var offer in changed)
                    if (offer.item)
                    {
                        offer.item.Transfer(offer.originalOwner);
                        if (offer.zone == sellArea &&
                            offer.item.TryGetComponent<XRGrabInteractable>(out var grab)) grab.enabled = true;
                    }
                merchantCats = oldMerchantCats;
                if (paymentPurse) paymentPurse.SetInitialBalance(oldPurseCats);
                if (payout) Destroy(payout.gameObject);
                Debug.LogError("Trade settlement failed and was rolled back: " + error.Message, this);
                Recalculate();
                return false;
            }
            finally { committing = false; }
        }
    }
}
