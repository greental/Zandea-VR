using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.Props;

namespace KenshiVR.MeleeCombat
{
    // Uses the existing NpcItemInstance identity and NpcWorldItem marker. The list is
    // authoritative; presented GameObjects can be recreated without rerolling contents.
    public sealed class PropContainer : MonoBehaviour
    {
        public NpcItemDefinition[] initialContents;
        public GameObject[] initialPhysicalPrefabs;
        public Transform[] presentationPoints;
        public bool openOnStart;
        public bool IsOpen { get; private set; }
        public bool IsBroken { get; private set; }
        public IReadOnlyList<NpcItemInstance> Contents => contents;
        readonly List<NpcItemInstance> contents = new();
        readonly Dictionary<string, PropItemPresentation> presentations = new();
        readonly Dictionary<string, GameObject> physicalPrefabs = new();
        string stableId;
        public string StableId => stableId;
        bool initialized;
        BreakableProp breakable;
        void Awake()
        {
            breakable = GetComponent<BreakableProp>();
            if (breakable) breakable.Broken += OnBroken;
            // A grabbable crate must not claim its hinged lid's collider in XRI.
            var rootGrab = GetComponent<XRGrabInteractable>();
            if (rootGrab && GetComponentInChildren<PropHingedLid>(true))
            {
                rootGrab.colliders.Clear();
                foreach (var shape in GetComponents<Collider>()) rootGrab.colliders.Add(shape);
            }
            EnsureVisibleLid();
            if (GetComponentInChildren<PropHingedLid>(true) && !GetComponent<ChestStorageVolume>())
                gameObject.AddComponent<ChestStorageVolume>();
        }
        void EnsureVisibleLid()
        {
            var lid = GetComponentInChildren<PropHingedLid>(true);
            if (lid)
            {
                lid.gameObject.SetActive(true);
                foreach (var renderer in lid.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                return;
            }
            // Streaming/import variants have occasionally lost the FBX lid child. A
            // physical fallback is preferable to silently spawning an open box.
            var bounds = new Bounds(Vector3.zero, new Vector3(.9f, .55f, .6f));
            var boxes = GetComponents<BoxCollider>();
            if (boxes.Length > 0)
            {
                bounds = new Bounds(boxes[0].center, boxes[0].size);
                for (int i = 1; i < boxes.Length; i++) { bounds.Encapsulate(boxes[i].center - boxes[i].size * .5f); bounds.Encapsulate(boxes[i].center + boxes[i].size * .5f); }
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Chest lid (runtime fallback)";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y + .035f, bounds.center.z);
            go.transform.localScale = new Vector3(bounds.size.x, .07f, bounds.size.z);
            var sourceRenderer = GetComponentInChildren<Renderer>(true);
            if (sourceRenderer) go.GetComponent<Renderer>().sharedMaterial = sourceRenderer.sharedMaterial;
            go.AddComponent<Rigidbody>();
            go.AddComponent<HingeJoint>();
            go.AddComponent<XRGrabInteractable>();
            lid = go.AddComponent<PropHingedLid>();
            lid.container = this;
            lid.Configure();
        }
        void Start()
        {
            // Scene-authored props are usable even when they were placed outside the
            // procedural population. Its Initialize call wins when one is supplied.
            if (!initialized) Initialize(OutpostPropPopulation.ScenePropId(gameObject));
        }
        void OnDestroy()
        {
            if (breakable) breakable.Broken -= OnBroken;
        }
        public void Initialize(string id)
        {
            if (initialized) return;
            initialized = true;
            stableId = id;
            for (int i = 0; i < initialContents.Length; i++)
                if (initialContents[i])
                {
                    var item = new NpcItemInstance(initialContents[i], id + "/item/" + i);
                    contents.Add(item);
                    var physical = initialPhysicalPrefabs != null && i < initialPhysicalPrefabs.Length
                        ? initialPhysicalPrefabs[i] : initialContents[i].worldPrefab;
                    if (physical) physicalPrefabs.Add(item.InstanceId, physical);
                }
            if (openOnStart) SetOpen(true);
        }
        public void SetOpen(bool value)
        {
            if (IsBroken || IsOpen == value) return;
            IsOpen = value;
            if (value)
            {
                for (int i = 0; i < contents.Count; i++) Present(contents[i], i);
            }
            else
            {
                foreach (var presentation in presentations.Values)
                    if (presentation) { presentation.UnbindForStreaming(); Destroy(presentation.gameObject); }
                presentations.Clear();
            }
        }
        void Present(NpcItemInstance item, int index)
        {
            if (!physicalPrefabs.TryGetValue(item.InstanceId, out var prefab) || presentations.ContainsKey(item.InstanceId)) return;
            var anchor = presentationPoints != null && presentationPoints.Length > 0
                ? presentationPoints[index % presentationPoints.Length] : transform;
            var world = Instantiate(prefab, anchor.position, anchor.rotation, anchor);
            world.name = item.Definition.displayName + " - " + item.InstanceId;
            var marker = world.GetComponent<NpcWorldItem>();
            if (!marker) marker = world.AddComponent<NpcWorldItem>();
            marker.Bind(item);
            item.SetLocation(null, NpcEquipmentSlot.None, null, world);
            var presentation = world.GetComponent<PropItemPresentation>();
            if (!presentation) presentation = world.AddComponent<PropItemPresentation>();
            presentation.Bind(this, null, item);
            presentations[item.InstanceId] = presentation;
        }
        internal void Claim(PropItemPresentation presentation, NpcItemInstance item)
        {
            if (!contents.Remove(item)) return;
            presentations.Remove(item.InstanceId);
            presentation.transform.SetParent(null, true);
            presentation.ClearSource();
        }
        void OnBroken(BreakableProp _) => Spill();
        public void Spill()
        {
            if (IsBroken) return;
            IsBroken = true;
            var remaining = new List<NpcItemInstance>(contents);
            for (int i = 0; i < remaining.Count; i++)
            {
                var item = remaining[i];
                if (!presentations.TryGetValue(item.InstanceId, out var presentation) || !presentation)
                {
                    if (!physicalPrefabs.TryGetValue(item.InstanceId, out var prefab)) continue;
                    var world = Instantiate(prefab,
                        transform.position + Vector3.up * .5f + new Vector3((i % 3 - 1) * .22f, 0, (i / 3) * .2f),
                        Quaternion.identity);
                    var marker = world.GetComponent<NpcWorldItem>();
                    if (!marker) marker = world.AddComponent<NpcWorldItem>();
                    marker.Bind(item);
                    item.SetLocation(null, NpcEquipmentSlot.None, null, world);
                    presentation = world.GetComponent<PropItemPresentation>();
                    if (!presentation) presentation = world.AddComponent<PropItemPresentation>();
                    presentation.Bind(this, null, item);
                }
                presentation.transform.SetParent(null, true);
                presentation.ClearSource();
                var body = presentation.GetComponent<Rigidbody>();
                if (body) { body.isKinematic = false; body.useGravity = true; body.AddForce(Vector3.up * .3f, ForceMode.Impulse); }
                contents.Remove(item);
            }
            presentations.Clear();
            IsOpen = false;
        }
    }

    public sealed class PropItemPresentation : MonoBehaviour
    {
        PropContainer container;
        PropRackSocket socket;
        NpcItemInstance item;
        XRGrabInteractable grab;
        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            if (grab) { grab.selectEntered.AddListener(Taken); grab.selectExited.AddListener(Released); }
        }
        void OnDestroy()
        {
            if (grab) { grab.selectEntered.RemoveListener(Taken); grab.selectExited.RemoveListener(Released); }
        }
        public void Bind(PropContainer sourceContainer, PropRackSocket sourceSocket, NpcItemInstance instance)
        {
            container = sourceContainer; socket = sourceSocket; item = instance;
            if (item != null) item.SetLocation(null, NpcEquipmentSlot.None, null, gameObject);
        }
        public void ClearSource() { container = null; }
        public void UnbindForStreaming()
        {
            var marker = GetComponent<NpcWorldItem>();
            if (marker) marker.Bind(null);
            if (item != null && item.WorldObject == gameObject)
                item.SetLocation(null, NpcEquipmentSlot.None, null, null);
            container = null; socket = null;
        }
        void Taken(SelectEnterEventArgs _)
        {
            if (container) container.Claim(this, item);
            if (socket) socket.ItemTaken(this, item);
        }
        void Released(SelectExitEventArgs _)
        {
            if (socket && isActiveAndEnabled) StartCoroutine(socket.TryReturnAfterRelease(this, item));
        }
    }
}
