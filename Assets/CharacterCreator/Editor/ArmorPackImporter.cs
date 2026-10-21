using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    /// <summary>Builds gameplay wardrobe and weapons from the conservatively cleaned Armor download pack.</summary>
    public static class ArmorPackImporter
    {
        const string Root = "Assets/CharacterCreator/ImportedArmor";
        const string Sources = Root + "/SourceMeshes";
        const string Items = "Assets/CharacterCreator/ImportedDownloads/Items";
        const string Weapons = "Assets/MeleeCombat/ImportedArmorWeapons";
        const string Model = "Assets/CharacterCreator/Models/male_creator.glb";
        const string CatalogPath = "Assets/CharacterCreator/CosmeticCatalog.asset";
        const string ManifestPath = "Tools/ArmorImport/armor_manifest.json";

        [Serializable] sealed class Manifest { public Entry[] items; }
        [Serializable] sealed class Entry { public string folder, source, name, slot; }

        static readonly HashSet<string> RiggedNames = new()
        {
            "bandit_rib_vest", "bandit_long_coat", "bandit_armored_skirt", "bandit_rib_outfit",
            "bandit_shoulder_pad", "bandit_armored_pants", "bandit_mercenary_chest", "bandit_rust_shoulder",
            "paladin_ragged_skirt", "paladin_medieval_chest", "paladin_metal_shoulder"
        };

        [MenuItem("Tools/Character Creator/Import Cleaned Armor Pack")]
        public static void Import()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (manifest?.items == null || !model) throw new InvalidOperationException("Armor manifest or creator body is missing");
            EnsureFolder(Root); EnsureFolder(Items); EnsureFolder(Weapons);
            var reference = (GameObject)PrefabUtility.InstantiatePrefab(model);
            reference.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var created = new List<AppearanceItemDefinition>();
            int weaponCount = 0;
            try
            {
                foreach (var entry in manifest.items)
                {
                    string sourcePath = Sources + "/" + entry.folder + "/" + entry.name + ".glb";
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                    if (!source) { Debug.LogError("Cleaned armor source is missing: " + sourcePath); continue; }
                    if (entry.slot == "Weapon") { CreateWeapon(entry, source); weaponCount++; continue; }
                    created.Add(CreateWearable(entry, source, reference));
                }
                AppendCatalog(created);
                AssetDatabase.SaveAssets();
                RiggedWardrobeBaker.BakeItems(RiggedNames);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Armor pack ready: {created.Count} wearable choices and {weaponCount} bandit weapon choices.");
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        [MenuItem("Tools/Character Creator/Validate Cleaned Armor Pack")]
        public static void Validate()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (manifest?.items == null || !catalog) throw new InvalidOperationException("Armor manifest or catalog is missing");
            int wearables = 0, weapons = 0;
            foreach (var entry in manifest.items)
            {
                if (entry.slot == "Weapon")
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Weapons + "/" + entry.name + ".prefab");
                    var weapon = prefab ? prefab.GetComponent<PhysicalMeleeWeapon>() : null;
                    if (!prefab || !weapon || !prefab.GetComponent<Rigidbody>() || !prefab.GetComponent<Collider>() ||
                        !weapon.grip || !weapon.rightGrip || !weapon.leftGrip || !weapon.secondaryGrip ||
                        !prefab.transform.Find("PositiveStrikingTip") || !prefab.transform.Find("NegativeStrikingTip"))
                        throw new InvalidOperationException("Incomplete imported weapon prefab: " + entry.name);
                    weapons++;
                    continue;
                }
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + entry.name + ".asset");
                if (!item || !item.prefab || item.slot.ToString() != entry.slot ||
                    catalog.wardrobeItems == null || !catalog.wardrobeItems.Contains(item))
                    throw new InvalidOperationException("Incomplete imported appearance item: " + entry.name);
                if (RiggedNames.Contains(entry.name) && (!item.skinned ||
                    item.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0))
                    throw new InvalidOperationException("Imported body armor was not rigged: " + entry.name);
                wearables++;
            }
            if (catalog.fullBeardVariants == null ||
                !catalog.fullBeardVariants.Any(item => item && item.name == "bandit_pointed_beard") ||
                !catalog.fullBeardVariants.Any(item => item && item.name == "paladin_full_beard"))
                throw new InvalidOperationException("Both imported beards were not added to the beard picker");
            Debug.Log($"Armor pack validation PASS: {wearables} wearable Inspector choices, 2 beard variants, {weapons} physical bandit weapon choices, and {RiggedNames.Count} CC-rigged armor pieces.");
        }

        static AppearanceItemDefinition CreateWearable(Entry entry, GameObject source, GameObject reference)
        {
            var slot = (AppearanceSlot)Enum.Parse(typeof(AppearanceSlot), entry.slot);
            string boneName = BoneFor(slot);
            var bone = reference.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == boneName);
            if (!bone) throw new InvalidOperationException("Missing creator bone " + boneName);
            var wrapper = new GameObject(entry.name + " fitted");
            wrapper.transform.SetPositionAndRotation(bone.position, bone.rotation);
            wrapper.transform.localScale = bone.lossyScale;
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = entry.name + " visual";
                visual.transform.SetParent(wrapper.transform, false);
                if (!BoundsOf(visual, out var bounds)) throw new InvalidOperationException("No renderer in " + entry.name);
                var fit = Fit(entry.name, slot);
                float factor = fit.width / Mathf.Max(.0001f, bounds.size.x);
                visual.transform.localScale *= factor;
                if (!BoundsOf(visual, out bounds)) throw new InvalidOperationException("Could not fit " + entry.name);
                visual.transform.localPosition += wrapper.transform.InverseTransformVector(fit.center - bounds.center);
                wrapper.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                wrapper.transform.localScale = Vector3.one;
                string prefabPath = Items + "/" + entry.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
                string itemPath = Items + "/" + entry.name + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(itemPath);
                if (!item) { item = ScriptableObject.CreateInstance<AppearanceItemDefinition>(); AssetDatabase.CreateAsset(item, itemPath); }
                item.slot = slot; item.prefab = prefab; item.attachBoneName = boneName; item.skinned = false;
                item.requiredMorphs = Array.Empty<string>(); item.hideRendererNames = Array.Empty<string>();
                item.styleFamily = entry.folder == "HeavyPaladin" ? "paladin" : "bandit";
                item.hideHair = slot == AppearanceSlot.Headwear; item.coversOutfit = entry.name.Contains("outfit") || entry.name.Contains("coat");
                item.randomWeight = 1f;
                item.fitNote = RiggedNames.Contains(entry.name) ? "Prepared for CC_Base skin-weight and morph transfer." : "Rigid accessory fitted to the CC_Base skeleton.";
                EditorUtility.SetDirty(item);
                return item;
            }
            finally { UnityEngine.Object.DestroyImmediate(wrapper); }
        }

        static void CreateWeapon(Entry entry, GameObject source)
        {
            var root = new GameObject(entry.name);
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = "Cleaned visual"; visual.transform.SetParent(root.transform, false);
                if (!BoundsOf(visual, out var bounds)) throw new InvalidOperationException("No weapon renderer");
                int axis = bounds.size.x > bounds.size.y ? (bounds.size.x > bounds.size.z ? 0 : 2) : (bounds.size.y > bounds.size.z ? 1 : 2);
                visual.transform.localRotation = axis == 0 ? Quaternion.Euler(0, -90, 0) : axis == 1 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                if (!BoundsOf(visual, out bounds)) throw new InvalidOperationException("Could not orient weapon");
                float targetLength = entry.name.Contains("spear") ? 2.1f : entry.name.Contains("greatsword") ? 1.45f : 1.05f;
                visual.transform.localScale *= targetLength / Mathf.Max(.001f, bounds.size.z);
                BoundsOf(visual, out bounds);
                visual.transform.position -= bounds.center;
                BoundsOf(visual, out bounds);
                var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center;
                collider.size = new Vector3(Mathf.Max(.035f, bounds.size.x), Mathf.Max(.035f, bounds.size.y), bounds.size.z);
                var body = root.AddComponent<Rigidbody>(); body.mass = entry.name.Contains("spear") ? 3.5f : 2.4f;
                var grip = Marker(root.transform, "Grip", new Vector3(0, 0, bounds.min.z + Mathf.Min(.24f, bounds.size.z * .22f)));
                var secondary = Marker(root.transform, "Secondary Grip", new Vector3(0, 0, grip.localPosition.z + .28f));
                var positive = Marker(root.transform, "PositiveStrikingTip", new Vector3(0, 0, bounds.max.z));
                var negative = Marker(root.transform, "NegativeStrikingTip", new Vector3(0, 0, bounds.min.z));
                var weapon = root.AddComponent<PhysicalMeleeWeapon>();
                weapon.weaponId = entry.name; weapon.grip = grip; weapon.rightGrip = grip; weapon.leftGrip = grip;
                weapon.secondaryGrip = secondary; weapon.mass = body.mass; weapon.strikeType = entry.name.Contains("spear") ? PhysicalMeleeWeapon.StrikeType.Piercing : PhysicalMeleeWeapon.StrikeType.Cutting;
                weapon.preset = entry.name.Contains("spear") ? PhysicalMeleeWeapon.PhysicsPreset.HeavyPolearm : PhysicalMeleeWeapon.PhysicsPreset.HeavySword;
                weapon.useMeshContactShape = false; weapon.npcPickupRoll = 180f;
                _ = positive; _ = negative;
                PrefabUtility.SaveAsPrefabAsset(root, Weapons + "/" + entry.name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static (Vector3 center, float width) Fit(string name, AppearanceSlot slot)
        {
            if (name.Contains("coat") || name.Contains("outfit")) return (new Vector3(0, .98f, 0), .72f);
            return slot switch
            {
                AppearanceSlot.Beard => (new Vector3(0, 1.59f, .09f), .17f),
                AppearanceSlot.Headwear => (new Vector3(0, 1.74f, -.035f), .30f),
                AppearanceSlot.Torso => (new Vector3(0, 1.28f, 0), .56f),
                AppearanceSlot.Legs => (new Vector3(0, .88f, 0), .52f),
                AppearanceSlot.Hands => (new Vector3(.84f, 1.46f, .02f), .15f),
                AppearanceSlot.Feet => (new Vector3(.11f, .14f, .04f), .15f),
                _ => (new Vector3(0, 1.39f, 0), .62f)
            };
        }

        static string BoneFor(AppearanceSlot slot) => slot switch
        {
            AppearanceSlot.Beard or AppearanceSlot.Headwear => "CC_Base_Head_038",
            AppearanceSlot.Torso or AppearanceSlot.OuterArmor => "CC_Base_Spine02_035",
            AppearanceSlot.Legs => "CC_Base_Hip_02",
            AppearanceSlot.Hands => "CC_Base_R_Hand_081",
            AppearanceSlot.Feet => "CC_Base_R_Foot_022",
            _ => "CC_Base_Hip_02"
        };

        static Transform Marker(Transform parent, string name, Vector3 position)
        { var marker = new GameObject(name).transform; marker.SetParent(parent, false); marker.localPosition = position; return marker; }

        static bool BoundsOf(GameObject root, out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            { if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds); }
            return found;
        }

        static void AppendCatalog(IEnumerable<AppearanceItemDefinition> items)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(CatalogPath);
            if (!catalog) throw new InvalidOperationException("Missing cosmetic catalog");
            catalog.wardrobeItems = (catalog.wardrobeItems ?? Array.Empty<AppearanceItemDefinition>()).Concat(items)
                .Where(x => x).GroupBy(x => AssetDatabase.GetAssetPath(x)).Select(g => g.First()).ToArray();
            var beards = items.Where(x => x && x.slot == AppearanceSlot.Beard);
            catalog.fullBeardVariants = (catalog.fullBeardVariants ?? Array.Empty<AppearanceItemDefinition>()).Concat(beards)
                .Where(x => x).GroupBy(x => AssetDatabase.GetAssetPath(x)).Select(g => g.First()).ToArray();
            EditorUtility.SetDirty(catalog);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
