using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerEquipment;
using TMPro;

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
        [Min(.05f)] public float mergeRadius = .28f;
        [Min(.1f)] public float returnRadius = .42f;
        [Tooltip("Optional authored money-bag visual. The pouch root keeps its existing XR grab and physics components.")]
        public GameObject visualPrefab;
        [Tooltip("Material for the authored money-bag mesh when the source FBX imports without its texture.")]
        public Material visualMaterial;
        public int Cats => cats;
        public XRGrabInteractable Grab { get; private set; }
        JunkerEquipmentItem equipment;
        Collider[] mergeHits = new Collider[24];
        bool returnPending;
        bool releasePending;
        TMP_Text balanceLabel;

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
            if (visualPrefab) InstallVisual();
            BuildBalanceLabel();
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
        void OnSelected(SelectEnterEventArgs _)
        {
            releasePending = false;
            Selected?.Invoke(this);
        }
        void OnReleased(SelectExitEventArgs args)
        {
            if (!args.isCanceled) releasePending = true;
        }

        public bool CanDebit(int amount) => amount >= 0 && cats >= amount;
        public bool TryDebit(int amount)
        {
            if (!CanDebit(amount)) return false;
            cats -= amount;
            RefreshBalanceLabel();
            return true;
        }
        public void Credit(int amount)
        {
            if (amount < 0 || cats > int.MaxValue - amount) throw new System.OverflowException();
            cats += amount;
            RefreshBalanceLabel();
        }
        public void SetInitialBalance(int amount)
        {
            cats = Mathf.Max(0, amount);
            RefreshBalanceLabel();
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
            return AbsorbPayout(other, true);
        }

        bool AbsorbPayout(CurrencyPouch other, bool requireProximity)
        {
            if (!personal || !other || other == this || other.personal || other.Cats <= 0 ||
                other.Grab.isSelected || requireProximity &&
                Vector3.Distance(transform.position, other.transform.position) > mergeRadius ||
                cats > int.MaxValue - other.Cats) return false;
            int amount = other.Cats;
            other.cats = 0;
            cats += amount;
            RefreshBalanceLabel();
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
        void LateUpdate()
        {
            if (!releasePending || Grab.isSelected) return;
            releasePending = false;
            Released?.Invoke(this);
            if (personal)
            {
                var backpack = homeAnchor ? homeAnchor.GetComponentInParent<BackpackInventory>() : null;
                bool inside = backpack && backpack.storageVolume &&
                    backpack.storageVolume.Contains(transform.position);
                if (homeAnchor && (inside ||
                    Vector3.Distance(transform.position, homeAnchor.transform.position) <= returnRadius))
                    ReturnHomeWhenReleased();
                TryMergeNearbyPayout();
            }
            else
            {
                var purse = PersonalInstance;
                var backpack = purse && purse.homeAnchor ?
                    purse.homeAnchor.GetComponentInParent<BackpackInventory>() : null;
                if (purse && (Vector3.Distance(transform.position, purse.transform.position) <=
                    Mathf.Max(mergeRadius, purse.mergeRadius) ||
                    backpack && backpack.IsUsable && backpack.storageVolume &&
                    backpack.storageVolume.Contains(transform.position)))
                    purse.AbsorbPayout(this, false);
            }
        }
        void TryMergeNearbyPayout()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, mergeRadius, mergeHits,
                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var other = mergeHits[i] ? mergeHits[i].GetComponentInParent<CurrencyPouch>() : null;
                if (TryMergePayout(other)) return;
            }
        }
        void InstallVisual()
        {
            var original = GetComponent<MeshRenderer>();
            if (original) original.enabled = false;
            var visual = Instantiate(visualPrefab, transform);
            visual.name = "Money bag visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 3f;
            if (visualMaterial)
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = visualMaterial;
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) Destroy(collider);
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>()) Destroy(body);
            foreach (var grab in visual.GetComponentsInChildren<XRGrabInteractable>()) Destroy(grab);
        }
        void BuildBalanceLabel()
        {
            var labelObject = new GameObject("Zeks balance");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, .85f, -.7f);
            labelObject.transform.localScale = Vector3.one * .12f;
            balanceLabel = labelObject.AddComponent<TextMeshPro>();
            balanceLabel.alignment = TextAlignmentOptions.Center;
            balanceLabel.fontSize = 3f;
            balanceLabel.color = new Color(.95f, .87f, .64f);
            balanceLabel.rectTransform.sizeDelta = new Vector2(8f, 2f);
            labelObject.AddComponent<BillboardLabel>();
            RefreshBalanceLabel();
        }
        void RefreshBalanceLabel()
        {
            if (balanceLabel) balanceLabel.text = CurrencyDefinition.FormatZeks(cats);
        }
        void TryReturnHome()
        {
            if (!returnPending || Grab.isSelected || !equipment || !homeAnchor ||
                homeAnchor.Occupant && homeAnchor.Occupant != equipment) return;
            if (equipment.currentSlot == homeAnchor || homeAnchor.TryMount(equipment)) returnPending = false;
        }
    }
}
