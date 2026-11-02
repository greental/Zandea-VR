using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    // Blueprint coordinates are metres relative to the plot centre; no prefab objects are created while planning.
    public static class ModularBuildingGenerator
    {
        public struct Plot
        {
            public Vector3 center;
            public int maximumWidth, maximumDepth;
            public Vector3 roadPosition;
            public int seed;
            public bool sharedLeft, sharedRight, sharedBack;
        }
        public struct Piece
        {
            public ModularBuildingCatalog.Kind kind;
            public bool upper;
            public int length;
            public Vector3 center;
            public Vector3 outward;
        }
        public struct Roof
        {
            public Vector3 center;
            public Vector2 size;
            public float baseHeight;
        }
        public sealed class Blueprint
        {
            public int width, depth, floors;
            public Vector3 center, entrance, entranceOutward;
            public Vector2 staircaseReserve;
            public readonly List<Piece> walls = new List<Piece>();
            public readonly List<Roof> roofs = new List<Roof>();
        }

        public static bool TryPlan(Plot plot, ModularBuildingCatalog catalog, Func<Vector3, float?> height,
            out Blueprint blueprint, out string warning)
        {
            blueprint = null;
            warning = null;
            if (!catalog) { warning = "No modular building catalog assigned."; return false; }
            var random = new System.Random(unchecked(plot.seed ^ catalog.seed));
            int min = Mathf.CeilToInt(Mathf.Max(8, catalog.minimumSize) / 4f) * 4;
            int maxW = Mathf.FloorToInt(Mathf.Min(16, catalog.maximumSize, plot.maximumWidth) / 4f) * 4;
            int maxD = Mathf.FloorToInt(Mathf.Min(16, catalog.maximumSize, plot.maximumDepth) / 4f) * 4;
            if (maxW < min || maxD < min) { warning = "Plot cannot fit a minimum 8 x 8 m building."; return false; }
            int width = min + random.Next((maxW - min) / 4 + 1) * 4;
            int depth = min + random.Next((maxD - min) / 4 + 1) * 4;
            float low = float.PositiveInfinity, high = float.NegativeInfinity, maxSlope = 0;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                Vector3 point = plot.center + new Vector3(x * width * .5f, 0, z * depth * .5f);
                float? y = height(point);
                if (!y.HasValue) { warning = "No terrain beneath the full plot."; return false; }
                low = Mathf.Min(low, y.Value); high = Mathf.Max(high, y.Value);
            }
            maxSlope = Mathf.Atan2(high - low, Mathf.Min(width, depth)) * Mathf.Rad2Deg;
            if (high - low > catalog.maximumTerrainVariation || maxSlope > catalog.maximumTerrainSlope)
            { warning = "Terrain height variation or slope exceeds catalog tolerance."; return false; }
            var b = new Blueprint { width = width, depth = depth, center = new Vector3(plot.center.x, (low + high) * .5f, plot.center.z), floors = random.NextDouble() < catalog.twoStoryProbability ? 2 : 1 };
            Vector3 road = plot.roadPosition - b.center;
            Vector3 face = Mathf.Abs(road.x / width) > Mathf.Abs(road.z / depth) ?
                (road.x >= 0 ? Vector3.right : Vector3.left) : (road.z >= 0 ? Vector3.forward : Vector3.back);
            b.entranceOutward = face;
            b.entrance = b.center + new Vector3(face.x * width * .5f, 0, face.z * depth * .5f);
            b.staircaseReserve = new Vector2(3, 3);
            if (!AddPerimeter(b, catalog, random, width, depth, Vector3.zero, false, face, plot, out warning)) return false;
            if (b.floors == 2)
            {
                int upperWidth = width > 8 && random.NextDouble() < .5 ? width - 4 : width;
                int upperDepth = depth > 8 && random.NextDouble() < .5 ? depth - 4 : depth;
                // A centered, contained upper floor is supported by the ground footprint.
                if (!AddPerimeter(b, catalog, random, upperWidth, upperDepth, Vector3.up * 2.3f, true, Vector3.zero, plot, out warning)) return false;
                b.roofs.Add(new Roof { center = Vector3.up * 4.6f, size = new Vector2(upperWidth, upperDepth), baseHeight = 4.6f });
                // A complete lower roof is deliberately rejected until exposed strips can be roofed without crossing upper walls.
                if (upperWidth != width || upperDepth != depth)
                {
                    b.walls.RemoveRange(b.walls.FindIndex(p => p.upper), b.walls.Count - b.walls.FindIndex(p => p.upper));
                    b.roofs.Clear(); b.floors = 1;
                }
            }
            if (b.roofs.Count == 0) b.roofs.Add(new Roof { center = Vector3.up * (b.floors * 2.3f), size = new Vector2(width, depth), baseHeight = b.floors * 2.3f });
            if (!catalog.Has(ModularBuildingCatalog.Kind.RoofSlope, false, 0)) { warning = "Catalog lacks an approved single-slope roof prefab with measured run, rise, ridge axis, and eave point."; return false; }
            foreach (var module in catalog.modules)
                if (module != null && module.prefab && module.kind == ModularBuildingCatalog.Kind.RoofSlope && module.weight > 0 &&
                    (module.roofHorizontalRun <= 0 || module.roofRidgeLength <= 0 || module.roofRise <= 0 ||
                     Vector3.Dot(module.roofRidgeAxis.normalized, Vector3.right) < .99f ||
                     Vector3.Dot(module.roofRunAxis.normalized, Vector3.forward) < .99f))
                { warning = $"Roof module {module.prefab.name} has invalid dimensions or unsupported ridge/run axes."; return false; }
            blueprint = b; return true;
        }

        static bool AddPerimeter(Blueprint b, ModularBuildingCatalog catalog, System.Random random, int width, int depth,
            Vector3 offset, bool upper, Vector3 entranceFace, Plot plot, out string warning)
        {
            warning = null;
            Vector3[] faces = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            int windows = random.Next(catalog.minimumWindows, catalog.maximumWindows + 1);
            foreach (Vector3 face in faces)
            {
                int span = face.x != 0 ? depth : width;
                bool entranceSide = !upper && face == entranceFace;
                bool shared = !entranceSide && ((face == Vector3.left && plot.sharedLeft) ||
                    (face == Vector3.right && plot.sharedRight) || (face == Vector3.back && plot.sharedBack));
                // Shared boundaries require a neighbor plan. Until that exists, retain this wall as the sole shell boundary.
                int cursor = 0;
                while (cursor < span)
                {
                    int length = span - cursor >= 8 && catalog.Has(ModularBuildingCatalog.Kind.SolidWall, upper, 8) ? 8 : 4;
                    if (!catalog.Has(ModularBuildingCatalog.Kind.SolidWall, upper, length))
                    { warning = $"Missing {(upper ? "framed" : "ground")} solid wall {length} m module."; return false; }
                    Vector3 tangent = face.x != 0 ? Vector3.forward : Vector3.right;
                    Vector3 position = offset + face * (face.x != 0 ? width * .5f : depth * .5f) + tangent * (cursor + length * .5f - span * .5f);
                    var kind = ModularBuildingCatalog.Kind.SolidWall;
                    if (entranceSide && cursor == 0)
                    {
                        if (!catalog.Has(ModularBuildingCatalog.Kind.DoorWall, false, length))
                        { warning = $"Missing ground-floor door wall {length} m module."; return false; }
                        kind = ModularBuildingCatalog.Kind.DoorWall;
                        b.entrance = b.center + position;
                    }
                    else if (windows > 0 && !shared && catalog.Has(ModularBuildingCatalog.Kind.WindowWall, upper, length) && random.NextDouble() < .5)
                    { kind = ModularBuildingCatalog.Kind.WindowWall; windows--; }
                    b.walls.Add(new Piece { kind = kind, upper = upper, length = length, center = position, outward = face });
                    cursor += length;
                }
            }
            return true;
        }

        public static GameObject Instantiate(Blueprint b, ModularBuildingCatalog catalog, int seed, Transform parent)
        {
            var random = new System.Random(unchecked(seed ^ catalog.seed));
            var root = new GameObject($"Modular Building {b.width}x{b.depth} ({b.floors} floors)");
            root.transform.SetParent(parent, false); root.transform.position = b.center;
            foreach (var piece in b.walls)
            {
                var module = catalog.Pick(piece.kind, piece.upper, piece.length, random);
                if (module == null) { UnityEngine.Object.Destroy(root); return null; }
                Quaternion authored = Quaternion.LookRotation(module.outwardAxis, module.upAxis);
                Quaternion desired = Quaternion.LookRotation(piece.outward, Vector3.up);
                Quaternion rotation = desired * Quaternion.Inverse(authored);
                Vector3 position = piece.center - rotation * module.pivotToCenter;
                UnityEngine.Object.Instantiate(module.prefab, root.transform.TransformPoint(position), rotation, root.transform);
            }
            foreach (var roof in b.roofs)
            {
                var module = catalog.Pick(ModularBuildingCatalog.Kind.RoofSlope, false, 0, random);
                if (module == null || module.roofHorizontalRun <= 0 || module.roofRidgeLength <= 0 || module.roofRise <= 0)
                { UnityEngine.Object.Destroy(root); return null; }
                float run = roof.size.y * .5f + catalog.roofOverhang;
                float ridgeLength = roof.size.x + catalog.roofOverhang * 2;
                Vector3 scale = new Vector3(ridgeLength / module.roofRidgeLength, 1, run / module.roofHorizontalRun);
                // The catalog axes make roof orientation explicit; scaling follows its measured ridge/run axes.
                if (Vector3.Dot(module.roofRidgeAxis.normalized, Vector3.right) < .99f ||
                    Vector3.Dot(module.roofRunAxis.normalized, Vector3.forward) < .99f)
                { Debug.LogWarning("Roof module requires local +X ridge and +Z eave-to-ridge axes for nonuniform scale."); UnityEngine.Object.Destroy(root); return null; }
                for (int side = -1; side <= 1; side += 2)
                {
                    Quaternion rotation = side == 1 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
                    Vector3 eave = roof.center + new Vector3(0, 0, side * run);
                    Vector3 local = eave - rotation * Vector3.Scale(module.roofEavePoint, scale);
                    var instance = UnityEngine.Object.Instantiate(module.prefab, root.transform.TransformPoint(local), rotation, root.transform);
                    instance.transform.localScale = Vector3.Scale(instance.transform.localScale, scale);
                }
            }
            return root;
        }
    }
}

