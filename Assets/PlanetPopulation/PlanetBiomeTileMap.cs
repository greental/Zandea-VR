using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    // Persistent authoring data, independent of the terrain's eventual paint layers.
    public sealed class PlanetBiomeTileMap : ScriptableObject
    {
        public PlanetTileCoord coord;
        public PlanetBiomeProfile[] biomes;
        public int resolution;
        public float[] weights; // [z, x, biome], including shared tile endpoints.

        public void Sample(float u, float v, float[] result)
        {
            float x = Mathf.Clamp01(u) * (resolution - 1), z = Mathf.Clamp01(v) * (resolution - 1);
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, resolution - 1), z1 = Mathf.Min(z0 + 1, resolution - 1);
            for (int b = 0; b < biomes.Length; b++)
            {
                float Read(int px, int pz) => weights[(pz * resolution + px) * biomes.Length + b];
                result[b] = Mathf.Lerp(Mathf.Lerp(Read(x0, z0), Read(x1, z0), x - x0),
                    Mathf.Lerp(Read(x0, z1), Read(x1, z1), x - x0), z - z0);
            }
        }
    }
}
