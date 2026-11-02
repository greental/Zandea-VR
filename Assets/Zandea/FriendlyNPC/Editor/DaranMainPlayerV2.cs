using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JunkerBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zandea.POI;

namespace Zandea.FriendlyNPC.Editor
{
    // Promotes the already-authored Main player setup to a reviewable prefab.
    // V1 and the established source prefab are never changed by this command.
    public static class DaranMainPlayerV2
    {
        const string Main = "Assets/Main.unity";
        const string Source = "Assets/CharacterCreator/Player/VR player - Man Creator.prefab";
        const string Folder = "Assets/CharacterCreator/Player/Versions";
        const string V1 = Folder + "/VR player - Man Creator V1.prefab";
        const string V2 = Folder + "/VR player - Man Creator V2.prefab";
        const string Report = "Temp/DaranV2Integration/report.txt";

        [MenuItem("Zandea/Friendly NPC/Prepare Daran Main V2 player")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before preparing the V2 player.");
            var scene = EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            var player = UnityEngine.Object.FindObjectsByType<VRBodyController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene == scene && p.gameObject.activeInHierarchy);
            if (!player) throw new InvalidOperationException("Main has no active walking player.");
            var oldRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(player.gameObject);
            if (!oldRoot || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(oldRoot) != Source)
                throw new InvalidOperationException("Main's active player is not the expected Man Creator prefab.");
            VerifyPlayer(oldRoot);
            VerifyMerchant(scene);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/CharacterCreator/Player", "Versions");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(V1) && !AssetDatabase.CopyAsset(Source, V1))
                throw new InvalidOperationException("Could not preserve the original player as V1.");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(V2))
            {
                GameObject copy = null;
                try
                {
                    copy = UnityEngine.Object.Instantiate(oldRoot);
                    copy.name = "VR player - Man Creator V2";
                    if (PrefabUtility.IsPartOfPrefabInstance(copy))
                        PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely,
                            InteractionMode.AutomatedAction);
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    copy.transform.localScale = Vector3.one;
                    var saved = PrefabUtility.SaveAsPrefabAsset(copy, V2);
                    if (!saved) throw new InvalidOperationException("Unity could not save the V2 player prefab.");
                }
                finally { if (copy) UnityEngine.Object.DestroyImmediate(copy); }
            }

            var v2Asset = AssetDatabase.LoadAssetAtPath<GameObject>(V2);
            VerifyPlayer(v2Asset);
            var newRoot = (GameObject)PrefabUtility.InstantiatePrefab(v2Asset, scene);
            newRoot.name = oldRoot.name;
            newRoot.transform.SetParent(oldRoot.transform.parent, false);
            newRoot.transform.SetSiblingIndex(oldRoot.transform.GetSiblingIndex());
            newRoot.transform.SetPositionAndRotation(oldRoot.transform.position, oldRoot.transform.rotation);
            newRoot.transform.localScale = oldRoot.transform.localScale;
            newRoot.SetActive(oldRoot.activeSelf);

            var mapping = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            MapHierarchy(oldRoot.transform, newRoot.transform, mapping);
            // Prefab assets cannot store references to other scene roots. Copy
            // those scene references before the old player is removed.
            foreach (var pair in mapping)
                if (pair.Key is Component oldComponent && pair.Value is Component newComponent)
                    CopyExternalSceneReferences(oldComponent, newComponent, mapping);
            int rebound = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (root != oldRoot)
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        if (component) rebound += RemapReferences(component, mapping);
            UnityEngine.Object.DestroyImmediate(oldRoot);
            VerifyPlayer(newRoot);
            VerifyMerchant(scene);
            var source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(newRoot);
            if (source != V2) throw new InvalidOperationException("Main player is not an instance of V2.");
            if (PrefabUtility.GetAddedComponents(newRoot).Count != 0)
                throw new InvalidOperationException("V2 instance unexpectedly has added components.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Main);
            // Serialization must preserve the prefab link and scene references.
            scene = EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            var reloadedPlayer = UnityEngine.Object.FindObjectsByType<VRBodyController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene == scene && p.gameObject.activeInHierarchy);
            if (!reloadedPlayer || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    PrefabUtility.GetOutermostPrefabInstanceRoot(reloadedPlayer.gameObject)) != V2)
                throw new InvalidOperationException("Saved Main did not retain its V2 player instance.");
            VerifyPlayer(reloadedPlayer.gameObject);
            VerifyMerchant(scene);
            var reloadedMerchant = UnityEngine.Object.FindObjectsByType<FriendlyNpcInteraction>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(n => n.gameObject.scene == scene && n.npcId == "merchant-daran");
            if (!reloadedMerchant || !reloadedMerchant.choices ||
                !reloadedMerchant.choices.transform.IsChildOf(reloadedPlayer.transform))
                throw new InvalidOperationException("Saved Daran dialogue did not rebind to the V2 player.");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, "PASS Main player uses V2; V1 preserved; rebound scene references=" + rebound + "\n");
            Debug.Log("[Daran V2] Main player upgraded, V1 preserved; rebound scene references=" + rebound);
        }

        public static void BuildBatch() => Build();

        [MenuItem("Zandea/Friendly NPC/Repair Main Daran player links")]
        public static void RepairMainLinks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before repairing Main links.");
            var scene = EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            var players = UnityEngine.Object.FindObjectsByType<VRBodyController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(p => p.gameObject.scene == scene && p.gameObject.activeInHierarchy).ToArray();
            if (players.Length != 1)
                throw new InvalidOperationException("Main needs exactly one active VR player; found " + players.Length);
            var player = players[0];
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(player.gameObject);
            if (!root || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) != V2)
                throw new InvalidOperationException("Main's active player must be the review V2 prefab.");
            VerifyPlayer(root);
            var notebook = root.GetComponentInChildren<PalmDialogueChoices>(true);
            var merchant = UnityEngine.Object.FindObjectsByType<FriendlyNpcInteraction>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(n => n.gameObject.scene == scene && n.npcId == "merchant-daran");
            if (!merchant) throw new InvalidOperationException("Main is missing Daran.");
            merchant.choices = notebook;
            PrefabUtility.RecordPrefabInstancePropertyModifications(merchant);
            EditorUtility.SetDirty(merchant);
            VerifyMerchant(scene);
            var counter = merchant.GetComponentInParent<BanditOutpost>()
                ?.GetComponentsInChildren<VendorCounter>(true).FirstOrDefault(c => c.merchant == merchant);
            if (!counter || !counter.configuration || !counter.buyArea || !counter.sellArea ||
                !counter.paymentSpot || !counter.handshake || !counter.payoutPouchPrefab || !counter.payoutPosition)
                throw new InvalidOperationException("Main counter has incomplete payment or trade wiring.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Main);
            scene = EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            VerifyMerchant(scene);
            var savedMerchant = UnityEngine.Object.FindObjectsByType<FriendlyNpcInteraction>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(n => n.gameObject.scene == scene && n.npcId == "merchant-daran");
            var savedPlayer = UnityEngine.Object.FindObjectsByType<VRBodyController>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Single(p => p.gameObject.scene == scene && p.gameObject.activeInHierarchy);
            if (!savedMerchant.choices ||
                !savedMerchant.choices.transform.IsChildOf(savedPlayer.transform))
                throw new InvalidOperationException("Saved Daran notebook still points outside the active player.");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText("Temp/DaranV2Integration/link-repair.txt",
                "PASS Main Daran notebook belongs to the active V2 player; counter payment links are present.\n");
            Debug.Log("[Daran V2] Rebound Main dialogue to active V2 notebook and checked counter payment links.");
        }

        public static void RepairMainLinksBatch() => RepairMainLinks();

        static void VerifyPlayer(GameObject root)
        {
            var notebook = root.GetComponentInChildren<PalmDialogueChoices>(true);
            var utilities = root.GetComponentInChildren<BackpackUtilityAttachments>(true);
            if (!root.GetComponentInChildren<VRBodyController>(true) || !notebook ||
                !notebook.leftHand || !notebook.rightHand || !notebook.panel ||
                notebook.optionLabels == null || notebook.optionLabels.Length < 2 ||
                !utilities || !utilities.personalPursePrefab)
                throw new InvalidOperationException("Player is missing its working notebook, controller, or external purse setup.");
        }

        static void VerifyMerchant(Scene scene)
        {
            var poi = UnityEngine.Object.FindObjectsByType<BanditOutpost>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).FirstOrDefault(p => p.gameObject.scene == scene &&
                    p.poiId == "zandea.pueblo.settlement.1");
            var layout = poi ? poi.transform.Find("Layout Preview") : null;
            var merchant = layout ? layout.GetComponentsInChildren<FriendlyNpcInteraction>(true)
                .FirstOrDefault(n => n.npcId == "merchant-daran") : null;
            var counter = layout ? layout.GetComponentsInChildren<VendorCounter>(true)
                .FirstOrDefault(c => c.merchant == merchant) : null;
            if (!merchant || !counter || !counter.handshake ||
                counter.handshake.counter != counter || !merchant.choices ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(merchant.gameObject) !=
                "Assets/Zandea/FriendlyNPC/Generated/DaranMerchant.prefab")
                throw new InvalidOperationException("Main Daran must use the voice-scene prefab and link to its counter, handshake, and player choices.");
        }

        static void MapHierarchy(Transform oldTransform, Transform newTransform,
            Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            if (oldTransform.childCount != newTransform.childCount)
                throw new InvalidOperationException("V2 player hierarchy differs from the scene source.");
            map.Add(oldTransform.gameObject, newTransform.gameObject);
            var oldComponents = oldTransform.GetComponents<Component>();
            var newComponents = newTransform.GetComponents<Component>();
            if (oldComponents.Length != newComponents.Length)
                throw new InvalidOperationException("V2 player components differ from the scene source.");
            for (int i = 0; i < oldComponents.Length; i++)
            {
                if (!oldComponents[i] || !newComponents[i] ||
                    oldComponents[i].GetType() != newComponents[i].GetType())
                    throw new InvalidOperationException("V2 player component order differs from the scene source.");
                map.Add(oldComponents[i], newComponents[i]);
            }
            for (int i = 0; i < oldTransform.childCount; i++)
                MapHierarchy(oldTransform.GetChild(i), newTransform.GetChild(i), map);
        }

        static void CopyExternalSceneReferences(Component oldComponent, Component newComponent,
            Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var original = new SerializedObject(oldComponent);
            var replacement = new SerializedObject(newComponent);
            var iterator = original.GetIterator();
            bool dirty = false;
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (IsUnityPrefabBookkeeping(iterator.propertyPath)) continue;
                var reference = iterator.objectReferenceValue;
                if (!reference || map.ContainsKey(reference) || EditorUtility.IsPersistent(reference)) continue;
                var target = replacement.FindProperty(iterator.propertyPath);
                if (target == null || target.propertyType != SerializedPropertyType.ObjectReference) continue;
                target.objectReferenceValue = reference;
                dirty = true;
            }
            if (dirty) replacement.ApplyModifiedPropertiesWithoutUndo();
        }

        static int RemapReferences(Component component,
            Dictionary<UnityEngine.Object, UnityEngine.Object> map)
        {
            var serialized = new SerializedObject(component);
            var iterator = serialized.GetIterator();
            int changed = 0;
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (IsUnityPrefabBookkeeping(iterator.propertyPath)) continue;
                var reference = iterator.objectReferenceValue;
                if (reference && map.TryGetValue(reference, out var replacement))
                {
                    iterator.objectReferenceValue = replacement;
                    changed++;
                }
            }
            if (changed > 0) serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        static bool IsUnityPrefabBookkeeping(string path) =>
            path == "m_GameObject" || path == "m_PrefabInstance" ||
            path == "m_PrefabAsset" || path == "m_CorrespondingSourceObject";
    }
}
