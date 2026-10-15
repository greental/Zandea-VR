namespace TerrainGeneratorPro
{
    /// <summary>
    /// Identifiers for optional procedural masks saved as textures for downstream tools
    /// (painters, scatter, biome systems). Values are stable for serialization.
    /// </summary>
    public enum MaskChannelId
    {
        Slope = 0,
        Height = 1,
        Cliffs = 2,
        Flats = 3,
        ErosionFlow = 4,
        VegetationSuitability = 5,
        RockSuitability = 6
    }
}
