using System.Collections;
using JunkerEquipment;
using KenshiVR.CharacterCreator;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat
{
    [RequireComponent(typeof(NpcWorldItem), typeof(VRInventoryItem))]
    public sealed class WearableLoot : MonoBehaviour
    {
        public NpcWorldItem WorldItem { get; private set; }
        public VRInventoryItem InventoryItem { get; private set; }
        public AppearanceItemDefinition Appearance => WorldItem && WorldItem.Item != null
            ? WorldItem.Item.Definition.equippedAppearance : null;
        public bool CanWear => WorldItem && WorldItem.Item != null &&
            WorldItem.Item.WorldObject == gameObject &&
            WorldItem.Item.Definition.category == NpcItemCategory.Clothing && Appearance &&
            (Appearance.slot == AppearanceSlot.Headwear || Appearance.slot == AppearanceSlot.Torso ||
             Appearance.slot == AppearanceSlot.Legs || Appearance.slot == AppearanceSlot.OuterArmor);
        public bool IsWorn => wearer;

        XRGrabInteractable grab;
        Rigidbody body;
        BoxCollider flatCollider;
        GameObject roll;
        Renderer rollRenderer;
        Collider rollCollider;
        Renderer[] flatRenderers;
        PlayerWearableClothing wearer;
        Vector3 flatWorldScale, rollPosition, rollScale;
        bool originalKinematic, originalGravity;
        int releaseSerial;

        void Awake()
        {
            WorldItem = GetComponent<NpcWorldItem>();
            InventoryItem = GetComponent<VRInventoryItem>();
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            flatCollider = GetComponent<BoxCollider>();
            flatRenderers = GetComponentsInChildren<Renderer>(true);
            if (!grab || !body || !flatCollider) return;
            CreateRoll();
            grab.selectEntered.AddListener(Selected);
            grab.selectExited.AddListener(Released);
        }

        void OnDestroy()
        {
            if (grab)
            {
                grab.selectEntered.RemoveListener(Selected);
                grab.selectExited.RemoveListener(Released);
            }
            if (wearer) wearer.Remove(this);
        }

        void CreateRoll()
        {
            roll = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            roll.name = "Rolled garment grip";
            roll.transform.SetParent(transform, false);
            rollRenderer = roll.GetComponent<Renderer>();
            rollCollider = roll.GetComponent<Collider>();
            if (flatRenderers.Length > 0) rollRenderer.sharedMaterial = flatRenderers[0].sharedMaterial;
            Vector3 scale = transform.lossyScale;
            Vector3 size = flatCollider.bounds.size;
            float length = Mathf.Clamp(Mathf.Max(size.x, size.y) * .5f, .18f, .42f);
            const float diameter = .11f;
            roll.transform.localPosition = flatCollider.center;
            roll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            roll.transform.localScale = new Vector3(diameter / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                length * .5f / Mathf.Max(.001f, Mathf.Abs(scale.y)),
                diameter / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            rollPosition = roll.transform.localPosition;
            rollScale = roll.transform.localScale;
            rollRenderer.enabled = false;
            rollCollider.enabled = false;
            rollCollider.isTrigger = true;
            if (!grab.colliders.Contains(rollCollider)) grab.colliders.Add(rollCollider);
        }

        // Called after VRInventoryItem applies its normal stored-item visibility.
        public void SetStoredPresentation(bool stored, bool accessible, bool hide)
        {
            if (!roll || IsWorn) return;
            foreach (var visual in flatRenderers) if (visual) visual.forceRenderingOff = stored;
            if (flatCollider) flatCollider.enabled = !stored;
            rollRenderer.enabled = stored;
            rollRenderer.forceRenderingOff = stored && !accessible && hide;
            rollCollider.enabled = stored && accessible;
        }

        void Selected(SelectEnterEventArgs args)
        {
            releaseSerial++;
            if (wearer) wearer.Remove(this);
        }

        void Released(SelectExitEventArgs args)
        {
            if (!IsWorn && rollCollider && rollCollider.enabled && flatCollider && flatCollider.enabled)
                rollCollider.enabled = false;
            if (!args.isCanceled) StartCoroutine(TryDressAfterRelease(++releaseSerial));
        }

        IEnumerator TryDressAfterRelease(int serial)
        {
            yield return null;
            if (serial != releaseSerial || !CanWear || InventoryItem.IsHeld ||
                InventoryItem.CurrentContainer || IsWorn) yield break;
            PlayerWearableClothing closest = null;
            float distance = float.PositiveInfinity;
            foreach (var candidate in FindObjectsByType<PlayerWearableClothing>(FindObjectsSortMode.None))
            {
                var anchor = candidate.Anchor(Appearance.slot);
                if (!anchor) continue;
                float d = (anchor.position - transform.position).sqrMagnitude;
                if (d < distance) { distance = d; closest = candidate; }
            }
            if (closest) closest.TryWear(this);
        }

        public void SetWorn(PlayerWearableClothing owner, Transform anchor)
        {
            if (owner && anchor)
            {
                flatWorldScale = transform.lossyScale;
                originalKinematic = body.isKinematic;
                originalGravity = body.useGravity;
                wearer = owner;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.useGravity = false;
                transform.SetParent(anchor, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                foreach (var visual in flatRenderers) if (visual) visual.forceRenderingOff = true;
                if (flatCollider) flatCollider.enabled = false;
                // Keep an easy-to-reach invisible pull volume on the visible worn item.
                // The previous 2 cm cylinder was effectively impossible to find in VR.
                Vector3 gripOffset = Appearance.slot switch
                {
                    AppearanceSlot.Headwear => new Vector3(0f, .06f, .10f),
                    AppearanceSlot.Legs => new Vector3(0f, .02f, .16f),
                    _ => new Vector3(0f, 0f, .18f)
                };
                // Creator skeletons may be imported at 0.01 scale. Keep the pull
                // volume at a usable world size after parenting it to a bone.
                Vector3 worldScale = transform.lossyScale;
                roll.transform.localPosition = new Vector3(
                    gripOffset.x / Mathf.Max(.001f, Mathf.Abs(worldScale.x)),
                    gripOffset.y / Mathf.Max(.001f, Mathf.Abs(worldScale.y)),
                    gripOffset.z / Mathf.Max(.001f, Mathf.Abs(worldScale.z)));
                roll.transform.localScale = new Vector3(
                    .14f / Mathf.Max(.001f, Mathf.Abs(worldScale.x)),
                    .12f / Mathf.Max(.001f, Mathf.Abs(worldScale.y)),
                    .14f / Mathf.Max(.001f, Mathf.Abs(worldScale.z)));
                rollRenderer.enabled = false;
                rollCollider.enabled = true;
                return;
            }
            if (!wearer) return;
            wearer = null;
            transform.SetParent(null, true);
            transform.localScale = flatWorldScale;
            body.isKinematic = originalKinematic;
            body.useGravity = originalGravity;
            roll.transform.localPosition = rollPosition;
            roll.transform.localScale = rollScale;
            // Keep the pull collider available until XRI finishes the current
            // selection; disabling it during selectEntered can drop the item.
            rollCollider.enabled = grab && grab.isSelected;
            if (flatCollider) flatCollider.enabled = true;
            foreach (var visual in flatRenderers) if (visual) visual.forceRenderingOff = false;
        }
    }
}
