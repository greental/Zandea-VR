#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MyAssets.Planet;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlanetProxyTerrainThumbnailBaker
{
    const string OutputRootFolder = "Assets/MyAssets/Terrain/PlanetTerrain/ProxyBake";
    const string AlbedoFileName = "PlanetProxyTerrainAlbedo.png";
    const string HeightFileName = "PlanetProxyTerrainHeight.png";
    const string NormalFileName = "PlanetProxyTerrainNormal.png";
    const string MaterialFileName = "M_PlanetProxy_TreadmillPOC.mat";
    const int OutputResolution = 1024;
    const int LayerPreviewResolution = 256;
    const int LayerAverageSampleGrid = 16;
    const int AlbedoAntiSquareResolution = 256;
    const float VerticalBakeCrop = 0.02f;
    const int RepeatEdgeBlendRows = 32;
    const float NormalStrength = 7.5f;
    // Tile the generated equirectangular proxy textures. The proxy texture
    // importers use Repeat on both axes so user/material tiling is preserved
    // instead of clamping the vertical pole borders.
    static readonly Vector2 ProxyTextureScale = new Vector2(3f, 3f);

    [MenuItem("Zandea/Koombar/Planet Terrain/Bake Proxy Sphere Terrain Thumbnail")]
    public static void BakeFromSelectionOrScene()
    {
        PlanetTerrainTreadmill treadmill = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<PlanetTerrainTreadmill>()
            : null;

        PlanetProxyVisual selectedProxy = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<PlanetProxyVisual>()
            : null;

        if (treadmill == null && selectedProxy != null)
            treadmill = FindTreadmillForProxy(selectedProxy);

        if (treadmill == null)
            treadmill = UnityEngine.Object.FindFirstObjectByType<PlanetTerrainTreadmill>();

        BakeFromTreadmill(treadmill, selectedProxy);
    }

    public static void BakeFromTreadmill(PlanetTerrainTreadmill treadmill)
    {
        BakeFromTreadmill(treadmill, FindProxyForTreadmill(treadmill));
    }

    public static void BakeFromProxy(PlanetProxyVisual proxy)
    {
        BakeFromTreadmill(FindTreadmillForProxy(proxy), proxy);
    }

    public static void BakeFromPlanetStack(PlanetSystemRig rig)
    {
        PlanetProxyVisual proxy = FindProxyForRig(rig);
        BakeFromTreadmill(FindTreadmillForRig(rig), proxy);
    }

    public static void BakeFromTreadmill(PlanetTerrainTreadmill treadmill, PlanetProxyVisual proxy)
    {
        if (treadmill == null)
        {
            EditorUtility.DisplayDialog("Bake Proxy Sphere Terrain Thumbnail", "Select a PlanetTerrainTreadmill or a PlanetProxyVisual with a nearby planet stack.", "OK");
            return;
        }

        BakePaths paths = BuildBakePaths(treadmill, proxy);
        EnsureFolders(paths.outputFolder);

        var context = BuildBakeContext(treadmill);
        var layerTextureCache = new Dictionary<TerrainLayer, LayerSampleCache>();
        var tileCache = new Dictionary<PlanetTileCoord, TileSampleCache>();
        float[] heightSamples = new float[OutputResolution * OutputResolution];
        Texture2D albedo = new Texture2D(OutputResolution, OutputResolution, TextureFormat.RGBA32, false, false)
        {
            name = "PlanetProxyTerrainAlbedo_Baked",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        Texture2D height = new Texture2D(OutputResolution, OutputResolution, TextureFormat.RGBA32, false, true)
        {
            name = "PlanetProxyTerrainHeight_Baked",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        Texture2D normal = new Texture2D(OutputResolution, OutputResolution, TextureFormat.RGBA32, false, true)
        {
            name = "PlanetProxyTerrainNormal_Baked",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };

        try
        {
            Color[] albedoPixels = new Color[OutputResolution * OutputResolution];
            Color[] heightPixels = new Color[OutputResolution * OutputResolution];
            for (int y = 0; y < OutputResolution; y++)
            {
                if ((y & 15) == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Bake Proxy Sphere Terrain Thumbnail",
                        $"Baking albedo/height row {y + 1}/{OutputResolution}",
                        y / (float)OutputResolution))
                    return;

                float v = RemapVerticalBakeCoordinate((y + 0.5f) / OutputResolution);
                for (int x = 0; x < OutputResolution; x++)
                {
                    float u = (x + 0.5f) / OutputResolution;
                    int index = y * OutputResolution + x;
                    float height01 = SampleTerrainHeight01(context, tileCache, u, v);
                    albedoPixels[index] = SampleTerrainAlbedo(context, tileCache, layerTextureCache, u, v);
                    heightSamples[index] = height01;
                    heightPixels[index] = new Color(height01, height01, height01, 1f);
                }
            }

            MatchHorizontalEdges(albedoPixels, heightPixels, heightSamples);
            MatchVerticalEdges(albedoPixels, heightPixels, heightSamples);
            MatchHorizontalAlbedoEdges(albedoPixels);
            albedo.SetPixels(albedoPixels);
            albedo.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            height.SetPixels(heightPixels);
            height.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            normal.SetPixels(BuildNormalPixels(heightSamples));
            normal.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            File.WriteAllBytes(paths.albedoPath, albedo.EncodeToPNG());
            File.WriteAllBytes(paths.heightPath, height.EncodeToPNG());
            File.WriteAllBytes(paths.normalPath, normal.EncodeToPNG());
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            ReleaseLayerTextureCache(layerTextureCache);
            UnityEngine.Object.DestroyImmediate(albedo);
            UnityEngine.Object.DestroyImmediate(height);
            UnityEngine.Object.DestroyImmediate(normal);
        }

        AssetDatabase.ImportAsset(paths.albedoPath, ImportAssetOptions.ForceUpdate);
        ConfigureAlbedoImporter(paths.albedoPath);
        AssetDatabase.ImportAsset(paths.heightPath, ImportAssetOptions.ForceUpdate);
        ConfigureHeightImporter(paths.heightPath);
        AssetDatabase.ImportAsset(paths.normalPath, ImportAssetOptions.ForceUpdate);
        ConfigureNormalImporter(paths.normalPath);
        Texture2D importedAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(paths.albedoPath);
        Texture2D importedHeight = AssetDatabase.LoadAssetAtPath<Texture2D>(paths.heightPath);
        Texture2D importedNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(paths.normalPath);

        Vector2 textureScale = treadmill.AuthoredTileSet != null && !treadmill.AuthoredTileSet.wrapCoordinates
            ? Vector2.one : ProxyTextureScale;
        Material material = CreateOrUpdateMaterial(paths.materialPath, importedAlbedo, importedHeight, importedNormal, textureScale);
        AssignProxyMaterial(proxy != null ? proxy : FindProxyForTreadmill(treadmill), material);

        AssetDatabase.SaveAssets();
        Debug.Log($"[PlanetProxyTerrainThumbnailBaker] Baked proxy terrain albedo/height/normal maps to '{paths.outputFolder}', updated '{paths.materialPath}', and assigned it to the selected planet proxy if present.", treadmill);
    }

    static BakeContext BuildBakeContext(PlanetTerrainTreadmill treadmill)
    {
        PlanetTerrainTileSet tileSet = treadmill.AuthoredTileSet;
        int tilesWide = tileSet != null && tileSet.wrapCoordinates ? Mathf.Max(1, tileSet.wrapWidth) : 3;
        int tilesHigh = tileSet != null && tileSet.wrapCoordinates ? Mathf.Max(1, tileSet.wrapHeight) : 3;
        int minX = tileSet != null && tileSet.wrapCoordinates ? 0 : -1;
        int minZ = tileSet != null && tileSet.wrapCoordinates ? 0 : -1;
        if (tileSet != null && !tileSet.wrapCoordinates && tileSet.authoredTiles != null && tileSet.authoredTiles.Count > 0)
        {
            int maxX = int.MinValue, maxZ = int.MinValue;
            minX = minZ = int.MaxValue;
            foreach (var tile in tileSet.authoredTiles)
            {
                if (tile?.terrainData == null) continue;
                minX = Mathf.Min(minX, tile.coord.x);
                minZ = Mathf.Min(minZ, tile.coord.z);
                maxX = Mathf.Max(maxX, tile.coord.x);
                maxZ = Mathf.Max(maxZ, tile.coord.z);
            }
            if (minX != int.MaxValue)
            {
                tilesWide = maxX - minX + 1;
                tilesHigh = maxZ - minZ + 1;
            }
            else { minX = minZ = -1; }
        }

        return new BakeContext
        {
            treadmill = treadmill,
            tileSet = tileSet,
            minX = minX,
            minZ = minZ,
            tilesWide = tilesWide,
            tilesHigh = tilesHigh
        };
    }

    static Color SampleTerrainAlbedo(
        BakeContext context,
        Dictionary<PlanetTileCoord, TileSampleCache> tileCache,
        Dictionary<TerrainLayer, LayerSampleCache> layerTextureCache,
        float u,
        float v)
    {
        float tileSpaceX = Mathf.Clamp(u * context.tilesWide, 0f, context.tilesWide - 0.0001f);
        float tileSpaceZ = Mathf.Clamp(v * context.tilesHigh, 0f, context.tilesHigh - 0.0001f);
        int tileOffsetX = Mathf.FloorToInt(tileSpaceX);
        int tileOffsetZ = Mathf.FloorToInt(tileSpaceZ);
        float localU = tileSpaceX - tileOffsetX;
        float localV = tileSpaceZ - tileOffsetZ;

        var coord = new PlanetTileCoord(context.minX + tileOffsetX, context.minZ + tileOffsetZ);
        TileSampleCache tile = GetTileCache(context, tileCache, coord);
        if (tile == null || tile.layers == null || tile.layers.Length == 0 || tile.alphamaps == null)
            return new Color(0.45f, 0.32f, 0.22f, 1f);

        Color color = Color.black;
        float totalWeight = 0f;

        int layerCount = Mathf.Min(tile.layers.Length, tile.alphamapLayers);
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            float weight = SampleAlphamapBilinear(tile, localU, localV, layerIndex);
            if (weight <= 0.0001f)
                continue;

            TerrainLayer layer = tile.layers[layerIndex];
            Color layerColor = SampleLayerColor(layer, layerTextureCache, localU, localV, tile.terrainSize);
            color += layerColor * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0.0001f)
            return new Color(0.45f, 0.32f, 0.22f, 1f);

        color /= totalWeight;
        color.a = 1f;
        return color;
    }

    static float RemapVerticalBakeCoordinate(float v)
    {
        // The equirectangular proxy should not sample the terrain atlas' exact
        // vertical borders. Those rows tend to contain generated/averaged pole
        // content and they also bleed together when the texture is filtered.
        // Remapping crops a small top/bottom margin while keeping the output at
        // full resolution, which preserves terrain color detail without a
        // visible smoothed cap or horizontal wrap line.
        return Mathf.Lerp(VerticalBakeCrop, 1f - VerticalBakeCrop, Mathf.Clamp01(v));
    }

    static float SampleAlphamapBilinear(TileSampleCache tile, float localU, float localV, int layerIndex)
    {
        if (tile == null || tile.alphamaps == null || layerIndex < 0 || layerIndex >= tile.alphamapLayers)
            return 0f;

        float sampleX = Mathf.Clamp01(localU) * (tile.alphamapWidth - 1);
        float sampleZ = Mathf.Clamp01(localV) * (tile.alphamapHeight - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, tile.alphamapWidth - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, tile.alphamapHeight - 1);
        int x1 = Mathf.Min(x0 + 1, tile.alphamapWidth - 1);
        int z1 = Mathf.Min(z0 + 1, tile.alphamapHeight - 1);
        float tx = sampleX - x0;
        float tz = sampleZ - z0;

        float w00 = tile.alphamaps[z0, x0, layerIndex];
        float w10 = tile.alphamaps[z0, x1, layerIndex];
        float w01 = tile.alphamaps[z1, x0, layerIndex];
        float w11 = tile.alphamaps[z1, x1, layerIndex];
        return Mathf.Lerp(Mathf.Lerp(w00, w10, tx), Mathf.Lerp(w01, w11, tx), tz);
    }

    static float SampleTerrainHeight01(BakeContext context, Dictionary<PlanetTileCoord, TileSampleCache> tileCache, float u, float v)
    {
        float tileSpaceX = Mathf.Clamp(u * context.tilesWide, 0f, context.tilesWide - 0.0001f);
        float tileSpaceZ = Mathf.Clamp(v * context.tilesHigh, 0f, context.tilesHigh - 0.0001f);
        int tileOffsetX = Mathf.FloorToInt(tileSpaceX);
        int tileOffsetZ = Mathf.FloorToInt(tileSpaceZ);
        float localU = tileSpaceX - tileOffsetX;
        float localV = tileSpaceZ - tileOffsetZ;

        var coord = new PlanetTileCoord(context.minX + tileOffsetX, context.minZ + tileOffsetZ);
        TileSampleCache tile = GetTileCache(context, tileCache, coord);
        if (tile == null || tile.heights == null)
            return 0.5f;

        float heightX = Mathf.Clamp(localU * (tile.heightWidth - 1), 0f, tile.heightWidth - 1);
        float heightZ = Mathf.Clamp(localV * (tile.heightHeight - 1), 0f, tile.heightHeight - 1);
        int x0 = Mathf.FloorToInt(heightX);
        int z0 = Mathf.FloorToInt(heightZ);
        int x1 = Mathf.Min(x0 + 1, tile.heightWidth - 1);
        int z1 = Mathf.Min(z0 + 1, tile.heightHeight - 1);
        float tx = heightX - x0;
        float tz = heightZ - z0;

        float h00 = tile.heights[z0, x0];
        float h10 = tile.heights[z0, x1];
        float h01 = tile.heights[z1, x0];
        float h11 = tile.heights[z1, x1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    static Color[] BuildNormalPixels(float[] heightSamples)
    {
        var pixels = new Color[OutputResolution * OutputResolution];
        for (int y = 0; y < OutputResolution; y++)
        {
            for (int x = 0; x < OutputResolution; x++)
            {
                float left = heightSamples[y * OutputResolution + PositiveModulo(x - 1, OutputResolution)];
                float right = heightSamples[y * OutputResolution + PositiveModulo(x + 1, OutputResolution)];
                float down = heightSamples[PositiveModulo(y - 1, OutputResolution) * OutputResolution + x];
                float up = heightSamples[PositiveModulo(y + 1, OutputResolution) * OutputResolution + x];
                float dx = (left - right) * NormalStrength;
                float dy = (down - up) * NormalStrength;
                Vector3 n = new Vector3(dx, dy, 1f).normalized;
                pixels[y * OutputResolution + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }

        return pixels;
    }

    static void MatchHorizontalEdges(Color[] albedoPixels, Color[] heightPixels, float[] heightSamples)
    {
        int lastX = OutputResolution - 1;
        for (int y = 0; y < OutputResolution; y++)
        {
            int left = y * OutputResolution;
            int right = left + lastX;
            Color albedo = (albedoPixels[left] + albedoPixels[right]) * 0.5f;
            float height = (heightSamples[left] + heightSamples[right]) * 0.5f;

            albedo.a = 1f;
            albedoPixels[left] = albedo;
            albedoPixels[right] = albedo;
            heightSamples[left] = height;
            heightSamples[right] = height;
            heightPixels[left] = new Color(height, height, height, 1f);
            heightPixels[right] = heightPixels[left];
        }
    }

    static void MatchHorizontalAlbedoEdges(Color[] albedoPixels)
    {
        int lastX = OutputResolution - 1;
        for (int y = 0; y < OutputResolution; y++)
        {
            int left = y * OutputResolution;
            int right = left + lastX;
            Color albedo = (albedoPixels[left] + albedoPixels[right]) * 0.5f;
            albedo.a = 1f;
            albedoPixels[left] = albedo;
            albedoPixels[right] = albedo;
        }
    }

    static void MatchVerticalEdges(Color[] albedoPixels, Color[] heightPixels, float[] heightSamples)
    {
        if (albedoPixels == null || heightPixels == null || heightSamples == null)
            return;

        int rows = Mathf.Clamp(RepeatEdgeBlendRows, 2, OutputResolution / 2);
        int lastY = OutputResolution - 1;
        for (int edgeRow = 0; edgeRow < rows; edgeRow++)
        {
            float matchStrength = 1f - Mathf.SmoothStep(0f, 1f, edgeRow / (float)(rows - 1));
            int bottomY = edgeRow;
            int topY = lastY - edgeRow;
            for (int x = 0; x < OutputResolution; x++)
            {
                int bottom = bottomY * OutputResolution + x;
                int top = topY * OutputResolution + x;
                Color matchedAlbedo = (albedoPixels[bottom] + albedoPixels[top]) * 0.5f;
                Color bottomAlbedo = Color.Lerp(albedoPixels[bottom], matchedAlbedo, matchStrength);
                Color topAlbedo = Color.Lerp(albedoPixels[top], matchedAlbedo, matchStrength);
                float matchedHeight = (heightSamples[bottom] + heightSamples[top]) * 0.5f;
                float bottomHeight = Mathf.Lerp(heightSamples[bottom], matchedHeight, matchStrength);
                float topHeight = Mathf.Lerp(heightSamples[top], matchedHeight, matchStrength);

                bottomAlbedo.a = 1f;
                topAlbedo.a = 1f;
                albedoPixels[bottom] = bottomAlbedo;
                albedoPixels[top] = topAlbedo;
                heightSamples[bottom] = bottomHeight;
                heightSamples[top] = topHeight;
                heightPixels[bottom] = new Color(bottomHeight, bottomHeight, bottomHeight, 1f);
                heightPixels[top] = new Color(topHeight, topHeight, topHeight, 1f);
            }
        }
    }

    static void RemoveAlbedoSquareArtifacts(Color[] albedoPixels)
    {
        if (albedoPixels == null || albedoPixels.Length != OutputResolution * OutputResolution)
            return;

        int lowResolution = Mathf.Clamp(AlbedoAntiSquareResolution, 1, OutputResolution);
        if (lowResolution >= OutputResolution)
            return;

        Color[] lowPixels = new Color[lowResolution * lowResolution];
        for (int y = 0; y < lowResolution; y++)
        {
            float sourceY = (y + 0.5f) * OutputResolution / lowResolution - 0.5f;
            for (int x = 0; x < lowResolution; x++)
            {
                float sourceX = (x + 0.5f) * OutputResolution / lowResolution - 0.5f;
                lowPixels[y * lowResolution + x] = SampleColorBilinear(albedoPixels, OutputResolution, OutputResolution, sourceX, sourceY);
            }
        }

        for (int y = 0; y < OutputResolution; y++)
        {
            float sourceY = (y + 0.5f) * lowResolution / OutputResolution - 0.5f;
            for (int x = 0; x < OutputResolution; x++)
            {
                float sourceX = (x + 0.5f) * lowResolution / OutputResolution - 0.5f;
                Color color = SampleColorBilinear(lowPixels, lowResolution, lowResolution, sourceX, sourceY);
                color.a = 1f;
                albedoPixels[y * OutputResolution + x] = color;
            }
        }
    }

    static Color SampleColorBilinear(Color[] pixels, int width, int height, float x, float y)
    {
        if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
            return Color.black;

        x = Mathf.Clamp(x, 0f, width - 1f);
        y = Mathf.Clamp(y, 0f, height - 1f);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);
        float tx = x - x0;
        float ty = y - y0;

        Color c00 = pixels[y0 * width + x0];
        Color c10 = pixels[y0 * width + x1];
        Color c01 = pixels[y1 * width + x0];
        Color c11 = pixels[y1 * width + x1];
        return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
    }

    static void BlendPoleRows(Color[] albedoPixels, Color[] heightPixels, float[] heightSamples)
    {
        const int poleBlendRows = 96;
        for (int y = 0; y < OutputResolution; y++)
        {
            int distanceFromPole = Mathf.Min(y, OutputResolution - 1 - y);
            float poleBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceFromPole / (float)poleBlendRows));
            if (poleBlend <= 0f)
                continue;

            int rowStart = y * OutputResolution;
            Color averageAlbedo = Color.black;
            float averageHeight = 0f;
            for (int x = 0; x < OutputResolution; x++)
            {
                int index = rowStart + x;
                averageAlbedo += albedoPixels[index];
                averageHeight += heightSamples[index];
            }

            averageAlbedo /= OutputResolution;
            averageAlbedo.a = 1f;
            averageHeight /= OutputResolution;

            for (int x = 0; x < OutputResolution; x++)
            {
                int index = rowStart + x;
                Color albedo = Color.Lerp(albedoPixels[index], averageAlbedo, poleBlend);
                float height = Mathf.Lerp(heightSamples[index], averageHeight, poleBlend);
                albedo.a = 1f;
                albedoPixels[index] = albedo;
                heightSamples[index] = height;
                heightPixels[index] = new Color(height, height, height, 1f);
            }
        }
    }

    static TileSampleCache GetTileCache(BakeContext context, Dictionary<PlanetTileCoord, TileSampleCache> cache, PlanetTileCoord coord)
    {
        if (cache.TryGetValue(coord, out TileSampleCache existing))
            return existing;

        TerrainData data = null;
        if (context.tileSet != null && context.tileSet.TryGetTerrainData(coord, out TerrainData authoredData))
            data = authoredData;

        if (data == null)
            data = context.treadmill.CreateGeneratedTerrainData(coord);

        TileSampleCache created = null;
        if (data != null && data.alphamapLayers > 0)
        {
            created = new TileSampleCache
            {
                alphamaps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight),
                alphamapWidth = data.alphamapWidth,
                alphamapHeight = data.alphamapHeight,
                alphamapLayers = data.alphamapLayers,
                heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution),
                heightWidth = data.heightmapResolution,
                heightHeight = data.heightmapResolution,
                layers = data.terrainLayers,
                terrainSize = data.size
            };
        }

        cache[coord] = created;
        return created;
    }

    static Color SampleLayerColor(
        TerrainLayer layer,
        Dictionary<TerrainLayer, LayerSampleCache> layerTextureCache,
        float localU,
        float localV,
        Vector3 terrainSize)
    {
        if (layer == null)
            return Color.magenta;

        LayerSampleCache cache = GetLayerSampleCache(layer, layerTextureCache);
        Color color = cache.averageColor;
        if (cache.preview != null)
        {
            float terrainWidth = Mathf.Max(0.0001f, terrainSize.x);
            float terrainLength = Mathf.Max(0.0001f, terrainSize.z);
            float tileSizeX = Mathf.Abs(layer.tileSize.x) > 0.0001f ? layer.tileSize.x : terrainWidth;
            float tileSizeZ = Mathf.Abs(layer.tileSize.y) > 0.0001f ? layer.tileSize.y : terrainLength;
            float textureU = (localU * terrainWidth + layer.tileOffset.x) / tileSizeX;
            float textureV = (localV * terrainLength + layer.tileOffset.y) / tileSizeZ;
            color = cache.preview.GetPixelBilinear(Mathf.Repeat(textureU, 1f), Mathf.Repeat(textureV, 1f));
        }

        color.r *= layer.diffuseRemapMax.x;
        color.g *= layer.diffuseRemapMax.y;
        color.b *= layer.diffuseRemapMax.z;
        color.a = 1f;
        return color;
    }

    static LayerSampleCache GetLayerSampleCache(TerrainLayer layer, Dictionary<TerrainLayer, LayerSampleCache> layerTextureCache)
    {
        if (layerTextureCache.TryGetValue(layer, out LayerSampleCache cached))
            return cached;

        Color color = Color.white;
        Texture2D preview = null;
        Texture2D diffuse = layer.diffuseTexture;
        if (diffuse != null)
        {
            preview = GetReadablePreview(diffuse);
            if (preview != null)
            {
                color = AverageTextureColor(preview);
            }
        }

        color.a = 1f;
        cached = new LayerSampleCache
        {
            preview = preview,
            averageColor = color
        };
        layerTextureCache[layer] = cached;
        return cached;
    }

    static Color AverageTextureColor(Texture2D texture)
    {
        if (texture == null)
            return Color.white;

        Color sum = Color.black;
        int sampleCount = 0;
        for (int y = 0; y < LayerAverageSampleGrid; y++)
        {
            float v = (y + 0.5f) / LayerAverageSampleGrid;
            for (int x = 0; x < LayerAverageSampleGrid; x++)
            {
                float u = (x + 0.5f) / LayerAverageSampleGrid;
                sum += texture.GetPixelBilinear(u, v);
                sampleCount++;
            }
        }

        if (sampleCount == 0)
            return Color.white;

        Color average = sum / sampleCount;
        average.a = 1f;
        return average;
    }

    static Texture2D GetReadablePreview(Texture2D source)
    {
        if (source == null)
            return null;

        int width = Mathf.Min(LayerPreviewResolution, Mathf.Max(1, source.width));
        int height = Mathf.Min(LayerPreviewResolution, Mathf.Max(1, source.height));
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D preview = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            name = source.name + "_ProxyBakePreview",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            preview.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            preview.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        return preview;
    }

    static void ReleaseLayerTextureCache(Dictionary<TerrainLayer, LayerSampleCache> layerTextureCache)
    {
        if (layerTextureCache == null)
            return;

        foreach (LayerSampleCache cache in layerTextureCache.Values)
        {
            if (cache != null && cache.preview != null)
                UnityEngine.Object.DestroyImmediate(cache.preview);
        }

        layerTextureCache.Clear();
    }

    static Material CreateOrUpdateMaterial(string materialPath, Texture2D albedo, Texture2D height, Texture2D normal, Vector2 textureScale)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else if (shader != null && material.shader != shader)
        {
            material.shader = shader;
        }

        material.name = "M_PlanetProxy_TreadmillPOC";
        material.color = Color.white;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", textureScale);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", albedo);
            material.SetTextureScale("_MainTex", textureScale);
            material.SetTextureOffset("_MainTex", Vector2.zero);
        }

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.15f);

        if (normal != null && material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", textureScale);
            material.SetTextureOffset("_BumpMap", Vector2.zero);
            material.EnableKeyword("_NORMALMAP");
        }

        if (material.HasProperty("_BumpScale"))
            material.SetFloat("_BumpScale", 0.65f);

        if (height != null && material.HasProperty("_ParallaxMap"))
        {
            material.SetTexture("_ParallaxMap", height);
            material.SetTextureScale("_ParallaxMap", textureScale);
            material.SetTextureOffset("_ParallaxMap", Vector2.zero);
            material.EnableKeyword("_PARALLAXMAP");
        }

        if (material.HasProperty("_Parallax"))
            material.SetFloat("_Parallax", 0.015f);

        EditorUtility.SetDirty(material);
        return material;
    }

    static void AssignProxyMaterial(PlanetProxyVisual proxy, Material material)
    {
        if (proxy == null || material == null)
            return;

        Undo.RecordObject(proxy, "Assign Planet Proxy Terrain POC Material");
        var serializedProxy = new SerializedObject(proxy);
        SerializedProperty surfaceMaterial = serializedProxy.FindProperty("surfaceMaterial");
        if (surfaceMaterial != null)
        {
            surfaceMaterial.objectReferenceValue = material;
            serializedProxy.ApplyModifiedProperties();
            EditorUtility.SetDirty(proxy);
            EditorSceneManager.MarkSceneDirty(proxy.gameObject.scene);
        }
    }

    static void ConfigureAlbedoImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static void ConfigureHeightImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = true;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static void ConfigureNormalImporter(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = true;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    static BakePaths BuildBakePaths(PlanetTerrainTreadmill treadmill, PlanetProxyVisual proxy)
    {
        string ownerName = proxy != null ? proxy.gameObject.name : treadmill.gameObject.name;
        string safeName = MakeSafeAssetName(ownerName);
        string outputFolder = OutputRootFolder + "/" + safeName;
        return new BakePaths
        {
            outputFolder = outputFolder,
            albedoPath = outputFolder + "/" + AlbedoFileName,
            heightPath = outputFolder + "/" + HeightFileName,
            normalPath = outputFolder + "/" + NormalFileName,
            materialPath = outputFolder + "/" + MaterialFileName
        };
    }

    static string MakeSafeAssetName(string rawName)
    {
        string name = string.IsNullOrWhiteSpace(rawName) ? "Planet" : rawName.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    static PlanetTerrainTreadmill FindTreadmillForProxy(PlanetProxyVisual proxy)
    {
        if (proxy == null)
            return null;

        PlanetSystemRig rig = proxy.GetComponentInParent<PlanetSystemRig>();
        PlanetTerrainTreadmill rigTreadmill = FindTreadmillForRig(rig);
        if (rigTreadmill != null)
            return rigTreadmill;

        PlanetTerrainTreadmill childTreadmill = proxy.GetComponentInChildren<PlanetTerrainTreadmill>(true);
        if (childTreadmill != null)
            return childTreadmill;

        PlanetTerrainTreadmill siblingTreadmill = proxy.transform.root.GetComponentInChildren<PlanetTerrainTreadmill>(true);
        if (siblingTreadmill != null)
            return siblingTreadmill;

        return UnityEngine.Object.FindFirstObjectByType<PlanetTerrainTreadmill>();
    }

    static PlanetTerrainTreadmill FindTreadmillForRig(PlanetSystemRig rig)
    {
        if (rig == null)
            return null;

        PlanetTerrainTreadmill childTreadmill = rig.GetComponentInChildren<PlanetTerrainTreadmill>(true);
        if (childTreadmill != null)
            return childTreadmill;

        return rig.Treadmill;
    }

    static PlanetProxyVisual FindProxyForRig(PlanetSystemRig rig)
    {
        if (rig == null)
            return null;

        PlanetProxyVisual proxy = rig.GetComponent<PlanetProxyVisual>();
        if (proxy != null)
            return proxy;

        return rig.GetComponentInChildren<PlanetProxyVisual>(true);
    }

    static PlanetProxyVisual FindProxyForTreadmill(PlanetTerrainTreadmill treadmill)
    {
        if (treadmill == null)
            return UnityEngine.Object.FindFirstObjectByType<PlanetProxyVisual>();

        PlanetSystemRig[] rigs = UnityEngine.Object.FindObjectsByType<PlanetSystemRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (PlanetSystemRig candidate in rigs)
        {
            if (candidate != null && candidate.Treadmill == treadmill)
            {
                PlanetProxyVisual candidateProxy = candidate.GetComponent<PlanetProxyVisual>();
                if (candidateProxy != null)
                    return candidateProxy;
            }
        }

        PlanetSystemRig rig = treadmill.GetComponentInParent<PlanetSystemRig>();
        if (rig != null)
        {
            PlanetProxyVisual rigProxy = rig.GetComponent<PlanetProxyVisual>();
            if (rigProxy != null)
                return rigProxy;
        }

        return UnityEngine.Object.FindFirstObjectByType<PlanetProxyVisual>();
    }

    static void EnsureFolders(string outputFolder)
    {
        EnsureFolder("Assets/MyAssets/Terrain");
        EnsureFolder("Assets/MyAssets/Terrain/PlanetTerrain");
        EnsureFolder(OutputRootFolder);
        EnsureFolder(outputFolder);
    }

    static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string name = Path.GetFileName(assetFolder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, name);
    }

    static int PositiveModulo(int value, int modulo)
    {
        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    sealed class BakeContext
    {
        public PlanetTerrainTreadmill treadmill;
        public PlanetTerrainTileSet tileSet;
        public int minX;
        public int minZ;
        public int tilesWide;
        public int tilesHigh;
    }

    sealed class BakePaths
    {
        public string outputFolder;
        public string albedoPath;
        public string heightPath;
        public string normalPath;
        public string materialPath;
    }

    sealed class TileSampleCache
    {
        public float[,,] alphamaps;
        public int alphamapWidth;
        public int alphamapHeight;
        public int alphamapLayers;
        public float[,] heights;
        public int heightWidth;
        public int heightHeight;
        public TerrainLayer[] layers;
        public Vector3 terrainSize;
    }

    sealed class LayerSampleCache
    {
        public Texture2D preview;
        public Color averageColor;
    }
}
#endif
