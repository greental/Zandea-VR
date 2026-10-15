using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    [Serializable]
    public sealed class AuthoredTerrainTileRecord
    {
        public PlanetTileCoord coord;
        public TerrainData terrainData;
    }

    /// <summary>
    /// Authored terrain registry for a finite, optionally wrapped planet tile grid.
    /// Runtime should prefer these persistent TerrainData assets and avoid generating tiles during traversal.
    /// </summary>
    [CreateAssetMenu(fileName = "PlanetTerrainTileSet", menuName = "Koombar/Planet Terrain/Tile Set", order = 0)]
    public sealed class PlanetTerrainTileSet : ScriptableObject
    {
        [Min(1)] public int wrapWidth = 8;
        [Min(1)] public int wrapHeight = 8;
        public bool wrapCoordinates = true;
        public PlanetTerrainGenerationSettings generationSettings = new PlanetTerrainGenerationSettings();
        public List<AuthoredTerrainTileRecord> authoredTiles = new List<AuthoredTerrainTileRecord>();

        public PlanetTileCoord WrapCoord(PlanetTileCoord coord)
        {
            if (!wrapCoordinates)
                return coord;

            return new PlanetTileCoord(
                PositiveModulo(coord.x, Mathf.Max(1, wrapWidth)),
                PositiveModulo(coord.z, Mathf.Max(1, wrapHeight)));
        }

        public PlanetTileCoord EastOf(PlanetTileCoord coord) => WrapCoord(new PlanetTileCoord(coord.x + 1, coord.z));
        public PlanetTileCoord WestOf(PlanetTileCoord coord) => WrapCoord(new PlanetTileCoord(coord.x - 1, coord.z));
        public PlanetTileCoord NorthOf(PlanetTileCoord coord) => WrapCoord(new PlanetTileCoord(coord.x, coord.z + 1));
        public PlanetTileCoord SouthOf(PlanetTileCoord coord) => WrapCoord(new PlanetTileCoord(coord.x, coord.z - 1));

        public bool TryGetTerrainData(PlanetTileCoord coord, out TerrainData terrainData)
        {
            return TryGetTerrainData(coord, out terrainData, out _);
        }

        public bool TryGetTerrainData(PlanetTileCoord coord, out TerrainData terrainData, out PlanetTileCoord resolvedCoord)
        {
            resolvedCoord = WrapCoord(coord);
            if (authoredTiles != null)
            {
                for (int i = 0; i < authoredTiles.Count; i++)
                {
                    AuthoredTerrainTileRecord record = authoredTiles[i];
                    if (record != null && record.coord.Equals(resolvedCoord) && record.terrainData != null)
                    {
                        terrainData = record.terrainData;
                        return true;
                    }
                }
            }

            terrainData = null;
            return false;
        }

        public void SetTerrainData(PlanetTileCoord coord, TerrainData terrainData)
        {
            PlanetTileCoord storedCoord = WrapCoord(coord);
            authoredTiles ??= new List<AuthoredTerrainTileRecord>();
            for (int i = 0; i < authoredTiles.Count; i++)
            {
                AuthoredTerrainTileRecord record = authoredTiles[i];
                if (record != null && record.coord.Equals(storedCoord))
                {
                    record.terrainData = terrainData;
                    return;
                }
            }

            authoredTiles.Add(new AuthoredTerrainTileRecord { coord = storedCoord, terrainData = terrainData });
        }

        static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }
    }
}