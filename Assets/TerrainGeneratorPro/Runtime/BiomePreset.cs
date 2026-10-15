using UnityEngine;

namespace TerrainGeneratorPro
{
    /// <summary>
    /// Authoring asset defining macro/micro terrain character and landform balance.
    /// Create via Assets → Create → Terrain Generator Pro → Biome Preset.
    /// </summary>
    [CreateAssetMenu(fileName = "BiomePreset", menuName = "Terrain Generator Pro/Biome Preset", order = 0)]
    public class BiomePreset : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Short label shown in UI lists.")]
        public string displayName = "New Biome";

        [TextArea(2, 4)]
        public string description;

        [Header("Elevation (Unity terrain heightmap is 0–1)")]
        [Tooltip("Normalized minimum height after shaping.")]
        [Range(0f, 1f)] public float heightFloor = 0.08f;

        [Tooltip("Normalized maximum height after shaping.")]
        [Range(0f, 1f)] public float heightCeiling = 0.92f;

        [Header("Noise scales (world units⁻¹ style; higher = finer detail)")]
        [Tooltip("Large-scale continental / basin structure.")]
        public float macroNoiseScale = 0.004f;

        [Tooltip("Fine surface grain and ripples.")]
        public float microNoiseScale = 0.12f;

        [Header("Feature shaping")]
        [Tooltip("How sharp transitions are for mesas and scarps.")]
        [Range(0.05f, 1f)] public float cliffSharpness = 0.55f;

        [Tooltip("How often mesa / plateau caps appear.")]
        [Range(0f, 2f)] public float mesaFrequency = 1f;

        [Tooltip("Thermal-style breakup and talus redistribution strength.")]
        [Range(0f, 1f)] public float erosionStrength = 0.35f;

        [Tooltip("Depth and spread of enclosed lows.")]
        [Range(0f, 2f)] public float basinFrequency = 1f;

        [Tooltip("Directional dune-like undulation.")]
        [Range(0f, 2f)] public float duneIntensity = 0.6f;

        [Tooltip("Post-pass smoothing (reduces high-frequency spikes).")]
        [Range(0f, 1f)] public float terrainSmoothing = 0.15f;

        [Header("Landform mix")]
        public LandformBalance landformBalance = LandformBalance.Balanced;

        /// <summary>Runtime copy for duplicate / compare workflows.</summary>
        public BiomePreset CloneRuntime()
        {
            var copy = CreateInstance<BiomePreset>();
            copy.name = name + " (Copy)";
            copy.displayName = displayName + " (Copy)";
            copy.description = description;
            copy.heightFloor = heightFloor;
            copy.heightCeiling = heightCeiling;
            copy.macroNoiseScale = macroNoiseScale;
            copy.microNoiseScale = microNoiseScale;
            copy.cliffSharpness = cliffSharpness;
            copy.mesaFrequency = mesaFrequency;
            copy.erosionStrength = erosionStrength;
            copy.basinFrequency = basinFrequency;
            copy.duneIntensity = duneIntensity;
            copy.terrainSmoothing = terrainSmoothing;
            copy.landformBalance = landformBalance;
            return copy;
        }
    }
}
