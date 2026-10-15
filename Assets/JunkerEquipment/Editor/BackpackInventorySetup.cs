using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment.Editor
{
    [InitializeOnLoad]
    public static class BackpackInventorySetup
    {
        const string Request = "Temp/BackpackInventory.request";
        const string MeshPath = "Assets/MyAssets/mesh/Armor/backpack/Backpack.glb";
        static readonly string[] Prefabs = {
            "Assets/MyAssets/prefabs/weapons/R4-PowerCell.prefab",
            "Assets/MyAssets/prefabs/Player/Junker.prefab",
            "Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab",
            "Assets/MyAssets/prefabs/weapons/PKO.prefab",
            "Assets/MyAssets/prefabs/weapons/JKR-88 1.prefab"
        };
        static BackpackInventorySetup() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Configure(); }
            catch (Exception e) { File.WriteAllText("Temp/BackpackInventory.setup", "FAIL " + e); Debug.LogException(e); }
        }
        static T Add<T>(GameObject go) where T : Component
        {
            var value = go.GetComponent<T>();
            return value ? value : go.AddComponent<T>();
        }
        static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing) return existing;
            var child = new GameObject(name).transform; child.SetParent(parent, false); return child;
        }

        [MenuItem("Tools/Junker Equipment/Configure physical backpack inventory")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure in Edit Mode.");
            AssetDatabase.ImportAsset(MeshPath, ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath)) throw new InvalidOperationException("Open backpack GLB did not import.");
            var log = new StringBuilder();
            foreach (string path in Prefabs)
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { ConfigureRoot(root, log); PrefabUtility.SaveAsPrefabAsset(root, path); log.AppendLine("Configured prefab: " + path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // Configure scene-owned tools without rebuilding equipment or touching unrelated scenes.
            string scenePath = JunkerEquipmentSetup.ScenePath;
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                // Prefab updates already configure inherited items. Avoid a needless save:
                // other edit-mode scene scripts may regenerate unrelated UI on scene load.
                bool needsSetup = roots.Any(root =>
                    root.GetComponentsInChildren<JunkerEquipmentItem>(true).Any(item =>
                        item.itemId == "backpack" ? !item.GetComponent<BackpackInventory>() ||
                            !item.GetComponent<BackpackInventory>().topHandle || !item.GetComponent<BackpackInventory>().backSlot :
                        (item.GetComponent<JunkerScrewdriver>() || item.GetComponent<JunkerPistol>() || item.GetComponent<JunkerBinoculars>()) && !item.GetComponent<VRInventoryItem>()) ||
                    root.GetComponentsInChildren<JunkerPowerCell>(true).Any(cell => !cell.GetComponent<VRInventoryItem>()));
                if (needsSetup)
                {
                    foreach (var root in roots) ConfigureRoot(root, log);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                else log.AppendLine("Scene items already configured through existing components and updated prefabs: " + scenePath);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
            log.AppendLine("PASS: original backpack root and back slot retained; floating release, rotation-preserving top handle, battery storage and open cavity configured.");
            File.WriteAllText("Temp/BackpackInventory.setup", log.ToString());
        }

        static void ConfigureRoot(GameObject root, StringBuilder log)
        {
            foreach (var item in root.GetComponentsInChildren<JunkerEquipmentItem>(true))
            {
                if (item.itemId == "backpack") { ConfigureBag(item); continue; }
                if (!item.GetComponent<JunkerScrewdriver>() && !item.GetComponent<JunkerPistol>() && !item.GetComponent<JunkerBinoculars>()) continue;
                ConfigureItem(item.gameObject, log);
            }
            foreach (var cell in root.GetComponentsInChildren<JunkerPowerCell>(true)) ConfigureItem(cell.gameObject, log);
        }

        static void ConfigureItem(GameObject item, StringBuilder log)
        {
                var inventoryItem = Add<VRInventoryItem>(item);
                if (!inventoryItem.InventoryAnchor)
                {
                    var anchor = Child(item.transform, "InventoryAnchor");
                    var shapes = item.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponentInParent<Rigidbody>() == item.GetComponent<Rigidbody>()).ToArray();
                    if (shapes.Length > 0)
                    {
                        var box = new Bounds(item.transform.InverseTransformPoint(shapes[0].bounds.center), Vector3.zero);
                        foreach (var shape in shapes) box.Encapsulate(item.transform.InverseTransformPoint(shape.bounds.center));
                        anchor.localPosition = box.center;
                    }
                    inventoryItem.InventoryAnchor = anchor;
                }
                EditorUtility.SetDirty(inventoryItem);
                if (PrefabUtility.IsPartOfPrefabInstance(inventoryItem)) PrefabUtility.RecordPrefabInstancePropertyModifications(inventoryItem);
                log.AppendLine("Inventory compatible: " + item.name);
        }

        static void ConfigureBag(JunkerEquipmentItem item)
        {
            var bag = Add<BackpackInventory>(item.gameObject);
            bag.equipment = item;
            bag.backSlot = item.rig ? item.rig.slots.FirstOrDefault(slot => slot && slot.startingItem == item) : null;
            if (!bag.backSlot && item.rig)
                bag.backSlot = item.rig.slots.FirstOrDefault(slot => slot && (slot.legacyAcceptedItemId == "backpack" || slot.approvedItemIds.Contains("backpack")));
            if (!bag.backSlot) throw new InvalidOperationException("Backpack has no matching back slot: " + item.name);
            bag.returnRadius = Mathf.Max(.35f, bag.returnRadius);
            var handle = Child(item.transform, "Top handle");
            if (!bag.topHandle) handle.localPosition = new Vector3(0, .04f, -.14f);
            bag.topHandle = handle;
            var handleCollider = Add<SphereCollider>(handle.gameObject);
            handleCollider.isTrigger = true; handleCollider.radius = .075f;
            bag.closedVisual = item.transform.Find("Visual").gameObject;
            bag.storedItemsRoot = Child(item.transform, "StoredItemsRoot");
            var visual = Child(item.transform, "Open backpack visual");
            if (visual.childCount == 0)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath), visual);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one * .26f;
                visual.localPosition = new Vector3(0, -.07f, 0);
            }
            bag.openVisual = visual.gameObject;
            bag.openVisual.SetActive(false);
            var volumeObject = Child(item.transform, "StorageVolume");
            bool newVolume = !volumeObject.GetComponent<BackpackStorageVolume>();
            var volume = Add<BackpackStorageVolume>(volumeObject.gameObject);
            volume.inventory = bag;
            volume.Box.isTrigger = true;
            if (newVolume)
            {
                volume.Box.center = new Vector3(0, -.16f, .04f);
                volume.Box.size = new Vector3(.30f, .24f, .28f);
            }
            bag.storageVolume = volume;
            // Only the top handle grabs the bag; reaching into the cavity selects contents.
            var grab = item.GetComponent<XRGrabInteractable>();
            grab.colliders.Clear(); grab.colliders.Add(handleCollider);
            grab.attachTransform = handle; grab.useDynamicAttach = true;
            grab.matchAttachPosition = false; grab.matchAttachRotation = true;
            grab.snapToColliderVolume = false; grab.attachEaseInTime = 0; grab.throwOnDetach = false;
            if (!volume.validStorageIndicator) volume.validStorageIndicator = CreateIndicator(volume);
            EditorUtility.SetDirty(bag); EditorUtility.SetDirty(volume); EditorUtility.SetDirty(grab);
            foreach (var component in new Component[] { bag, volume, grab })
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        static GameObject CreateIndicator(BackpackStorageVolume volume)
        {
            var line = Child(volume.transform, "Valid placement rim").gameObject.AddComponent<LineRenderer>();
            const string path = "Assets/JunkerEquipment/Generated/Backpack valid placement.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", new Color(.2f, .8f, .65f, 1));
                AssetDatabase.CreateAsset(mat, path);
            }
            line.sharedMaterial = mat; line.useWorldSpace = false; line.loop = true;
            line.widthMultiplier = .002f; line.positionCount = 4;
            var c = volume.Box.center; var h = volume.Box.size * .5f;
            line.SetPositions(new[] { c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(h.x, h.y, -h.z), c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z) });
            line.gameObject.SetActive(false);
            return line.gameObject;
        }
    }
}
