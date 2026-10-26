using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyAssets.PlanetTerrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Zandea.POI;
using Zandea.Props;

namespace Zandea.Pueblo.Editor
{
    public static class PuebloSettlementBuilder
    {
        const string Root = "Assets/Zandea/Pueblo";
        const string Models = Root + "/Models/";
        const string Prefabs = Root + "/Prefabs/";
        const string Materials = Root + "/Materials/";
        const string DefinitionPath = Root + "/PuebloSettlement.asset";
        const string PropsPath = Root + "/PuebloProps.asset";
        const string MainPath = "Assets/Main.unity";
        const string ReviewPath = "Assets/Scenes/Zandea/PuebloSettlementReview.unity";
        const string PoiId = "zandea.pueblo.settlement.1";
        static readonly string[] Structures = {
            "house_base", "house_base_stairs", "house_large_two_stories", "house_single",
            "Ramada_attached", "Ramada_attached_3", "Ramada_full", "round",
            "stairCase", "wall_1", "wall_2", "wall_3", "wall_gate",
            "Ramada_new", "animal_pen", "stone_bakery", "stepped_house", "windcatcher",
            "domed_temple", "adobe_blacksmith", "watchtower_tall", "dome_hut"
        };
        static readonly string[] NewPots = { "earth_pot", "terracotta_pot", "round_jar", "tall_jar" };

        [MenuItem("Zandea/POI/Pueblo/Build Settlement")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the pueblo settlement.");
            if (new DriveInfo("D:\\").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("D: has less than 20 GiB free; postpone this import.");
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory("Assets/Scenes/Zandea");
            AssetDatabase.Refresh();
            var built = new Dictionary<string, GameObject>();
            foreach (var key in Structures)
                built[key] = BuildStatic(key);
            var basket = BuildBasket();
            foreach (var key in NewPots)
                built[key] = BuildPhysicalPot(key);
            built["feeding_trough"] = BuildGroundProp("feeding_trough");
            var definition = BuildDefinition(built);
            var props = BuildProps(basket, built);
            var layout = OutpostLayout.Generate(definition, definition.seed);
            if (!layout.Validate(definition, out var issue))
                throw new InvalidOperationException("Pueblo layout invalid: " + issue);
            BuildReviewScene(definition, props);
            InstallMain(definition, props);
            AssetDatabase.SaveAssets();
            if (new DriveInfo("D:\\").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                Debug.LogWarning("[Pueblo] D: fell below 20 GiB after import.");
            Debug.Log("[Pueblo] Built the settlement prefabs, reusable POI and prop definitions, review scene, and Main placement.");
        }

        static Material Material(string key)
        {
            string path = Materials + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new InvalidOperationException("URP Lit shader is missing.");
                material = new Material(shader) { name = "Pueblo " + key };
                AssetDatabase.CreateAsset(material, path);
            }
            string texturePath = Models + key + "_base." + (key == "basket" ? "jpg" : "png");
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing imported Pueblo texture: " + texturePath);
            if (importer.maxTextureSize != 2048 || !importer.mipmapEnabled ||
                importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            string normalPath = Models + key + "_normal.png";
            var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter)
            {
                if (normalImporter.textureType != TextureImporterType.NormalMap || normalImporter.maxTextureSize != 2048)
                {
                    normalImporter.textureType = TextureImporterType.NormalMap;
                    normalImporter.maxTextureSize = 2048;
                    normalImporter.SaveAndReimport();
                }
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .1f);
            material.SetFloat("_Cull", 0f); // generated source surfaces are often single-sided
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject BuildStatic(string key)
        {
            EnsureModelScale(key);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + ".fbx");
            if (!source) throw new InvalidOperationException("Missing imported Pueblo FBX: " + key);
            var root = new GameObject("Pueblo_" + key);
            try
            {
                var model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = key + " mesh";
                // Keep the FBX root's axis conversion: Blender Z-up becomes Unity Y-up.
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                if (key == "house_base")
                    Debug.Log("[Pueblo] Imported house axis=" + model.transform.localRotation.eulerAngles +
                        " bounds=" + RenderBounds(root).size);
                var material = Material(key);
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = material;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    var collider = filter.gameObject.GetComponent<MeshCollider>();
                    if (!collider) collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false; // preserves doors, the widened gate, and space under ramadas
                }
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Pueblo_" + key + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject BuildBasket()
        {
            const string key = "basket";
            EnsureModelScale(key);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + ".fbx");
            if (!source) throw new InvalidOperationException("Missing imported basket FBX.");
            var root = new GameObject("Pueblo_basket");
            try
            {
                var model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = "basket mesh";
                // Keep the FBX root's axis conversion for the same reason as buildings.
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = Material(key);
                var bounds = RenderBounds(root);
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                var body = root.AddComponent<Rigidbody>();
                body.mass = .45f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<XRGrabInteractable>();
                root.AddComponent<PhysicalProp>();
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Pueblo_basket.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject BuildPhysicalPot(string key)
        {
            EnsureModelScale(key);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + ".fbx");
            if (!source) throw new InvalidOperationException("Missing Pueblo pot model: " + key);
            var root = new GameObject("Pueblo_" + key);
            try
            {
                var model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = key + " mesh";
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = Material(key);
                var bounds = RenderBounds(root);
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = bounds.center;
                capsule.radius = Mathf.Max(bounds.size.x, bounds.size.z) * .5f;
                capsule.height = Mathf.Max(bounds.size.y, capsule.radius * 2);
                var body = root.AddComponent<Rigidbody>();
                body.mass = .7f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<XRGrabInteractable>();
                root.AddComponent<PhysicalProp>();
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Pueblo_" + key + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject BuildGroundProp(string key)
        {
            EnsureModelScale(key);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models + key + ".fbx");
            if (!source) throw new InvalidOperationException("Missing Pueblo ground prop model: " + key);
            var root = new GameObject("Pueblo_" + key);
            try
            {
                var model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = key + " mesh";
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = Material(key);
                var bounds = RenderBounds(root);
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "Pueblo_" + key + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void EnsureModelScale(string key)
        {
            var importer = AssetImporter.GetAtPath(Models + key + ".fbx") as ModelImporter;
            if (!importer) throw new InvalidOperationException("Missing Pueblo model importer: " + key);
            float metres = key == "basket" ? 100f : key == "wall_gate" ? 1000f : 500f;
            if (Mathf.Abs(importer.globalScale - metres) < .01f) return;
            // Blender's FBX file units arrive at 0.001 of the intended metre scale.
            importer.globalScale = metres;
            importer.SaveAndReimport();
        }

        static Bounds RenderBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Pueblo model has no renderer: " + instance.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static OutpostAsset Asset(GameObject prefab, float entranceYaw = 0)
        {
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var bounds = RenderBounds(instance);
                return new OutpostAsset { prefab = prefab, size = bounds.size, center = bounds.center,
                    entranceYaw = entranceYaw };
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static BanditOutpostDefinition BuildDefinition(Dictionary<string, GameObject> built)
        {
            var definition = AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>(DefinitionPath);
            if (definition)
            {
                // Upgrade the original POC configuration once, while preserving later
                // Inspector choices made after the new assets have been installed.
                if (definition.mainBuilding.prefab == built["house_large_two_stories"])
                {
                    AssignPuebloAssets(definition, built);
                    EditorUtility.SetDirty(definition);
                }
                definition.Validate();
                return definition;
            }
            var existing = AssetDatabase.LoadAssetAtPath<BanditOutpostDefinition>("Assets/Zandea/POI/BanditOutpost.asset");
            if (!existing) throw new InvalidOperationException("The existing Zandea POI definition is missing.");
            definition = UnityEngine.Object.Instantiate(existing);
            definition.name = "PuebloSettlement";
            definition.seed = 1862;
            definition.buildingCount = 5;
            definition.residentBandits = 6;
            definition.useAllBuildingVariants = true;
            definition.useAllShadeVariants = true;
            AssignPuebloAssets(definition, built);
            definition.blendDistance = 32;
            definition.maximumSiteSlope = 12;
            definition.maximumHeightRange = 6;
            definition.Validate();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static void AssignPuebloAssets(BanditOutpostDefinition definition, Dictionary<string, GameObject> built)
        {
            definition.mainBuilding = Asset(built["domed_temple"]);
            definition.buildings = new[] { Asset(built["animal_pen"]), Asset(built["stone_bakery"]),
                Asset(built["stepped_house"]), Asset(built["windcatcher"]),
                Asset(built["adobe_blacksmith"]), Asset(built["dome_hut"]) };
            definition.shades = new[] { Asset(built["Ramada_new"]), Asset(built["Ramada_new"]),
                Asset(built["Ramada_new"]), Asset(built["Ramada_full"]),
                Asset(built["Ramada_attached"]), Asset(built["Ramada_attached_3"]) };
            definition.wall = Asset(built["wall_1"]);
            definition.wallVariants = new[] { Asset(built["wall_1"]), Asset(built["wall_2"]),
                Asset(built["wall_3"]) };
            definition.gate = Asset(built["wall_gate"]);
            definition.tower = Asset(built["watchtower_tall"]);
        }

        static OutpostPropDefinition BuildProps(GameObject basket, Dictionary<string, GameObject> built)
        {
            var definition = AssetDatabase.LoadAssetAtPath<OutpostPropDefinition>(PropsPath);
            if (definition)
            {
                if (definition.ceramicPotVariants == null || definition.ceramicPotVariants.Length == 0)
                {
                    definition.ceramicPotVariants = new[] { definition.ceramicPot, built["earth_pot"],
                        built["terracotta_pot"], built["round_jar"], built["tall_jar"] };
                    definition.extraStorageProps = new[] { built["feeding_trough"] };
                    EditorUtility.SetDirty(definition);
                }
                return definition;
            }
            var existing = AssetDatabase.LoadAssetAtPath<OutpostPropDefinition>(
                "Assets/Zandea/Props/Resources/ZandeaOutpostProps.asset");
            if (!existing) throw new InvalidOperationException("The existing Zandea prop definition is missing.");
            definition = UnityEngine.Object.Instantiate(existing);
            definition.name = "PuebloProps";
            definition.wickerBasket = basket;
            definition.ceramicPotVariants = new[] { definition.ceramicPot, built["earth_pot"],
                built["terracotta_pot"], built["round_jar"], built["tall_jar"] };
            definition.extraStorageProps = new[] { built["feeding_trough"] };
            definition.extraPotsPerCanopy = 3;
            definition.basketsPerCanopy = 2;
            definition.storageBaskets = 4;
            AssetDatabase.CreateAsset(definition, PropsPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static void BuildReviewScene(BanditOutpostDefinition definition, OutpostPropDefinition props)
        {
            if (File.Exists(ReviewPath))
            {
                var review = EditorSceneManager.OpenScene(ReviewPath, OpenSceneMode.Additive);
                try
                {
                    var poi = review.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true))
                        .FirstOrDefault(p => p.poiId == PoiId + ".review");
                    var preview = poi ? poi.transform.Find("Layout Preview") ??
                        poi.transform.Find("Generated Layout Preview") : null;
                    if (poi)
                    {
                        if (preview) UnityEngine.Object.DestroyImmediate(preview.gameObject);
                        poi.InvalidateLayout();
                        var rebuilt = poi.InstantiateStructures("Generated Layout Preview");
                        PopulatePreview(poi, rebuilt.transform, props);
                        EditorSceneManager.SaveScene(review);
                    }
                }
                finally { EditorSceneManager.CloseScene(review, true); }
                return; // retain user-authored edits on repeat runs
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var data = new TerrainData { heightmapResolution = 129, size = new Vector3(256, 40, 256) };
                var heights = new float[129, 129];
                for (int z = 0; z < 129; z++) for (int x = 0; x < 129; x++) heights[z, x] = .1f;
                data.SetHeights(0, 0, heights);
                var terrainPath = Root + "/ReviewTerrain.asset";
                var oldData = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
                if (oldData) { UnityEngine.Object.DestroyImmediate(data); data = oldData; }
                else AssetDatabase.CreateAsset(data, terrainPath);
                var ground = Terrain.CreateTerrainGameObject(data);
                ground.name = "Pueblo review ground";
                ground.transform.position = new Vector3(-128, -4, -128);
                var camera = new GameObject("Pueblo review camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(90, 80, -100);
                camera.transform.LookAt(Vector3.zero);
                camera.farClipPlane = 500;
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(42, -35, 0);
                var treadmillRoot = new GameObject("Inactive review treadmill");
                var treadmill = treadmillRoot.AddComponent<PlanetTerrainTreadmill>();
                treadmill.enabled = false;
                treadmill.SetTileVerticalOffset(0);
                treadmill.ConfigureTarget(camera.transform);
                treadmillRoot.SetActive(false); // flat review terrain supplies the collider
                var root = new GameObject("Pueblo Settlement Review");
                var poi = root.AddComponent<BanditOutpost>();
                poi.definition = definition;
                poi.treadmill = treadmill;
                poi.poiId = PoiId + ".review";
                poi.seed = definition.seed;
                poi.siteSelected = true;
                poi.stream = false;
                var population = root.AddComponent<OutpostPropPopulation>();
                population.definition = props;
                var preview = poi.InstantiateStructures("Generated Layout Preview");
                PopulatePreview(poi, preview.transform, props);
                EditorSceneManager.SaveScene(scene, ReviewPath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
        }

        static void InstallMain(BanditOutpostDefinition definition, OutpostPropDefinition props)
        {
            var main = SceneManager.GetSceneByPath(MainPath);
            bool opened = !main.isLoaded;
            if (opened) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);
            try
            {
                var all = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BanditOutpost>(true)).ToArray();
                var existing = all.FirstOrDefault(p => p.poiId == PoiId);
                var original = all.FirstOrDefault(p => p.poiId == "zandea.frontier.outpost.1") ??
                    all.FirstOrDefault(p => p != existing);
                if (!original) throw new InvalidOperationException("Main's original outpost is missing.");
                var treadmill = original.treadmill;
                if (!treadmill) throw new InvalidOperationException("Main's original outpost has no terrain treadmill.");
                bool newSite = !existing;
                if (!existing)
                {
                    var root = new GameObject("Pueblo Settlement");
                    SceneManager.MoveGameObjectToScene(root, main);
                    existing = root.AddComponent<BanditOutpost>();
                    existing.poiId = PoiId;
                }
                existing.definition = definition;
                existing.treadmill = treadmill;
                if (newSite) existing.seed = definition.seed;
                var population = existing.GetComponent<OutpostPropPopulation>();
                if (!population) population = existing.gameObject.AddComponent<OutpostPropPopulation>();
                population.definition = props;
                if (!existing.siteSelected)
                {
                    bool found = false;
                    for (int attempt = 0; attempt < 12 && !found; attempt++)
                    {
                        var start = original.stableSite + new Vector2(1500 + attempt * 170, 700 + attempt * 130);
                        if (!OutpostTerrain.FindSite(treadmill, definition, start, out var candidate, out var height)) continue;
                        if (Vector2.Distance(candidate, original.stableSite) < 800) continue;
                        float tile = treadmill.TileSizeMeters;
                        float x = Mathf.Repeat(candidate.x, tile), z = Mathf.Repeat(candidate.y, tile);
                        float margin = Mathf.Max(definition.dimensions.x, definition.dimensions.y) / 2 + 15;
                        if (x < margin || z < margin || x > tile - margin || z > tile - margin) continue;
                        existing.stableSite = candidate;
                        existing.siteHeight = height;
                        existing.siteSelected = found = true;
                    }
                    if (!found)
                    {
                        if (newSite) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                        throw new InvalidOperationException("No second site found at least 800 m from the bandit outpost. Main was not saved.");
                    }
                }
                var generatedPreview = existing.transform.Find("Generated Layout Preview");
                if (generatedPreview) UnityEngine.Object.DestroyImmediate(generatedPreview.gameObject);
                existing.InvalidateLayout();
                var preview = existing.InstantiateStructures("Generated Layout Preview");
                preview.tag = "EditorOnly";
                PopulatePreview(existing, preview.transform, props);
                EditorUtility.SetDirty(existing);
                EditorUtility.SetDirty(population);
                EditorSceneManager.MarkSceneDirty(main);
                EditorSceneManager.SaveScene(main);
            }
            finally { if (opened) EditorSceneManager.CloseScene(main, true); }
        }

        // A visual-only edit-mode copy makes the settlement readable before Play Mode.
        // Runtime props are spawned independently with stable IDs by OutpostPropPopulation.
        static void PopulatePreview(BanditOutpost poi, Transform preview, OutpostPropDefinition props)
        {
            var root = new GameObject("Prop Preview");
            root.transform.SetParent(preview, false);
            int index = 0;
            var pots = props.ceramicPotVariants != null
                ? props.ceramicPotVariants.Where(p => p).ToArray() : Array.Empty<GameObject>();
            if (pots.Length == 0) pots = new[] { props.ceramicPot };
            int potIndex = 0;
            void AddPot(Vector2 center, Vector2 offset, Quaternion turn)
            {
                var pot = pots[potIndex++ % pots.Length];
                PreviewProp(pot, pot == props.ceramicPot ? props.potScale : 1,
                    center, offset, turn, poi, root.transform, index++);
            }
            foreach (var shade in poi.Layout.structures.Where(p => p.kind == ReservationKind.Shade))
            {
                var turn = Quaternion.Euler(0, shade.yaw, 0);
                foreach (var offset in new[] { new Vector2(-2, -1), new Vector2(0, -1.2f), new Vector2(2, -1) })
                    AddPot(shade.position, offset, turn);
                foreach (var offset in new[] { new Vector2(-1.5f, 1), new Vector2(1.5f, 1) })
                    PreviewProp(props.wickerBasket, 1, shade.position, offset, turn, poi, root.transform, index++);
            }
            var storageAreas = poi.Layout.reservations.Where(r =>
                r.kind == ReservationKind.FutureProp && r.category == "Storage").ToArray();
            if (storageAreas.Length > 0)
            {
                var storage = storageAreas[0];
                foreach (var offset in new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(-2, 2), new Vector2(2, 2) })
                    PreviewProp(props.wickerBasket, 1, storage.center, offset, Quaternion.identity, poi, root.transform, index++);
                AddPot(storage.center, new Vector2(0, 2), Quaternion.identity);
                PreviewProp(props.barrel, 1, storage.center, new Vector2(-1, 0), Quaternion.identity,
                    poi, root.transform, index++);
                PreviewProp(props.crate, props.crateScale, storage.center, new Vector2(1, 0), Quaternion.identity,
                    poi, root.transform, index++, 90);
                PreviewProp(props.sack, props.sackScale, storage.center, new Vector2(0, -2), Quaternion.identity,
                    poi, root.transform, index++);
                if (props.extraStorageProps != null)
                    foreach (var extra in props.extraStorageProps)
                        PreviewProp(extra, 1, storage.center, new Vector2(0, 3), Quaternion.identity,
                            poi, root.transform, index++);
            }
        }

        static void PreviewProp(GameObject prefab, float scale, Vector2 center, Vector2 offset,
            Quaternion turn, BanditOutpost poi, Transform parent, int index, float pitch = 0)
        {
            if (!prefab) return;
            var rotated = turn * new Vector3(offset.x, 0, offset.y);
            var local = center + new Vector2(rotated.x, rotated.z);
            var instance = UnityEngine.Object.Instantiate(prefab, poi.ToWorld(local),
                poi.Rotation * turn * Quaternion.Euler(pitch, 0, 0), parent);
            instance.name = "Preview " + index + " - " + prefab.name;
            instance.transform.localScale = prefab.transform.localScale * scale;
            var bounds = RenderBounds(instance);
            float ground = poi.Center.y;
            float nearest = float.PositiveInfinity;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (!terrain || !terrain.enabled || !terrain.terrainData) continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (instance.transform.position.x < origin.x || instance.transform.position.x > origin.x + size.x ||
                    instance.transform.position.z < origin.z || instance.transform.position.z > origin.z + size.z) continue;
                float candidate = terrain.SampleHeight(instance.transform.position) + origin.y;
                float delta = Mathf.Abs(candidate - poi.Center.y);
                if (delta < nearest && delta < 30f) { ground = candidate; nearest = delta; }
            }
            instance.transform.position += Vector3.up * (ground - bounds.min.y);
        }
    }
}
