using System;
using System.Collections.Generic;
using System.Text;
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
        public TMP_Text ledgerText;
        public TMP_FontAsset ledgerFont;
        public TMP_Text priceLabelPrefab;
        public CurrencyPouch payoutPouchPrefab;
        public Transform payoutPosition;
        [Header("Sold stock display")]
        public Transform soldStockDisplay;
        [Range(1, 4)] public int visibleSoldStockLimit = 2;
        public bool becomeHostileOnTheft = true;
        [SerializeField, Min(0)] int merchantCats;
        [SerializeField] int committedTransactions;
        readonly Dictionary<string, Offer> offers = new();
        CurrencyPouch paymentPurse;
        bool trading;
        bool ready;
        bool committing;
        bool acknowledgedSale;
        readonly StringBuilder ledgerBuffer = new(256);
        static bool ledgerGlyphsReady;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLedgerGlyphs() => ledgerGlyphsReady = false;
        readonly List<TradeItem> soldStock = new();
        readonly List<TradeItem> displayedSoldStock = new();
        readonly Dictionary<string, TradeItem> carriedStock = new();
        readonly HashSet<string> warnedStock = new();
        readonly List<string> finishedCarriedStock = new();
        Transform soldStorage;
        Material ledgerPaperMaterial;
        Material soldShelfMaterial;
        public IReadOnlyList<TradeItem> SoldStock => soldStock;
        public event Action<TradeItem> StockStolen;

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
            if (netLabel) { netLabel.fontSize = Mathf.Max(netLabel.fontSize, 3.4f); netLabel.gameObject.SetActive(false); }
            if (!ledgerText) BuildLedger();
            if (ledgerText) ledgerText.gameObject.SetActive(false);
            var stockObject = new GameObject("Sold stock storage");
            stockObject.transform.SetParent(transform, false);
            stockObject.SetActive(false);
            soldStorage = stockObject.transform;
            if (!soldStockDisplay) BuildSoldStockDisplay();
        }

        void Start()
        {
            if (!configuration) return;
            var state = MerchantSessionState.Get(configuration.vendorId, configuration.startingCats);
            if (state.hostileForTheft && merchant && merchant.brain)
                merchant.brain.hostileToPlayer = true;
            if (!payoutPouchPrefab || !payoutPosition) return;
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
            carriedStock.Clear(); warnedStock.Clear();
        }

        void OnDestroy()
        {
            if (ledgerPaperMaterial) Destroy(ledgerPaperMaterial);
            if (soldShelfMaterial) Destroy(soldShelfMaterial);
        }

        void Update()
        {
            if (carriedStock.Count == 0 || !merchant || !configuration) return;
            finishedCarriedStock.Clear();
            foreach (var pair in carriedStock)
            {
                var item = pair.Value;
                if (!item || item.ownerId != configuration.vendorId || item.IsStolen)
                { finishedCarriedStock.Add(pair.Key); continue; }
                float distance = Vector3.ProjectOnPlane(item.transform.position - merchant.transform.position,
                    Vector3.up).magnitude;
                if (distance > 9f)
                {
                    item.MarkStolen(configuration.vendorId);
                    MerchantSessionState.MarkStockStolen(configuration.vendorId, item.InstanceId);
                    if (becomeHostileOnTheft)
                        MerchantSessionState.MarkVendorHostileForTheft(configuration.vendorId);
                    merchant.RespondToStockTheft(becomeHostileOnTheft);
                    StockStolen?.Invoke(item);
                    finishedCarriedStock.Add(pair.Key);
                }
                else if (distance > 6f)
                {
                    if (warnedStock.Add(pair.Key)) merchant.WarnAboutStock(false);
                }
                else if (!item.IsHeld && distance < 5f)
                { warnedStock.Remove(pair.Key); finishedCarriedStock.Add(pair.Key); }
            }
            foreach (var id in finishedCarriedStock) carriedStock.Remove(id);
        }

        void OnTradingChanged(bool enabled)
        {
            if (enabled) { trading = true; acknowledgedSale = false; Recalculate(); }
            else Cancel();
        }

        void OnItemSelected(TradeItem item)
        {
            if (item) displayedSoldStock.Remove(item);
            if (item && offers.Remove(item.InstanceId, out var old))
            { if (old.label) Destroy(old.label.gameObject); Recalculate(); }
            if (item && configuration && merchant && item.ownerId == configuration.vendorId &&
                !item.IsStolen && !string.IsNullOrEmpty(item.InstanceId) &&
                Vector3.ProjectOnPlane(item.transform.position - merchant.transform.position,
                    Vector3.up).sqrMagnitude <= 25f)
                carriedStock[item.InstanceId] = item;
        }

        void OnItemReleased(TradeItem item)
        {
            if (!item || item.IsHeld || !configuration ||
                string.IsNullOrEmpty(item.InstanceId) || item.GetComponent<CurrencyPouch>()) return;
            bool buying = buyArea && buyArea.Contains(item) &&
                item.ownerId == configuration.vendorId;
            bool selling = !buying && sellArea && sellArea.Contains(item) &&
                item.ownerId == "player";
            if (buying) TryCapture(item, buyArea);
            else if (selling) TryCapture(item, sellArea);
            if (!trading) return;
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
                offer.label.fontSize = Mathf.Max(offer.label.fontSize, 3.8f);
                offer.label.transform.localScale = Vector3.Max(offer.label.transform.localScale,
                    Vector3.one * .06f);
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

        // The holding volume behaves like the backpack even while Daran is idle.
        // Only a later active trade adds a quote; physical capture is not a sale.
        public bool TryCapture(TradeItem item, TradeZone zone)
        {
            if (!item || !item.HasPhysicalRelease || item.IsHeld || !configuration || item.GetComponent<CurrencyPouch>() ||
                (zone != buyArea && zone != sellArea) || !zone.Contains(item) ||
                item.ownerId != (zone == buyArea ? configuration.vendorId : "player")) return false;
            if (!item.IsFloatingForTrade) item.HoldForTradeAt(zone.ClampHoldPoint(item.transform.position));
            return true;
        }

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
            if (ledgerText) ledgerText.gameObject.SetActive(false);
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
            RefreshLedger();
            if (handshake) handshake.Arm(ready);
            if (!ready) merchant?.WithdrawContextLine("transaction_ready_001");
            if (ready && !previouslyReady) merchant?.QueueContextLine("transaction_ready_001");
            else if (!ready && NetCats > 0 && merchantCats < NetCats)
                merchant?.PlayContextLine("merchant_lacks_funds_001");
        }

        void BuildLedger()
        {
            var paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            paper.name = "Counter ledger";
            paper.transform.SetParent(transform, false);
            paper.transform.localPosition = new Vector3(0f, 1.08f, .52f);
            paper.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            paper.transform.localScale = new Vector3(.72f, .42f, .018f);
            Destroy(paper.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            ledgerPaperMaterial = new Material(shader)
                { color = new Color(.78f, .69f, .49f) };
            paper.GetComponent<Renderer>().sharedMaterial = ledgerPaperMaterial;
            var text = new GameObject("Ledger writing");
            text.transform.SetParent(paper.transform, false);
            text.transform.localPosition = new Vector3(-.45f, .42f, -.55f);
            text.transform.localScale = new Vector3(.073f, .125f, 1f);
            ledgerText = text.AddComponent<TextMeshPro>();
            ledgerText.fontSize = 3.1f;
            ledgerText.alignment = TextAlignmentOptions.TopLeft;
            ledgerText.color = new Color(.15f, .105f, .065f);
            if (!ledgerFont) ledgerFont = Resources.Load<TMP_FontAsset>("CaveatLedger");
            if (ledgerFont)
            {
                if (!ledgerGlyphsReady)
                {
                    ledgerFont.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-:.'!() ");
                    ledgerGlyphsReady = true;
                }
                ledgerText.font = ledgerFont;
            }
        }

        void BuildSoldStockDisplay()
        {
            var display = new GameObject("Sold goods display");
            display.transform.SetParent(transform, false);
            display.transform.localPosition = new Vector3(0f, 1.25f, .63f);
            soldStockDisplay = display.transform;
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Upper stock shelf";
            board.transform.SetParent(soldStockDisplay, false);
            board.transform.localScale = new Vector3(1.55f, .07f, .35f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            soldShelfMaterial = new Material(shader) { color = new Color(.32f, .2f, .12f) };
            board.GetComponent<Renderer>().sharedMaterial = soldShelfMaterial;
        }

        void DisplayCommittedSale(TradeItem item)
        {
            if (!item) return;
            soldStock.Add(item);
            if (!soldStockDisplay)
            {
                item.transform.SetParent(soldStorage, true);
                return;
            }
            displayedSoldStock.Add(item);
            while (displayedSoldStock.Count > visibleSoldStockLimit)
            {
                var oldest = displayedSoldStock[0];
                displayedSoldStock.RemoveAt(0);
                if (!oldest) continue;
                oldest.ReleaseTradeHold();
                oldest.transform.SetParent(soldStorage, true);
            }
            for (int i = 0; i < displayedSoldStock.Count; i++)
            {
                var stock = displayedSoldStock[i];
                if (!stock || stock.IsHeld) continue;
                stock.transform.SetParent(soldStockDisplay, true);
                float x = (i - (displayedSoldStock.Count - 1) * .5f) * .58f;
                stock.HoldForTradeAt(soldStockDisplay.TransformPoint(new Vector3(x, .18f, 0f)));
            }
        }

        void RefreshLedger()
        {
            if (!ledgerText) return;
            ledgerText.gameObject.SetActive(trading && offers.Count > 0);
            if (!trading || offers.Count == 0) return;
            ledgerBuffer.Clear().Append(merchant ? merchant.displayName : "Merchant")
                .Append("'s ledger\n");
            int shown = 0;
            foreach (var offer in offers.Values)
            {
                if (shown++ == 6) { ledgerBuffer.Append("...\n"); break; }
                bool selling = offer.zone == sellArea;
                ledgerBuffer.Append(selling ? "+ " : "- ")
                    .Append(offer.item && offer.item.definition ? offer.item.definition.name : "Goods")
                    .Append("  ").Append(offer.quote).Append(" cats\n");
            }
            ledgerBuffer.Append("Net: ");
            if (NetCats < 0) ledgerBuffer.Append("Pay ").Append(-NetCats);
            else if (NetCats > 0) ledgerBuffer.Append("Receive ").Append(NetCats);
            else ledgerBuffer.Append("Even");
            if (NetCats != 0) ledgerBuffer.Append(" cats");
            ledgerText.text = ledgerBuffer.ToString();
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
            int oldCommittedTransactions = committedTransactions;
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
                if (payout) payout.gameObject.SetActive(true);
                if (paymentPurse) paymentPurse.ReturnHomeWhenReleased();
                Cancel();
                var sourceIds = new List<string>(changed.Count);
                var soldIds = new List<string>();
                foreach (var offer in changed) sourceIds.Add(offer.item.InstanceId);
                foreach (var offer in changed)
                    if (offer.zone == sellArea) soldIds.Add(offer.item.InstanceId);
                MerchantSessionState.RecordCommit(configuration.vendorId, merchantCats,
                    sourceIds, payoutId, payoutCats, soldIds);
                foreach (var offer in changed)
                    if (offer.zone == sellArea && offer.item)
                    {
                        offer.item.ReleaseTradeHold();
                        try { DisplayCommittedSale(offer.item); }
                        catch (Exception error)
                        {
                            Debug.LogError("Sold stock display failed: " + error.Message, this);
                            offer.item.transform.SetParent(soldStorage, true);
                        }
                    }
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
                committedTransactions = oldCommittedTransactions;
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
