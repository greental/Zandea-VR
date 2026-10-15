using System;
using UnityEngine;
using MyAssets.PlanetTerrain;

namespace MyAssets.Planet
{
    public enum PlanetCubeFace
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    /// <summary>
    /// Cube-sphere style logical surface address. Keeps spherical entry addressing independent from flat Terrain rendering.
    /// </summary>
    [Serializable]
    public readonly struct PlanetSurfaceAddress : IEquatable<PlanetSurfaceAddress>
    {
        public readonly PlanetCubeFace face;
        public readonly Vector2 faceUv;
        public readonly PlanetTileCoord tile;

        public PlanetSurfaceAddress(PlanetCubeFace face, Vector2 faceUv, PlanetTileCoord tile)
        {
            this.face = face;
            this.faceUv = faceUv;
            this.tile = tile;
        }

        public bool Equals(PlanetSurfaceAddress other)
        {
            return face == other.face && tile.Equals(other.tile) && faceUv == other.faceUv;
        }

        public override bool Equals(object obj) => obj is PlanetSurfaceAddress other && Equals(other);
        public override int GetHashCode() => (((int)face * 397) ^ tile.GetHashCode()) * 397 ^ faceUv.GetHashCode();
        public override string ToString() => $"{face} uv=({faceUv.x:0.000}, {faceUv.y:0.000}) tile={tile}";
    }
}