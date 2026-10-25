using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JunkerEquipment.Thermal.Editor
{
    [InitializeOnLoad]
    public static class ThermalDemoUpgrade
    {
        const string Request="Temp/JunkerThermal.upgrade";
        const string Report="Temp/JunkerThermal.upgrade-result";
        static readonly string[] ToolPaths={
            "Assets/MyAssets/prefabs/Tools/PLS-T ScrapCutter.prefab",
            "Assets/JunkerEquipment/Thermal/Generated/ScrapCutter.prefab"
        };
        static readonly string[] PlayerPrefabs={"Assets/MyAssets/prefabs/Player/Junker.prefab","Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab"};
        static ThermalDemoUpgrade() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            if(File.Exists("Temp/JunkerThermal.reload") && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete("Temp/JunkerThermal.reload"); ReloadDemo(); return;
            }
            if(!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Upgrade(); File.WriteAllText(Report,"PASS: PLS-T inventory prefab, dedicated right-hip slots, and thermal scene updated."); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Zandea/Junker Equipment/Thermal/Reload saved demo")]
        public static void ReloadDemo()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ThermalDemoBuilder.ScenePath || scene.isDirty || SceneManager.sceneCount!=1)
                throw new InvalidOperationException("Save and open only the thermal demo before reloading it.");
            EditorSceneManager.OpenScene(ThermalDemoBuilder.ScenePath,OpenSceneMode.Single);
        }
        public static void ConfigureTool(GameObject root)
        {
            var item=root.GetComponent<JunkerEquipmentItem>();
            if(!item || !item.GetComponent<ThermalToolController>()) throw new InvalidOperationException("Expected PLS-T tool root.");
            item.holsterRadius=Mathf.Max(item.holsterRadius,.25f);
            var inventory=root.GetComponent<VRInventoryItem>();
            if(!inventory) inventory=root.AddComponent<VRInventoryItem>();
            inventory.CanStoreInBackpack=true;
            if(!inventory.InventoryAnchor)
            {
                var anchor=root.transform.Find("InventoryAnchor");
                if(!anchor) { anchor=new GameObject("InventoryAnchor").transform; anchor.SetParent(root.transform,false); anchor.localPosition=new Vector3(0,.065f,.1f); }
                inventory.InventoryAnchor=anchor;
            }
        }
        public static JunkerEquipmentSlot ConfigureRig(JunkerEquipmentRig rig)
        {
            if(!rig) throw new InvalidOperationException("No Junker equipment rig.");
            var slot=rig.GetComponentsInChildren<JunkerEquipmentSlot>(true).FirstOrDefault(s=>s.name=="Right hip - PLS-T");
            if(!slot)
            {
                var go=new GameObject("Right hip - PLS-T"); go.transform.SetParent(rig.transform,false);
                go.transform.localPosition=new Vector3(.38f,.95f,-.22f);
                go.transform.localRotation=Quaternion.Euler(90,0,0);
                slot=go.AddComponent<JunkerEquipmentSlot>();
                slot.acceptedItemId="PLS-T"; slot.approvedItemIds.Add("PLS-T"); slot.radius=.25f;
            }
            if(rig.slots==null || !rig.slots.Contains(slot)) rig.slots=(rig.slots??Array.Empty<JunkerEquipmentSlot>()).Append(slot).ToArray();
            return slot;
        }
        [MenuItem("Zandea/Junker Equipment/Thermal/Upgrade PLS-T inventory and pipe demo")]
        public static void Upgrade()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Upgrade in Edit mode.");
            foreach(string path in ToolPaths)
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path); if(!asset) continue;
                var tool=PrefabUtility.LoadPrefabContents(path);
                try { ConfigureTool(tool); PrefabUtility.SaveAsPrefabAsset(tool,path); }
                finally { PrefabUtility.UnloadPrefabContents(tool); }
            }
            foreach(string path in PlayerPrefabs)
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path); if(!asset) continue;
                var player=PrefabUtility.LoadPrefabContents(path);
                try { foreach(var rig in player.GetComponentsInChildren<JunkerEquipmentRig>(true)) ConfigureRig(rig); PrefabUtility.SaveAsPrefabAsset(player,path); }
                finally { PrefabUtility.UnloadPrefabContents(player); }
            }
            const string scenePath=ThermalDemoBuilder.ScenePath;
            var scene=SceneManager.GetSceneByPath(scenePath); bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
            try
            {
                var roots=scene.GetRootGameObjects();
                var cutter=roots.SelectMany(r=>r.GetComponentsInChildren<ThermalToolController>(true)).FirstOrDefault();
                if(!cutter) throw new InvalidOperationException("Thermal scene has no PLS-T.");
                var inventory=cutter.GetComponents<VRInventoryItem>();
                foreach(var extra in inventory.Where(PrefabUtility.IsAddedComponentOverride).ToArray())
                    UnityEngine.Object.DestroyImmediate(extra);
                var rig=roots.SelectMany(r=>r.GetComponentsInChildren<JunkerEquipmentRig>(true)).FirstOrDefault();
                ConfigureRig(rig);
                // Preserve the scene's rig link when the tool prefab has no scene reference.
                var item=cutter.GetComponent<JunkerEquipmentItem>(); if(!item.rig) item.rig=rig;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
            AssetDatabase.SaveAssets();
        }
    }
}
