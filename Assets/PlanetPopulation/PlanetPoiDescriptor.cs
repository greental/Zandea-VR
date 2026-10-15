using UnityEngine;
using ScavengerNPC;

namespace MyAssets.PlanetTerrain
{
    public readonly struct PlanetPoiDescriptor
    {
        public readonly double x, z;
        public readonly uint seed;
        public readonly OutpostTier tier;
        public PlanetPoiDescriptor(double x, double z, uint seed, OutpostTier tier)
        { this.x = x; this.z = z; this.seed = seed; this.tier = tier; }
    }

}
