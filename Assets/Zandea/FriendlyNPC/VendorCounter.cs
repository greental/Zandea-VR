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
        public CurrencyDefinition currency;
        public VendorConfiguration configuration;
        public TradeZone buyArea, sellArea, paymentSpot;
        public HandshakeTarget handshake;
        public TMP_Text netLabel;
        public TMP_Text ledgerText;
        public TMP_FontAsset ledgerFont;
        public TMP_Text priceLabelPrefab;
        [Min(.04f)] public float itemPriceWorldScale = .09f;
        public CurrencyPouch payoutPouchPrefab;
        public Transform payoutPosition;
        [Header("Sold stock display")]
        public Transform soldStockDisplay;
        [Range(1, 4)] public int visibleSoldStockLimit = 2;
        public bool becomeHostileOnTheft = true;
        [SerializeField, Min(0)] int merchantCats;
        [SerializeField] int committedTransactions;
        readonly Dictionary<string, Offer> offers = new();
        readonly Dictionary<TradeItem, TradeZone> capturedItems = new();
        readonly List<TradeItem> escapedItems = new();
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
        Material payoutTrayMaterial;
        TMP_Text paymentCue, payoutCue;
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

        public string Format(int amount) => currency ? currency.Format(amount) : CurrencyDefinition.FormatZeks(amount);
        public bool TryContractPayout(string contractId, int amount)
        {
            if (amount <= 0 || merchantCats < amount || !payoutPouchPrefab || !payoutPosition || !configuration) return false;
            string id = "contract-" + contractId;
            var state = MerchantSessionState.Get(configuration.vendorId, configuration.startingCats);
            if (state.outstandingPayouts.ContainsKey(id) || MerchantSessionState.IsLive(id)) return false;
            var pouch = Instantiate(payoutPouchPrefab, payoutPosition.position, payoutPosition.rotation, transform);
            pouch.gameObject.SetActive(false);
            pouch.AssignStableId(id);
            pouch.SetInitialBalance(amount);
            merchantCats -= amount;
            state.cats = merchantCats;
            state.outstandingPayouts[id] = amount;
            pouch.gameObject.SetActive(true);
            TryPresentContractPayout(contractId);
            return true;
        }
        public bool TryPresentContractPayout(string contractId)
        {
            if (!configuration || !payoutPouchPrefab || !payoutPosition) return false;
            string id = "contract-" + contractId;
            var state = MerchantSessionState.Get(configuration.vendorId, configuration.startingCats);
            if (!state.outstandingPayouts.TryGetValue(id, out int amount) || amount <= 0) return false;
            CurrencyPouch pouch = null;
            foreach (var candidate in FindObjectsByType<CurrencyPouch>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (candidate && candidate.stableId == id && !candidate.personal)
                { pouch = candidate; break; }
            if (!pouch)
            {
                pouch = Instantiate(payoutPouchPrefab, payoutPosition.position,
                    payoutPosition.rotation, transform);
                pouch.AssignStableId(id);
                pouch.SetInitialBalance(amount);
            }
            if (pouch.Cats != amount) return false;
            if (!pouch.Grab.isSelected)
            {
                pouch.transform.SetParent(transform, true);
                pouch.transform.SetPositionAndRotation(payoutPosition.position, payoutPosition.rotation);
                var body = pouch.GetComponent<Rigidbody>();
                if (body && !body.isKinematic)
                { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            }
            RefreshMoneyCues();
            return true;
        }
        public bool TryContractDebtPayment(int amount)
        {
            // The zone callback may not have run yet when the player selects
            // "I'll pay now". The explicit dialogue choice plus a released
            // purse physically on the spot are the authority for payment.
            var purse = paymentPurse ? paymentPurse : CurrencyPouch.PersonalInstance;
            if (amount <= 0 || !configuration || !purse || !purse.personal ||
                purse.Grab.isSelected || !paymentSpot || !paymentSpot.Contains(purse) ||
                !purse.CanDebit(amount) || merchantCats > int.MaxValue - amount) return false;
            if (!purse.TryDebit(amount)) return false;
            merchantCats += amount;
            MerchantSessionState.Get(configuration.vendorId, configuration.startingCats).cats = merchantCats;
            purse.ReturnHomeWhenReleased();
            paymentPurse = null;
            return true;
        }
        public int MerchantCats => merchantCats;
        public int CommittedTransactions => committedTransactions;
        public int NetCats { get; private set; }
        public bool Ready => ready;
        public bool Trading => trading;
        public bool MissionHandshakeAvailable => merchant && merchant.contract &&
            merchant.contract.CanHandshakeSettle;
        public bool AcceptingContractPayment => merchant && merchant.contract && merchant.contract.HasDebt;

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
            BuildMoneyCues();
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
            capturedItems.Clear();
        }

        void OnDestroy()
        {
            if (ledgerPaperMaterial) Destroy(ledgerPaperMaterial);
            if (soldShelfMaterial) Destroy(soldShelfMaterial);
            if (payoutTrayMaterial) Destroy(payoutTrayMaterial);
        }

        void Update()
        {
            RefreshMoneyCues();
            if (capturedItems.Count > 0)
            {
                escapedItems.Clear();
                foreach (var capture in capturedItems)
                    if (!capture.Key || !capture.Value || capture.Key.IsHeld ||
                        !capture.Value.Contains(capture.Key)) escapedItems.Add(capture.Key);
                foreach (var item in escapedItems)
                    if (!item) capturedItems.Remove(item);
                    else if (capturedItems.TryGetValue(item, out var zone))
                        ReleaseCaptureIfOutside(item, zone);
                escapedItems.Clear();
            }
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
            if (item) capturedItems.Remove(item);
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
            if ((buying || selling) && !trading) merchant?.TryBeginTradeFromCounter();
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
                offer.label.text = (buying ? "−" : "+") + Format(price);
                offer.label.fontSize = Mathf.Max(offer.label.fontSize, 4.3f);
                offer.label.rectTransform.sizeDelta = new Vector2(8f, 1.7f);
                var parentScale = item.transform.lossyScale;
                offer.label.transform.localScale = new Vector3(
                    itemPriceWorldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                    itemPriceWorldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.y)),
                    itemPriceWorldScale / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
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
            item.SetTradeHoldingZone(zone);
            capturedItems[item] = zone;
            return true;
        }

        public void ReleaseCaptureIfOutside(TradeItem item, TradeZone zone)
        {
            if (!item || !zone || zone.Contains(item) ||
                !capturedItems.TryGetValue(item, out var capturedZone) || capturedZone != zone) return;
            capturedItems.Remove(item);
            item.ReleaseTradeHold();
            if (offers.Remove(item.InstanceId, out var offer))
            {
                if (offer.label) Destroy(offer.label.gameObject);
                Recalculate();
            }
        }

        void OnPurseSelected(CurrencyPouch pouch)
        {
            if (paymentPurse == pouch) { paymentPurse = null; Recalculate(); }
        }

        void OnPurseReleased(CurrencyPouch pouch)
        {
            if (!trading && !AcceptingContractPayment || !pouch || !pouch.personal || pouch.Grab.isSelected || !paymentSpot ||
                !paymentSpot.Contains(pouch)) return;
            paymentPurse = pouch;
            if (trading)
            {
                Recalculate();
                if (NetCats < 0 && !ready) merchant?.PlayContextLine("insufficient_funds_001");
            }
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
                    netLabel.text = !paymentPurse ? "Pay " + Format(due) + "\nPlace purse on spot" :
                        !paymentPurse.CanDebit(due) ? "Pay " + Format(due) + "\nPurse has " + paymentPurse.Cats :
                        "Pay " + Format(due) + "\nGrip offered hand";
                    netLabel.color = new Color(1f, .4f, .36f);
                }
                else
                {
                    netLabel.text = NetCats > 0 ? "Receive " + Format(NetCats) + "\nGrip offered hand" :
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
            ledgerText.rectTransform.pivot = new Vector2(0f, 1f);
            ledgerText.rectTransform.sizeDelta = new Vector2(10.8f, 6.2f);
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

        void BuildMoneyCues()
        {
            if (!payoutPosition || !paymentSpot) return;
            // Keep the physical payout on an obvious shelf on the player's
            // side, clear of the buy/sell volumes. The tray supports the
            // Rigidbody rather than hiding the pouch inside the counter.
            if (payoutPosition.parent == transform &&
                Mathf.Abs(payoutPosition.localPosition.z) < .1f)
                payoutPosition.localPosition = new Vector3(0f, 1.16f, .38f);
            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = "Payout tray";
            tray.transform.SetParent(transform, false);
            tray.transform.localPosition = new Vector3(0f, .96f, .38f);
            tray.transform.localScale = new Vector3(.36f, .04f, .34f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            payoutTrayMaterial = new Material(shader) { color = new Color(.36f, .24f, .12f) };
            tray.GetComponent<Renderer>().sharedMaterial = payoutTrayMaterial;
            paymentCue = BuildMoneyCue("Payment cue", paymentSpot.transform.position +
                Vector3.up * .2f, new Color(1f, .77f, .42f));
            payoutCue = BuildMoneyCue("Payout cue", payoutPosition.position +
                Vector3.up * .12f, new Color(.75f, 1f, .65f));
            RefreshMoneyCues();
        }

        TMP_Text BuildMoneyCue(string label, Vector3 worldPosition, Color color)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = transform.InverseTransformPoint(worldPosition);
            go.transform.localScale = Vector3.one * .04f;
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 3f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.rectTransform.sizeDelta = new Vector2(12f, 4f);
            go.AddComponent<BillboardLabel>();
            text.enabled = false;
            return text;
        }

        void RefreshMoneyCues()
        {
            if (!paymentCue || !payoutCue) return;
            int due = merchant && merchant.contract ? merchant.contract.DebtAmount : 0;
            if (trading && NetCats < 0) due = Mathf.Max(due, -NetCats);
            paymentCue.enabled = due > 0;
            if (due > 0)
            {
                string label = "PAY HERE\nPlace purse\n" + Format(due);
                if (paymentCue.text != label) paymentCue.text = label;
            }
            int payout = 0;
            if (configuration && merchant && merchant.contract && merchant.contract.definition)
            {
                string id = "contract-" + merchant.contract.definition.missionId;
                MerchantSessionState.Get(configuration.vendorId, configuration.startingCats)
                    .outstandingPayouts.TryGetValue(id, out payout);
            }
            bool readyJob = merchant && merchant.contract && merchant.contract.CanHandshakeSettle;
            payoutCue.enabled = payout > 0 || readyJob || trading && ready && NetCats > 0;
            if (payoutCue.enabled)
            {
                string label = payout > 0 ? "COLLECT HERE\n" + Format(payout) :
                    readyJob ? "SETTLE JOB\nGrip Daran's hand" :
                    "PAYOUT HERE\n" + Format(NetCats);
                if (payoutCue.text != label) payoutCue.text = label;
            }
        }
        public void RefreshContractCues() => RefreshMoneyCues();

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
                    .Append("  ").Append(offer.quote).Append(" ").Append(currency ? currency.pluralName : "Zeks").Append("\n");
            }
            ledgerBuffer.Append("Net: ");
            if (NetCats < 0) ledgerBuffer.Append("Pay ").Append(-NetCats);
            else if (NetCats > 0) ledgerBuffer.Append("Receive ").Append(NetCats);
            else ledgerBuffer.Append("Even");
            if (NetCats != 0) ledgerBuffer.Append(" ").Append(currency ? currency.pluralName : "Zeks");
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
                !paymentSpot || !paymentSpot.Contains(paymentPurse) ||
                !paymentPurse.CanDebit(checked((int)-net)) || merchantCats > int.MaxValue + net))
            { reason = "Player payment unavailable"; return false; }
            if (net > 0 && (merchantCats < net || !payoutPouchPrefab || !payoutPosition))
            { reason = "Merchant payment unavailable"; return false; }
            return true;
        }

        // The sole settlement path. All operations are local, prevalidated, and rolled back on error.
        public bool TryCommit()
        {
            if (MissionHandshakeAvailable && (!trading || !ready))
                return merchant.contract.TryHandshakeSettle();
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
