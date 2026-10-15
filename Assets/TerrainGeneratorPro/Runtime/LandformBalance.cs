using System;
using UnityEngine;

namespace TerrainGeneratorPro
{
    /// <summary>
    /// Relative weights for major landform passes. Values are normalized at generation time.
    /// </summary>
    [Serializable]
    public struct LandformBalance
    {
        [Tooltip("Flat-to-rolling open ground suitable for traversal.")]
        [Range(0f, 2f)] public float openPlains;

        [Tooltip("Isolated hills and rolling dunes.")]
        [Range(0f, 2f)] public float hillsAndDunes;

        [Tooltip("Table-top mesas and stepped plateaus.")]
        [Range(0f, 2f)] public float mesasPlateaus;

        [Tooltip("Sharp breaks, scarps, and vertical relief.")]
        [Range(0f, 2f)] public float cliffsScarps;

        [Tooltip("Closed or open low basins and playas.")]
        [Range(0f, 2f)] public float basins;

        [Tooltip("Linear ridges and hogbacks.")]
        [Range(0f, 2f)] public float ridges;

        [Tooltip("Incised dry channels and arroyos.")]
        [Range(0f, 2f)] public float dryRiverbeds;

        public static LandformBalance Balanced => new LandformBalance
        {
            openPlains = 1f,
            hillsAndDunes = 1f,
            mesasPlateaus = 1f,
            cliffsScarps = 1f,
            basins = 1f,
            ridges = 1f,
            dryRiverbeds = 0.75f
        };
    }
}
