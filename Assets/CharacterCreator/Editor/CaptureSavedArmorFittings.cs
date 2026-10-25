using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KenshiVR.CharacterCreator.Editor
{
    // Captures the user's saved ArmorFittings scene, including replacement meshes
    // placed beside disabled generated previews. Never rebuild the source scene.
    public static class CaptureSavedArmorFittings
    {
        const string Source = "Assets/CharacterCreator/ImportedArmor/ArmorFittings_UserFitBackup_20261022.unity";
        const string Request = "Temp/CaptureSavedArmorFittings.request";
        const string Result = "Temp/CaptureSavedArmorFittings.result";

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(Request);
                try { File.WriteAllText(Result, Capture()); }
                catch (Exception error) { Debug.LogException(error); File.WriteAllText(Result, error.ToString()); }
            };
        }

        [MenuItem("Zandea/Character Creator/Capture User Saved Armor Fittings")]
        public static string Capture()
        {
            var scene = EditorSceneManager.OpenScene(Source, OpenSceneMode.Additive);
            var lines = new List<string>();
            try
            {
                var entries = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ArmorShowcaseEntry>(true))
                    .Where(entry => entry.appearanceItem && entry.appearanceItem.prefab)
                    .GroupBy(entry => entry.appearanceItem);
                foreach (var group in entries)
                {
                    var item = group.Key;
                    // These were explicitly left unmatched by the artist.
                    if (item.name.Contains("pants") || item.slot == AppearanceSlot.Hands)
                    { lines.Add("SKIP unmatched " + item.name); continue; }
                    var entry = group.FirstOrDefault(e => !e.gameObject.activeSelf) ?? group.First();
                    var station = entry.GetComponentInParent<CharacterCosmeticsController>();
                    if (!station) { lines.Add("SKIP no station " + item.name); continue; }
                    var replacements = station.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != entry.transform &&
                            (t.name == item.name || t.name.StartsWith(item.name + " (", StringComparison.Ordinal)) &&
                            t.gameObject.activeSelf && Vector3.Distance(t.position, station.transform.position) < 3f &&
                            t.GetComponentInChildren<Renderer>(true) &&
                            !t.GetComponent<AppearanceSpawnedItem>())
                        .Where(t => !t.parent || !t.parent.name.StartsWith(item.name, StringComparison.Ordinal))
                        .ToArray();
                    string path = AssetDatabase.GetAssetPath(item.prefab);
                    if (replacements.Length > 0)
                    {
                        CaptureReplacement(item, path, station.transform, replacements);
                        lines.Add("REPLACED " + item.name + " from " + string.Join(", ", replacements.Select(t => t.name)));
                    }
                    else if (entry.gameObject.activeSelf)
                    {
                        CaptureExisting(entry, path);
                        lines.Add("FITTED " + item.name);
                    }
                    else lines.Add("SKIP disabled without replacement " + item.name);
                }
                AssetDatabase.SaveAssets();
                return string.Join("\n", lines);
            }
            finally { if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        static void CaptureExisting(ArmorShowcaseEntry entry, string path)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Copy(entry.transform, contents.transform);
                foreach (Transform from in entry.transform)
                {
                    var to = contents.transform.Cast<Transform>().FirstOrDefault(t => t.name == from.name);
                    if (to) Copy(from, to);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        static void CaptureReplacement(AppearanceItemDefinition item, string path, Transform station, Transform[] replacements)
        {
            var root = new GameObject(item.name);
            try
            {
                root.transform.SetPositionAndRotation(station.position, station.rotation);
                root.transform.localScale = station.lossyScale;
                foreach (var source in replacements)
                {
                    var copy = UnityEngine.Object.Instantiate(source.gameObject);
                    copy.name = source.name;
                    copy.transform.SetParent(root.transform, true);
                    copy.SetActive(true);
                    foreach (var extra in copy.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != copy.transform &&
                            t.name.StartsWith(item.name + " (", StringComparison.Ordinal) &&
                            Vector3.Distance(t.position, station.position) >= 3f).ToArray())
                        UnityEngine.Object.DestroyImmediate(extra.gameObject);
                }
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                if (!PrefabUtility.SaveAsPrefabAsset(root, path))
                    throw new IOException("Could not save replacement prefab " + path);
                // These are additional instances of the rigged source prefab.
                // Keep bone remapping and body morphs for gameplay animation.
                item.skinned = true;
                item.attachBoneName = item.slot == AppearanceSlot.Legs ? "CC_Base_Hip_02" : "CC_Base_Spine02_035";
                item.requiredMorphs = new[] { "WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular" };
                item.fitNote = "User-authored replacement mesh fit from " + Source + ".";
                EditorUtility.SetDirty(item);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Copy(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            target.gameObject.SetActive(source.gameObject.activeSelf);
        }
    }
}
