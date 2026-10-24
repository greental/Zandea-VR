using System;
using UnityEngine;
using KenshiVR.MeleeCombat;

namespace Zandea.POI
{
    [Serializable]
    public sealed class OutpostAsset
    {
        public GameObject prefab;
        public Vector3 size;
        public Vector3 center;
        public float entranceYaw;
    }

    [CreateAssetMenu(menuName = "Zandea/Bandit Outpost")]
    public sealed class BanditOutpostDefinition : ScriptableObject
    {
        public int seed = 4301;
        [Range(4, 5)] public int buildingCount = 5;
        [Range(0, 6)] public int residentBandits = 6;
        public bool useAllBuildingVariants;
        public bool useAllShadeVariants;
        public const int ShadeCount = 6, BanditCount = 6;
        public Vector2 dimensions = new Vector2(88, 80);
        public OutpostAsset mainBuilding;
        public OutpostAsset[] buildings, shades;
        public OutpostAsset wall, gate, tower;
        public OutpostAsset[] wallVariants;
        public StarvingBandit bandit;
        [Range(0, 3)] public int gateQuarterTurns;
        [Min(1)] public float pathWidth = 5;
        [Min(1)] public float approachLength = 24;
        [Min(1)] public float blendDistance = 24;
        [Min(0)] public float maximumHeightRange = 5;
        [Range(0, 45)] public float maximumSiteSlope = 12;
        [Min(1)] public float loadDistance = 280, unloadDistance = 380;
        public float patrolWalkSpeed = 1.5f;

        public void Validate()
        {
            if (buildingCount < 4 || buildingCount > 5 || (residentBandits != 0 && residentBandits != 6) ||
                dimensions.x < 84 || dimensions.y < 78 ||
                pathWidth < 3 || pathWidth > 6 || unloadDistance <= loadDistance || blendDistance < 4)
                throw new InvalidOperationException("Outpost needs 4–5 buildings, at least 84 x 78 m, a 3–6 m path, blending and streaming hysteresis.");
            Check(mainBuilding); Check(wall); Check(gate); Check(tower);
            if (buildings == null || buildings.Length == 0 || shades == null || shades.Length == 0 || !bandit)
                throw new InvalidOperationException("Outpost assets/bandit missing.");
            foreach (var item in buildings) Check(item);
            foreach (var item in shades) Check(item);
            if (wallVariants != null) foreach (var item in wallVariants) Check(item);
        }
        static void Check(OutpostAsset item)
        {
            if (item == null || !item.prefab || item.size.x <= 0 || item.size.z <= 0)
                throw new InvalidOperationException("Assign a prefab and measure its bounds in the outpost editor.");
        }
    }
}
