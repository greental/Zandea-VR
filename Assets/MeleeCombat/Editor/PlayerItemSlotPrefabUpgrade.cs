using System.Linq;
using JunkerEquipment;
using UnityEditor;
using UnityEngine;

namespace KenshiVR.MeleeCombat.Editor
{
    [InitializeOnLoad]
    static class PlayerItemSlotPrefabUpgrade
    {
        static readonly string[] Players={"Assets/CharacterCreator/Player/VR player - Man Creator.prefab","Assets/MyAssets/prefabs/Player/VR player - Man Creator.prefab"};
        const string SwordPath="Assets/MyAssets/prefabs/weapons/Player practice sword - generated long blade.prefab";
        static PlayerItemSlotPrefabUpgrade()
        {
            EditorApplication.delayCall+=Upgrade;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Upgrade; };
        }
        [MenuItem("Zandea/Junker Equipment/Upgrade ordinary player item slots")]
        static void Upgrade()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            UpgradeWeapons();
            foreach(string path in Players) if(AssetDatabase.LoadAssetAtPath<GameObject>(path)) UpgradePlayer(path);
            AssetDatabase.SaveAssets();
        }
        static void UpgradeWeapons()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/MyAssets/prefabs/weapons"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid); var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var weapon=root.GetComponentInChildren<PhysicalMeleeWeapon>(true); if(!weapon)continue;
                    if(path==SwordPath&&weapon.weaponId=="bo-staff")weapon.weaponId="long-blade";
                    var item=weapon.GetComponent<JunkerEquipmentItem>(); if(!item)item=weapon.gameObject.AddComponent<JunkerEquipmentItem>();
                    item.itemId=weapon.weaponId; item.gripPose=weapon.grip; item.useGripTransformDirectly=true; item.holsterRadius=Mathf.Max(.4f,item.holsterRadius);
                    var inventory=weapon.GetComponent<VRInventoryItem>(); if(!inventory)inventory=weapon.gameObject.AddComponent<VRInventoryItem>();
                    inventory.CanStoreInBackpack=true;
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        static void UpgradePlayer(string path)
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var rig=root.GetComponentInChildren<JunkerEquipmentRig>(true); if(!rig)return;
                var all=rig.slots?.Where(slot=>slot).ToList()??new System.Collections.Generic.List<JunkerEquipmentSlot>();
                var left=Slot(rig.transform,"Left hip item",rig.meleeSheathLocalPosition,rig.meleeSheathLocalEuler,all);
                rig.meleeSheath=left.transform;
                rig.rightHipSheath=Slot(rig.transform,"Right hip item",rig.rightHipLocalPosition,new Vector3(0,0,-90),all).transform;
                rig.leftBackSheath=Slot(rig.transform,"Left backpack item",rig.leftBackLocalPosition,new Vector3(0,0,35),all).transform;
                rig.rightBackSheath=Slot(rig.transform,"Right backpack item",rig.rightBackLocalPosition,new Vector3(0,0,-35),all).transform;
                rig.slots=all.ToArray();
                var loadout=root.GetComponent<PlayerStartingItems>();if(!loadout)loadout=root.AddComponent<PlayerStartingItems>();
                loadout.leftHip=left;
                loadout.rightHip=rig.rightHipSheath.GetComponent<JunkerEquipmentSlot>();
                loadout.leftBackpack=rig.leftBackSheath.GetComponent<JunkerEquipmentSlot>();
                loadout.rightBackpack=rig.rightBackSheath.GetComponent<JunkerEquipmentSlot>();
                if(!left.startingItem)
                {
                    var sword=AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath);
                    if(sword)
                    {
                        var instance=(GameObject)PrefabUtility.InstantiatePrefab(sword,left.transform);
                        instance.name="Player demo sword";
                        instance.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
                        left.startingItem=instance.GetComponent<JunkerEquipmentItem>();
                        if(left.startingItem)left.startingItem.rig=rig;
                    }
                }
                loadout.leftHipItem=left.startingItem;
                loadout.rightHipItem=loadout.rightHip.startingItem;
                loadout.leftBackpackItem=loadout.leftBackpack.startingItem;
                loadout.rightBackpackItem=loadout.rightBackpack.startingItem;
                if(loadout.leftHipItem)loadout.leftHipPrefab=PrefabUtility.GetCorrespondingObjectFromSource(loadout.leftHipItem.gameObject) as GameObject;
                if(!root.GetComponent<PlayerWeaponGripAuthoring>())root.AddComponent<PlayerWeaponGripAuthoring>();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static JunkerEquipmentSlot Slot(Transform parent,string name,Vector3 position,Vector3 euler,System.Collections.Generic.List<JunkerEquipmentSlot> all)
        {
            var child=parent.Find(name); if(!child){child=new GameObject(name).transform;child.SetParent(parent,false);}
            child.localPosition=position; child.localRotation=Quaternion.Euler(euler);
            var slot=child.GetComponent<JunkerEquipmentSlot>();if(!slot)slot=child.gameObject.AddComponent<JunkerEquipmentSlot>();slot.radius=.3f;
            if(!all.Contains(slot))all.Add(slot);return slot;
        }
    }
}
