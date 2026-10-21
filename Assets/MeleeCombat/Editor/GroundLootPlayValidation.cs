using System;
using System.IO;
using System.Linq;
using KenshiVR.MeleeCombat;
using JunkerEquipment;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GroundLootPlayValidation
{
    const string Request = "Temp/GroundLootPlayValidation.request";
    const string Result = "Temp/GroundLootPlayValidation.result";
    const string Running = "GroundLootPlayValidation.Running";
    const string StartedPlay = "GroundLootPlayValidation.StartedPlay";
    const string Sword = "Assets/MyAssets/prefabs/weapons/Player practice sword - generated long blade.prefab";
    const string Cloth = "Assets/MeleeCombat/Inventory/GeneratedLoot/rag-shirt.prefab";
    static GameObject sword, cloth, extraSlotObject;
    static readonly System.Collections.Generic.List<GameObject> mountedSwords = new();
    static Terrain terrain;
    static int startFrame = -1;
    static float swordStart, clothStart;

    static GroundLootPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Tools/Melee Combat/Validate dropped weapon and clothing on terrain")]
    public static void Run() { File.WriteAllText(Request, "run"); Tick(); }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            return;
        if (!SessionState.GetBool(Running, false))
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            SessionState.SetBool(Running, true);
            SessionState.SetBool(StartedPlay, !EditorApplication.isPlaying);
            startFrame = -1;
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (startFrame < 0)
            {
                foreach (var candidate in Terrain.activeTerrains)
                    if (candidate && candidate.enabled && candidate.GetComponent<TerrainCollider>())
                    { terrain = candidate; break; }
                if (!terrain) throw new Exception("No active terrain collider in the loaded scene");
                var prefabSword = AssetDatabase.LoadAssetAtPath<GameObject>(Sword);
                var prefabCloth = AssetDatabase.LoadAssetAtPath<GameObject>(Cloth);
                if (!prefabSword || !prefabCloth) throw new Exception("Missing weapon or clothing loot prefab");
                var center = terrain.transform.position + terrain.terrainData.size * .5f;
                center.y = terrain.SampleHeight(center) + terrain.transform.position.y;
                swordStart = center.y + 1.5f;
                clothStart = center.y + 1.5f;
                sword = UnityEngine.Object.Instantiate(prefabSword, new Vector3(center.x - 2f, swordStart, center.z),
                    Quaternion.Euler(0, 0, 90));
                cloth = UnityEngine.Object.Instantiate(prefabCloth, new Vector3(center.x + 2f, clothStart, center.z),
                    Quaternion.identity);
                GroundLootCollider.Ensure(sword);
                GroundLootCollider.Ensure(cloth);
                startFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount < startFrame + 120) return;
            Check(sword, swordStart, "sword");
            Check(cloth, clothStart, "clothing loot");
            var rig = UnityEngine.Object.FindFirstObjectByType<JunkerEquipmentRig>();
            var equipment = sword.GetComponent<JunkerEquipmentItem>();
            var physical = sword.GetComponent<PhysicalMeleeWeapon>();
            if (!physical || !physical.rightGrip || !physical.leftGrip ||
                physical.rightGripEuler != Vector3.zero ||
                physical.leftGripEuler != Vector3.zero)
                throw new Exception("Practice sword lacks its fixed authored hand angle");
            if (!rig || !rig.meleeSheath || !rig.rightHipSheath || !rig.leftBackSheath ||
                !rig.rightBackSheath || !equipment)
                throw new Exception("Player equipment rig does not expose all four additional item slots");
            var anchors = new[] { rig.meleeSheath, rig.rightHipSheath, rig.leftBackSheath,
                rig.rightBackSheath };
            var testSwordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Sword);
            var meleeSlots = anchors.Select(anchor => anchor.GetComponent<JunkerEquipmentSlot>()).ToArray();
            if (meleeSlots.Any(slot => !slot || !slot.Accepts(equipment)) || meleeSlots.Distinct().Count() != 4 ||
                meleeSlots.Any(slot => !rig.slots.Contains(slot)))
                throw new Exception("Four distinct item slots accepting weapons were not registered in the existing rig");
            var ordinaryItem = rig.GetComponentsInChildren<JunkerEquipmentItem>(true)
                .FirstOrDefault(item => item && !item.GetComponent<PhysicalMeleeWeapon>());
            if (!ordinaryItem || meleeSlots.Any(slot => !slot.Accepts(ordinaryItem)))
                throw new Exception("Added body slots reject ordinary equipment items");
            extraSlotObject = new GameObject("Validation extra item slot");
            extraSlotObject.transform.SetParent(rig.transform, false);
            extraSlotObject.transform.localPosition = new Vector3(0f, 3f, 0f);
            var extraSlot = extraSlotObject.AddComponent<JunkerEquipmentSlot>();
            rig.ConfigureItemSlots();
            if (!rig.slots.Contains(extraSlot) || !extraSlot.Accepts(equipment) ||
                !extraSlot.Accepts(ordinaryItem))
                throw new Exception("An arbitrary additional item slot did not register or accept both item kinds");
            extraSlot.approvedItemIds.Add(equipment.itemId);
            if (!extraSlot.Accepts(equipment) || extraSlot.Accepts(ordinaryItem))
                throw new Exception("Shared item ID filter did not apply to weapons and ordinary equipment");
            extraSlot.approvedItemIds.Clear();
            for (int index = 0; index < anchors.Length; index++)
            {
                var current = index == 0 ? sword : UnityEngine.Object.Instantiate(testSwordPrefab);
                if (index > 0) mountedSwords.Add(current);
                var item = current.GetComponent<JunkerEquipmentItem>();
                item.rig = rig;
                current.transform.position = anchors[index].position;
                current.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                if (!rig.TryHolster(item) || item.currentSlot != meleeSlots[index] ||
                    meleeSlots[index].Occupant != item)
                    throw new Exception("Sword did not enter item slot " + index);
            }
            if (rig.TryHolster(equipment)) throw new Exception("Occupied item slot accepted a second sword");
            foreach (var item in new[] { sword }.Concat(mountedSwords))
            {
                var equipped = item.GetComponent<JunkerEquipmentItem>();
                equipped.currentSlot.ReleaseItem(equipped);
                if (equipped.currentSlot || item.GetComponent<Rigidbody>().isKinematic)
                    throw new Exception("Sword did not leave its equipment slot cleanly");
            }
            File.WriteAllText(Result, "PASS terrain ground contact and shared item slots: four body slots accept weapons and other equipment; an arbitrary fifth slot auto-registers and obeys item ID filtering; four swords mount and unmount separately");
            Finish();
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL terrain ground contact\n" + error);
            Debug.LogException(error);
            Finish();
        }
    }

    static void Check(GameObject item, float initialY, string label)
    {
        if (!item) throw new Exception(label + " was destroyed before landing");
        var body = item.GetComponent<Rigidbody>();
        var box = item.GetComponent<BoxCollider>();
        if (!body || !box || !box.enabled || box.isTrigger || body.isKinematic)
            throw new Exception(label + " has no dynamic body and solid ground box");
        var point = item.transform.position;
        float ground = terrain.SampleHeight(point) + terrain.transform.position.y;
        // On a sloped tile, one corner of a rotated box can sit below the
        // terrain height sampled at its pivot while another corner supports it.
        if (point.y >= initialY - .3f || box.bounds.max.y < ground + .02f ||
            point.y < ground - .25f || body.linearVelocity.y < -1f)
            throw new Exception($"{label} missed terrain: centerY={point.y:F3} " +
                $"box=({box.bounds.min.y:F3},{box.bounds.max.y:F3}) ground={ground:F3} " +
                $"velocityY={body.linearVelocity.y:F3}");
    }

    static void Finish()
    {
        if (sword) UnityEngine.Object.Destroy(sword);
        if (cloth) UnityEngine.Object.Destroy(cloth);
        if (extraSlotObject) UnityEngine.Object.Destroy(extraSlotObject);
        foreach (var item in mountedSwords) if (item) UnityEngine.Object.Destroy(item);
        mountedSwords.Clear();
        sword = cloth = null;
        extraSlotObject = null;
        terrain = null;
        SessionState.SetBool(Running, false);
        bool startedPlay = SessionState.GetBool(StartedPlay, false);
        SessionState.SetBool(StartedPlay, false);
        if (startedPlay) EditorApplication.isPlaying = false;
    }
}
