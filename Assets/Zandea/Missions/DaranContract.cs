using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.FriendlyNPC;

namespace Zandea.Missions
{
    // Mission state and money remain in the existing session/counter. This
    // component chooses dialogue for the current authoritative state.
    public sealed class DaranContract : MonoBehaviour, IDialogueConditionSource
    {
        public TransportMissionDefinition definition;
        public VendorCounter counter;
        public ContractBarrel[] barrels;
        public bool HasDebt => definition && Session.debt > 0;
        public string WorkNodeId => SelectWorkNode(false);

        TransportMissionSession Session => MissionSessions.Get(definition.missionId);
        FriendlyNpcInteraction npc;
        string settlementLineId;
        float nextEncouragement;
        float nextCaution;

        void Awake()
        {
            npc = GetComponent<FriendlyNpcInteraction>();
            if (npc) npc.contract = this;
            if (!counter) counter = FindFirstObjectByType<VendorCounter>();
        }

        void OnEnable()
        {
            if (barrels != null)
                foreach (var barrel in barrels) if (barrel) barrel.Changed += OnBarrelChanged;
            MerchantSessionState.PayoutCollected += OnPayoutCollected;
        }

        void OnDisable()
        {
            if (barrels != null)
                foreach (var barrel in barrels) if (barrel) barrel.Changed -= OnBarrelChanged;
            MerchantSessionState.PayoutCollected -= OnPayoutCollected;
        }

        public bool Evaluate(string id)
        {
            if (!definition) return false;
            return id switch
            {
                "contract_available" => Session.state == MissionState.Available,
                "contract_active" => Session.state == MissionState.Accepted ||
                                     Session.state == MissionState.InProgress,
                "contract_ready" => Session.state == MissionState.ReadyToTurnIn,
                "contract_debt" => Session.debt > 0,
                _ => false
            };
        }

        public string ResolveNodeId(string requested)
        {
            if (!definition) return requested;
            switch (requested)
            {
                case "work_001": return SelectWorkNode(true);
                case "work_result": return OutcomeNodeId;
                case "work_settlement":
                case "work_debt_result":
                    settlementLineId = null;
                    Settle();
                    return settlementLineId ??
                    (HasDebt ? "daran_job_debt_request" : "daran_job_payment_complete");
                case "daran_job_accept":
                    return Session.state == MissionState.Accepted || Session.state == MissionState.InProgress
                        ? requested : SelectWorkNode(false);
                case "daran_job_payment_complete":
                    Session.paymentCompleteSpoken = true;
                    return requested;
                default: return requested;
            }
        }

        public string OutcomeNodeId
        {
            get
            {
                if (!definition || !Session.Terminal) return "daran_job_incomplete";
                return (Session.Delivered, Session.Broken) switch
                {
                    (3, 0) => "daran_job_perfect",
                    (2, 1) => "daran_job_two_intact",
                    (1, 2) => "daran_job_one_intact",
                    (0, 3) => "daran_job_all_broken",
                    _ => "daran_job_incomplete"
                };
            }
        }

        string SelectWorkNode(bool acknowledge)
        {
            if (!definition) return "work_offer";
            var session = Session;
            if (session.debt > 0) return "daran_job_debt_reminder";
            if (session.state == MissionState.Available) return "daran_job_offer";
            if (session.state == MissionState.Completed)
            {
                if (!string.IsNullOrEmpty(session.payoutId))
                {
                    bool awaitingCollection = MerchantSessionState.Get(definition.giverId, 0)
                        .outstandingPayouts.ContainsKey(session.payoutId);
                    if (awaitingCollection) return "daran_job_payout";
                    if (!session.paymentCompleteSpoken)
                    {
                        if (acknowledge) session.paymentCompleteSpoken = true;
                        return "daran_job_payment_complete";
                    }
                }
                return "daran_job_completed_return";
            }
            if (session.state == MissionState.Abandoned) return "daran_job_abandon";
            if (session.state == MissionState.ReadyToTurnIn) return "daran_job_turnin";
            if (session.Broken > session.acknowledgedBroken)
            {
                if (acknowledge) session.acknowledgedBroken = session.Broken;
                return "daran_job_barrel_broken";
            }
            if (session.Delivered is 1 or 2 &&
                !session.acknowledgedDeliveries.Contains(session.Delivered))
            {
                if (acknowledge) session.acknowledgedDeliveries.Add(session.Delivered);
                return session.Delivered == 1 ? "daran_job_one_delivered" :
                    "daran_job_two_delivered";
            }
            return session.Delivered == 0 ? "daran_job_no_progress" : "work_progress";
        }

        void OnBarrelChanged(ContractBarrel barrel)
        {
            if (!definition || !barrel || barrel.definition != definition || !npc) return;
            var session = Session;
            if (barrel.State == TransportState.Destroyed &&
                session.Broken > session.acknowledgedBroken)
            {
                if (npc.TrySpeakWorldLine("daran_job_barrel_broken", barrel.transform.position, 9f))
                    session.acknowledgedBroken = session.Broken;
                return;
            }
            if (barrel.State == TransportState.DeliveredIntact &&
                session.Delivered is 1 or 2 &&
                !session.acknowledgedDeliveries.Contains(session.Delivered))
            {
                string line = session.Delivered == 1 ? "daran_job_one_delivered" :
                    "daran_job_two_delivered";
                if (npc.TrySpeakWorldLine(line, barrel.transform.position, 9f))
                    session.acknowledgedDeliveries.Add(session.Delivered);
                return;
            }
            if (barrel.State != TransportState.InTransit) return;
            var grab = barrel.GetComponent<XRGrabInteractable>();
            if (grab && grab.isSelected && Time.time >= nextEncouragement &&
                npc.TrySpeakWorldLine("daran_job_encouragement", barrel.transform.position, 7f))
                nextEncouragement = Time.time + 60f;
            else if (grab && !grab.isSelected && Time.time >= nextCaution &&
                barrel.TryGetComponent<Rigidbody>(out var body) && body.linearVelocity.sqrMagnitude > 6.25f &&
                npc.TrySpeakWorldLine("daran_job_careful", barrel.transform.position, 7f))
                nextCaution = Time.time + 60f;
        }

        void OnPayoutCollected(string payoutId)
        {
            if (!definition || Session.payoutId != payoutId || Session.paymentCompleteSpoken || !npc) return;
            if (npc.TrySpeakWorldLine("daran_job_payment_complete", npc.transform.position, 7f))
                Session.paymentCompleteSpoken = true;
        }

        public void Accept()
        {
            if (!definition || Session.state != MissionState.Available) return;
            foreach (var barrel in barrels)
                if (barrel && barrel.definition == definition && !string.IsNullOrEmpty(barrel.objectId))
                    Session.barrels[barrel.objectId] = TransportState.AwaitingPickup;
            if (Session.barrels.Count != definition.barrelCount)
            {
                Debug.LogError("Contract barrel assignment mismatch", this);
                Session.barrels.Clear();
                return;
            }
            Session.state = MissionState.Accepted;
            Debug.Log(definition.title + ": move " + definition.barrelCount +
                " barrels to Daran's stall. +" + CurrencyDefinition.FormatZeks(definition.rewardPerDelivery) +
                " intact, -" + CurrencyDefinition.FormatZeks(definition.penaltyPerBreakage) + " broken.");
        }

        public void Abandon()
        {
            if (!definition || Session.state == MissionState.Completed ||
                Session.state == MissionState.Available || Session.state == MissionState.Abandoned) return;
            Session.debt = Mathf.Max(Session.debt, Session.Broken * definition.penaltyPerBreakage);
            Session.state = MissionState.Abandoned;
        }

        public void Settle()
        {
            if (!definition || !counter) return;
            var session = Session;
            if (session.debt > 0)
            {
                if (counter.TryContractDebtPayment(session.debt))
                {
                    session.debt = 0;
                    session.state = MissionState.Completed;
                    settlementLineId = "daran_job_debt_paid";
                }
                else
                    settlementLineId = CurrencyPouch.PersonalInstance &&
                        !CurrencyPouch.PersonalInstance.CanDebit(session.debt)
                            ? "daran_job_insufficient_funds" : "daran_job_debt_request";
                return;
            }
            if (session.state != MissionState.ReadyToTurnIn || !session.Terminal) return;
            int net = session.Net(definition);
            if (net > 0)
            {
                if (!counter.TryContractPayout(definition.missionId, net))
                {
                    settlementLineId = "merchant_lacks_funds_001";
                    return;
                }
                session.payoutId = "contract-" + definition.missionId;
                session.state = MissionState.Completed;
                settlementLineId = "daran_job_payout";
            }
            else if (net == 0)
            {
                session.state = MissionState.Completed;
                settlementLineId = "daran_job_payment_complete";
            }
            else
            {
                session.debt = -net;
                settlementLineId = "daran_job_debt_request";
            }
            Debug.Log("Delivered: " + session.Delivered + ", broken: " + session.Broken +
                ", total: " + CurrencyDefinition.FormatZeks(net));
        }
    }
}
