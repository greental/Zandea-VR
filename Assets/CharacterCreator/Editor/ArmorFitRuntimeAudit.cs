using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KenshiVR.CharacterCreator.Editor
{
    public static class ArmorFitRuntimeAudit
    {
        const string Request = "Temp/ArmorFitRuntimeAudit.request";
        const string Output = "Temp/ArmorFitRuntimeAudit.txt";
        const string Bandit = "Assets/MeleeCombat/CombinedStarvingBandit.prefab";
        const string Items = "Assets/CharacterCreator/ImportedDownloads/Items";

        [InitializeOnLoadMethod]
        static void OnReload()
        {
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(Request);
                try { File.WriteAllText(Output, Run()); }
                catch (Exception error) { Debug.LogException(error); File.WriteAllText(Output, error.ToString()); }
            };
        }

        [MenuItem("Zandea/Character Creator/Audit Captured Armor Visibility")]
        public static string Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Bandit);
            if (!prefab) throw new InvalidOperationException("Missing bandit prefab");
            var scene = EditorSceneManager.NewPreviewScene();
            var report = new StringBuilder();
            try
            {
                foreach (var name in new[] { "bandit_barred_helmet", "bandit_mercenary_chest",
                    "bandit_rib_vest", "paladin_medieval_chest", "paladin_ragged_skirt",
                    "bandit_rust_shoulder", "bandit_shoulder_pad", "paladin_metal_shoulder" })
                {
                    var item = AssetDatabase.LoadAssetAtPath<AppearanceItemDefinition>(Items + "/" + name + ".asset");
                    var bandit = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    try
                    {
                        var cosmetics = bandit.GetComponent<CharacterCosmeticsController>();
                        if (!cosmetics || !item) { report.AppendLine(name + " MISSING"); continue; }
                        cosmetics.headwear = cosmetics.torso = cosmetics.legs = cosmetics.feet =
                            cosmetics.hands = cosmetics.outerArmor = null;
                        switch (item.slot)
                        {
                            case AppearanceSlot.Headwear: cosmetics.headwear = item; break;
                            case AppearanceSlot.Torso: cosmetics.torso = item; break;
                            case AppearanceSlot.Legs: cosmetics.legs = item; break;
                            case AppearanceSlot.OuterArmor: cosmetics.outerArmor = item; break;
                        }
                        cosmetics.Refresh();
                        var marker = bandit.GetComponentsInChildren<AppearanceSpawnedItem>(true)
                            .FirstOrDefault(m => m.definition == item);
                        var renderers = marker ? marker.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                        report.Append(name).Append(" marker=").Append(marker != null)
                            .Append(" active=").Append(marker && marker.gameObject.activeInHierarchy)
                            .Append(" renderers=").Append(renderers.Length)
                            .Append(" enabled=").Append(renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy))
                            .Append(" parent=").Append(marker && marker.transform.parent ? marker.transform.parent.name : "NONE");
                        foreach (var renderer in renderers)
                        {
                            var center = renderer.bounds.center - bandit.transform.position;
                            report.Append(" | ").Append(renderer.name).Append(" @ ").Append(center.ToString("F2"))
                                .Append(" size ").Append(renderer.bounds.size.ToString("F2"));
                        }
                        report.AppendLine();
                    }
                    finally { UnityEngine.Object.DestroyImmediate(bandit); }
                }
                return report.ToString();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
