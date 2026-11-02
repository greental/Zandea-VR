using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
namespace Zandea.FriendlyNPC.Editor
{
    public static class TradeCatalogV2Validation
    {
        const string Report="Temp/TradeCatalogV2/validation.txt";
        [MenuItem("Zandea/Friendly NPC/Validate Trade Catalog V2")]
        public static void Run()
        {
            var issues=new List<string>();int weapons=0,garments=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/MyAssets/prefabs/weapons"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(Path.GetDirectoryName(path)?.Replace('\\','/')!="Assets/MyAssets/prefabs/weapons" || Path.GetFileNameWithoutExtension(path)=="R4-PowerCell")continue;
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source || !source.GetComponent<XRGrabInteractable>())continue;
                var original=source.GetComponent<TradeItem>();
                string id=new string(Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray()).Trim('-');
                var candidate=original && original.definition && original.definition.valueCats>0 ? source : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Zandea/FriendlyNPC/TradeCatalogV2/Prefabs/"+id+"_V2.prefab");
                Check(candidate,path);weapons++;
            }
            var definitions=AssetDatabase.FindAssets("t:NpcItemDefinition").Select(g=>AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d=>d && d.equippedAppearance).ToArray();
            foreach(var guid in AssetDatabase.FindAssets("t:AppearanceItemDefinition",new[]{"Assets/CharacterCreator"}))
            {
                var appearance=AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if(!appearance || !appearance.prefab || appearance.slot==AppearanceSlot.Hair || appearance.slot==AppearanceSlot.Beard)continue;
                var item=definitions.FirstOrDefault(d=>d.equippedAppearance==appearance && d.worldPrefab && d.worldPrefab.GetComponent<TradeItem>());
                if(!item)issues.Add("No tradeable loot for "+appearance.name);
                else Check(item.worldPrefab,appearance.name);
                garments++;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,$"Weapons: {weapons}; clothing/armor: {garments}; issues: {issues.Count}\n"+string.Join("\n",issues));
            if(issues.Count>0)throw new InvalidOperationException(string.Join("\n",issues));
            Debug.Log("PASS Trade Catalog V2: "+weapons+" weapons, "+garments+" clothing/armor appearances have tradeable physical prefabs and prices.");
            void Check(GameObject go,string label)
            {
                var trade=go?go.GetComponent<TradeItem>():null;
                if(!go || !go.GetComponent<XRGrabInteractable>() || !go.GetComponent<Rigidbody>() || !trade || !trade.definition || trade.definition.valueCats<=0 || trade.definition.worldPrefab!=go)
                    issues.Add("Missing physical trade/price linkage: "+label);
            }
        }
    }
}
