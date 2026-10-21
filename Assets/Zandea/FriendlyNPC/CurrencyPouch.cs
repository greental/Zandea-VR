using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerEquipment;

namespace Zandea.FriendlyNPC
{
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class CurrencyPouch : MonoBehaviour
    {
        public static CurrencyPouch PersonalInstance { get; private set; }
        public static event System.Action<CurrencyPouch> Released;
        public static event System.Action<CurrencyPouch> Selected;
        public string stableId;
        public bool personal;
        [Min(0), SerializeField] int cats;
        public JunkerEquipmentSlot homeAnchor;
        [Min(.05f)] public float mergeRadius = .18f;
        public int Cats => cats;
        public XRGrabInteractable Grab { get; private set; }
        JunkerEquipmentItem equipment;
        Collider[] mergeHits = new Collider[24];
        bool returnPending;

        void Awake()
        {
            Grab = GetComponent<XRGrabInteractable>();
            equipment = GetComponent<JunkerEquipmentItem>();
            if (string.IsNullOrEmpty(stableId)) stableId = System.Guid.NewGuid().ToString("N");
            if (personal)
            {
                if (PersonalInstance && PersonalInstance != this)
                { Destroy(gameObject); return; }
                PersonalInstance = this;
                if (!transform.parent) DontDestroyOnLoad(gameObject);
            }
            var inventory = GetComponent<VRInventoryItem>();
            if (inventory) inventory.CanStoreInBackpack = false;
        }

        void OnEnable()
        {
            if (!Grab) return;
            Grab.activated.AddListener(OnActivated);
            Grab.selectEntered.AddListener(OnSelected);
            Grab.selectExited.AddListener(OnReleased);
            MerchantSessionState.RegisterLive(this);
        }
        void OnDisable()
        {
            MerchantSessionState.UnregisterLive(this);
            if (!Grab) return;
            Grab.activated.RemoveListener(OnActivated);
            Grab.selectEntered.RemoveListener(OnSelected);
            Grab.selectExited.RemoveListener(OnReleased);
        }
        void OnDestroy() { if (PersonalInstance == this) PersonalInstance = null; }
        void OnSelected(SelectEnterEventArgs _) => Selected?.Invoke(this);
        void OnReleased(SelectExitEventArgs args)
        {
            if (!args.isCanceled && !Grab.isSelected) Released?.Invoke(this);
        }

        public bool CanDebit(int amount) => amount >= 0 && cats >= amount;
        public bool TryDebit(int amount)
        {
            if (!CanDebit(amount)) return false;
            cats -= amount;
            return true;
        }
        public void Credit(int amount)
        {
            if (amount < 0 || cats > int.MaxValue - amount) throw new System.OverflowException();
            cats += amount;
        }
        public void SetInitialBalance(int amount)
        {
            cats = Mathf.Max(0, amount);
            if (isActiveAndEnabled) MerchantSessionState.RegisterLive(this);
        }
        public void AssignStableId(string id)
        {
            MerchantSessionState.UnregisterLive(this);
            stableId = id;
            if (isActiveAndEnabled) MerchantSessionState.RegisterLive(this);
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (!personal || !Grab.isSelected) return;
            int count = Physics.OverlapSphereNonAlloc(transform.position, mergeRadius, mergeHits, ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var other = mergeHits[i] ? mergeHits[i].GetComponentInParent<CurrencyPouch>() : null;
                if (TryMergePayout(other)) return;
            }
        }

        public bool TryMergePayout(CurrencyPouch other)
        {
            if (!personal || !other || other == this || other.personal || other.Cats <= 0 ||
                other.Grab.isSelected || Vector3.Distance(transform.position, other.transform.position) > mergeRadius ||
                cats > int.MaxValue - other.Cats) return false;
            int amount = other.Cats;
            other.cats = 0;
            cats += amount;
            MerchantSessionState.MarkPayoutCollected(other.stableId);
            Destroy(other.gameObject);
            return true;
        }

        public void ReturnHomeWhenReleased()
        {
            if (!personal || !homeAnchor) return;
            returnPending = true;
            TryReturnHome();
        }

        void Update()
        {
            if (personal && !homeAnchor && equipment && equipment.Body && !Grab.isSelected)
            { equipment.Body.isKinematic = true; equipment.Body.useGravity = false; }
            if (returnPending) TryReturnHome();
        }
        void TryReturnHome()
        {
            if (!returnPending || Grab.isSelected || !equipment || !homeAnchor ||
                homeAnchor.Occupant && homeAnchor.Occupant != equipment) return;
            if (equipment.currentSlot == homeAnchor || homeAnchor.TryMount(equipment)) returnPending = false;
        }
    }
}
