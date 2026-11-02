using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.POI;

namespace Zandea.Wildlife
{
    // Settlement footprints are evaluated only when a wildlife spawn is attempted.
    // Wall bounds are cached in settlement-local coordinates so floating-origin shifts
    // move the exclusion with the authored settlement.
    public static class WildlifeSettlementExclusion
    {
        public const float DefaultOutsideWallMargin = 20f;
        sealed class WallFootprint
        {
            public Transform root;
            public Vector2 min, max;
        }
        static readonly Dictionary<int, WallFootprint> WallCache = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => WallCache.Clear();

        public static bool IsExcluded(Vector3 worldPoint, float outsideWallMargin = DefaultOutsideWallMargin)
        {
            outsideWallMargin = Mathf.Max(0f, outsideWallMargin);
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!root || !root.name.Contains("Settlement", StringComparison.OrdinalIgnoreCase)) continue;
                    var footprint = GetFootprint(root.transform);
                    if (footprint != null && Contains(footprint, worldPoint, outsideWallMargin)) return true;
                }
            }
            foreach (var poi in UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!poi || !poi.definition || !poi.treadmill || !poi.siteSelected) continue;
                var layout = poi.transform.Find("Layout Preview");
                var footprint = layout ? GetFootprint(layout) : null;
                if (footprint != null)
                {
                    if (Contains(footprint, worldPoint, outsideWallMargin)) return true;
                    continue;
                }
                Vector3 local = Quaternion.Inverse(poi.Rotation) * (worldPoint - poi.Center);
                Vector2 half = poi.definition.dimensions * .5f + Vector2.one * outsideWallMargin;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y) return true;
            }
            return false;
        }

        static bool Contains(WallFootprint footprint, Vector3 worldPoint, float margin)
        {
            Vector3 local = footprint.root.InverseTransformPoint(worldPoint);
            return local.x >= footprint.min.x - margin && local.x <= footprint.max.x + margin &&
                   local.z >= footprint.min.y - margin && local.z <= footprint.max.y + margin;
        }

        static WallFootprint GetFootprint(Transform root)
        {
            int id = root.GetInstanceID();
            if (WallCache.TryGetValue(id, out var cached) && cached.root == root) return cached;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            int walls = 0;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("Wall", StringComparison.OrdinalIgnoreCase) &&
                    !child.name.StartsWith("Gate", StringComparison.OrdinalIgnoreCase)) continue;
                var renderer = child.GetComponent<Renderer>();
                if (renderer)
                {
                    Bounds bounds = renderer.bounds;
                    for (int x = 0; x < 2; x++)
                        for (int z = 0; z < 2; z++)
                        {
                            var corner = root.InverseTransformPoint(new Vector3(
                                x == 0 ? bounds.min.x : bounds.max.x, bounds.center.y,
                                z == 0 ? bounds.min.z : bounds.max.z));
                            min = Vector2.Min(min, new Vector2(corner.x, corner.z));
                            max = Vector2.Max(max, new Vector2(corner.x, corner.z));
                        }
                }
                else
                {
                    var local = root.InverseTransformPoint(child.position);
                    min = Vector2.Min(min, new Vector2(local.x, local.z));
                    max = Vector2.Max(max, new Vector2(local.x, local.z));
                }
                walls++;
            }
            if (walls < 4) return null;
            var footprint = new WallFootprint { root = root, min = min, max = max };
            WallCache[id] = footprint;
            return footprint;
        }
    }
}
