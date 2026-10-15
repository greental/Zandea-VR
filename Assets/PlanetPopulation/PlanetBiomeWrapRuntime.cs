using System.Collections.Generic;
using UnityEngine;

namespace MyAssets.PlanetTerrain
{
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(PlanetTerrainTreadmill))]
    public sealed class PlanetBiomeWrapRuntime : MonoBehaviour
    {
        PlanetTerrainTileSet runtimeSet;

        void Awake()
        {
            if (!Application.isPlaying) return;
            var treadmill = GetComponent<PlanetTerrainTreadmill>();
            var source = treadmill.AuthoredTileSet;
            if (!source || source.authoredTiles == null || source.authoredTiles.Count == 0) return;

            runtimeSet = CreateWrappedSet(source);
            treadmill.SetAuthoredTileSet(runtimeSet);
        }

        public static PlanetTerrainTileSet CreateWrappedSet(PlanetTerrainTileSet source)
        {
            if (!source || source.authoredTiles == null || source.authoredTiles.Count == 0)
                throw new System.ArgumentException("A populated authored tile set is required.", nameof(source));

            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            foreach (var record in source.authoredTiles)
            {
                if (record == null || !record.terrainData) continue;
                minX = Mathf.Min(minX, record.coord.x); maxX = Mathf.Max(maxX, record.coord.x);
                minZ = Mathf.Min(minZ, record.coord.z); maxZ = Mathf.Max(maxZ, record.coord.z);
            }
            if (minX == int.MaxValue) throw new System.ArgumentException("The tile set has no terrain data.", nameof(source));
            var wrapped = Instantiate(source);
            wrapped.hideFlags = HideFlags.DontSave;
            wrapped.wrapWidth = maxX - minX + 1;
            wrapped.wrapHeight = maxZ - minZ + 1;
            wrapped.wrapCoordinates = true;
            wrapped.authoredTiles = new List<AuthoredTerrainTileRecord>(source.authoredTiles.Count);
            foreach (var record in source.authoredTiles)
            {
                if (record == null || !record.terrainData) continue;
                wrapped.authoredTiles.Add(new AuthoredTerrainTileRecord
                {
                    coord = new PlanetTileCoord(PositiveModulo(record.coord.x, wrapped.wrapWidth),
                        PositiveModulo(record.coord.z, wrapped.wrapHeight)),
                    terrainData = record.terrainData
                });
            }
            return wrapped;
        }

        void OnDestroy()
        {
            if (runtimeSet) Destroy(runtimeSet);
        }

        static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
