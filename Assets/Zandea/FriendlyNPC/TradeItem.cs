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
        Rigidbody body;
        Collider[] itemColliders;
        bool tradeHeld;
        bool wasKinematic;
        bool hadGravity;
        RigidbodyInterpolation oldInterpolation;
        Vector3 heldPosition;
        Quaternion heldRotation;
        bool releasePending;
        bool hasPhysicalRelease;
        public static event Action<TradeItem> Released;
        public static event Action<TradeItem> Selected;
        public string InstanceId => persistentInstanceId;
        public string ItemId => definition ? definition.itemId : null;
        public bool IsHeld => grab && grab.isSelected;
        public bool IsFloatingForTrade => tradeHeld;
        public bool HasPhysicalRelease => hasPhysicalRelease;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            itemColliders = GetComponentsInChildren<Collider>(true);
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
        void OnSelected(SelectEnterEventArgs _)
        {
            releasePending = false;
            hasPhysicalRelease = false;
            ReleaseTradeHold();
            Selected?.Invoke(this);
        }
        void OnRelease(SelectExitEventArgs args)
        {
            if (!args.isCanceled)
            {
                hasPhysicalRelease = true;
                releasePending = true;
            }
        }

        // Match the backpack's released-pose behavior without changing legal ownership.
        public void HoldForTrade()
        {
            if (!body || IsHeld) return;
            if (!tradeHeld)
            {
                wasKinematic = body.isKinematic;
                hadGravity = body.useGravity;
                oldInterpolation = body.interpolation;
            }
            heldPosition = transform.position;
            heldRotation = transform.rotation;
            tradeHeld = true;
            FreezeTradeBody();
        }

        public void HoldForTradeAt(Vector3 position)
        {
            if (IsHeld) return;
            transform.position = position;
            HoldForTrade();
        }

        public void ReleaseTradeHold()
        {
            if (!tradeHeld) return;
            tradeHeld = false;
            if (!body) return;
            body.isKinematic = wasKinematic;
            body.useGravity = hadGravity;
            body.interpolation = oldInterpolation;
        }

        void LateUpdate()
        {
            if (releasePending && !IsHeld)
            {
                releasePending = false;
                Released?.Invoke(this);
            }
            if (!tradeHeld || IsHeld) return;
            FreezeTradeBody();
            transform.SetPositionAndRotation(heldPosition, heldRotation);
        }

        void FreezeTradeBody()
        {
            if (!body) return;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
        }

        public bool Intersects(Bounds volume)
        {
            if (itemColliders == null) return false;
            foreach (var itemCollider in itemColliders)
                if (itemCollider && itemCollider.enabled && !itemCollider.isTrigger &&
                    itemCollider.bounds.Intersects(volume)) return true;
            return false;
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
