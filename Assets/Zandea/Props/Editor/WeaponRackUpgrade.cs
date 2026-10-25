using System.Linq;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;

namespace Zandea.Props.Editor
{
    [InitializeOnLoad]
    static class WeaponRackUpgrade
    {
        const string Rack="Assets/Zandea/Props/Prefabs/rack.prefab",Folder="Assets/Zandea/Props/WeaponItems";
        static WeaponRackUpgrade()=>EditorApplication.delayCall+=Upgrade;
        [MenuItem("Zandea/Props/Upgrade multi-weapon rack")]
        static void Upgrade()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!AssetDatabase.LoadAssetAtPath<GameObject>(Rack))return;
            if(!AssetDatabase.IsValidFolder(Folder)){if(!AssetDatabase.IsValidFolder("Assets/Zandea/Props/WeaponItems"))AssetDatabase.CreateFolder("Assets/Zandea/Props","WeaponItems");}
            var paths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/MyAssets/prefabs/weapons"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path=>AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<PhysicalMeleeWeapon>(true)).OrderBy(path=>path).Take(4).ToArray();
            var root=PrefabUtility.LoadPrefabContents(Rack);
            try
            {
                var rack=root.GetComponent<PropWeaponRack>();if(!rack)rack=root.AddComponent<PropWeaponRack>();
                rack.weaponPrefabs=paths.Select(path=>AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToArray();
                rack.items=new NpcItemDefinition[paths.Length];
                for(int i=0;i<paths.Length;i++)
                {
                    var weapon=rack.weaponPrefabs[i].GetComponentInChildren<PhysicalMeleeWeapon>(true);string assetPath=Folder+"/"+System.IO.Path.GetFileNameWithoutExtension(paths[i])+".asset";
                    var item=AssetDatabase.LoadAssetAtPath<NpcItemDefinition>(assetPath);
                    if(!item){item=ScriptableObject.CreateInstance<NpcItemDefinition>();AssetDatabase.CreateAsset(item,assetPath);}
                    item.itemId=weapon.weaponId;item.displayName=rack.weaponPrefabs[i].name;item.category=NpcItemCategory.Weapon;item.defaultSlot=NpcEquipmentSlot.MainWeapon;item.worldPrefab=rack.weaponPrefabs[i];item.weight=Mathf.Max(.5f,weapon.mass);EditorUtility.SetDirty(item);rack.items[i]=item;
                }
                PrefabUtility.SaveAsPrefabAsset(root,Rack);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
    }
}
