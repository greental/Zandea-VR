using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using KenshiVR.MeleeCombat.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.FriendlyNPC.Editor
{
    public static class TradeCatalogV2Builder
    {
        const string Root = "Assets/Zandea/FriendlyNPC/TradeCatalogV2";
        const string Definitions = Root + "/Definitions";
        const string Prefabs = Root + "/Prefabs";
        static readonly string[] WeaponFolders = { "Assets/MyAssets/prefabs/weapons" };
        [MenuItem("Zandea/Friendly NPC/Build Trade Catalog V2")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (new DriveInfo("D:").AvailableFreeSpace < 20L * 1024 * 1024 * 1024) throw new InvalidOperationException("D: has less than 20 GiB free.");
            EnsureFolder(Root); EnsureFolder(Definitions); EnsureFolder(Prefabs);
            int weapons = BuildWeapons();
            int garments = BuildGarments();
            BuildWearableCatalog();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"Trade Catalog V2: {weapons} weapons and {garments} clothing/armor definitions checked.");
        }
        static int BuildWeapons()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponFolders))
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(sourcePath)?.Replace('\\','/') != WeaponFolders[0]) continue;
                string name = Path.GetFileNameWithoutExtension(sourcePath);
                if (name == "R4-PowerCell") continue;
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (!source || !source.GetComponent<XRGrabInteractable>()) continue;
                var trade = source.GetComponent<TradeItem>();
                if (trade && trade.definition && trade.definition.valueCats > 0)
                {
                    if (trade.definition.worldPrefab != source)
                    {
                        trade.definition.worldPrefab = source;
                        EditorUtility.SetDirty(trade.definition);
                    }
                    count++;
                    continue;
                }
                string id = Slug(name);
                string v1 = Prefabs + "/" + id + "_V1.prefab";
                string v2 = Prefabs + "/" + id + "_V2.prefab";
                CopyOnce(sourcePath, v1); CopyOnce(sourcePath, v2);
                var definition = GetDefinition(id, name, NpcItemCategory.Weapon, WeaponPrice(name), 2f);
                var root = PrefabUtility.LoadPrefabContents(v2);
                try { var component = root.GetComponent<TradeItem>(); if (!component) component = root.AddComponent<TradeItem>(); component.definition = definition; component.ownerId = "player"; PrefabUtility.SaveAsPrefabAsset(root, v2); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                definition.worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(v2);
                EditorUtility.SetDirty(definition); count++;
            }
            return count;
        }
        static int BuildGarments()
        {
            var existing = AssetDatabase.FindAssets("t:NpcItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d && d.equippedAppearance).GroupBy(d => d.equippedAppearance).ToDictionary(g => g.Key, g => g.First());
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition", new[] { "Assets/CharacterCreator" }))
            {
                var appearance = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (!appearance || !appearance.prefab || appearance.slot == AppearanceSlot.Hair || appearance.slot == AppearanceSlot.Beard) continue;
                string name = appearance.name.Replace('_',' ');
                string id = Slug(appearance.name);
                if (!existing.TryGetValue(appearance, out var definition))
                {
                    definition = GetDefinition(id, name, NpcItemCategory.Clothing, GarmentPrice(name, appearance.slot), 1f);
                    definition.equippedAppearance = appearance;
                    definition.defaultSlot = appearance.slot switch { AppearanceSlot.Headwear => NpcEquipmentSlot.Headwear, AppearanceSlot.Legs => NpcEquipmentSlot.Legs, AppearanceSlot.Torso or AppearanceSlot.OuterArmor => NpcEquipmentSlot.Torso, _ => NpcEquipmentSlot.None };
                    EditorUtility.SetDirty(definition);
                }
                if (definition.worldPrefab)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(definition.worldPrefab);
                    if (!sourcePath.StartsWith(Root, StringComparison.Ordinal))
                    {
                        string v1 = Prefabs + "/" + id + "_V1.prefab";
                        string v2 = Prefabs + "/" + id + "_V2.prefab";
                        CopyOnce(sourcePath, v1); CopyOnce(sourcePath, v2);
                        definition.worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(v2);
                    }
                }
                else ClothingLootBaker.Bake(definition);
                if (!definition.worldPrefab) { Debug.LogWarning("Could not bake trade garment " + name); continue; }
                string path = AssetDatabase.GetAssetPath(definition.worldPrefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                try { var trade = root.GetComponent<TradeItem>(); if (!trade) trade = root.AddComponent<TradeItem>(); trade.definition = definition; trade.ownerId = "player"; PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                EditorUtility.SetDirty(definition); count++;
            }
            return count;
        }
        static void BuildWearableCatalog()
        {
            const string resources = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resources)) AssetDatabase.CreateFolder("Assets", "Resources");
            const string path = resources + "/ZandeaNpcWearables.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<NpcWearableCatalog>(path);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<NpcWearableCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }
            catalog.items.Clear();
            var definitions = AssetDatabase.FindAssets("t:NpcItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d && d.equippedAppearance && d.worldPrefab)
                .OrderByDescending(d => d.worldPrefab.GetComponent<TradeItem>() != null)
                .ThenByDescending(d => d.valueCats);
            var seen = new HashSet<AppearanceItemDefinition>();
            foreach (var item in definitions)
                if (seen.Add(item.equippedAppearance)) catalog.items.Add(item);
            catalog.weapons.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:NpcItemDefinition"))
            {
                var item = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item && item.category == NpcItemCategory.Weapon && item.worldPrefab &&
                    item.worldPrefab.GetComponent<TradeItem>()) catalog.weapons.Add(item);
            }
            EditorUtility.SetDirty(catalog);
        }
        static NpcItemDefinition GetDefinition(string id, string name, NpcItemCategory category, int price, float weight)
        {
            string path = Definitions + "/" + id + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(path);
            if (!item) { item = ScriptableObject.CreateInstance<NpcItemDefinition>(); AssetDatabase.CreateAsset(item, path); item.itemId = id; item.displayName = name; item.category = category; item.valueCats = price; item.weight = weight; }
            return item;
        }
        static int WeaponPrice(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("jkr") || n.Contains("rustfang")) return 320;
            if (n.Contains("practice")) return 80;
            if (n.Contains("staff")) return 120;
            if (n.Contains("greatsword") || n.Contains("double axe")) return 180;
            if (n.Contains("scrap")) return 45;
            if (n.Contains("heavy") || n.Contains("pole")) return 150;
            return 110;
        }
        static int GarmentPrice(string name, AppearanceSlot slot)
        {
            string n=name.ToLowerInvariant();
            if (n.Contains("rag") || n.Contains("scavenger")) return 18;
            if (n.Contains("leather") || n.Contains("wrapped") || n.Contains("scarf")) return 35;
            if (n.Contains("paladin") || n.Contains("samurai") || n.Contains("crusader")) return 160;
            if (n.Contains("armored") || n.Contains("metal") || n.Contains("mercenary")) return 110;
            return slot == AppearanceSlot.OuterArmor ? 120 : slot == AppearanceSlot.Headwear ? 55 : 65;
        }
        static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        static void CopyOnce(string source, string target) { if (!AssetDatabase.LoadAssetAtPath<GameObject>(target)) { string error = AssetDatabase.CopyAsset(source, target) ? null : "Copy failed: " + source; if (error != null) throw new InvalidOperationException(error); } }
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\','/'); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
