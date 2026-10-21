using System.Collections;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KenshiVR.MeleeCombat
{
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(NpcInventory))]
    public sealed class NpcClothingLoot : MonoBehaviour
    {
        public NpcInventory inventory;
        public ScavengerHealth health;
        [Min(.1f)] public float pullDistance = .24f;
        [Range(-1f, 1f)] public float minimumOutwardDot = -1f;
        [Min(.04f)] public float handleRadius = .18f;
        readonly Dictionary<NpcEquipmentSlot, Handle> handles = new();
        float nextHandleRefresh;

        public bool IsPullHandle(XRGrabInteractable interactable)
        {
            foreach (var handle in handles.Values)
                if (!handle.complete && handle.grab == interactable) return true;
            return false;
        }

        sealed class Handle
        {
            public NpcItemInstance item;
            public AppearanceSpawnedItem worn;
            public ClothingLootFactory.Snapshot snapshot;
            public GameObject proxy;
            public XRGrabInteractable grab;
            public XRDirectInteractor hand;
            public Vector3 startLocal;
            public bool complete;
            public System.Action<SelectEnterEventArgs> entered;
            public System.Action<SelectExitEventArgs> exited;
        }

        void Awake()
        {
            if (!inventory) inventory = GetComponent<NpcInventory>();
            if (!health) health = GetComponent<ScavengerHealth>();
        }
        void Start()
        {
            Build(NpcEquipmentSlot.Torso, AppearanceSlot.Torso);
            Build(NpcEquipmentSlot.Legs, AppearanceSlot.Legs);
            Build(NpcEquipmentSlot.Headwear, AppearanceSlot.Headwear);
            if (health) health.StateChanged += OnStateChanged;
            SetAvailability();
        }
        void OnDestroy()
        {
            if (health) health.StateChanged -= OnStateChanged;
            foreach (var handle in handles.Values)
            {
                if (handle.grab)
                {
                    handle.grab.selectEntered.RemoveListener(handle.entered.Invoke);
                    handle.grab.selectExited.RemoveListener(handle.exited.Invoke);
                }
                if (handle.proxy) Destroy(handle.proxy);
                handle.snapshot?.Dispose();
            }
            handles.Clear();
        }
        void OnStateChanged(NpcConsciousness before, NpcConsciousness after) => SetAvailability();
        bool CanStrip => health && (health.State == NpcConsciousness.Unconscious || health.State == NpcConsciousness.Dead);
        void SetAvailability()
        {
            foreach (var handle in handles.Values)
            {
                if (!handle.grab) continue;
                if (!CanStrip && handle.hand) Cancel(handle);
                handle.grab.interactionLayers = CanStrip && !handle.complete ? (InteractionLayerMask)(-1) : (InteractionLayerMask)0;
                var collider = handle.proxy.GetComponent<Collider>();
                if (collider) collider.enabled = CanStrip && !handle.complete;
            }
        }
        void Build(NpcEquipmentSlot slot, AppearanceSlot visualSlot)
        {
            var item = inventory ? inventory.Equipped(slot) : null;
            if (handles.TryGetValue(slot, out var previous))
            {
                if (previous.complete && item == null || previous.hand || !previous.complete && previous.item == item && previous.worn &&
                    previous.worn.definition == item?.Definition?.equippedAppearance) return;
                Cancel(previous);
                if (previous.proxy) Destroy(previous.proxy);
                previous.snapshot?.Dispose();
                handles.Remove(slot);
            }
            if (item == null || !item.Definition || item.Definition.category != NpcItemCategory.Clothing) return;
            AppearanceSpawnedItem worn = null;
            foreach (var marker in GetComponentsInChildren<AppearanceSpawnedItem>(true))
                if (marker.slot == visualSlot && marker.definition == item.Definition.equippedAppearance) { worn = marker; break; }
            if (!worn) { Debug.LogWarning("No worn visual for " + item.Definition.displayName, this); return; }
            ClothingLootFactory.Snapshot snapshot = null;
            if (!item.Definition.preferCachedLoot || !item.Definition.worldPrefab)
            {
                snapshot = ClothingLootFactory.Capture(worn, item.Definition);
                if (!snapshot.Valid) { snapshot.Dispose(); return; }
            }
            var proxy = new GameObject("Pull " + item.Definition.displayName);
            proxy.transform.SetParent(worn.transform.parent, false);
            Bounds bounds = snapshot != null ? snapshot.Bounds : new Bounds(Vector3.zero, Vector3.one * .2f);
            if (snapshot == null && item.Definition.worldPrefab.TryGetComponent<BoxCollider>(out var shell))
                bounds = new Bounds(shell.center, shell.size);
            proxy.transform.SetPositionAndRotation(worn.transform.TransformPoint(ClothingLootFactory.GripPoint(bounds, item.Definition)), worn.transform.rotation);
            var collider = proxy.AddComponent<SphereCollider>();
            Vector3 worldScale = proxy.transform.lossyScale;
            collider.radius = handleRadius / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.y), Mathf.Abs(worldScale.z)));
            collider.isTrigger = true;
            var body = proxy.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false;
            var grab = proxy.AddComponent<XRGrabInteractable>();
            grab.trackPosition = false; grab.trackRotation = false;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.selectMode = InteractableSelectMode.Single;
            var handle = new Handle { item = item, worn = worn, snapshot = snapshot, proxy = proxy, grab = grab };
            handle.entered = args => Selected(handle, args);
            handle.exited = args => Released(handle);
            grab.selectEntered.AddListener(handle.entered.Invoke);
            grab.selectExited.AddListener(handle.exited.Invoke);
            handles[slot] = handle;
        }
        void Selected(Handle handle, SelectEnterEventArgs args)
        {
            if (!CanStrip || handle.complete || handle.item.Owner != inventory || handle.item.EquippedSlot == NpcEquipmentSlot.None)
            { Cancel(handle); return; }
            handle.hand = args.interactorObject as XRDirectInteractor;
            if (!handle.hand) { Cancel(handle); return; }
            handle.startLocal = handle.proxy.transform.InverseTransformPoint(handle.hand.transform.position);
        }
        void Released(Handle handle) => handle.hand = null;
        void Update()
        {
            if (Time.time >= nextHandleRefresh)
            {
                nextHandleRefresh = Time.time + .5f;
                Build(NpcEquipmentSlot.Torso, AppearanceSlot.Torso);
                Build(NpcEquipmentSlot.Legs, AppearanceSlot.Legs);
                Build(NpcEquipmentSlot.Headwear, AppearanceSlot.Headwear);
                SetAvailability();
            }
            if (!CanStrip) return;
            foreach (var handle in handles.Values)
            {
                if (!handle.hand || handle.complete) continue;
                Vector3 delta = handle.proxy.transform.InverseTransformPoint(handle.hand.transform.position) - handle.startLocal;
                if (handle.proxy.transform.TransformVector(delta).magnitude < pullDistance) continue;
                if (minimumOutwardDot > -1f && Vector3.Dot(delta.normalized, Vector3.forward) < minimumOutwardDot) continue;
                Complete(handle);
            }
        }
        void Cancel(Handle handle)
        {
            handle.hand = null;
            if (handle.grab && handle.grab.isSelected && handle.grab.interactionManager)
                handle.grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)handle.grab);
        }
        void Complete(Handle handle)
        {
            if (handle.complete || !CanStrip || handle.item.Owner != inventory) return;
            var hand = handle.hand;
            Vector3 position = hand ? hand.transform.position : handle.proxy.transform.position;
            Quaternion rotation = handle.worn ? handle.worn.transform.rotation : handle.proxy.transform.rotation;
            Vector3 scale = handle.worn ? handle.worn.transform.lossyScale : Vector3.one;
            var definition = handle.item.Definition;
            var physical = definition.preferCachedLoot && definition.worldPrefab
                ? Instantiate(definition.worldPrefab, position, rotation * Quaternion.Euler(definition.lootEuler))
                : ClothingLootFactory.Create(definition, handle.snapshot, position, rotation, scale);
            // Cached world prefabs already include the garment's fitting scale in their
            // child transforms. Applying the worn renderer scale again magnifies items
            // such as scarf_wrap_b by several dozen times.
            if (!physical) return;
            if (!inventory.Drop(handle.item, position, rotation, physical))
            { Destroy(physical); return; }
            handle.complete = true;
            if (handle.worn) handle.worn.gameObject.SetActive(false);
            if(handle.item.Definition.equippedAppearance&&handle.item.Definition.equippedAppearance.slot==AppearanceSlot.Headwear)
                handle.proxy.GetComponentInParent<DetachedHeadAppearance>()?.RevealHair();
            PocketLootSpill.Spill(inventory, handle.item, transform, position);
            handle.proxy.GetComponent<Collider>().enabled = false;
            handle.grab.interactionLayers = (InteractionLayerMask)0;
            StartCoroutine(Handoff(handle, hand, physical.GetComponent<XRGrabInteractable>()));
        }
        public void TransferHeadwearToDetachedHead(Transform detachedHead)
        {
            if (!detachedHead || !handles.TryGetValue(NpcEquipmentSlot.Headwear, out var handle) || handle.complete) return;
            AppearanceSpawnedItem copied = null;
            foreach (var item in detachedHead.GetComponentsInChildren<AppearanceSpawnedItem>(true))
                if (item.slot == AppearanceSlot.Headwear && item.definition == handle.item.Definition.equippedAppearance) { copied = item; break; }
            if (!copied) return;
            handle.worn = copied;
            if (handle.proxy)
            {
                handle.proxy.transform.SetParent(detachedHead, true);
                Bounds bounds = handle.snapshot != null ? handle.snapshot.Bounds : new Bounds(Vector3.zero, Vector3.one * .2f);
                handle.proxy.transform.SetPositionAndRotation(copied.transform.TransformPoint(ClothingLootFactory.GripPoint(bounds, handle.item.Definition)), copied.transform.rotation);
            }
        }
        IEnumerator Handoff(Handle handle, XRDirectInteractor hand, XRGrabInteractable physical)
        {
            Cancel(handle);
            yield return null;
            if (hand && hand.isActiveAndEnabled && physical && !hand.hasSelection)
            {
                hand.StartManualInteraction((IXRSelectInteractable)physical);
                physical.gameObject.AddComponent<LootManualHold>().Bind(hand);
            }
            if (handle.proxy) Destroy(handle.proxy);
        }
    }
}
