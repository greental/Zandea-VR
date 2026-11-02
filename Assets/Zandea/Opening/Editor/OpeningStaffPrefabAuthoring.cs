using System;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

namespace Zandea.Opening.Editor
{
    public static class OpeningStaffPrefabAuthoring
    {
        const string PrefabPath = OpeningHarnessBuilder.BanditPath;
        const string Items = "Assets/CharacterCreator/ImportedDownloads/Items/";

        [MenuItem("Zandea/Opening/Dress staff slaver V2")]
        public static void DressV2()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var appearance = root.GetComponent<NPCCharacterAppearance>();
                var cosmetics = root.GetComponent<CharacterCosmeticsController>();
                if (!appearance || !cosmetics || !root.GetComponent<StarvingBandit>())
                    throw new InvalidOperationException("Opening staff V2 lacks bandit appearance components.");
                appearance.Generate(appearance.seed);
                cosmetics.headwear = Item("bandit_head_wrap");
                cosmetics.torso = Item("bandit_rib_vest");
                cosmetics.legs = Item("bandit_armored_pants");
                cosmetics.feet = Item("bandit_armored_boot");
                cosmetics.Refresh();
                var inventory = root.GetComponent<NpcInventory>();
                if (inventory) inventory.AdoptWornClothing();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Opening staff slaver V2 clothing saved in prefab; V1 is unchanged.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static AppearanceItemDefinition Item(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + name + ".asset");
            if (!item) throw new InvalidOperationException("Missing clothing item: " + name);
            return item;
        }
    }
}
