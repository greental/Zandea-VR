using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "BiomePreset",
    menuName = "Smart Terrain Painter/Biome Preset",
    order = 3000)]
public class BiomePreset : ScriptableObject
{
    [Header("Identity")]
    public string biomeName = "New Biome";

    [TextArea]
    public string description;

    [Header("Global Settings")]
    [Min(0.0001f)]
    public float noiseScale = 0.015f;

    [Range(0f, 2f)]
    public float globalNoiseStrength = 0.35f;

    [Range(0f, 1f)]
    public float blendSmoothness = 0.25f;

    [Range(0f, 2f)]
    public float heightInfluence = 1f;

    [Range(0f, 2f)]
    public float slopeInfluence = 1f;

    public bool canyonCliffBoost = false;

    [Header("Surface Override")]
    [Tooltip("When enabled, painting enforces dry-looking terrain layers (low/zero smoothness). Disable for wet/lush biomes.")]
    public bool enforceDrySurface = true;

    [Range(0f, 1f)]
    [Tooltip("Smoothness value applied to terrain layers when dry surface override is enabled.")]
    public float drySurfaceSmoothness = 0f;

    [Header("Rules")]
    [Tooltip("3 to 5 rules per biome is recommended for practical iteration.")]
    public List<BiomeRule> rules = new List<BiomeRule>();
}
