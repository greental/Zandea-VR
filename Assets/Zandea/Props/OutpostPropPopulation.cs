using System;
using System.Collections.Generic;
using KenshiVR.MeleeCombat;
using UnityEngine;
using Zandea.POI;

namespace Zandea.Props
{
    // One stateful population per POI. Physical instances survive terrain tile recycling.
    // The first load creates a deterministic placement; later loads reactivate the same objects.
    public sealed class OutpostPropPopulation : MonoBehaviour
    {
        public OutpostPropDefinition definition;
        public IReadOnlyList<GameObject> Instances => instances;
        readonly List<GameObject> instances = new();
        readonly List<Vector2> footprints = new();
        GameObject root;
        bool created;
        BanditOutpost poi;

        public static string ScenePropId(GameObject instance)
        {
            var p = instance.transform.position;
            return "scene/" + instance.scene.path + "/" + instance.name + "/" +
                Mathf.RoundToInt(p.x * 100) + "/" + Mathf.RoundToInt(p.y * 100) + "/" + Mathf.RoundToInt(p.z * 100);
        }

        public void Load(BanditOutpost owner)
        {
            poi = owner;
            if (!definition) definition = Resources.Load<OutpostPropDefinition>("ZandeaOutpostProps");
            if (!definition) return;
            if (!created) Create();
            else foreach (var instance in instances)
                if (instance && !instance.activeSelf &&
                    !(instance.GetComponent<BreakableProp>()?.IsBroken ?? false)) instance.SetActive(true);
        }
        public void Unload()
        {
            if (!poi || !poi.definition) return;
            foreach (var instance in instances)
            {
                if (!instance) continue;
                var physical = instance.GetComponent<PhysicalProp>();
                if (physical && physical.IsHeld) continue;
                // A dropped object may already have lost its terrain collider during a
                // large teleport. Use the horizontal site distance so it still retires.
                var offset = instance.transform.position - poi.Center;
                if (new Vector2(offset.x, offset.z).magnitude > poi.definition.unloadDistance) continue;
                if (physical) physical.SetDormant(true); else instance.SetActive(false);
            }
        }
        void Create()
        {
            created = true;
            root = new GameObject("Persistent props - " + poi.poiId);
            root.transform.SetParent(poi.transform, false);
            root.transform.SetPositionAndRotation(poi.Center, poi.Rotation);
            var random = new System.Random(unchecked(poi.seed * 31 + 1808));
            var pots = new List<GameObject>();
            if (definition.ceramicPotVariants != null)
                foreach (var variant in definition.ceramicPotVariants)
                    if (variant) pots.Add(variant);
            if (pots.Count == 0 && definition.ceramicPot) pots.Add(definition.ceramicPot);
            int potIndex = 0;
            GameObject NextPot()
            {
                if (pots.Count == 0) return null;
                if (potIndex % pots.Count == 0)
                    for (int i = pots.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        var swap = pots[i]; pots[i] = pots[j]; pots[j] = swap;
                    }
                return pots[potIndex++ % pots.Count];
            }
            int slot = 0;
            foreach (var reservation in poi.Layout.reservations)
            {
                if (reservation.kind != ReservationKind.FutureProp) continue;
                string category = reservation.category;
                if (category == "Under canopy")
                {
                    Place(NextPot(), reservation, ref slot, random, .45f);
                    for (int j = 1; j < definition.extraPotsPerCanopy; j++)
                        Place(NextPot(), reservation, ref slot, random, .42f);
                    for (int j = 0; j < definition.basketsPerCanopy; j++)
                        Place(definition.wickerBasket, reservation, ref slot, random, .25f);
                }
                else if (category == "Storage")
                {
                    Place(definition.chest, reservation, ref slot, random, .7f);
                    Place(definition.barrel, reservation, ref slot, random, .5f);
                    Place(definition.crate, reservation, ref slot, random, .55f);
                    Place(definition.sack, reservation, ref slot, random, .4f);
                    for (int j = 0; j < definition.storageBaskets; j++)
                        Place(definition.wickerBasket, reservation, ref slot, random, .25f);
                    if (definition.extraStorageProps != null)
                        foreach (var extra in definition.extraStorageProps)
                            Place(extra, reservation, ref slot, random, .7f);
                }
                else if (category == "Courtyard social")
                {
                    Place(definition.table, reservation, ref slot, random, .75f);
                    Place(definition.woodenStool, reservation, ref slot, random, .35f);
                }
                else if (category == "Guard post / future ladder")
                    Place(definition.weaponRack, reservation, ref slot, random, .55f);
                else if (category == "Perimeter clutter")
                    Place(definition.statue, reservation, ref slot, random, .6f);
                else if (category == "Entrance side")
                {
                    if (definition.wickerBasket)
                        Place(NextPot(), reservation, ref slot, random, .4f);
                    else if (random.NextDouble() < .55)
                        Place(definition.sack ? definition.sack : definition.ceramicPot,
                            reservation, ref slot, random, .4f);
                }
            }
        }
        void Place(GameObject prefab, OutpostReservation area, ref int slot, System.Random random, float radius)
        {
            if (!prefab) return;
            float scale = prefab == definition.ceramicPot ? Mathf.Max(.5f, definition.potScale) :
                prefab == definition.sack ? Mathf.Max(.5f, definition.sackScale) :
                prefab == definition.crate ? Mathf.Max(.5f, definition.crateScale) : 1f;
            radius *= scale;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var offset = new Vector2((float)(random.NextDouble() - .5) * (area.size.x - radius * 2),
                                         (float)(random.NextDouble() - .5) * (area.size.y - radius * 2));
                var rotated = Quaternion.Euler(0, area.yaw, 0) * new Vector3(offset.x, 0, offset.y);
                var local = area.center + new Vector2(rotated.x, rotated.z);
                if (!Clear(local, radius)) continue;
                var world = poi.ToWorld(local);
                float pitch = prefab == definition.chest || prefab == definition.crate ? 90 : prefab == definition.sack ? random.Next(-5, 6) : 0;
                float roll = prefab == definition.sack ? random.Next(-6, 7) : 0;
                var instance = Instantiate(prefab, world,
                    poi.Rotation * Quaternion.Euler(pitch, area.yaw + random.Next(-15, 16), roll), root.transform);
                instance.transform.localScale = prefab.transform.localScale * scale;
                instance.name = poi.poiId + "/prop/" + slot + " - " + prefab.name;
                Physics.SyncTransforms();
                // A ray cast after instantiation usually hits the prop's own collider.
                // Query the active terrain directly, then lower its actual colliders to it.
                foreach (var terrain in Terrain.activeTerrains)
                {
                    if (!terrain || !terrain.enabled) continue;
                    var terrainCollider = terrain.GetComponent<TerrainCollider>();
                    if (!terrainCollider || !terrainCollider.enabled) continue;
                    var origin = terrain.transform.position;
                    var size = terrain.terrainData.size;
                    if (world.x < origin.x || world.x > origin.x + size.x ||
                        world.z < origin.z || world.z > origin.z + size.z) continue;
                    float ground = terrain.SampleHeight(world) + origin.y;
                    var colliders = instance.GetComponentsInChildren<Collider>();
                    float bottom = float.PositiveInfinity;
                    foreach (var collider in colliders)
                        if (collider.enabled && !collider.isTrigger) bottom = Mathf.Min(bottom, collider.bounds.min.y);
                    instance.transform.position += Vector3.up * (ground - (float.IsPositiveInfinity(bottom) ? world.y : bottom));
                    break;
                }
                string id = poi.poiId + "/prop/" + slot++;
                var physical = instance.GetComponent<PhysicalProp>();
                if (physical) physical.AssignId(id);
                var container = instance.GetComponent<PropContainer>();
                if (container) container.Initialize(id);
                foreach (var socket in instance.GetComponentsInChildren<PropRackSocket>()) socket.Initialize(id + "/socket/" + socket.transform.GetSiblingIndex());
                instances.Add(instance);
                footprints.Add(local);
                return;
            }
            Debug.LogWarning("No clear prop socket for " + prefab.name + " at " + area.category, poi);
        }
        bool Clear(Vector2 point, float radius)
        {
            foreach (var r in poi.Layout.reservations)
                if ((r.kind == ReservationKind.Path || r.kind == ReservationKind.Building ||
                     r.kind == ReservationKind.Wall || r.kind == ReservationKind.Gate ||
                     r.kind == ReservationKind.Tower || r.kind == ReservationKind.Approach) &&
                    r.Contains(point, radius)) return false;
            foreach (var other in footprints) if (Vector2.Distance(other, point) < radius + .8f) return false;
            return true;
        }
    }
}
