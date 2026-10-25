using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    /// <summary>Creates fitted, rigid preview prefabs from the curated Downloads inventory.</summary>
    public static class DownloadedWardrobeImporter
    {
        const string Root = "Assets/CharacterCreator/ImportedDownloads";
        const string Sources = Root + "/SourceMeshes";
        const string Items = Root + "/Items";
        const string ModelPath = "Assets/CharacterCreator/Models/male_creator.glb";
        const string CatalogPath = "Assets/CharacterCreator/CosmeticCatalog.asset";
        const string PreviewNote = "Rigid visual preview fitted to the neutral CC_Base body. Needs skinning and morphs before animated gameplay.";
        const string HeadNote = "Rigid head attachment fitted to the neutral CC_Base head. Review clipping at face morph extremes.";

        sealed class Spec
        {
            public readonly string name, extension, bone, family, texture, sourceName;
            public readonly AppearanceSlot slot;
            public readonly Vector3 center;
            public readonly float width, heightStretch, depthStretch;
            public Spec(string name, AppearanceSlot slot, string bone, Vector3 center, float width, string family,
                string extension = ".glb", string texture = null, float heightStretch = 1f, float depthStretch = 1f,
                string sourceName = null)
            { this.name = name; this.slot = slot; this.bone = bone; this.center = center; this.width = width;
                this.family = family; this.extension = extension; this.texture = texture; this.sourceName = sourceName;
                this.heightStretch = heightStretch; this.depthStretch = depthStretch; }
        }

        static readonly Spec[] Specs =
        {
            new Spec("scarf_wrap_a", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.58f, 0), .33f, "desert"),
            new Spec("scarf_wrap_b", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.58f, 0), .33f, "desert"),
            new Spec("ragged_skirt_a", AppearanceSlot.Legs, "CC_Base_Hip_02", new Vector3(0, .9f, 0), .5f, "desert", depthStretch: 1.2f),
            new Spec("desert_hood_a", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.72f, -.04f), .28f, "desert", depthStretch: 1.4f),
            new Spec("scavenger_vest", AppearanceSlot.Torso, "CC_Base_Spine02_035", new Vector3(0, 1.28f, 0), .55f, "scavenger", depthStretch: 1.2f),
            new Spec("desert_hood_b", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.72f, -.04f), .28f, "desert", depthStretch: 1.4f),
            new Spec("leather_glove_a", AppearanceSlot.Hands, "CC_Base_L_Hand_055", new Vector3(-.84f, 1.46f, .02f), .13f, "scavenger"),
            new Spec("armored_torso", AppearanceSlot.Torso, "CC_Base_Spine02_035", new Vector3(0, 1.27f, 0), 1.84f, "raider", depthStretch: 1.1f),
            new Spec("leather_glove_b", AppearanceSlot.Hands, "CC_Base_R_Hand_081", new Vector3(.84f, 1.46f, .02f), .13f, "scavenger"),
            new Spec("ragged_skirt_b", AppearanceSlot.Legs, "CC_Base_Hip_02", new Vector3(0, .9f, 0), .5f, "desert", depthStretch: 1.2f),
            new Spec("shoulder_harness_a", AppearanceSlot.OuterArmor, "CC_Base_Spine02_035", new Vector3(0, 1.43f, 0), .58f, "raider", depthStretch: 1.15f),
            new Spec("spiked_helmet_a", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.76f, -.04f), .27f, "raider", depthStretch: 1.5f),
            new Spec("ragged_skirt_c", AppearanceSlot.Legs, "CC_Base_Hip_02", new Vector3(0, .9f, 0), .5f, "desert", depthStretch: 1.2f),
            new Spec("long_wild_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.755f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.75f),
            new Spec("short_spiky_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.765f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.55f),
            new Spec("wrapped_boot", AppearanceSlot.Feet, "CC_Base_R_Foot_022", new Vector3(.11f, .14f, .02f), .12f, "desert"),
            new Spec("swept_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.755f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.75f),
            new Spec("long_strand_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.755f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.6f),
            new Spec("shoulder_harness_b", AppearanceSlot.OuterArmor, "CC_Base_Spine02_035", new Vector3(0, 1.43f, 0), .58f, "raider", depthStretch: 1.15f),
            new Spec("dreadlocks_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.755f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.6f),
            new Spec("spiked_helmet_b", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.76f, -.04f), .27f, "raider", depthStretch: 1.5f),
            new Spec("bronze_spiked_helmet", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.76f, -.045f), .285f, "raider", depthStretch: 1.55f, sourceName: "spiked_helmet_a"),
            new Spec("black_guard_helmet", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.76f, -.045f), .285f, "raider", depthStretch: 1.55f, sourceName: "spiked_helmet_b"),
            new Spec("messy_crop_hair", AppearanceSlot.Hair, "CC_Base_Head_038", new Vector3(0, 1.765f, -.035f), .23f, "wild", heightStretch: 1.06f, depthStretch: 1.55f),
            new Spec("wrapped_headgear", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.72f, -.04f), .28f, "desert", depthStretch: 1.4f),
            new Spec("horned_hair_beard", AppearanceSlot.Headwear, "CC_Base_Head_038", new Vector3(0, 1.72f, -.04f), .3f, "wild", depthStretch: 1.45f),
            new Spec("scavenger_outfit", AppearanceSlot.OuterArmor, "CC_Base_Hip_02", new Vector3(0, .91f, 0), .85f, "scavenger", depthStretch: 1.1f),
            new Spec("faun_beard", AppearanceSlot.Beard, "CC_Base_Head_038", new Vector3(0, 1.59f, .09f), .16f, "wild", ".obj", "brown.png"),
            new Spec("beard_sigmund", AppearanceSlot.Beard, "CC_Base_Head_038", new Vector3(0, 1.59f, .09f), .16f, "wild", ".obj", "Material_Diffuse_Color1_d.png"),
            new Spec("beardviking", AppearanceSlot.Beard, "CC_Base_Head_038", new Vector3(0, 1.57f, .09f), .16f, "wild", ".obj", "BeardViking.png"),
            new Spec("moustacheviking", AppearanceSlot.Beard, "CC_Base_Head_038", new Vector3(0, 1.62f, .09f), .15f, "wild", ".obj", "MoustacheViking.png"),
            new Spec("scruffy_beard", AppearanceSlot.Beard, "CC_Base_Head_038", new Vector3(0, 1.59f, .09f), .16f, "wild", ".obj", "beard2_texture-SMUDGE2.png")
        };

        // Authored on six CombinedStarvingBandits in Assets/Scenes/Fittings.unity.
        // These are the imported visual's local transform under the fitted prefab root.
        readonly struct HairFit
        {
            public readonly Vector3 rootPosition, position, scale;
            public readonly Quaternion rotation;
            public HairFit(Vector3 rootPosition, Vector3 position, Quaternion rotation, Vector3 scale)
            { this.rootPosition = rootPosition; this.position = position; this.rotation = rotation; this.scale = scale; }
        }

        static bool TryGetHairFit(string name, out HairFit fit)
        {
            switch (name)
            {
                case "long_wild_hair": fit = new HairFit(new Vector3(0, -.89f, -1.4f),
                    new Vector3(-.4f, -.95f, -.94f), Quaternion.identity, new Vector3(24, 24, 36)); return true;
                case "short_spiky_hair": fit = new HairFit(Vector3.zero,
                    new Vector3(-.29f, 12.6f, -1.47f), new Quaternion(-.034899585f, 0, 0, .99939084f),
                    new Vector3(12, 13, 18)); return true;
                case "swept_hair": fit = new HairFit(Vector3.zero,
                    new Vector3(.02f, 13.38f, .65f), new Quaternion(0, .71654546f, 0, .6975404f),
                    new Vector3(20, 18, 10)); return true;
                case "long_strand_hair": fit = new HairFit(Vector3.zero,
                    new Vector3(-.3f, 4.23f, -6.18f), new Quaternion(.05808675f, 0, 0, .9983116f),
                    new Vector3(16, 20, 21)); return true;
                case "dreadlocks_hair": fit = new HairFit(Vector3.zero,
                    new Vector3(0, 7.77f, 0), Quaternion.identity, new Vector3(15, 14, 18)); return true;
                case "messy_crop_hair": fit = new HairFit(Vector3.zero,
                    new Vector3(-.44f, 6.62f, .7f), Quaternion.identity,
                    new Vector3(16, 16, 19.884315f)); return true;
                default: fit = default; return false;
            }
        }

        [MenuItem("Zandea/Character Creator/Import Downloaded Wardrobe")]
        public static void Import()
        {
            if (AssetDatabase.FindAssets("t:AppearanceItemDefinition", new[] { Items }).Length > 0)
            {
                Debug.LogWarning("Wardrobe items already exist. Import is initial setup only; existing hand-fitted items and intentional deletions are preserved.");
                return;
            }
            EnsureFolder(Items);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) { Debug.LogError("Creator body is missing: " + ModelPath); return; }
            var reference = (GameObject)PrefabUtility.InstantiatePrefab(model);
            reference.transform.position = Vector3.zero;
            reference.transform.rotation = Quaternion.identity;
            var created = new Dictionary<string, AppearanceItemDefinition>();
            try
            {
                foreach (var spec in Specs)
                {
                    var existing = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + spec.name + ".asset");
                    if (existing && existing.skinned && existing.prefab)
                    {
                        // A tuned rigged item already supersedes its rigid preview.
                        created[spec.name] = existing;
                        continue;
                    }
                    var sourcePath = SourcePath(spec);
                    if (string.IsNullOrEmpty(sourcePath)) { Debug.LogWarning("Missing source for " + spec.name); continue; }
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                    if (!source) { Debug.LogWarning("Unity could not import " + sourcePath); continue; }
                    var bone = FindBone(reference.transform, spec.bone);
                    if (!bone) { Debug.LogError("Creator bone missing: " + spec.bone); continue; }
                    try { created[spec.name] = CreateItem(spec, source, bone, sourcePath); }
                    catch (Exception error) { Debug.LogError("Failed " + spec.name + ": " + error); }
                }
                PopulateCatalog(created);
                ConnectSampleScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Downloaded wardrobe imported: " + created.Count + "/" + Specs.Length + " items. Rigid clothing is for neutral-pose preview.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        [MenuItem("Zandea/Character Creator/Validate Downloaded Wardrobe")]
        public static void Validate()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (!model || !catalog) { Debug.LogError("Wardrobe validation: creator model or catalog missing"); return; }
            var reference = (GameObject)PrefabUtility.InstantiatePrefab(model);
            int checkedItems = 0, failures = 0;
            try
            {
                foreach (var spec in Specs)
                {
                    var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + spec.name + ".asset");
                    var fittedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Items + "/" + spec.name + ".prefab");
                    var bone = FindBone(reference.transform, spec.bone);
                    if (!item || !item.prefab || !fittedPrefab || !bone) { Debug.LogError("Wardrobe validation missing " + spec.name); failures++; continue; }
                    var preview = (GameObject)PrefabUtility.InstantiatePrefab(fittedPrefab);
                    try
                    {
                        preview.transform.SetParent(bone, false);
                        if (!TryGetHairFit(spec.name, out _)) preview.transform.localPosition = Vector3.zero;
                        preview.transform.localRotation = Quaternion.identity;
                        preview.transform.localScale = Vector3.one;
                        if (TryGetHairFit(spec.name, out var hairFit))
                        {
                            var hairVisual = preview.transform.childCount > 0 ? preview.transform.GetChild(0) : null;
                            if (!hairVisual ||
                                Vector3.Distance(preview.transform.localPosition, hairFit.rootPosition) > .001f ||
                                Vector3.Distance(hairVisual.localPosition, hairFit.position) > .001f ||
                                Vector3.Distance(hairVisual.localScale, hairFit.scale) > .001f ||
                                Quaternion.Angle(hairVisual.localRotation, hairFit.rotation) > .1f)
                            { Debug.LogError("Authored hair fit mismatch " + spec.name); failures++; }
                        }
                        else if (!BoundsOf(preview, out var bounds) ||
                            Vector3.Distance(bounds.center, spec.center) > .035f ||
                            Mathf.Abs(bounds.size.x - spec.width) > .035f)
                        {
                            Debug.LogError("Wardrobe fit mismatch " + spec.name + " at " + bounds.center + ", width " + bounds.size.x);
                            failures++;
                        }
                        checkedItems++;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(preview); }
                    if (item.skinned)
                    {
                        var skins = item.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        if (skins.Length == 0) { Debug.LogError("Rigged item has no skinned mesh: " + spec.name); failures++; }
                        foreach (var skin in skins)
                        {
                            if (!skin.sharedMesh || skin.bones.Length == 0) { failures++; continue; }
                            foreach (string morph in new[] { "WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular" })
                                if (skin.sharedMesh.GetBlendShapeIndex(morph) < 0)
                                { Debug.LogError(spec.name + " missing rigged morph " + morph); failures++; }
                        }
                        if (spec.name == "wrapped_boot" && skins.Length < 4)
                        { Debug.LogError("Paired wrapped boot is incomplete"); failures++; }
                    }
                }
                if (catalog.wardrobeItems == null || catalog.wardrobeItems.Length != 21)
                { Debug.LogError("Wardrobe catalog item count mismatch"); failures++; }
                if (catalog.HairVariantCount(HairStyle.Short) != 2 ||
                    catalog.HairVariantCount(HairStyle.Medium) != 1 ||
                    catalog.HairVariantCount(HairStyle.LongTiedBack) != 3 ||
                    catalog.BeardVariantCount(BeardStyle.Stubble) != 2 ||
                    catalog.BeardVariantCount(BeardStyle.ShortBeard) != 2 ||
                    catalog.BeardVariantCount(BeardStyle.FullBeard) != 1)
                { Debug.LogError("Hair or beard catalog variant count mismatch"); failures++; }
                Debug.Log("Wardrobe validation: " + checkedItems + "/" + Specs.Length + " fitted items checked, " + failures + " failures.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        static AppearanceItemDefinition CreateItem(Spec spec, GameObject source, Transform bone, string sourcePath)
        {
            var wrapper = new GameObject(spec.name + " fitted preview");
            wrapper.transform.SetPositionAndRotation(bone.position, bone.rotation);
            wrapper.transform.localScale = bone.lossyScale;
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = spec.name;
                visual.transform.SetParent(wrapper.transform, false);
                if (!string.IsNullOrEmpty(spec.texture)) AssignBeardMaterial(visual, spec, sourcePath);
                if (spec.sourceName != null) AssignHelmetVariantMaterial(visual, spec.name);
                if (!BoundsOf(visual, out var bounds)) throw new InvalidOperationException("No renderers");
                if (bounds.size.x < .001f) throw new InvalidOperationException("Mesh has zero width");
                float factor = spec.width / bounds.size.x;
                visual.transform.localScale = Vector3.Scale(visual.transform.localScale,
                    new Vector3(factor, factor * spec.heightStretch, factor * spec.depthStretch));
                if (!BoundsOf(visual, out bounds)) throw new InvalidOperationException("Cannot fit renderers");
                visual.transform.localPosition += wrapper.transform.InverseTransformVector(spec.center - bounds.center);
                wrapper.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                wrapper.transform.localScale = Vector3.one;
                if (TryGetHairFit(spec.name, out var hairFit))
                {
                    wrapper.transform.localPosition = hairFit.rootPosition;
                    visual.transform.localPosition = hairFit.position;
                    visual.transform.localRotation = hairFit.rotation;
                    visual.transform.localScale = hairFit.scale;
                }
                string prefabPath = Items + "/" + spec.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
                if (!prefab) throw new InvalidOperationException("Could not save prefab");
                string itemPath = Items + "/" + spec.name + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(itemPath);
                if (!item)
                {
                    item = ScriptableObject.CreateInstance<AppearanceItemDefinition>();
                    AssetDatabase.CreateAsset(item, itemPath);
                }
                item.name = spec.name;
                item.slot = spec.slot;
                item.prefab = prefab;
                item.attachBoneName = spec.bone;
                item.skinned = false;
                item.requiredMorphs = Array.Empty<string>();
                item.styleFamily = spec.family;
                item.hideHair = spec.slot == AppearanceSlot.Headwear && !spec.name.StartsWith("scarf_wrap", StringComparison.Ordinal);
                item.coversOutfit = spec.name == "scavenger_outfit";
                item.fitNote = spec.slot == AppearanceSlot.Hair || spec.slot == AppearanceSlot.Beard || spec.slot == AppearanceSlot.Headwear ? HeadNote : PreviewNote;
                item.randomWeight = 1f;
                EditorUtility.SetDirty(item);
                return item;
            }
            finally { UnityEngine.Object.DestroyImmediate(wrapper); }
        }

        static void AssignBeardMaterial(GameObject visual, Spec spec, string sourcePath)
        {
            string materialPath = Items + "/" + spec.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            string texturePath = Path.GetDirectoryName(sourcePath).Replace('\\', '/') + "/" + spec.texture;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_MainTex", texture);
            }
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                renderer.sharedMaterials = mats;
            }
            EditorUtility.SetDirty(material);
        }

        static void AssignHelmetVariantMaterial(GameObject visual, string name)
        {
            int index = 0;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;
                    string path = Items + "/" + name + "_material_" + index++ + ".mat";
                    var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!variant)
                    {
                        variant = new Material(materials[i]);
                        AssetDatabase.CreateAsset(variant, path);
                    }
                    var color = name == "bronze_spiked_helmet"
                        ? new Color(.77f, .48f, .22f)
                        : new Color(.18f, .25f, .3f);
                    if (variant.HasProperty("_BaseColor")) variant.SetColor("_BaseColor", color);
                    if (variant.HasProperty("_Color")) variant.SetColor("_Color", color);
                    if (variant.HasProperty("baseColorFactor")) variant.SetColor("baseColorFactor", color);
                    if (variant.HasProperty("diffuseFactor")) variant.SetColor("diffuseFactor", color);
                    EditorUtility.SetDirty(variant);
                    materials[i] = variant;
                }
                renderer.sharedMaterials = materials;
            }
        }

        static string SourcePath(Spec spec)
        {
            if (spec.extension == ".glb") return Sources + "/" + (spec.sourceName ?? spec.name) + ".glb";
            foreach (string guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(spec.name)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Root + "/BeardSource/", StringComparison.Ordinal) &&
                    path.EndsWith("/" + spec.name + ".obj", StringComparison.OrdinalIgnoreCase)) return path;
            }
            return null;
        }

        static bool BoundsOf(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            return null;
        }

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static void ConnectSampleScene()
        {
            const string path = "Assets/CharacterCreator/CharacterCreatorMorphPOC.unity";
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (!sceneAsset) return;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
                foreach (var root in scene.GetRootGameObjects())
                foreach (var appearance in root.GetComponentsInChildren<CharacterAppearanceController>(true))
                {
                    var cosmetics = appearance.GetComponent<CharacterCosmeticsController>();
                    if (!cosmetics) cosmetics = appearance.gameObject.AddComponent<CharacterCosmeticsController>();
                    cosmetics.appearance = appearance;
                    cosmetics.skeletonRoot = appearance.visualRoot;
                    cosmetics.catalog = catalog;
                    EditorUtility.SetDirty(cosmetics);
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        static void PopulateCatalog(Dictionary<string, AppearanceItemDefinition> items)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (!catalog)
            {
                if (File.Exists(CatalogPath))
                {
                    string legacy = AssetDatabase.GenerateUniqueAssetPath("Assets/CharacterCreator/CosmeticCatalog.legacy.asset");
                    string error = AssetDatabase.MoveAsset(CatalogPath, legacy);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("Could not preserve legacy catalog: " + error);
                }
                catalog = ScriptableObject.CreateInstance<CosmeticCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            AppearanceItemDefinition Get(string key) => items.TryGetValue(key, out var value) ? value : null;
            catalog.shortHairVariants = new[] { Get("short_spiky_hair"), Get("messy_crop_hair") };
            catalog.mediumHairVariants = new[] { Get("swept_hair") };
            catalog.longHairVariants = new[] { Get("long_wild_hair"), Get("long_strand_hair"), Get("dreadlocks_hair") };
            catalog.stubbleVariants = new[] { Get("moustacheviking"), Get("scruffy_beard") };
            catalog.shortBeardVariants = new[] { Get("beard_sigmund"), Get("faun_beard") };
            catalog.fullBeardVariants = new[] { Get("beardviking"), Get("horned_hair_beard") };
            catalog.wardrobeItems = new List<AppearanceItemDefinition>(items.Values).FindAll(item => item && item.slot >= AppearanceSlot.Headwear).ToArray();
            catalog.shortHair = catalog.shortHairVariants[0];
            catalog.mediumHair = catalog.mediumHairVariants[0];
            catalog.longTiedBackHair = catalog.longHairVariants[0];
            catalog.stubble = catalog.stubbleVariants[0];
            catalog.shortBeard = catalog.shortBeardVariants[0];
            catalog.fullBeard = catalog.fullBeardVariants[0];
            EditorUtility.SetDirty(catalog);
        }
    }
}


