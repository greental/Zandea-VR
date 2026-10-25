using System;
using System.IO;
using System.Linq;
using System.Text;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CombatFixAudit
{
    const string Request = "Temp/CombatFixAudit.request";
    const string Result = "Temp/CombatFixAudit.result";
    const string Variants = "Assets/MeleeCombat/MocapTest/GeneratedVariants";

    static CombatFixAudit() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        try { File.WriteAllText(Result, RunAudit()); }
        catch (Exception error) { File.WriteAllText(Result, "FAIL " + error); Debug.LogException(error); }
    }

    [MenuItem("Zandea/Melee Combat/Audit current combat fixes")]
    public static void Run() => Debug.Log(RunAudit());

    static string RunAudit()
    {
        var report = new StringBuilder();
        int failures = 0, variants = 0;
        var lootTable = AssetDatabase.LoadAssetAtPath<NpcLootTable>("Assets/MeleeCombat/Inventory/Starving Bandit Loot.asset");
        foreach (var file in Directory.GetFiles(Variants, "StarvingBandit_*.prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
            if (!prefab || !prefab.GetComponent<StarvingBandit>()) continue;
            variants++;
            var inventory = prefab.GetComponent<NpcInventory>();
            var clothing = prefab.GetComponent<NpcClothingLoot>();
            bool ready = inventory && clothing && inventory.lootTable == lootTable &&
                clothing.inventory == inventory && clothing.handleRadius >= .18f;
            if (!ready) { failures++; report.AppendLine("FAIL loot " + prefab.name); }
        }
        report.AppendLine("Generated bandit loot: " + (variants - failures) + "/" + variants);

        var sword = AssetDatabase.LoadAssetAtPath<GameObject>(Variants + "/PlayerPracticeLongBlade.prefab");
        var weapon = sword ? sword.GetComponent<PhysicalMeleeWeapon>() : null;
        var grab = sword ? sword.GetComponent<XRGrabInteractable>() : null;
        bool gripReady = weapon && grab && weapon.grip && weapon.leftGrip == weapon.grip &&
            weapon.rightGrip == weapon.grip && grab.attachTransform == weapon.grip;
        if (!gripReady) failures++;
        report.AppendLine("Practice sword grip: " + (gripReady ? "PASS" : "FAIL"));

        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyAssets/prefabs/Player/JunkerWeapon.prefab");
        var bag = player ? player.GetComponentInChildren<BackpackInventory>(true) : null;
        bool bagReady = bag && bag.storageVolume && bag.storedItemsRoot && bag.storageVolume.inventory == bag;
        if (!bagReady) failures++;
        report.AppendLine("Player backpack storage: " + (bagReady ? "PASS" : "FAIL"));

        var bandit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MeleeCombat/CombinedStarvingBandit.prefab");
        var anatomy = bandit ? bandit.GetComponent<DismemberableCharacter>() : null;
        var head = anatomy ? anatomy.GetJointBone(SeverPoint.Head) : null;
        int readableSkins = 0, headWeightedSkins = 0;
        if (bandit && head)
            foreach (var skin in bandit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                if (!mesh || !mesh.isReadable) continue;
                readableSkins++;
                if (skin.bones.Any(bone => bone && (bone == head || bone.IsChildOf(head)))) headWeightedSkins++;
            }
        bool goreReady = anatomy && head && readableSkins > 0;
        if (!goreReady) failures++;
        report.AppendLine($"Head sever source: {(goreReady ? "PASS" : "FAIL")} readableSkins={readableSkins} headWeightedSkins={headWeightedSkins}");
        if (bandit && head)
            foreach (var skin in bandit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                if (!mesh || !mesh.isReadable || mesh.boneWeights.Length != mesh.vertexCount) continue;
                bool[] affected = skin.bones.Select(b => b && (b == head || b.IsChildOf(head))).ToArray();
                var weights = mesh.boneWeights;
                int weighted = 0, triangles = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    float value = (w.boneIndex0 < affected.Length && affected[w.boneIndex0] ? w.weight0 : 0) +
                        (w.boneIndex1 < affected.Length && affected[w.boneIndex1] ? w.weight1 : 0) +
                        (w.boneIndex2 < affected.Length && affected[w.boneIndex2] ? w.weight2 : 0) +
                        (w.boneIndex3 < affected.Length && affected[w.boneIndex3] ? w.weight3 : 0);
                    if (value >= .5f) weighted++;
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) triangles += mesh.GetTriangles(sub).Length / 3;
                report.AppendLine($"Head mesh {skin.name}: vertices={mesh.vertexCount} headWeighted={weighted} triangles={triangles}");
            }
        report.Insert(0, failures == 0 ? "PASS static combat audit\n" : $"FAIL static combat audit: {failures} checks\n");
        return report.ToString();
    }
}
