using System;
using UnityEngine;

[Serializable]
public class BiomeRule
{
    [Tooltip("Friendly label for readability in the preset.")]
    public string ruleName = "Rule";

    [Tooltip("Used for quick auto-matching against TerrainLayer names.")]
    public string layerHint = string.Empty;

    [Tooltip("Terrain layer painted when this rule is dominant.")]
    public TerrainLayer terrainLayer;

    [Range(0f, 1f)]
    [Tooltip("Minimum normalized height (0 = terrain min, 1 = terrain max).")]
    public float minHeight = 0f;

    [Range(0f, 1f)]
    [Tooltip("Maximum normalized height (0 = terrain min, 1 = terrain max).")]
    public float maxHeight = 1f;

    [Range(0f, 90f)]
    [Tooltip("Minimum slope in degrees.")]
    public float minSlope = 0f;

    [Range(0f, 90f)]
    [Tooltip("Maximum slope in degrees.")]
    public float maxSlope = 90f;

    [Range(0f, 2f)]
    [Tooltip("Rule-specific noise influence (multiplies global noise strength).")]
    public float noiseStrength = 0.25f;

    [Min(0f)]
    [Tooltip("Relative contribution of this rule before normalization.")]
    public float blendStrength = 1f;

    public bool IsLayerAssigned => terrainLayer != null;
}
