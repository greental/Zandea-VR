using JunkerEquipment;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.FriendlyNPC
{
    [RequireComponent(typeof(BackpackInventory))]
    public sealed class BackpackUtilityAttachments : MonoBehaviour
    {
        public CurrencyPouch personalPursePrefab;
        public JunkerEquipmentSlot coinPurseAnchor;
        public JunkerEquipmentSlot mapAnchor;
        public Vector3 coinLocalPosition = new(-.22f, .24f, -.06f);
        public Vector3 mapLocalPosition = new(.22f, .24f, -.06f);
        public Vector3 coinLocalEuler = new(0f, 0f, -14f);
        public Vector3 mapLocalEuler = new(0f, 0f, 14f);

        void Awake()
        {
            coinPurseAnchor = EnsureSlot(coinPurseAnchor, "CoinPurseAnchor", coinLocalPosition,
                coinLocalEuler, "personal-coin-purse");
            mapAnchor = EnsureSlot(mapAnchor, "MapAnchor", mapLocalPosition,
                mapLocalEuler, "folded-map");
        }

        void Start()
        {
            // Holstered pouches use trigger colliders. Both direct hands must
            // include triggers in their proximity query to grab the purse.
            var playerRoot = transform.root;
            foreach (var direct in playerRoot.GetComponentsInChildren<XRDirectInteractor>(true))
                direct.physicsTriggerInteraction = QueryTriggerInteraction.Collide;
            var purse = CurrencyPouch.PersonalInstance;
            if (!purse && personalPursePrefab) purse = Instantiate(personalPursePrefab);
            if (!purse || !purse.personal || !coinPurseAnchor) return;
            purse.homeAnchor = coinPurseAnchor;
            var item = purse.GetComponent<JunkerEquipmentItem>();
            if (item && !item.Grab.isSelected)
            {
                if (item.currentSlot && item.currentSlot != coinPurseAnchor) item.currentSlot.ReleaseItem(item);
                coinPurseAnchor.TryMount(item);
            }
        }

        JunkerEquipmentSlot EnsureSlot(JunkerEquipmentSlot slot, string label, Vector3 position,
            Vector3 euler, string acceptedId)
        {
            if (!slot)
            {
                var child = transform.Find(label);
                if (!child)
                {
                    child = new GameObject(label).transform;
                    child.SetParent(transform, false);
                }
                slot = child.GetComponent<JunkerEquipmentSlot>();
                if (!slot) slot = child.gameObject.AddComponent<JunkerEquipmentSlot>();
            }
            slot.transform.localPosition = position;
            slot.transform.localRotation = Quaternion.Euler(euler);
            slot.legacyAcceptedItemId = acceptedId;
            slot.approvedItemIds.Clear();
            slot.approvedItemIds.Add(acceptedId);
            slot.detachMountedItem = true;
            slot.radius = .24f;
            return slot;
        }
    }
}
