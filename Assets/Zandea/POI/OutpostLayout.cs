using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zandea.POI
{
    public enum ReservationKind { Building, Shade, Wall, Gate, Path, Tower, FutureProp, Approach }
    [Serializable]
    public struct OutpostReservation
    {
        public ReservationKind kind;
        public string category;
        public Vector2 center, size;
        public float yaw;
        public bool Contains(Vector2 point, float radius = 0)
        {
            var p = Quaternion.Euler(0, -yaw, 0) * new Vector3(point.x - center.x, 0, point.y - center.y);
            return Mathf.Abs(p.x) <= size.x * .5f + radius && Mathf.Abs(p.z) <= size.y * .5f + radius;
        }
    }
    public sealed class OutpostLayout
    {
        public sealed class Placement
        {
            public OutpostAsset asset;
            public ReservationKind kind;
            public Vector2 position;
            public float yaw;
            public Vector3 scale = Vector3.one;
            public Vector2 Footprint
            {
                get
                {
                    float a = yaw * Mathf.Deg2Rad;
                    float x = asset.size.x * scale.x, z = asset.size.z * scale.z;
                    return new Vector2(Mathf.Abs(Mathf.Cos(a)) * x + Mathf.Abs(Mathf.Sin(a)) * z,
                        Mathf.Abs(Mathf.Sin(a)) * x + Mathf.Abs(Mathf.Cos(a)) * z);
                }
            }
        }
        public readonly List<Placement> structures = new List<Placement>();
        public readonly List<OutpostReservation> reservations = new List<OutpostReservation>();
        public readonly List<Vector2> spawns = new List<Vector2>();
        public readonly List<Vector2> patrol = new List<Vector2>();
        public Vector2 gate;
        public float rotation;

        public static OutpostLayout Generate(BanditOutpostDefinition d, int seed)
        {
            d.Validate();
            // Koombar's courtyard composition, rebuilt as bounded functional zones.
            string error = null;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var l = Compose(d, new System.Random(unchecked(seed + attempt * 7919)), attempt == 15);
                if (l.Validate(d, out error)) return l;
            }
            throw new InvalidOperationException("No complete clear outpost fits these asset bounds: " + error);
        }
        static OutpostLayout Compose(BanditOutpostDefinition d, System.Random rng, bool fallback)
        {
            var l = new OutpostLayout { rotation = d.gateQuarterTurns * 90, gate = new Vector2(0, -d.dimensions.y / 2) };
            float x = d.dimensions.x / 2, z = d.dimensions.y / 2;
            float J() => fallback ? 0 : (float)(rng.NextDouble() - .5) * 1.4f;
            OutpostAsset Pick(OutpostAsset[] a) => a[fallback ? 0 : rng.Next(a.Length)];
            OutpostAsset[] Order(OutpostAsset[] source)
            {
                var result = (OutpostAsset[])source.Clone();
                if (!fallback)
                    for (int i = result.Length - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        var swap = result[i]; result[i] = result[j]; result[j] = swap;
                    }
                return result;
            }
            var buildingOrder = d.useAllBuildingVariants ? Order(d.buildings) : null;
            var shadeOrder = d.useAllShadeVariants ? Order(d.shades) : null;
            int buildingIndex = 0, shadeIndex = 0;
            OutpostAsset PickBuilding() => buildingOrder == null ? Pick(d.buildings) : buildingOrder[buildingIndex++ % buildingOrder.Length];
            OutpostAsset PickShade() => shadeOrder == null ? Pick(d.shades) : shadeOrder[shadeIndex++ % shadeOrder.Length];
            l.Reserve(ReservationKind.Path, "Gate to courtyard", new Vector2(0, -z / 2), new Vector2(d.pathWidth, z));
            l.Reserve(ReservationKind.Path, "Combat courtyard", Vector2.zero, new Vector2(23, 22));
            l.Reserve(ReservationKind.Path, "Building access", new Vector2(0, 17), new Vector2(59, d.pathWidth));
            l.Reserve(ReservationKind.Approach, "Future road connection", l.gate - Vector2.up * d.approachLength / 2, new Vector2(d.pathWidth, d.approachLength));
            int side = fallback || rng.Next(2) == 0 ? 1 : -1;
            l.Add(d.tower, ReservationKind.Tower, side * (x - 9), -z + 10, 0);
            l.Add(d.mainBuilding, ReservationKind.Building, J(), z - 11.25f, 180 + d.mainBuilding.entranceYaw);
            var west = PickBuilding();
            var east = PickBuilding();
            var northWest = PickBuilding();
            l.Add(west, ReservationKind.Building, -x + 11 + J(), 7 + J(), 90 + west.entranceYaw);
            l.Add(east, ReservationKind.Building, x - 11 + J(), 7 + J(), -90 + east.entranceYaw);
            l.Add(northWest, ReservationKind.Building, -x + 11 + J(), z - 10 + J(), 180 + northWest.entranceYaw);
            if (d.buildingCount == 5)
            {
                var northEast = PickBuilding();
                l.Add(northEast, ReservationKind.Building, x - 11 + J(), z - 10 + J(), 180 + northEast.entranceYaw);
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -5f, -18f, -31f })
                    l.Add(PickShade(), ReservationKind.Shade, sx * (18 + (sz == -18 ? 3 : sz == -31 ? -1 : 0) + J()), sz + J(), fallback ? 0 : J() * 16);
            l.Add(d.gate, ReservationKind.Gate, 0, -z, 0);
            // Fit each run to measured module widths, with only a small joint overlap.
            l.WallRun(d.wall, d.wallVariants, rng, new Vector2(-x, z), new Vector2(x, z));
            l.WallRun(d.wall, d.wallVariants, rng, new Vector2(-x, -z), new Vector2(-x, z));
            l.WallRun(d.wall, d.wallVariants, rng, new Vector2(x, -z), new Vector2(x, z));
            l.WallRun(d.wall, d.wallVariants, rng, new Vector2(-x, -z), new Vector2(-d.gate.size.x / 2, -z));
            l.WallRun(d.wall, d.wallVariants, rng, new Vector2(d.gate.size.x / 2, -z), new Vector2(x, -z));
            l.spawns.AddRange(new[] { new Vector2(-2, -z + 7), new Vector2(2, -z + 7),
                new Vector2(-7, 1), new Vector2(7, 5), new Vector2(-10, -18), new Vector2(side * (x - 9), -z + 18) });
            l.patrol.AddRange(new[] { new Vector2(0, -z + 6), new Vector2(0, -16), new Vector2(-7, 0), new Vector2(7, 7), new Vector2(0, 17) });
            l.Reserve(ReservationKind.FutureProp, "Storage", new Vector2(-side * (x - 9), -z + 11), new Vector2(7, 8));
            l.Reserve(ReservationKind.FutureProp, "Courtyard social", new Vector2(15, -11), new Vector2(5, 4));
            l.Reserve(ReservationKind.FutureProp, "Guard post / future ladder", new Vector2(side * (x - 9), -z + 15), new Vector2(3, 2));
            l.Reserve(ReservationKind.FutureProp, "Perimeter clutter", new Vector2(-x + 3, -5), new Vector2(2, 4));
            return l;
        }
        void Add(OutpostAsset a, ReservationKind k, float x, float z, float yaw, Vector3? scale = null)
        {
            var p = new Placement { asset = a, kind = k, position = new Vector2(x, z), yaw = yaw, scale = scale ?? Vector3.one };
            structures.Add(p);
            Reserve(k, a.prefab.name, p.position, new Vector2(a.size.x * p.scale.x, a.size.z * p.scale.z), yaw);
            if (k == ReservationKind.Shade)
                Reserve(ReservationKind.FutureProp, "Under canopy", p.position,
                    new Vector2(Mathf.Min(6, a.size.x - 2), Mathf.Min(4, a.size.z - 2)), yaw);
            if (k == ReservationKind.Building)
            {
                var forward = Quaternion.Euler(0, yaw - a.entranceYaw, 0) * Vector3.forward;
                var entrance = p.position + new Vector2(forward.x, forward.z) * (a.size.z / 2 + 2);
                Reserve(ReservationKind.Path, "Door clearance", entrance, new Vector2(4, 3), yaw);
                Reserve(ReservationKind.FutureProp, "Entrance side", entrance + new Vector2(forward.z, -forward.x) * 3, new Vector2(2, 2), yaw);
            }
        }
        void WallRun(OutpostAsset asset, OutpostAsset[] variants, System.Random rng, Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / asset.size.x));
            float step = length / count;
            float yaw = -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            var order = variants != null && variants.Length > 0 ? (OutpostAsset[])variants.Clone() : null;
            for (int i = 0; i < count; i++)
            {
                var p = Vector2.Lerp(a, b, (i + .5f) / count);
                if (order != null && i % order.Length == 0)
                    for (int j = order.Length - 1; j > 0; j--)
                    {
                        int swapIndex = rng.Next(j + 1);
                        var swap = order[j]; order[j] = order[swapIndex]; order[swapIndex] = swap;
                    }
                var module = order != null ? order[i % order.Length] : asset;
                float moduleYaw = order != null && rng.Next(2) == 0 ? yaw + 180 : yaw;
                Add(module, ReservationKind.Wall, p.x, p.y, moduleYaw,
                    new Vector3((step + .04f) / module.size.x, 1, 1));
            }
        }
        void Reserve(ReservationKind k, string name, Vector2 p, Vector2 size, float yaw = 0) =>
            reservations.Add(new OutpostReservation { kind = k, category = name, center = p, size = size, yaw = yaw });
        public bool Validate(BanditOutpostDefinition d, out string error)
        {
            error = null;
            if (structures.FindAll(p => p.kind == ReservationKind.Building).Count != d.buildingCount ||
                structures.FindAll(p => p.kind == ReservationKind.Shade).Count != 6 || spawns.Count != 6)
                error = "Required counts missing";
            for (int i = 0; i < structures.Count; i++)
            {
                var a = structures[i];
                if (a.kind == ReservationKind.Wall || a.kind == ReservationKind.Gate) continue;
                var ext = a.Footprint / 2;
                if (Mathf.Abs(a.position.x) + ext.x > d.dimensions.x / 2 - 2 || Mathf.Abs(a.position.y) + ext.y > d.dimensions.y / 2 - 2) error = "Perimeter clearance";
                for (int j = i + 1; j < structures.Count; j++)
                {
                    var b = structures[j];
                    if (b.kind == ReservationKind.Wall || b.kind == ReservationKind.Gate) continue;
                    var gap = (a.Footprint + b.Footprint) / 2 + Vector2.one;
                    if (Mathf.Abs(a.position.x - b.position.x) < gap.x && Mathf.Abs(a.position.y - b.position.y) < gap.y) error = "Structure overlap";
                }
                foreach (var r in reservations)
                {
                    if (r.kind != ReservationKind.Path) continue;
                    float angle = r.yaw * Mathf.Deg2Rad;
                    var size = new Vector2(Mathf.Abs(Mathf.Cos(angle)) * r.size.x + Mathf.Abs(Mathf.Sin(angle)) * r.size.y,
                        Mathf.Abs(Mathf.Sin(angle)) * r.size.x + Mathf.Abs(Mathf.Cos(angle)) * r.size.y);
                    if (Mathf.Abs(r.center.x - a.position.x) < (size.x + a.Footprint.x) / 2 &&
                        Mathf.Abs(r.center.y - a.position.y) < (size.y + a.Footprint.y) / 2)
                        error = "Blocked path '" + r.category + "' by " + a.asset.prefab.name +
                            " at " + a.position + " (footprint " + a.Footprint + ")";
                }
                foreach (var p in spawns)
                    if (Mathf.Abs(p.x - a.position.x) < ext.x + .6f && Mathf.Abs(p.y - a.position.y) < ext.y + .6f) error = "Blocked NPC spawn";
            }
            return error == null;
        }
    }
}
