using System;
using System.IO;
using System.Linq;
using System.Text;
using JunkerEquipment;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CombatInteractionPlayValidation
{
    const string Request = "Temp/CombatInteractionPlayValidation.v2.request";
    const string Result = "Temp/CombatInteractionPlayValidation.result";
    const string Running = "CombatInteractionPlayValidation.Running";
    const string StartedPlay = "CombatInteractionPlayValidation.StartedPlay";
    const string Variants = "Assets/MeleeCombat/MocapTest/GeneratedVariants";
    static GameObject[] actors;
    static int spawnedFrame;

    static CombatInteractionPlayValidation() => EditorApplication.update += Tick;

    [MenuItem("Tools/Melee Combat/Validate generated weapon pickup in Play mode")]
    public static void Run()
    {
        File.WriteAllText(Request, "run");
        Tick();
    }

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
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
        }
        if (!EditorApplication.isPlaying) return;
        if (actors == null)
        {
            actors = Directory.GetFiles(Variants, "StarvingBandit_*.prefab")
                .OrderBy(path => path)
                .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/')))
                .Where(prefab => prefab && prefab.GetComponent<StarvingBandit>())
                .Select(prefab => UnityEngine.Object.Instantiate(prefab, new Vector3(0, 2, 0), Quaternion.identity))
                .ToArray();
            spawnedFrame = Time.frameCount;
            return;
        }
        // Legacy embedded weapons equip in Start on a later Play frame.
        if (Time.frameCount < spawnedFrame + 5) return;
        try { File.WriteAllText(Result, Validate()); }
        catch (Exception error)
        {
            File.WriteAllText(Result, "FAIL generated weapon pickup\n" + error);
            Debug.LogException(error);
        }
        finally
        {
            foreach (var actor in actors) if (actor) UnityEngine.Object.Destroy(actor);
            actors = null;
            SessionState.SetBool(Running, false);
            bool startedPlay = SessionState.GetBool(StartedPlay, false);
            SessionState.SetBool(StartedPlay, false);
            if (startedPlay) EditorApplication.isPlaying = false;
        }
    }

    static string Validate()
    {
        var report = new StringBuilder();
        int checkedWeapons = 0;
        foreach (var actor in actors)
        {
            var prefabName = actor.name;
            PhysicalMeleeWeapon weapon = null;
            try
            {
                var generated = actor.GetComponent<NpcGeneratedWeapon>();
                var bandit = actor.GetComponent<StarvingBandit>();
                weapon = generated ? generated.Instance : bandit.staff;
                if (!weapon || weapon.State != PhysicalMeleeWeapon.HoldState.HeldByNpc)
                    throw new Exception(prefabName + " did not equip its generated weapon: " +
                        $"weapon={(weapon ? weapon.name : "null")}, enabled={(weapon && weapon.enabled)}, " +
                        $"active={(weapon && weapon.gameObject.activeInHierarchy)}, state={(weapon ? weapon.State.ToString() : "none")}, " +
                        $"target={(weapon && weapon.npcGripTarget ? weapon.npcGripTarget.name : "none")}");
                var health = actor.GetComponent<ScavengerHealth>();
                if (!health) throw new Exception(prefabName + " has no health");
                health.ForceDeath();
                var grab = weapon.Grab;
                var body = weapon.GetComponent<Rigidbody>();
                var groundShape = weapon.GetComponent<BoxCollider>();
                var equipment = weapon.GetComponent<JunkerEquipmentItem>();
                bool hasPhysicalGrab = grab && grab.enabled && grab.isActiveAndEnabled &&
                    grab.colliders.Any(shape => shape && shape.enabled && !shape.isTrigger);
                if (weapon.State != PhysicalMeleeWeapon.HoldState.World || !hasPhysicalGrab ||
                    !body || body.isKinematic || !weapon.GetComponent<VRInventoryItem>() ||
                    !groundShape || !groundShape.enabled || groundShape.isTrigger || !equipment)
                    throw new Exception(prefabName + " weapon cannot be picked up after death: " +
                        $"state={weapon.State} grab={hasPhysicalGrab} kinematic={(body && body.isKinematic)}");
                var slotObject = new GameObject("Validation melee equipment slot");
                try
                {
                    var slot = slotObject.AddComponent<JunkerEquipmentSlot>();
                    slot.meleeOnly = true;
                    if (!slot.TryMount(equipment) || equipment.currentSlot != slot)
                        throw new Exception(prefabName + " did not mount in a standard melee equipment slot");
                    slot.ReleaseItem(equipment);
                }
                finally { UnityEngine.Object.Destroy(slotObject); }
                checkedWeapons++;
                report.AppendLine("PASS " + prefabName + " -> " + weapon.name);
            }
            finally
            {
                if (weapon) UnityEngine.Object.Destroy(weapon.gameObject);
            }
        }
        if (checkedWeapons < 12) throw new Exception($"Only {checkedWeapons} generated weapons checked");
        report.Insert(0, $"PASS generated weapon pickup: {checkedWeapons} variants\n");
        return report.ToString();
    }
}
