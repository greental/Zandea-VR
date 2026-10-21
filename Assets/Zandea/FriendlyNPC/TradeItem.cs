using System;
using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.FriendlyNPC
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class TradeItem : MonoBehaviour
    {
        public NpcItemDefinition definition;
        [Tooltip("Stable legal owner; physical storage does not change this before settlement.")]
        public string ownerId = "player";
        [SerializeField] string persistentInstanceId;
        XRGrabInteractable grab;
        public static event Action<TradeItem> Released;
        public static event Action<TradeItem> Selected;
        public string InstanceId => persistentInstanceId;
        public string ItemId => definition ? definition.itemId : null;
        public bool IsHeld => grab && grab.isSelected;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            var existing = GetComponent<NpcWorldItem>();
            if (string.IsNullOrEmpty(persistentInstanceId) && existing && existing.Item != null)
                persistentInstanceId = existing.Item.InstanceId;
            if (string.IsNullOrEmpty(persistentInstanceId)) persistentInstanceId = Guid.NewGuid().ToString("N");
        }

        void Start()
        {
            // A newly streamed source presentation cannot recreate an already traded item.
            if (MerchantSessionState.WasTransferred(persistentInstanceId))
            { gameObject.SetActive(false); Destroy(gameObject); }
        }

        void OnEnable()
        {
            if (!grab) return;
            grab.selectExited.AddListener(OnRelease);
            grab.selectEntered.AddListener(OnSelected);
        }
        void OnDisable()
        {
            if (!grab) return;
            grab.selectExited.RemoveListener(OnRelease);
            grab.selectEntered.RemoveListener(OnSelected);
        }
        void OnSelected(SelectEnterEventArgs _) => Selected?.Invoke(this);
        void OnRelease(SelectExitEventArgs args)
        {
            if (!args.isCanceled && !grab.isSelected) Released?.Invoke(this);
        }

        public void Transfer(string newOwnerId)
        {
            if (string.IsNullOrWhiteSpace(newOwnerId)) throw new ArgumentException("Owner ID required");
            ownerId = newOwnerId;
        }

        public void AssignPersistentIdentity(string id)
        {
            if (!string.IsNullOrWhiteSpace(id)) persistentInstanceId = id;
        }
    }
}
