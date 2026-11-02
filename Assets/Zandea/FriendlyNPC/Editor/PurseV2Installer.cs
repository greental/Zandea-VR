using System;
using System.IO;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Zandea.FriendlyNPC.Editor
{
    public static class PurseV2Installer
    {
        const string Folder = "Assets/Zandea/FriendlyNPC/Generated";
        const string VisualPath = "Assets/Zandea/Props/Remaining/Models/money_bag.fbx";
        const string PlayerPath = "Assets/MyAssets/prefabs/Player/VR player - Man Creator V2.prefab";

        [MenuItem("Zandea/Merchant/Audit Main counter weapon sizes")]
        public static void AuditMainWeaponSizes()
        {
            EditorSceneManager.OpenScene("Assets/Main.unity");
            foreach (var contract in UnityEngine.Object.FindObjectsByType<Zandea.Missions.DaranContract>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!contract || !contract.gameObject.name.Contains("DaranMerchant")) continue;
                var root = contract.transform.root;
                foreach (var weapon in root.GetComponentsInChildren<PhysicalMeleeWeapon>(true))
                {
                    if (!weapon || !weapon.GetComponent<TradeItem>()) continue;
                    var renderers = weapon.GetComponentsInChildren<Renderer>(true);
                    var bounds = new Bounds(weapon.transform.position, Vector3.zero);
                    foreach (var renderer in renderers)
                        if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
                    Debug.Log("Counter weapon size: " + weapon.name + " " + bounds.size +
                        " reach=" + weapon.CombatReach + " scale=" + weapon.transform.localScale);
                }
            }
        }

        [MenuItem("Zandea/Merchant/Tune Pueblo counter weapons")]
        public static void TuneMainCounterWeapons()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Main.unity");
            int changed = 0;
            foreach (var contract in UnityEngine.Object.FindObjectsByType<Zandea.Missions.DaranContract>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!contract || !contract.gameObject.name.Contains("DaranMerchant")) continue;
                foreach (var weapon in contract.transform.root.GetComponentsInChildren<PhysicalMeleeWeapon>(true))
                {
                    if (!weapon || !weapon.GetComponent<TradeItem>() ||
                        !weapon.name.Contains("Bo Staff merchant stock")) continue;
                    // The authored stock measured 1.83 m tall on the counter.
                    // Scale this Main instance to about 1.5 m without editing
                    // the shared combat prefab or its V1 reference.
                    weapon.transform.localScale = Vector3.one * .82f;
                    weapon.combatReachMeters = 1.44232f * .82f;
                    EditorUtility.SetDirty(weapon);
                    changed++;
                }
            }
            if (changed != 1) throw new InvalidOperationException("Expected one Pueblo stock Bo Staff, found " + changed);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Scaled Pueblo counter Bo Staff to 82% of authored size.");
        }

        [MenuItem("Zandea/Merchant/Install V2 money bags in Main")]
        public static void Install()
        {
            if (new DriveInfo("D").AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("D: has less than 20 GiB free.");
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
            if (!visual) throw new InvalidOperationException("Authored money bag visual is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Zandea/Props/Remaining/Materials/money_bag_00.mat");
            if (!material) throw new InvalidOperationException("Authored money bag material is missing.");
            var personal = MakeVersion("PersonalCoinPurse", visual, material);
            var payout = MakeVersion("PayoutPouch", visual, material);

            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var attachments = player.GetComponentInChildren<BackpackUtilityAttachments>(true);
                if (!attachments) throw new InvalidOperationException("V2 player has no purse attachment.");
                attachments.personalPursePrefab = personal;
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }

            var currency = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(
                "Assets/MeleeCombat/Inventory/Cats.asset");
            var loot = AssetDatabase.LoadAssetAtPath<NpcLootTable>(
                "Assets/MeleeCombat/Inventory/Starving Bandit Loot.asset");
            var scrap = AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(
                "Assets/MeleeCombat/Inventory/Scrap Metal.asset");
            if (!currency || !loot || !scrap) throw new InvalidOperationException("Bandit loot definitions are missing.");
            // Keep the legacy asset ID/GUID so existing inventory references survive.
            currency.displayName = "Zeks";
            currency.worldPrefab = payout.gameObject;
            foreach (var entry in loot.rolls)
                if (entry.item == scrap)
                {
                    entry.item = currency;
                    entry.minimum = 1;
                    entry.maximum = 3;
                    entry.probability = .25f;
                }
            EditorUtility.SetDirty(currency);
            EditorUtility.SetDirty(loot);

            var scene = EditorSceneManager.OpenScene("Assets/Main.unity");
            int changed = 0;
            foreach (var contract in UnityEngine.Object.FindObjectsByType<Zandea.Missions.DaranContract>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (contract && contract.counter && contract.gameObject.name.Contains("DaranMerchant"))
                {
                    contract.counter.payoutPouchPrefab = payout;
                    EditorUtility.SetDirty(contract.counter);
                    changed++;
                }
            if (changed == 0) throw new InvalidOperationException("Active Pueblo Daran counter not found.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Installed V2 money bags: player, Pueblo payout, and bandit Zeks loot.");
        }

        static CurrencyPouch MakeVersion(string stem, GameObject visual, Material material)
        {
            string source = Folder + "/" + stem + ".prefab";
            string v1 = Folder + "/" + stem + "V1.prefab";
            string v2 = Folder + "/" + stem + "V2.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(v1) && !AssetDatabase.CopyAsset(source, v1))
                throw new InvalidOperationException("Could not preserve " + stem + " V1.");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(v2) && !AssetDatabase.CopyAsset(source, v2))
                throw new InvalidOperationException("Could not create " + stem + " V2.");
            var root = PrefabUtility.LoadPrefabContents(v2);
            try
            {
                var purse = root.GetComponent<CurrencyPouch>();
                if (!purse) throw new InvalidOperationException(stem + " has no CurrencyPouch.");
                purse.visualPrefab = visual;
                purse.visualMaterial = material;
                purse.mergeRadius = .28f;
                PrefabUtility.SaveAsPrefabAsset(root, v2);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(v2).GetComponent<CurrencyPouch>();
        }
    }
}
