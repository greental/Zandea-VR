using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.FriendlyNPC;

namespace KenshiVR.MeleeCombat
{
    public static class PocketLootSpill
    {
        public static void Spill(NpcInventory inventory, NpcItemInstance garment, Transform npc, Vector3 center)
        {
            if (!inventory || garment == null || garment.Contents.Count == 0) return;
            var groups = new Dictionary<NpcItemDefinition, List<NpcItemInstance>>();
            foreach (var item in garment.Contents)
            {
                if (!groups.TryGetValue(item.Definition, out var group)) groups[item.Definition] = group = new List<NpcItemInstance>();
                group.Add(item);
            }
            int index = 0;
            foreach (var pair in groups)
            {
                Vector3 outward = npc ? (center - npc.position).normalized : Vector3.forward;
                if (outward.sqrMagnitude < .01f) outward = npc ? npc.forward : Vector3.forward;
                Vector3 position = center + outward * .42f + Vector3.Cross(Vector3.up, outward) * ((index - (groups.Count - 1) * .5f) * .18f);
                float ground = position.y - .15f;
                var hits = Physics.RaycastAll(position + Vector3.up, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                    if (hit.collider && (!npc || !hit.collider.transform.IsChildOf(npc)) && hit.point.y > ground - 1f)
                    { ground = hit.point.y; break; }
                position.y = Mathf.Max(position.y, ground + .08f);
                var physical = MakeBundle(pair.Key, pair.Value.Count, position);
                if (!inventory.SpillWorldContents(garment, pair.Value, physical)) UnityEngine.Object.Destroy(physical);
                else
                {
                    var body = physical.GetComponent<Rigidbody>();
                    body.AddForce((Vector3.up * .12f + outward * .08f) * body.mass, ForceMode.Impulse);
                }
                index++;
            }
        }

        static GameObject MakeBundle(NpcItemDefinition item, int count, Vector3 position)
        {
            var category = item.category;
            if (category == NpcItemCategory.Currency && item.worldPrefab &&
                item.worldPrefab.TryGetComponent<CurrencyPouch>(out _))
            {
                var pouch = UnityEngine.Object.Instantiate(item.worldPrefab, position,
                    Quaternion.identity);
                pouch.name = item.displayName + " pouch";
                pouch.GetComponent<CurrencyPouch>().SetInitialBalance(
                    Mathf.Max(0, count * item.valueCats));
                return pouch;
            }
            var shape = category == NpcItemCategory.Currency || category == NpcItemCategory.Consumable ? PrimitiveType.Sphere : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(shape);
            go.name = item.displayName + (count > 1 ? " x" + count : "");
            go.transform.position = position;
            go.transform.localScale = category == NpcItemCategory.Currency ? new Vector3(.13f, .08f, .11f) :
                category == NpcItemCategory.Consumable ? new Vector3(.11f, .07f, .10f) : new Vector3(.10f, .06f, .08f);
            var renderer = go.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            var color = category == NpcItemCategory.Currency ? new Color(.46f, .32f, .16f) :
                category == NpcItemCategory.Consumable ? new Color(.7f, .6f, .4f) : new Color(.36f, .35f, .33f);
            block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            var body = go.AddComponent<Rigidbody>();
            body.mass = Mathf.Clamp(.08f + item.weight * count, .08f, .8f);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;
            return go;
        }
    }
}
