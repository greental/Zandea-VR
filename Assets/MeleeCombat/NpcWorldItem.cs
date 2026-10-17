using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat
{
    public sealed class NpcWorldItem : MonoBehaviour
    {
        readonly List<NpcItemInstance> items = new();
        public NpcItemInstance Item => items.Count > 0 ? items[0] : null;
        public IReadOnlyList<NpcItemInstance> Items => items;
        public static event Action<NpcItemInstance, Vector3> WorldObjectDestroyed;
        public void Bind(NpcItemInstance item)
        {
            GroundLootCollider.Ensure(gameObject);
            items.Clear();
            if (item != null) items.Add(item);
        }
        public void BindMany(IReadOnlyList<NpcItemInstance> group)
        {
            GroundLootCollider.Ensure(gameObject);
            items.Clear();
            if (group != null) for (int i = 0; i < group.Count; i++) if (group[i] != null) items.Add(group[i]);
        }
        void OnDestroy()
        {
            foreach (var item in items)
                if (item != null && item.WorldObject == gameObject)
                {
                    WorldObjectDestroyed?.Invoke(item, transform.position);
                    item.SetLocation(null, NpcEquipmentSlot.None, null, null);
                }
        }
    }

    public static class GroundLootCollider
    {
        // A broad, shallow contact patch keeps narrow blades and small loot from
        // tunnelling or rolling through terrain while leaving their grip colliders intact.
        public static BoxCollider Ensure(GameObject root)
        {
            if (!root) return null;
            var box = root.GetComponent<BoxCollider>();
            if (!box) box = root.AddComponent<BoxCollider>();
            var transform = root.transform;
            bool found = false;
            var bounds = new Bounds();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                Bounds meshBounds;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh) meshBounds = skin.localBounds;
                else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
                    meshBounds = filter.sharedMesh.bounds;
                else continue;
                var toRoot = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = toRoot.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                        (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                        (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z));
                    if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                    else bounds.Encapsulate(local);
                }
            }
            if (!found)
            {
                if (box.size.sqrMagnitude > .0001f) return box;
                bounds = new Bounds(Vector3.zero, Vector3.one * .1f);
            }
            var scale = transform.lossyScale;
            box.center = bounds.center;
            box.size = new Vector3(
                Mathf.Max(bounds.size.x, .045f / Mathf.Max(.001f, Mathf.Abs(scale.x))),
                Mathf.Max(bounds.size.y, .045f / Mathf.Max(.001f, Mathf.Abs(scale.y))),
                Mathf.Max(bounds.size.z, .045f / Mathf.Max(.001f, Mathf.Abs(scale.z))));
            box.isTrigger = false;
            box.enabled = true;
            var grab = root.GetComponent<XRGrabInteractable>();
            if (grab && !grab.colliders.Contains(box)) grab.colliders.Add(box);
            var body = root.GetComponent<Rigidbody>();
            if (body) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return box;
        }
    }
}
