using UnityEngine;
using ScavengerNPC;

namespace MyAssets.PlanetTerrain
{
    public enum PlanetBiomeTerrainShape
    {
        Legacy,
        SourceTerrain,
        MoonCraters,
        RollingHills,
        DuneFields,
        RockyHighlands,
        OpenPlains,
        SaltFlats,
        CombDesert,
        DesertBadlands,
        Karst
    }

    [CreateAssetMenu(menuName = "Koombar/Planet/Biome Profile")]
    public sealed class PlanetBiomeProfile : ScriptableObject
    {
        public string identifier = "Grasslands";
        public TerrainLayer[] terrainLayers;
        [Tooltip("Independent height shape for this biome. Legacy preserves the existing canyon cracks.")]
        public PlanetBiomeTerrainShape terrainShape;
        [Tooltip("The existing planet or moon height profile, copied into this biome asset.")]
        public PlanetHeightProvider sourceTerrain = new PlanetHeightProvider();
        [Min(0)] public float sourceReliefMeters = 1100f;
        [Min(50)] public float broadScale = 1800;
        [Min(5)] public float detailScale = 180;
        [Min(0)] public float reliefMeters = 35;
        [Min(0)] public float detailMeters = 4;
        [Range(0, 1)] public float ridgeStrength;
        [Range(0, 1)] public float terraceStrength;
        [Range(0, 1)] public float duneStrength;
        [Range(0, 1)] public float canyonStrength;
    }

    public enum OutpostTier { Small, Medium, Large }

}
