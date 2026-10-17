using System;
using System.IO;
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
    static GameObject sword, cloth;
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
            if (!rig || !rig.meleeSheath || !equipment)
                throw new Exception("Player equipment rig or melee slot is unavailable");
            sword.transform.position = rig.meleeSheath.position;
            sword.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            equipment.rig = rig;
            if (!rig.TryHolster(equipment) || !equipment.currentSlot || !equipment.currentSlot.meleeOnly)
                throw new Exception("Sword did not enter the player's normal equipment slot");
            equipment.currentSlot.ReleaseItem(equipment);
            if (equipment.currentSlot || sword.GetComponent<Rigidbody>().isKinematic)
                throw new Exception("Sword did not leave the equipment slot cleanly");
            File.WriteAllText(Result, "PASS terrain ground contact and equipment slot: sword and clothing landed on terrain; sword holstered and unmounted through the player rig");
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
        sword = cloth = null;
        terrain = null;
        SessionState.SetBool(Running, false);
        bool startedPlay = SessionState.GetBool(StartedPlay, false);
        SessionState.SetBool(StartedPlay, false);
        if (startedPlay) EditorApplication.isPlaying = false;
    }
}
